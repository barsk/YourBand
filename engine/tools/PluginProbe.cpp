// plugin_probe: checks whether a plugin can serve as one of the band's instruments.
//
//   plugin_probe <plugin.vst3> [out-folder] [program-name-filter ... | --state <file> ...] [--map [--velocity n] [--channel n]]
//
// With --map, instead of the phrase it plays every note from 24 to 96 on its own and lists the ones that sound,
// with how much of each is low (below 150 Hz), mid and high (above 6 kHz), and the pitch it sounds at (as a
// note number, 0 for none): a drum kit's note layout, or an instrument's range.
//
// With --state, each file holds the plugin's own state and gives one take; this is how sounds are
// chosen in plugins without a program list (Numa Player). A file may be the plugin's state as a VST3
// host stores it (starting with the "VC2" magic, as the engine saves it) or just the inner IComponent
// data (wrapped here).
//
// With --strum-test (with --state), instead of the phrase it plays a scripted strum test on channel 3:
// a held G voicing, down/up strum keyswitches (G#5/A5) at several velocities, the muted-strum options
// (C#0 Mute-articulation latch, C6 hold), and reports per-segment levels plus each strum's onset offset.
//
// Loads the plugin without a window and reports what it offers (inputs, outputs, MIDI, programs, latency).
// For each program whose name contains one of the filters (all programs if none are given), it plays a
// keyboard comping phrase (Bm - A - E - E at 112 BPM, two bars each), rendered in 64-sample blocks as the
// engine would, and reports the processing time as a share of each block's duration (the engine's
// "audio CPU"). Each take is written as a WAV file to listen to.

#include <juce_audio_formats/juce_audio_formats.h>
#include <juce_audio_processors/juce_audio_processors.h>
#include <juce_gui_basics/juce_gui_basics.h>

#include <algorithm>
#include <cstring>
#include <iostream>
#include <vector>

namespace
{
/** The MIDI channels the note map plays on (--channel n for one): a drum kit and a piano answer on 10 and 1. */
std::vector<int> mapChannels { 10, 1 };

double g_lagMs = 15.0, g_keyMs = 50.0;  // --lag/--key for the sweep test, in milliseconds
std::vector<double> g_keyList;  // --keys 40,60,80: per-stroke key lengths, overriding --key
std::vector<int> g_velList;     // --vels 40,64,96: per-stroke strum keyswitch velocities (else 96)
std::vector<int> g_voicingList; // --voicing 41,46,51,56,61,66: the chord held, lowest first (else G: E2 B2 G3 B3 D4 G4)
std::vector<int> g_keysToPress; // --press 79,82,83: the stroke keys, one per stroke (else down/up strums by --dirs)
std::vector<double> g_atList;   // --at 5,5.02,5.5: per-stroke times in beats, overriding --strum/--gap
std::string g_dirs;      // --dirs uduu: u = up (A5), d = down (G#5)
std::vector<double> g_muteList;  // --mutes 0,10,100: C6 hold length per stroke in ms (0 = no mute)
double g_f5Beat = 3.5, g_chordBeat = 4.0, g_firstStroke = 5.0, g_gapBeats = 1.0;
int g_numStrokes = 4;  // --f5/--chord/--strum/--gap/--n for the sweep test
double g_chord2Beat = -1.0;
std::vector<int> g_chord2Notes;  // --chord2 <beat> <n,n,n,n,n,n>: the voicing changes at that beat

constexpr double rate = 48000.0;
constexpr int block = 64;
constexpr double bpm = 112.0;

/** The phrase: per bar a chord, left hand root and fifth, right hand a close triad, struck on every beat. */
juce::MidiBuffer phrase (int& totalSamples)
{
    struct Chord { int left[2]; int right[3]; };
    // MIDI notes: Bm (B1 F#2 | D4 F#4 B4), A (A1 E2 | C#4 E4 A4), E (E2 B2 | B3 E4 G#4)
    const Chord bm { { 35, 42 }, { 62, 66, 71 } }, a { { 33, 40 }, { 61, 64, 69 } }, e { { 40, 47 }, { 59, 64, 68 } };
    const Chord* bars[] = { &bm, &bm, &a, &a, &e, &e, &e, &e };
    const auto beat = rate * 60.0 / bpm;

    juce::MidiBuffer midi;
    int t = 0;
    for (int bar = 0; bar < 8; ++bar)
        for (int b = 0; b < 4; ++b)
        {
            t = (int) std::round ((bar * 4 + b) * beat);
            const auto off = (int) std::round (t + 0.85 * beat);
            const auto* c = bars[bar];
            const auto velocity = (juce::uint8) (b == 0 ? 100 : 80);
            if (b % 2 == 0)
                for (auto n : c->left)
                {
                    midi.addEvent (juce::MidiMessage::noteOn (1, n, velocity), t);
                    midi.addEvent (juce::MidiMessage::noteOff (1, n), (int) std::round (t + 1.85 * beat));
                }
            for (auto n : c->right)
            {
                midi.addEvent (juce::MidiMessage::noteOn (1, n, velocity), t);
                midi.addEvent (juce::MidiMessage::noteOff (1, n), off);
            }
        }
    totalSamples = (int) std::round (32 * beat + 3.0 * rate);  // plus room for the release
    return midi;
}

/** The pitch of a sound as a MIDI note number (YIN, 30 Hz to 1 kHz), or 0 if it has none. */
double pitchOf (const std::vector<float>& x)
{
    constexpr int window = 4096;
    const int maxLag = (int) (rate / 30.0), minLag = (int) (rate / 1000.0);
    if ((int) x.size() < window + maxLag)
        return 0;
    std::vector<double> d ((size_t) maxLag + 1, 0.0);
    for (int lag = 1; lag <= maxLag; ++lag)
        for (int i = 0; i < window; ++i)
        {
            const double e = x[(size_t) i] - x[(size_t) (i + lag)];
            d[(size_t) lag] += e * e;
        }
    // each lag against the mean of the shorter ones; the first clear dip is the period
    double sum = 0;
    int best = 0;
    double bestValue = 1e9;
    for (int lag = 1; lag <= maxLag; ++lag)
    {
        sum += d[(size_t) lag];
        const double value = sum > 0 ? d[(size_t) lag] * lag / sum : 1.0;
        if (lag < minLag)
            continue;
        if (value < 0.15)
        {
            while (lag < maxLag && d[(size_t) lag + 1] * (lag + 1) / (sum + d[(size_t) lag + 1]) < value)
                sum += d[(size_t) ++lag];
            best = lag;
            break;
        }
        if (value < bestValue)
            bestValue = value, best = lag;
    }
    if (best == 0 || (bestValue < 1e9 && bestValue > 0.5))
        return 0;
    return 69.0 + 12.0 * std::log2 (rate / best / 440.0);
}

/** Waits like a host does: the message loop runs and silent audio blocks are processed at real-time pace (a plugin
    like Kontakt only finishes loading an instrument while its audio runs). */
void settle (juce::AudioPluginInstance& plugin, int milliseconds)
{
    const auto outs = juce::jmax (1, plugin.getTotalNumOutputChannels());
    juce::AudioBuffer<float> buffer (juce::jmax (outs, plugin.getTotalNumInputChannels()), block);
    const auto began = juce::Time::getMillisecondCounterHiRes();
    double done = 0;
    while (juce::Time::getMillisecondCounterHiRes() - began < milliseconds)
    {
        buffer.clear();
        juce::MidiBuffer none;
        plugin.processBlock (buffer, none);
        done += block;
        const auto due = began + done / rate * 1000.0;
        const auto wait = due - juce::Time::getMillisecondCounterHiRes();
        juce::MessageManager::getInstance()->runDispatchLoopUntil (wait > 1 ? (int) wait : 1);
    }
}

/** Every note on its own, at real-time pace: which sound, how loud, how low or high, and at which pitch. */
void mapNotes (juce::AudioPluginInstance& plugin, int velocity)
{
    const auto outs = juce::jmax (1, plugin.getTotalNumOutputChannels());
    juce::AudioBuffer<float> buffer (juce::jmax (outs, plugin.getTotalNumInputChannels()), block);
    const int length = (int) (0.5 * rate);
    const auto lowCoeff = (float) (1.0 - std::exp (-2.0 * juce::MathConstants<double>::pi * 150.0 / rate));
    const auto highCoeff = (float) (1.0 - std::exp (-2.0 * juce::MathConstants<double>::pi * 6000.0 / rate));
    plugin.reset();
    auto loudest = -120.0;
    std::cout << "note  level   low   mid  high  pitch\n";
    for (int note = 24; note <= 96; ++note)
    {
        double total = 0, low = 0, high = 0;
        float lowState = 0, highState = 0;
        std::vector<float> mono;  // from 0.1 s on: past the attack
        const auto began = juce::Time::getMillisecondCounterHiRes();
        for (int start = 0; start < length; start += block)
        {
            juce::MidiBuffer events;
            if (start == 0)
                for (int channel : mapChannels)
                    events.addEvent (juce::MidiMessage::noteOn (channel, note, (juce::uint8) velocity), 0);
            if (start <= length / 2 && start + block > length / 2)
                for (int channel : mapChannels)
                    events.addEvent (juce::MidiMessage::noteOff (channel, note), length / 2 - start);
            buffer.clear();
            plugin.processBlock (buffer, events);
            for (int i = 0; i < block; ++i)
            {
                float x = 0;
                for (int ch = 0; ch < outs; ++ch)
                    x += buffer.getSample (ch, i);
                if (start + i >= (int) (0.1 * rate))
                    mono.push_back (x);
                lowState += lowCoeff * (x - lowState);
                highState += highCoeff * (x - highState);
                total += x * x;
                low += lowState * lowState;
                high += (x - highState) * (x - highState);
            }
            const auto due = began + (start + block) / rate * 1000.0;
            const auto wait = due - juce::Time::getMillisecondCounterHiRes();
            if (wait > 2)
                juce::MessageManager::getInstance()->runDispatchLoopUntil ((int) wait);
        }
        const auto level = 10.0 * std::log10 (total / length + 1e-12);
        loudest = std::max (loudest, level);
        if (level > -60)
            std::cout << juce::String (note).paddedLeft (' ', 4) << juce::String (level, 1).paddedLeft (' ', 7)
                      << juce::String (juce::roundToInt (100 * low / total)).paddedLeft (' ', 5) << "%"
                      << juce::String (juce::roundToInt (100 * (total - low - high) / total)).paddedLeft (' ', 5) << "%"
                      << juce::String (juce::roundToInt (100 * high / total)).paddedLeft (' ', 5) << "%"
                      << juce::String (pitchOf (mono), 1).paddedLeft (' ', 7) << "\n";
        // all notes off before the next
        juce::MidiBuffer off;
        for (int channel : mapChannels)
            off.addEvent (juce::MidiMessage::allNotesOff (channel), 0);
        buffer.clear();
        plugin.processBlock (buffer, off);
    }
    std::cout << "loudest note: " << juce::String (loudest, 1) << " dB\n";
}

/** A middle C, played for 0.4 s at real-time pace: its level in dB (silence: -100). */
double noteLevel (juce::AudioPluginInstance& plugin)
{
    const auto outs = juce::jmax (1, plugin.getTotalNumOutputChannels());
    juce::AudioBuffer<float> buffer (juce::jmax (outs, plugin.getTotalNumInputChannels()), block);
    const int length = (int) (0.4 * rate);
    double total = 0;
    const auto began = juce::Time::getMillisecondCounterHiRes();
    for (int start = 0; start < length; start += block)
    {
        juce::MidiBuffer events;
        if (start == 0)
            events.addEvent (juce::MidiMessage::noteOn (1, 60, (juce::uint8) 100), 0);
        buffer.clear();
        plugin.processBlock (buffer, events);
        for (int ch = 0; ch < outs; ++ch)
            for (int i = 0; i < block; ++i)
                total += buffer.getSample (ch, i) * buffer.getSample (ch, i);
        const auto wait = began + (start + block) / rate * 1000.0 - juce::Time::getMillisecondCounterHiRes();
        if (wait > 2)
            juce::MessageManager::getInstance()->runDispatchLoopUntil ((int) wait);
    }
    juce::MidiBuffer off;
    off.addEvent (juce::MidiMessage::allNotesOff (1), 0);
    buffer.clear();
    plugin.processBlock (buffer, off);
    return 10.0 * std::log10 (total / length + 1e-10);
}

struct Take
{
    double meanLoad = 0, peakLoad = 0;
    float peak = 0, rms = 0;
};

Take render (juce::AudioPluginInstance& plugin, const juce::File& wav)
{
    int total = 0;
    const auto midi = phrase (total);
    const auto outs = juce::jmax (1, plugin.getTotalNumOutputChannels());
    juce::AudioBuffer<float> buffer (juce::jmax (outs, plugin.getTotalNumInputChannels()), block);
    juce::AudioBuffer<float> recording (outs, total);

    plugin.reset();
    Take take;
    double sumLoad = 0;
    int blocks = 0;
    const auto blockSeconds = block / rate;
    const auto began = juce::Time::getHighResolutionTicks();

    for (int start = 0; start < total; start += block)
    {
        const auto n = juce::jmin (block, total - start);
        juce::MidiBuffer events;
        for (const auto m : midi)
            if (m.samplePosition >= start && m.samplePosition < start + n)
                events.addEvent (m.getMessage(), m.samplePosition - start);
        buffer.clear();

        const auto t0 = juce::Time::getHighResolutionTicks();
        plugin.processBlock (buffer, events);
        const auto seconds = juce::Time::highResolutionTicksToSeconds (juce::Time::getHighResolutionTicks() - t0);

        // real-time pace, as in the engine (sample players stream from disk in the background); the waits
        // run the message loop, which some plugins need while they play
        const auto due = (start + n) / rate;
        const auto elapsed = juce::Time::highResolutionTicksToSeconds (juce::Time::getHighResolutionTicks() - began);
        if (due - elapsed > 0.002)
            juce::MessageManager::getInstance()->runDispatchLoopUntil ((int) ((due - elapsed) * 1000.0));

        const auto load = seconds / blockSeconds;
        sumLoad += load;
        take.peakLoad = std::max (take.peakLoad, load);
        ++blocks;
        for (int ch = 0; ch < outs; ++ch)
            recording.copyFrom (ch, start, buffer, ch, 0, n);
    }

    take.meanLoad = sumLoad / std::max (1, blocks);
    for (int ch = 0; ch < outs; ++ch)
    {
        take.peak = std::max (take.peak, recording.getMagnitude (ch, 0, total));
        take.rms = std::max (take.rms, recording.getRMSLevel (ch, 0, total));
    }

    wav.getParentDirectory().createDirectory();
    wav.deleteFile();  // an output stream on an existing file appends to it
    juce::WavAudioFormat format;
    if (std::unique_ptr<juce::OutputStream> stream = wav.createOutputStream())
    {
        auto options = juce::AudioFormatWriterOptions{}.withSampleRate (rate)
                                                       .withNumChannels (outs)
                                                       .withBitsPerSample (24);
        if (auto writer = format.createWriterFor (stream, options))
        {
            writer->writeFromAudioSampleBuffer (recording, 0, total);
        }
    }
    return take;
}

/** Loads a state file into the plugin: a full VST3PluginState binary (starting with the "VC2" magic, as the
    engine saves it) goes straight in; anything else is the inner IComponent data, wrapped as a host would
    store it. False if the file cannot be read. */
bool loadState (juce::AudioPluginInstance& plugin, const juce::File& stateFile)
{
    juce::MemoryBlock data;
    if (! stateFile.loadFileAsData (data))
        return false;
    if (data.getSize() >= 4 && std::memcmp (data.getData(), "VC2", 3) == 0)
    {
        plugin.setStateInformation (data.getData(), (int) data.getSize());
        return true;
    }
    juce::XmlElement wrapper ("VST3PluginState");
    wrapper.createNewChildElement ("IComponent")->addTextElement (data.toBase64Encoding());
    juce::MemoryBlock wrapped;
    juce::AudioProcessor::copyXmlToBinary (wrapper, wrapped);
    plugin.setStateInformation (wrapped.getData(), (int) wrapped.getSize());
    return true;
}
}

/** A scripted strum test for a strum-capable guitar (Shreddage): a held voicing, strum keyswitches
    (G#5 down, A5 up) at several velocities, the two muted-strum options (C#0 Mute-articulation latch,
    C6 hold), and a keyswitch scan (G5 to C6, to see which high notes strum and which silently join the
    voicing). Plays on channel 3, as the engine's band guitarist, and reports per-segment levels and each
    strum's onset offset from its keyswitch. */
void strumTest (juce::AudioPluginInstance& plugin, const juce::File& wav)
{
    constexpr int channel = 3;
    constexpr double beat = rate * 60.0 / 96.0;
    const int voicing[] = { 40, 47, 55, 59, 62, 67 };  // G: E2 B2 G3 B3 D4 G4
    constexpr int kDown = 80, kUp = 81, kMuteLatch = 1, kMuteHold = 84;  // G#5 A5 C#0 C6

    struct Event { double sample; int note, velocity; bool off; };
    std::vector<Event> events;
    auto at = [&] (double beats, int note, int velocity = 100, bool off = false)
    { events.push_back ({ std::round (beats * beat), note, velocity, off }); };
    for (auto n : voicing) at (2.0, n);
    at (3.0, kDown, 100);
    at (4.0, kDown, 64);
    at (5.0, kUp, 100);
    at (6.0, kUp, 64);
    at (7.0, kMuteLatch);
    at (7.5, kDown, 100);
    at (8.5, kMuteLatch, 100, true);
    at (9.0, kMuteHold);
    at (9.5, kDown, 100);
    at (10.5, kMuteHold, 100, true);
    at (11.0, kDown, 127);
    for (auto n : voicing) at (12.0, n, 100, true);
    // keyswitch scan: one high note per beat, held (if it is a voicing note it joins the stored voicing;
    // if it is a strum keyswitch it strums). F#5 G5 G#5 A5 A#5 B5 C6 C#6
    for (auto n : voicing) at (12.5, n);
    at (13.0, 78, 100);  // F#5
    at (14.0, 79, 100);  // G5
    at (15.0, 80, 100);  // G#5
    at (16.0, 81, 100);  // A5
    at (17.0, 82, 100);  // A#5
    at (18.0, 83, 100);  // B5
    at (19.0, 84, 100);  // C6
    at (20.0, 85, 100);  // C#6
    for (auto n : voicing) at (21.0, n, 100, true);
    for (int n : { 78, 79, 80, 81, 82, 83, 84, 85 })
        at (21.0, n, 100, true);
    const int total = (int) std::round (22.0 * beat);

    const auto outs = juce::jmax (1, plugin.getTotalNumOutputChannels());
    juce::AudioBuffer<float> buffer (juce::jmax (outs, plugin.getTotalNumInputChannels()), block);
    juce::AudioBuffer<float> recording (outs, total);
    plugin.reset();
    const auto began = juce::Time::getHighResolutionTicks();
    for (int start = 0; start < total; start += block)
    {
        const auto n = juce::jmin (block, total - start);
        juce::MidiBuffer evs;
        for (const auto& e : events)
            if (e.sample >= start && e.sample < start + n)
                evs.addEvent (e.off ? juce::MidiMessage::noteOff (channel, e.note)
                                    : juce::MidiMessage::noteOn (channel, e.note, (juce::uint8) e.velocity),
                              (int) (e.sample - start));
        buffer.clear();
        plugin.processBlock (buffer, evs);
        const auto due = (start + n) / rate;
        const auto elapsed = juce::Time::highResolutionTicksToSeconds (juce::Time::getHighResolutionTicks() - began);
        if (due - elapsed > 0.002)
            juce::MessageManager::getInstance()->runDispatchLoopUntil ((int) ((due - elapsed) * 1000.0));
        for (int ch = 0; ch < outs; ++ch)
            recording.copyFrom (ch, start, buffer, ch, 0, n);
    }

    wav.getParentDirectory().createDirectory();
    wav.deleteFile();  // an output stream on an existing file appends to it
    juce::WavAudioFormat format;
    if (std::unique_ptr<juce::OutputStream> stream = wav.createOutputStream())
    {
        auto options = juce::AudioFormatWriterOptions{}.withSampleRate (rate)
                                                   .withNumChannels (outs)
                                                   .withBitsPerSample (24);
        if (auto writer = format.createWriterFor (stream, options))
            writer->writeFromAudioSampleBuffer (recording, 0, total);
    }

    std::vector<float> mono (total);
    for (int i = 0; i < total; ++i)
    {
        float x = 0;
        for (int ch = 0; ch < outs; ++ch)
            x += recording.getSample (ch, i);
        mono[(size_t) i] = x;
    }
    struct Seg { const char* name; double beats; };
    const Seg segs[] = {
        { "voicing on (silent in strumming mode)", 2.0 },
        { "down-strum vel 100", 3.0 },
        { "down-strum vel 64", 4.0 },
        { "up-strum vel 100", 5.0 },
        { "up-strum vel 64", 6.0 },
        { "muted via C#0 latch, vel 100", 7.5 },
        { "muted via C6 hold, vel 100", 9.5 },
        { "down-strum vel 127", 11.0 },
        { "scan F#5 (78)", 13.0 },
        { "scan G5 (79)", 14.0 },
        { "scan G#5 (80)", 15.0 },
        { "scan A5 (81)", 16.0 },
        { "scan A#5 (82)", 17.0 },
        { "scan B5 (83)", 18.0 },
        { "scan C6 (84)", 19.0 },
        { "scan C#6 (85)", 20.0 },
    };
    std::cout << "strum test (96 bpm, channel 3, voicing G: E2 B2 G3 B3 D4 G4):\n";
    std::cout << "  segment                            peak    rms   onset\n";
    const int hop = (int) (0.005 * rate);
    for (const auto& s : segs)
    {
        const int a = (int) std::round (s.beats * beat);
        const int b = juce::jmin (total, a + (int) beat);
        float peak = 0;
        double sum = 0;
        for (int i = a; i < b; ++i)
        {
            peak = std::max (peak, std::abs (mono[(size_t) i]));
            sum += (double) mono[(size_t) i] * mono[(size_t) i];
        }
        auto frameDb = [&] (int i0) {  // rms of one 5 ms frame, in dB
            double x = 0;
            for (int j = i0; j < i0 + hop; ++j)
                x += (double) mono[(size_t) j] * mono[(size_t) j];
            return 20.0 * std::log10 (std::sqrt (x / hop) + 1e-9);
        };
        double onsetMs = -1;
        for (int i = a + 2 * hop; i < b - hop; i += hop)
            if (const auto d0 = frameDb (i); d0 > -50.0 && d0 - std::min (frameDb (i - hop), frameDb (i - 2 * hop)) > 6.0)
            {
                onsetMs = (i - a) / rate * 1000.0;
                break;
            }
        std::cout << "  " << juce::String (s.name).paddedRight (' ', 34)
                  << juce::String (peak, 2).paddedLeft (' ', 7)
                  << juce::String (10.0 * std::log10 (sum / (b - a) + 1e-12), 1).paddedLeft (' ', 8)
                  << (onsetMs < 0 ? "  (silent)" : juce::String (onsetMs, 1) + " ms") << "\n";
    }
    std::cout << "wav: " << wav.getFullPathName() << "\n";
}

/** The F#5 strumming-mode script: per the manual (and DAW testing), F#5 held keeps the strumming mode on,
    where chord notes are stored unheard until a strum keyswitch fires them; C6 or C#6 held with a strum
    mutes or chokes it. Verifies each part of that model against the plugin's current state, on channel 3:
    strums with F#5 held (clean, C6, C#6), a chord with F#5 released (plays directly?), and a chord with F#5
    held again (stored, silent?). */
void f5Test (juce::AudioPluginInstance& plugin, const juce::File& wav, bool holdModeKey)
{
    constexpr int channel = 3;
    constexpr double beat = rate * 60.0 / 96.0;
    const int voicing[] = { 40, 47, 55, 59, 62, 67 };  // G: E2 B2 G3 B3 D4 G4
    constexpr int kMode = 78, kUp = 81, kMute = 84, kChoke = 85;  // F#5 A5 C6 C#6
    constexpr double sweep = 0.06 * rate;  // the strum keyswitch's held length, in samples (60 ms)

    struct Event { double sample; int note, velocity; bool off; };
    std::vector<Event> events;
    auto at = [&] (double beats, int note, int velocity = 100, bool off = false)
    { events.push_back ({ std::round (beats * beat), note, velocity, off }); };
    auto strum = [&] (double beats, int key, int muteKey = -1)
    {
        if (muteKey >= 0)
            at (beats - 4.0 / beat, muteKey);  // a few samples before the strum, as the engine does
        at (beats, key, 96);
        at (beats + sweep / beat, key, 96, true);
        if (muteKey >= 0)
            at (beats + (sweep + 0.02 * rate) / beat, muteKey, 100, true);
    };
    if (holdModeKey)
        at (1.5, kMode);
    for (auto n : voicing) at (2.0, n, 96);
    strum (3.0, kUp);                 // clean strum
    strum (4.0, kUp, kMute);          // C6 with the strum (muted?)
    strum (5.0, kUp, kChoke);         // C#6 with the strum (choked?)
    strum (6.0, kUp);                 // clean control
    if (holdModeKey)
    {
        for (auto n : voicing) at (7.0, n, 96, true);
        at (7.0, kMode, 100, true);       // full release: chord and mode key off
        for (auto n : voicing) at (7.5, n, 96);  // F#5 released: the chord plays directly?
        for (auto n : voicing) at (8.5, n, 96, true);
        at (9.0, kMode);
        for (auto n : voicing) at (9.25, n, 96);  // F#5 held again: stored, silent?
        strum (9.75, kUp);
        for (auto n : voicing) at (10.5, n, 96, true);
        at (10.5, kMode, 100, true);
    }
    else
        for (auto n : voicing) at (7.0, n, 96, true);
    const int total = (int) std::round (12.0 * beat);

    const auto outs = juce::jmax (1, plugin.getTotalNumOutputChannels());
    juce::AudioBuffer<float> buffer (juce::jmax (outs, plugin.getTotalNumInputChannels()), block);
    juce::AudioBuffer<float> recording (outs, total);
    plugin.reset();
    const auto began = juce::Time::getHighResolutionTicks();
    for (int start = 0; start < total; start += block)
    {
        const auto n = juce::jmin (block, total - start);
        juce::MidiBuffer evs;
        for (const auto& e : events)
            if (e.sample >= start && e.sample < start + n)
                evs.addEvent (e.off ? juce::MidiMessage::noteOff (channel, e.note)
                                    : juce::MidiMessage::noteOn (channel, e.note, (juce::uint8) e.velocity),
                              (int) (e.sample - start));
        buffer.clear();
        plugin.processBlock (buffer, evs);
        const auto due = (start + n) / rate;
        const auto elapsed = juce::Time::highResolutionTicksToSeconds (juce::Time::getHighResolutionTicks() - began);
        if (due - elapsed > 0.002)
            juce::MessageManager::getInstance()->runDispatchLoopUntil ((int) ((due - elapsed) * 1000.0));
        for (int ch = 0; ch < outs; ++ch)
            recording.copyFrom (ch, start, buffer, ch, 0, n);
    }

    wav.getParentDirectory().createDirectory();
    wav.deleteFile();  // an output stream on an existing file appends to it
    juce::WavAudioFormat format;
    if (std::unique_ptr<juce::OutputStream> stream = wav.createOutputStream())
    {
        auto options = juce::AudioFormatWriterOptions{}.withSampleRate (rate)
                                                   .withNumChannels (outs)
                                                   .withBitsPerSample (24);
        if (auto writer = format.createWriterFor (stream, options))
            writer->writeFromAudioSampleBuffer (recording, 0, total);
    }

    std::vector<float> mono (total);
    for (int i = 0; i < total; ++i)
    {
        float x = 0;
        for (int ch = 0; ch < outs; ++ch)
            x += recording.getSample (ch, i);
        mono[(size_t) i] = x;
    }
    struct Seg { const char* name; double beats; };
    std::vector<Seg> segs = {
        { holdModeKey ? "F#5 held: clean strum (A5)" : "no F#5: clean strum (A5)", 3.0 },
        { holdModeKey ? "F#5 held: C6 + A5 (muted?)" : "no F#5: C6 + A5 (muted?)", 4.0 },
        { holdModeKey ? "F#5 held: C#6 + A5 (choked?)" : "no F#5: C#6 + A5 (choked?)", 5.0 },
        { holdModeKey ? "F#5 held: clean strum (A5)" : "no F#5: clean strum (A5)", 6.0 },
    };
    if (holdModeKey)
    {
        segs.push_back ({ "F#5 released: chord, no strum", 7.5 });
        segs.push_back ({ "F#5 held: chord stored (silent?)", 9.25 });
        segs.push_back ({ "F#5 held: strum after re-hold", 9.75 });
    }
    std::cout << (holdModeKey ? "F#5 test (96 bpm, channel 3, voicing G: E2 B2 G3 B3 D4 G4):\n"
                              : "F#5 test WITHOUT the F#5 mode key (96 bpm, channel 3, voicing G):\n");
    std::cout << "  segment                            peak    rms   onset\n";
    const int hop = (int) (0.005 * rate);
    for (const auto& s : segs)
    {
        const int a = (int) std::round (s.beats * beat);
        const int b = juce::jmin (total, a + (int) beat);
        float peak = 0;
        double sum = 0;
        for (int i = a; i < b; ++i)
        {
            peak = std::max (peak, std::abs (mono[(size_t) i]));
            sum += (double) mono[(size_t) i] * mono[(size_t) i];
        }
        auto frameDb = [&] (int i0) {  // rms of one 5 ms frame, in dB
            double x = 0;
            for (int j = i0; j < i0 + hop; ++j)
                x += (double) mono[(size_t) j] * mono[(size_t) j];
            return 20.0 * std::log10 (std::sqrt (x / hop) + 1e-9);
        };
        double onsetMs = -1;
        for (int i = a + 2 * hop; i < b - hop; i += hop)
            if (const auto d0 = frameDb (i); d0 > -50.0 && d0 - std::min (frameDb (i - hop), frameDb (i - 2 * hop)) > 6.0)
            {
                onsetMs = (i - a) / rate * 1000.0;
                break;
            }
        std::cout << "  " << juce::String (s.name).paddedRight (' ', 34)
                  << juce::String (peak, 2).paddedLeft (' ', 7)
                  << juce::String (10.0 * std::log10 (sum / (b - a) + 1e-12), 1).paddedLeft (' ', 8)
                  << (onsetMs < 0 ? "  (silent)" : juce::String (onsetMs, 1) + " ms") << "\n";
    }
    std::cout << "wav: " << wav.getFullPathName() << "\n";
}

/** Parametrised strum test: F#5 held from --f5, G voicing from --chord, up-strums (A5) at --strum and
    every --gap beats, each key held --key ms after a --lag ms offset, as the engine does it. Also
    reports the level between the chord and the first strum: silence means the chord is stored
    (strumming mode on), a steady level means it rings (mode off). On channel 3. */
void sweepLenTest (juce::AudioPluginInstance& plugin, const juce::File& wav)
{
    constexpr int channel = 3;
    constexpr double beat = rate * 60.0 / 96.0;
    const std::vector<int> voicing = g_voicingList.empty() ? std::vector<int> { 40, 47, 55, 59, 62, 67 } : g_voicingList;  // G: E2 B2 G3 B3 D4 G4
    constexpr int kMode = 78, kUp = 81, kDown = 80, kMute = 84;  // F#5 A5 G#5 C6

    struct Event { double sample; int note, velocity; bool off; };
    std::vector<Event> events;
    auto at = [&] (double beats, int note, int velocity = 100, bool off = false)
    { events.push_back ({ std::round (beats * beat), note, velocity, off }); };
    const double lag = g_lagMs / 1000.0 * rate;
    at (g_f5Beat, kMode);
    for (auto n : voicing) at (g_chordBeat, n, 96);
    if (g_chord2Beat > 0.0)
    {
        for (auto n : voicing) at (g_chord2Beat, n, 96, true);
        for (auto n : g_chord2Notes) at (g_chord2Beat, n, 96);
    }
    for (int i = 0; i < g_numStrokes; ++i)
    {
        const double b = g_atList.size() > (size_t) i ? g_atList[(size_t) i] : g_firstStroke + g_gapBeats * i;
        const double hold = (g_keyList.size() > (size_t) i ? g_keyList[(size_t) i] : g_keyMs) / 1000.0 * rate;
        const int key = g_keysToPress.size() > (size_t) i ? g_keysToPress[(size_t) i]
                      : (g_dirs.size() > (size_t) i && g_dirs[(size_t) i] == 'd') ? kDown : kUp;
        const double muteMs = g_muteList.size() > (size_t) i ? g_muteList[(size_t) i] : 0.0;
        if (muteMs > 0.0)
            at (b + lag / beat - 4.0 / beat, kMute);  // a few samples before the strum, as the engine does
        const int velocity = g_velList.size() > (size_t) i ? g_velList[(size_t) i] : 96;
        at (b + lag / beat, key, velocity);
        at (b + (lag + hold) / beat, key, velocity, true);
        if (muteMs > 0.0)
            at (b + lag / beat + muteMs / 1000.0 * rate / beat, kMute, 100, true);  // C6 off strum + muteMs
    }
    const double lastChord = g_firstStroke + g_gapBeats * (g_numStrokes - 1) + 4.0;
    for (auto n : voicing) at (lastChord, n, 96, true);
    for (auto n : g_chord2Notes) at (lastChord, n, 96, true);
    at (lastChord, kMode, 100, true);
    const int total = (int) std::round (lastChord + 2.0) * (int) beat;

    const auto outs = juce::jmax (1, plugin.getTotalNumOutputChannels());
    juce::AudioBuffer<float> buffer (juce::jmax (outs, plugin.getTotalNumInputChannels()), block);
    juce::AudioBuffer<float> recording (outs, total);
    plugin.reset();
    const auto began = juce::Time::getHighResolutionTicks();
    for (int start = 0; start < total; start += block)
    {
        const auto n = juce::jmin (block, total - start);
        juce::MidiBuffer evs;
        for (const auto& e : events)
            if (e.sample >= start && e.sample < start + n)
                evs.addEvent (e.off ? juce::MidiMessage::noteOff (channel, e.note)
                                    : juce::MidiMessage::noteOn (channel, e.note, (juce::uint8) e.velocity),
                              (int) (e.sample - start));
        buffer.clear();
        plugin.processBlock (buffer, evs);
        const auto due = (start + n) / rate;
        const auto elapsed = juce::Time::highResolutionTicksToSeconds (juce::Time::getHighResolutionTicks() - began);
        if (due - elapsed > 0.002)
            juce::MessageManager::getInstance()->runDispatchLoopUntil ((int) ((due - elapsed) * 1000.0));
        for (int ch = 0; ch < outs; ++ch)
            recording.copyFrom (ch, start, buffer, ch, 0, n);
    }

    wav.getParentDirectory().createDirectory();
    wav.deleteFile();  // an output stream on an existing file appends to it
    juce::WavAudioFormat format;
    if (std::unique_ptr<juce::OutputStream> stream = wav.createOutputStream())
    {
        auto options = juce::AudioFormatWriterOptions{}.withSampleRate (rate)
                                                   .withNumChannels (outs)
                                                   .withBitsPerSample (24);
        if (auto writer = format.createWriterFor (stream, options))
            writer->writeFromAudioSampleBuffer (recording, 0, total);
    }

    std::vector<float> mono (total);
    for (int i = 0; i < total; ++i)
    {
        float x = 0;
        for (int ch = 0; ch < outs; ++ch)
            x += recording.getSample (ch, i);
        mono[(size_t) i] = x;
    }
    // the level between the chord and the first strum: stored chord = silence, ringing chord = steady level
    {
        float peak = 0;
        const int a = (int) ((g_chordBeat + 0.2) * beat), b = (int) ((g_firstStroke - 0.1) * beat);
        for (int j = juce::jmax (0, a); j < juce::jmin (total, b); ++j)
            peak = std::max (peak, std::abs (mono[(size_t) j]));
        std::cout << "level before first strum: "
                  << (peak < 1e-4 ? "SILENT (chord stored, mode on)" : juce::String (peak, 3) + " (chord RINGING, mode off)") << "\n";
    }
    std::cout << "sweep test (96 bpm, ch 3, G voicing, F#5 at " << g_f5Beat << ", chord at " << g_chordBeat
              << ", strokes from " << g_firstStroke << " every " << g_gapBeats << " beats, lag " << g_lagMs
              << " ms, key " << g_keyMs << " ms, n=" << g_numStrokes << "):\n";
    std::cout << "  stroke   vel     peak    rms   to peak\n";
    for (int i = 0; i < g_numStrokes; ++i)
    {
        const int a = (int) std::round ((g_firstStroke + g_gapBeats * i) * beat);
        const int b = juce::jmin (total, a + (int) (g_gapBeats * beat));
        float peak = 0;
        int peakAt = a;
        double sum = 0;
        for (int j = a; j < b; ++j)
        {
            if (std::abs (mono[(size_t) j]) > peak)
            {
                peak = std::abs (mono[(size_t) j]);
                peakAt = j;
            }
            sum += (double) mono[(size_t) j] * mono[(size_t) j];
        }
        const int velocity = g_velList.size() > (size_t) i ? g_velList[(size_t) i] : 96;
        std::cout << "  " << juce::String (g_firstStroke + g_gapBeats * i).paddedRight (' ', 6)
                  << juce::String (velocity).paddedLeft (' ', 5)
                  << juce::String (peak, 2).paddedLeft (' ', 8)
                  << juce::String (10.0 * std::log10 (sum / (b - a) + 1e-12), 1).paddedLeft (' ', 8)
                  << (juce::String ((peakAt - a) / rate * 1000.0 - g_lagMs, 0) + " ms").paddedLeft (' ', 10) << "\n";
    }
    std::cout << "wav: " << wav.getFullPathName() << "\n";
}

int main (int argc, char* argv[])
{
    juce::ScopedJuceInitialiser_GUI juce;  // plugins expect a message thread

    if (argc < 2)
    {
        std::cout << "usage: plugin_probe <plugin.vst3> [out-folder] [program-name-filter ... | --state <file> ...] [--map [--velocity n] [--channel n]] [--strum-test] [--f5-test] [--sweep-test]\n";
        return 1;
    }
    const juce::File pluginFile { juce::String (argv[1]) };
    const juce::File outFolder (argc > 2 ? juce::String (argv[2]) : juce::File::getCurrentWorkingDirectory().getFullPathName());
    juce::StringArray filters;
    juce::Array<juce::File> states;
    bool map = false, loadTime = false, strumMode = false, f5Mode = false, noF5 = false, sweepMode = false;
    int velocity = 110;
    for (int i = 3; i < argc; ++i)
    {
        if (juce::String (argv[i]) == "--state" && i + 1 < argc)
            states.add (juce::File (juce::String (argv[++i])));
        else if (juce::String (argv[i]) == "--map")
            map = true;
        else if (juce::String (argv[i]) == "--load-time")
            loadTime = true;
        else if (juce::String (argv[i]) == "--strum-test")
            strumMode = true;
        else if (juce::String (argv[i]) == "--f5-test")
            f5Mode = true;
        else if (juce::String (argv[i]) == "--no-f5")
            noF5 = true;
        else if (juce::String (argv[i]) == "--sweep-test")
            sweepMode = true;
        else if (juce::String (argv[i]) == "--lag" && i + 1 < argc)
            g_lagMs = juce::String (argv[++i]).getDoubleValue();
        else if (juce::String (argv[i]) == "--key" && i + 1 < argc)
            g_keyMs = juce::String (argv[++i]).getDoubleValue();
        else if (juce::String (argv[i]) == "--keys" && i + 1 < argc)
            for (const auto& s : juce::StringArray::fromTokens (juce::String (argv[++i]), ",", ""))
                g_keyList.push_back (s.getDoubleValue());
        else if (juce::String (argv[i]) == "--voicing" && i + 1 < argc)
            for (const auto& s : juce::StringArray::fromTokens (juce::String (argv[++i]), ",", ""))
                g_voicingList.push_back (s.getIntValue());
        else if (juce::String (argv[i]) == "--at" && i + 1 < argc)
            for (const auto& s : juce::StringArray::fromTokens (juce::String (argv[++i]), ",", ""))
                g_atList.push_back (s.getDoubleValue());
        else if (juce::String (argv[i]) == "--press" && i + 1 < argc)
            for (const auto& s : juce::StringArray::fromTokens (juce::String (argv[++i]), ",", ""))
                g_keysToPress.push_back (s.getIntValue());
        else if (juce::String (argv[i]) == "--vels" && i + 1 < argc)
            for (const auto& s : juce::StringArray::fromTokens (juce::String (argv[++i]), ",", ""))
                g_velList.push_back (s.getIntValue());
        else if (juce::String (argv[i]) == "--dirs" && i + 1 < argc)
            g_dirs = argv[++i];
        else if (juce::String (argv[i]) == "--mutes" && i + 1 < argc)
            for (const auto& s : juce::StringArray::fromTokens (juce::String (argv[++i]), ",", ""))
                g_muteList.push_back (s.getDoubleValue());
        else if (juce::String (argv[i]) == "--f5" && i + 1 < argc)
            g_f5Beat = juce::String (argv[++i]).getDoubleValue();
        else if (juce::String (argv[i]) == "--chord" && i + 1 < argc)
            g_chordBeat = juce::String (argv[++i]).getDoubleValue();
        else if (juce::String (argv[i]) == "--strum" && i + 1 < argc)
            g_firstStroke = juce::String (argv[++i]).getDoubleValue();
        else if (juce::String (argv[i]) == "--gap" && i + 1 < argc)
            g_gapBeats = juce::String (argv[++i]).getDoubleValue();
        else if (juce::String (argv[i]) == "--n" && i + 1 < argc)
            g_numStrokes = juce::String (argv[++i]).getIntValue();
        else if (juce::String (argv[i]) == "--chord2" && i + 2 < argc)
        {
            g_chord2Beat = juce::String (argv[++i]).getDoubleValue();
            for (const auto& s : juce::StringArray::fromTokens (juce::String (argv[++i]), ",", ""))
                g_chord2Notes.push_back (s.getIntValue());
        }
        else if (juce::String (argv[i]) == "--channel" && i + 1 < argc)
        {
            const auto n = juce::String (argv[++i]).getIntValue();  // 0: all sixteen
            mapChannels.clear();
            for (int c = 1; c <= 16; ++c)
                if (n == 0 || n == c)
                    mapChannels.push_back (c);
        }
        else if (juce::String (argv[i]) == "--velocity" && i + 1 < argc)
            velocity = juce::jlimit (1, 127, juce::String (argv[++i]).getIntValue());
        else
            filters.add (argv[i]);
    }
    if ((strumMode || f5Mode || sweepMode) && states.isEmpty())
    {
        std::cout << "--strum-test/--f5-test/--sweep-test needs --state <file>\n";
        return 1;
    }

    juce::AudioPluginFormatManager formats;
    juce::addDefaultFormatsToManager (formats);
    juce::OwnedArray<juce::PluginDescription> types;
    for (auto* format : formats.getFormats())
        if (format->fileMightContainThisPluginType (pluginFile.getFullPathName()))
            format->findAllTypesForFile (types, pluginFile.getFullPathName());
    if (types.isEmpty())
    {
        std::cout << "No plugin found in " << pluginFile.getFullPathName() << "\n";
        return 1;
    }

    const auto& d = *types[0];
    std::cout << "Plugin: " << d.name << " " << d.version << " by " << d.manufacturerName << " (" << d.pluginFormatName
              << ", " << (d.isInstrument ? "instrument" : "effect") << ", category " << d.category << ")\n";

    juce::String error;
    auto plugin = formats.createPluginInstance (d, rate, block, error);
    if (plugin == nullptr)
    {
        std::cout << "Could not load: " << error << "\n";
        return 1;
    }
    plugin->enableAllBuses();
    plugin->setRateAndBufferSizeDetails (rate, block);
    plugin->prepareToPlay (rate, block);
    // some plugins only finish setting up once their editor exists: make one (never shown)
    std::unique_ptr<juce::AudioProcessorEditor> editor (plugin->hasEditor() ? plugin->createEditorAndMakeActive() : nullptr);
    std::cout << "Editor " << (editor != nullptr ? "created" : "none") << "; waiting 5 s for the plugin to load its sounds...\n";
    juce::MessageManager::getInstance()->runDispatchLoopUntil (5000);

    // what the plugin keeps in its state (how a host would select its sounds)
    juce::MemoryBlock state;
    plugin->getStateInformation (state);
    juce::String printable;
    for (size_t i = 0; i < juce::jmin<size_t> (state.getSize(), 1500); ++i)
    {
        const auto c = (char) state[i];
        printable << ((c >= 32 && c < 127) ? juce::String::charToString (c) : juce::String ("."));
    }
    std::cout << "State: " << (int) state.getSize() << " bytes, starting: " << printable.substring (0, 120) << "\n";
    // a VST3 state wraps the plugin's own data, encoded, in an <IComponent> element
    if (auto xml = juce::parseXML (printable.fromFirstOccurrenceOf ("<?xml", true, false)
                                   + juce::String::createStringFromData ((const char*) state.getData() + juce::jmin<size_t> (state.getSize(), 1500),
                                                                         (int) state.getSize() - juce::jmin<int> ((int) state.getSize(), 1500))))
        if (auto* component = xml->getChildByName ("IComponent"))
        {
            juce::MemoryBlock inner;
            if (inner.fromBase64Encoding (component->getAllSubText().trim()))
            {
                juce::String text;
                for (size_t i = 0; i < inner.getSize(); ++i)
                {
                    const auto c = (char) inner[i];
                    text << ((c >= 32 && c < 127) ? juce::String::charToString (c) : juce::String ("."));
                }
                // skip the long run of MIDI-mapping slots at the start, if any
                const auto body = text.contains ("<MASTER_TREE") ? text.fromFirstOccurrenceOf ("<MASTER_TREE", true, false) : text;
                std::cout << "Plugin's own state (" << (int) inner.getSize() << " bytes): " << body.substring (0, 4000) << "\n";
            }
        }

    std::cout << "Inputs " << plugin->getTotalNumInputChannels() << ", outputs " << plugin->getTotalNumOutputChannels()
              << ", accepts MIDI " << (plugin->acceptsMidi() ? "yes" : "no") << ", latency " << plugin->getLatencySamples()
              << " samples, parameters " << plugin->getParameters().size() << ", programs " << plugin->getNumPrograms() << "\n";

    // parameters that look like sound selection
    const auto& parameters = plugin->getParameters();
    for (int i = 0; i < parameters.size(); ++i)
    {
        const auto name = parameters[i]->getName (64);
        if (i < 12 || name.containsIgnoreCase ("sound") || name.containsIgnoreCase ("instr")
            || name.containsIgnoreCase ("preset") || name.containsIgnoreCase ("program") || name.containsIgnoreCase ("library")
            || name.containsIgnoreCase ("layer") || name.containsIgnoreCase ("select"))
            std::cout << "  parameter " << i << ": " << name << " = " << parameters[i]->getCurrentValueAsText() << "\n";
    }

    juce::Array<int> chosen;
    for (int i = 0; i < plugin->getNumPrograms(); ++i)
    {
        const auto name = plugin->getProgramName (i);
        std::cout << "  program " << i << ": " << name << "\n";
        bool match = filters.isEmpty();
        for (const auto& f : filters)
            match = match || name.containsIgnoreCase (f);
        if (match && name.isNotEmpty())
            chosen.add (i);
    }
    if (plugin->getNumPrograms() <= 1)
        chosen = { -1 };  // no program list: play whatever is loaded

    auto report = [&] (const juce::String& name)
    {
        const auto file = outFolder.getChildFile (juce::File::createLegalFileName (d.name + " - " + name) + ".wav");
        const auto take = render (*plugin, file);
        std::cout << "Take '" << name << "': audio CPU mean " << juce::String (100.0 * take.meanLoad, 1) << " %, peak "
                  << juce::String (100.0 * take.peakLoad, 1) << " % of a " << block << "-sample block; level peak "
                  << juce::String (take.peak, 2) << ", RMS " << juce::String (take.rms, 3) << " -> "
                  << file.getFullPathName() << "\n";
    };

    // the notes that sound, and every parameter with the sound loaded, to a file beside the takes
    auto mapSound = [&] (const juce::String& name)
    {
        std::cout << "Notes of '" << name << "' at velocity " << velocity << ":\n";
        mapNotes (*plugin, velocity);
        juce::String all;
        const auto& params = plugin->getParameters();
        for (int i = 0; i < params.size(); ++i)
            all << i << "\t" << params[i]->getName (64) << "\t" << params[i]->getCurrentValueAsText() << "\t" << params[i]->getValue() << "\n";
        outFolder.getChildFile (juce::File::createLegalFileName (name) + ".params.txt").replaceWithText (all);
    };

    // how long a sound takes to load: another sound first, then this one, then a note after a wait
    auto setState = [&plugin] (const juce::File& stateFile)
    {
        loadState (*plugin, stateFile);
    };
    if (loadTime && states.size() >= 2)
    {
        for (int i = 0; i < states.size(); ++i)
        {
            std::cout << "Loading '" << states[i].getFileNameWithoutExtension() << "' (after '"
                      << states[(i + 1) % states.size()].getFileNameWithoutExtension() << "'):";
            for (int delay : { 100, 250, 500, 1000, 2000, 3000 })
            {
                setState (states[(i + 1) % states.size()]);
                juce::MessageManager::getInstance()->runDispatchLoopUntil (4000);
                setState (states[i]);
                juce::MessageManager::getInstance()->runDispatchLoopUntil (delay);
                std::cout << "  " << delay << " ms: " << juce::String (noteLevel (*plugin), 1) << " dB" << std::flush;
            }
            std::cout << "\n";
        }
        editor.reset();
        plugin->releaseResources();
        return 0;
    }

    if (! states.isEmpty())
    {
        // one take per state file: the plugin's own state, as a JUCE-built plugin's standalone app saves it
        for (const auto& stateFile : states)
        {
            if (! loadState (*plugin, stateFile))
            {
                std::cout << "Cannot read " << stateFile.getFullPathName() << "\n";
                continue;
            }
            settle (*plugin, juce::SystemStats::getEnvironmentVariable ("PROBE_WAIT_MS", "12000").getIntValue());  // the sound loads in the background, while the audio runs
            {
                // what the plugin holds now: a state with an instrument loaded is far bigger than an empty one
                juce::MemoryBlock after;
                plugin->getStateInformation (after);
                juce::String text;
                for (size_t i = 0; i < after.getSize(); ++i)
                {
                    const auto c = (char) after[i];
                    text << ((c >= 32 && c < 127) ? juce::String::charToString (c) : juce::String ("."));
                }
                std::cout << "State after loading: " << (int) after.getSize() << " bytes"
                          << (text.containsIgnoreCase ("shreddage") ? ", mentions Shreddage" : "") << std::endl;
            }
            if (strumMode)
                strumTest (*plugin, outFolder.getChildFile (stateFile.getFileNameWithoutExtension() + ".wav"));
            else if (sweepMode)
                sweepLenTest (*plugin, outFolder.getChildFile (stateFile.getFileNameWithoutExtension() + ".sweep.wav"));
            else if (f5Mode)
                f5Test (*plugin, outFolder.getChildFile (stateFile.getFileNameWithoutExtension() + (noF5 ? ".nof5.wav" : ".wav")), ! noF5);
            else if (map)
                mapSound (stateFile.getFileNameWithoutExtension());
            else
                report (stateFile.getFileNameWithoutExtension());
        }
    }
    else
    {
        for (auto program : chosen)
        {
            juce::String name = "current sound";
            if (program >= 0)
            {
                plugin->setCurrentProgram (program);
                name = plugin->getProgramName (program);
                // give a program change time to load samples (message-thread work in many plugins)
                juce::MessageManager::getInstance()->runDispatchLoopUntil (1500);
            }
            if (map)
                mapSound (name);
            else
                report (name);
        }
    }

    editor.reset();  // the editor goes before its plugin
    plugin->releaseResources();
    return 0;
}
