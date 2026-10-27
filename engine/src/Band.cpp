#include "Band.h"

#include <algorithm>
#include <cmath>
#include <limits>

namespace gt
{

namespace
{
    const double none = std::numeric_limits<double>::quiet_NaN();

    // Shreddage 3's strumming keyswitches (Stratus FREE manual, the strumming keyswitch map): F#5 held keeps
    // the strumming mode on, where chord notes are stored unheard until a strum keyswitch fires them; C6
    // held with a strum makes it a muted strum (C#6 would choke it). G#5 down-strums, A5 up-strums
    // Measured with plugin_probe: the plugin rings the strummed chord at full level for as long as the
    // strum keyswitch is held and damps it (a fast release) when the key is let go, so the key's held
    // length is the chord's ring length - a DAW strum track does exactly this. The key must be held at
    // least ~40 ms for a full sweep of all six strings; shorter holds are a partial or silent sweep, and
    // a zero-length key (noteOff on the same sample) is a degenerate case that plucks only one string on
    // an up-strum. A chord change (the held voicing swapped) damps the active strum early, as a hand
    // moving to the new chord would. Muted strokes hold the Mute keyswitch C6 around the strum, which
    // makes the chord a tight chug with its own short decay
    constexpr int strumModeKey = 78, downKey = 80, upKey = 81, muteKey = 84;
    constexpr double strokeHoldMinSec = 0.080;  // the shortest key hold: below ~40 ms the sweep is partial
    constexpr double strokeHoldMaxSec = 1.2;    // the longest: a held key sustains the chord at full level,
                                                // so a longer hold would ring it like a synth
    constexpr double strokeReleaseLeadSec = 0.1; // let the key go this much before the next stroke, so the
                                                // chord has started to damp when the hand comes back
    constexpr double strokeLagSec = 0.015; // a stroke lands this much after its beat: a hand takes a moment
                                           // to turn and to reach a new chord
    constexpr double muteTailSec = 0.040;   // the Mute keyswitch is let go this much after the strum (it is
                                             // pressed a few samples before, so a chug of ~44 ms)
}

//==============================================================================
double Score::bpmAt (double q) const
{
    double bpm = tempos.empty() ? 120.0 : tempos.front().bpm;
    for (const auto& t : tempos)
    {
        if (t.at > q)
            break;
        bpm = t.bpm;
    }
    return bpm;
}

double Score::beatAt (double q) const
{
    int denominator = 4;
    for (const auto& m : meters)
    {
        if (m.at > q)
            break;
        denominator = m.denominator;
    }
    return 4.0 / juce::jmax (1, denominator);
}

std::unique_ptr<Score> Score::fromMessage (const juce::var& message, juce::String& error)
{
    auto score = std::make_unique<Score>();
    score->title = message["title"].toString();
    score->style = message["style"].toString();
    score->keys = message["keys"].toString();

    if (auto* tempos = message["tempos"].getArray())
        for (const auto& t : *tempos)
            if (t.size() >= 2 && (double) t[1] > 0.0)
                score->tempos.push_back ({ (double) t[0], (double) t[1] });
    if (score->tempos.empty())
        score->tempos.push_back ({ 0.0, 120.0 });

    // meter changes are given at bars: find where those bars start
    if (auto* meters = message["meters"].getArray())
    {
        int bar = 0, numerator = 4, denominator = 4;
        double at = 0.0;
        for (const auto& m : *meters)
        {
            if (m.size() < 3)
                continue;
            const int toBar = (int) m[0];
            at += (toBar - bar) * numerator * 4.0 / denominator;
            bar = toBar;
            numerator = juce::jmax (1, (int) m[1]);
            denominator = juce::jmax (1, (int) m[2]);
            score->meters.push_back ({ at, numerator, denominator });
        }
    }
    if (score->meters.empty())
        score->meters.push_back ({ 0.0, 4, 4 });

    auto* notes = message["notes"].getArray();
    if (notes == nullptr)
    {
        error = "no notes in the load message";
        return nullptr;
    }
    score->notes.reserve ((size_t) notes->size());
    for (const auto& n : *notes)
    {
        if (n.size() < 5)
            continue;
        BandNote note;
        note.part = (int) n[0];
        note.pitch = (int) n[1];
        note.at = (double) n[2];
        note.length = (double) n[3];
        note.velocity = juce::jlimit (0.0f, 1.0f, (float) (int) n[4] / 127.0f);
        if (note.part >= 0 && note.part <= 3 && note.pitch >= 0 && note.pitch < 128)
            score->notes.push_back (note);
    }
    std::stable_sort (score->notes.begin(), score->notes.end(), [] (const BandNote& a, const BandNote& b) { return a.at < b.at; });

    if (auto* voicings = message["voicings"].getArray())
        for (const auto& v : *voicings)
            if (v.size() >= 3 && (int) v[0] >= 0 && (int) v[0] < 128)
                score->voicings.push_back ({ (int) v[0], (double) v[1], (double) v[2] });
    std::stable_sort (score->voicings.begin(), score->voicings.end(),
                      [] (const Voicing& a, const Voicing& b) { return a.at < b.at; });
    if (auto* strokes = message["strokes"].getArray())
        for (const auto& s : *strokes)
            if (s.size() >= 4)
                score->strokes.push_back ({ (double) s[0], (int) s[1] == 1 ? 1 : 0,
                                            juce::jlimit (0, 127, (int) s[2]), (int) s[3] != 0 });
    std::stable_sort (score->strokes.begin(), score->strokes.end(),
                      [] (const Stroke& a, const Stroke& b) { return a.at < b.at; });
    return score;
}

//==============================================================================
BandProcessor::BandProcessor (SharedClock& sharedClock)
    : SimpleProcessor (BusesProperties().withOutput ("Drums", juce::AudioChannelSet::stereo(), true)
                                        .withOutput ("Bass", juce::AudioChannelSet::mono(), true)
                                        .withOutput ("Guitar", juce::AudioChannelSet::mono(), true)),
      clock (sharedClock)
{
    countInUntil = none;
}

void BandProcessor::prepareToPlay (double newSampleRate, int)
{
    sampleRate = newSampleRate;
    kit.prepare (sampleRate);
    bassVoice.prepare (sampleRate);
    guitarVoice.prepare (sampleRate);
}

void BandProcessor::push (Command command)
{
    const auto scope = fifo.write (1);
    if (scope.blockSize1 > 0)
        commands[(size_t) scope.startIndex1] = command;
    else if (scope.blockSize2 > 0)
        commands[(size_t) scope.startIndex2] = command;
}

void BandProcessor::load (std::unique_ptr<Score> score)
{
    score->guitar.clear();
    for (size_t i = 0; i < score->notes.size(); ++i)
        if (score->notes[i].part == guitar)
            score->guitar.push_back (i);
    owned.push_back (std::move (score));
    latest = owned.back().get();
    pending.store (latest);
    collectGarbage();
}

void BandProcessor::collectGarbage()
{
    const auto* waiting = pending.load();
    const auto* used = inUse.load();
    owned.erase (std::remove_if (owned.begin(), owned.end(), [&] (const std::unique_ptr<Score>& s)
                                 { return s.get() != latest && s.get() != waiting && s.get() != used; }),
                 owned.end());
}

void BandProcessor::play (double from, double countIn) { push ({ Command::play, from, juce::jmax (0.0, countIn) }); }
void BandProcessor::pause() { push ({ Command::pause }); }
void BandProcessor::seek (double to) { push ({ Command::seek, to }); }
void BandProcessor::setSpeed (double value) { push ({ Command::speed, juce::jlimit (0.25, 2.0, value) }); }

size_t BandProcessor::firstNoteFrom (double q) const
{
    if (playingScore == nullptr)
        return 0;
    const auto& notes = playingScore->notes;
    return (size_t) (std::lower_bound (notes.begin(), notes.end(), q - 1.0e-9,
                                       [] (const BandNote& n, double at) { return n.at < at; })
                     - notes.begin());
}

size_t BandProcessor::firstGuitarNoteFrom (double q) const
{
    if (playingScore == nullptr)
        return 0;
    const auto& notes = playingScore->notes;
    return (size_t) (std::lower_bound (playingScore->guitar.begin(), playingScore->guitar.end(), q - 1.0e-9,
                                       [&notes] (size_t i, double at) { return notes[i].at < at; })
                     - playingScore->guitar.begin());
}

size_t BandProcessor::firstVoicingFrom (double q) const
{
    if (playingScore == nullptr)
        return 0;
    const auto& v = playingScore->voicings;
    return (size_t) (std::lower_bound (v.begin(), v.end(), q - 1.0e-9,
                                       [] (const Score::Voicing& x, double at) { return x.at < at; })
                     - v.begin());
}

size_t BandProcessor::firstStrokeFrom (double q) const
{
    if (playingScore == nullptr)
        return 0;
    const auto& s = playingScore->strokes;
    return (size_t) (std::lower_bound (s.begin(), s.end(), q - 1.0e-9,
                                       [] (const Score::Stroke& x, double at) { return x.at < at; })
                     - s.begin());
}

void BandProcessor::addOff (Off off)
{
    if (numOffs < (int) offs.size())
        offs[(size_t) numOffs++] = off;
}

void BandProcessor::releaseAll (juce::MidiBuffer& midi)
{
    for (int i = 0; i < numOffs; ++i)
        midi.addEvent (juce::MidiMessage::noteOff (channelOf (offs[(size_t) i].part), offs[(size_t) i].pitch), 0);
    for (auto part : { keys, bass, guitar, drums })
        midi.addEvent (juce::MidiMessage::allNotesOff (channelOf (part)), 0);
    numOffs = 0;
    strumModeOn = false;  // allNotesOff let go of the strumming-mode keyswitch
    bassVoice.noteOff (0);
    guitarVoice.releaseAll();
}

void BandProcessor::apply (const Command& c, juce::MidiBuffer& midi)
{
    switch (c.type)
    {
        case Command::play:
            releaseAll (midi);
            quarter = c.a - c.b;
            countInLength = c.b;
            countInUntil = c.b > 0.0 ? c.a : none;
            cursor = firstNoteFrom (c.a);
            guitarCursor = firstGuitarNoteFrom (c.a);
            voicingCursor = firstVoicingFrom (c.a);
            strokeCursor = firstStrokeFrom (c.a);
            playing = true;
            break;
        case Command::pause:
            if (playing)
                releaseAll (midi);
            playing = false;
            countInUntil = none;
            break;
        case Command::seek:
            releaseAll (midi);
            quarter = c.a;
            countInUntil = none;
            cursor = firstNoteFrom (c.a);
            guitarCursor = firstGuitarNoteFrom (c.a);
            voicingCursor = firstVoicingFrom (c.a);
            strokeCursor = firstStrokeFrom (c.a);
            break;
        case Command::speed:
            speed = c.a;
            break;
    }
}

/** A note onto its instrument, at a sample of the block: the plugin as MIDI (its end noted for later), or the built-in
    voice. <paramref name="time"/> is the note's own time (its end and release go from there). */
void BandProcessor::emit (const BandNote& note, double time, int at, juce::MidiBuffer& midi)
{
    if (note.part == keys || onPlugin[(size_t) note.part].load())
    {
        // a drum hit is short; a bass note lets go just before the next, as a player's would
        const auto length = note.part == drums ? juce::jmax (0.1, note.length) : note.part == bass ? note.length * 0.95 : note.length;
        // bass plugins sound an octave below the note they get, as bass parts are written (MONSTER Bass
        // and Ample Bass: note 40 is the low E, measured with plugin_probe --map)
        const auto pitch = note.part == bass ? note.pitch + 12 : note.pitch;
        midi.addEvent (juce::MidiMessage::noteOn (channelOf (note.part), pitch, note.velocity), at);
        addOff ({ note.part, pitch, -1, time + length });
    }
    else if (note.part == drums)
        kit.trigger (note.pitch, note.velocity, at);
    else if (note.part == bass)
    {
        bassVoice.noteOn (note.pitch, note.velocity, at);
        addOff ({ bass, note.pitch, ++bassSerial, time + note.length * 0.95 });
    }
    else if (note.part == guitar)
    {
        guitarVoice.noteOn (note.pitch, note.velocity, at);
        addOff ({ guitar, note.pitch, 0, time + note.length });
    }
}

void BandProcessor::processBlock (juce::AudioBuffer<float>& buffer, juce::MidiBuffer& midi)
{
    const auto n = buffer.getNumSamples();
    const auto started = SharedClock::now();
    buffer.clear();
    midi.clear();

    if (const auto* fresh = pending.exchange (nullptr))
    {
        if (playing)
            releaseAll (midi);
        playingScore = fresh;
        inUse.store (fresh);
        playing = false;
        countInUntil = none;
        cursor = firstNoteFrom (quarter);
        guitarCursor = firstGuitarNoteFrom (quarter);
        voicingCursor = firstVoicingFrom (quarter);
        strokeCursor = firstStrokeFrom (quarter);
    }
    {
        const auto scope = fifo.read (fifo.getNumReady());
        for (int i = 0; i < scope.blockSize1; ++i)
            apply (commands[(size_t) (scope.startIndex1 + i)], midi);
        for (int i = 0; i < scope.blockSize2; ++i)
            apply (commands[(size_t) (scope.startIndex2 + i)], midi);
    }

    const auto* s = playingScore;
    const auto blockStart = quarter;
    double rate = 0.0;  // quarter notes per second
    if (playing && s != nullptr)
    {
        rate = s->bpmAt (juce::jmax (0.0, quarter)) / 60.0 * speed;
        const auto q0 = quarter, q1 = q0 + n / sampleRate * rate;
        auto offsetOf = [&] (double q) { return juce::jlimit (0, n - 1, (int) std::floor ((q - q0) / rate * sampleRate)); };

        // the count-in: sticks on every beat, the first one louder
        if (! std::isnan (countInUntil))
        {
            const auto beat = s->beatAt (juce::jmax (0.0, countInUntil));
            const auto from = countInUntil - countInLength;
            for (int k = 0; from + k * beat < countInUntil - 1.0e-9; ++k)
                if (const auto b = from + k * beat; b >= q0 - 1.0e-9 && b < q1)
                    kit.trigger (drum::sticks, k == 0 ? 0.95f : 0.8f, offsetOf (b));
        }

        // notes ending in this block, before the ones starting (a chord re-struck lets go first)
        for (int i = 0; i < numOffs;)
        {
            const auto& off = offs[(size_t) i];
            if (off.at < q1)
            {
                if (off.serial < 0)  // played on a plugin
                    midi.addEvent (juce::MidiMessage::noteOff (channelOf (off.part), off.pitch), offsetOf (off.at));
                else if (off.part == bass && off.serial == bassSerial)
                    bassVoice.noteOff (offsetOf (off.at));
                else if (off.part == guitar)
                    guitarVoice.noteOff (off.pitch, offsetOf (off.at));
                offs[(size_t) i] = offs[(size_t) --numOffs];
            }
            else
                ++i;
        }

        const auto countingIn = ! std::isnan (countInUntil);
        while (cursor < s->notes.size() && s->notes[cursor].at < q1)
        {
            const auto& note = s->notes[cursor++];
            if (note.part == guitar)  // the guitar has its own cursor, below
                continue;
            if (note.at < q0 - 1.0e-9 || (countingIn && note.at < countInUntil - 1.0e-9))
                continue;
            emit (note, note.at, offsetOf (note.at), midi);
        }

        // the guitar, early by its plugin's lag: its notes go out that much before their time
        const auto early = onPlugin[(size_t) guitar].load() ? guitarAdvance.load() * rate : 0.0;
        // strumming mode only for a song that has strokes: one from a game that does not send them (or a song without a
        // strumming pattern) keeps the per-string notes, which the strumming mode would silence
        const auto strums = strummingMode.load() && onPlugin[(size_t) guitar].load() && ! s->strokes.empty();
        if (guitarResync.exchange (false))
        {
            // the guitar's sound changed (a plugin was swapped in or out, or strumming mode turned): drop
            // what it holds and start its three lists from where the song is now
            midi.addEvent (juce::MidiMessage::allNotesOff (channelOf (guitar)), 0);
            for (int i = 0; i < numOffs;)
                if (offs[(size_t) i].part == guitar)
                    offs[(size_t) i] = offs[(size_t) --numOffs];
                else
                    ++i;
            guitarVoice.releaseAll();
            strumModeOn = false;
            guitarCursor = firstGuitarNoteFrom (quarter);
            voicingCursor = firstVoicingFrom (quarter);
            strokeCursor = firstStrokeFrom (quarter);
        }
        // the per-string notes, for the built-in guitar and the non-strum plugins; in strumming mode the
        // plugin hears the held voicing and the keyswitches instead (the per-string notes are silent in it)
        while (guitarCursor < s->guitar.size() && s->notes[s->guitar[guitarCursor]].at - early < q1)
        {
            const auto& note = s->notes[s->guitar[guitarCursor++]];
            if (note.at < q0 - 1.0e-9 || (countingIn && note.at < countInUntil - 1.0e-9) || ! guitarPlays.load())
                continue;
            if (strums)
                continue;
            emit (note, note.at, offsetOf (note.at - early), midi);
        }
        if (! strums && strumModeOn)
        {
            midi.addEvent (juce::MidiMessage::noteOff (channelOf (guitar), strumModeKey), 0);  // the plugin plays notes again
            strumModeOn = false;
        }
        if (strums)
        {
            // the strumming mode, held on with F#5: the plugin stores the chord notes unheard until a strum
            // keyswitch fires them (releaseAll's allNotesOff lets go of it when the guitar stops or changes)
            if (! strumModeOn)
            {
                midi.addEvent (juce::MidiMessage::noteOn (channelOf (guitar), strumModeKey, (juce::uint8) 100), 0);
                strumModeOn = true;
            }
            // the held voicing, a hair before its time: the plugin must know the chord before a strum at the
            // same moment (the cursors walk on while the guitarist is off, to catch up with him)
            while (voicingCursor < s->voicings.size() && s->voicings[voicingCursor].at - early < q1)
            {
                const auto& v = s->voicings[voicingCursor++];
                if (v.at < q0 - 1.0e-9 || (countingIn && v.at < countInUntil - 1.0e-9) || ! guitarPlays.load())
                    continue;
                const auto at = juce::jmax (0, offsetOf (v.at - early) - 4);
                midi.addEvent (juce::MidiMessage::noteOn (channelOf (guitar), v.pitch, (juce::uint8) 96), at);
                addOff ({ guitar, v.pitch, -1, v.at + v.length });
            }
            // the strokes: a strum keyswitch (down G#5, up A5) a hair after each beat, as a hand takes a
            // moment to turn and to reach a new chord. The key is held until the next stroke (or the cap):
            // the plugin rings the chord while the key is down and damps it on release, so the hold is the
            // chord's ring. A muted stroke adds the Mute keyswitch C6 around the strum (a fast chug). A
            // keyswitch's noteOff must land in the buffer after its noteOn; the offs list fires before this
            // loop, so an off ending in this block is added here, right after its on
            while (strokeCursor < s->strokes.size() && s->strokes[strokeCursor].at - early < q1)
            {
                const auto& st = s->strokes[strokeCursor++];
                if (st.at < q0 - 1.0e-9 || (countingIn && st.at < countInUntil - 1.0e-9) || ! guitarPlays.load())
                    continue;
                const double atSec = st.at - early + strokeLagSec * rate;
                const auto at = offsetOf (atSec);
                double hold = strokeHoldMaxSec;
                if (strokeCursor < s->strokes.size())
                {
                    const double gap = (s->strokes[strokeCursor].at - st.at) / rate;  // seconds to the next stroke
                    const double lead = juce::jmin (strokeReleaseLeadSec, gap * 0.5);
                    hold = juce::jmin (gap, juce::jlimit (strokeHoldMinSec, strokeHoldMaxSec, gap - lead));
                }
                const double keyEnd = atSec + hold * rate;
                const double muteEnd = atSec + muteTailSec * rate;
                if (st.muted)
                    midi.addEvent (juce::MidiMessage::noteOn (channelOf (guitar), muteKey, (juce::uint8) 100),
                                   juce::jmax (0, at - 4));  // the Mute keyswitch a few samples before the strum
                const auto key = st.dir == 1 ? upKey : downKey;
                midi.addEvent (juce::MidiMessage::noteOn (channelOf (guitar), key, (juce::uint8) st.velocity), at);
                if (keyEnd < q1)
                    midi.addEvent (juce::MidiMessage::noteOff (channelOf (guitar), key), offsetOf (keyEnd));
                else
                    addOff ({ guitar, key, -1, keyEnd });
                if (st.muted)
                {
                    if (muteEnd < q1)
                        midi.addEvent (juce::MidiMessage::noteOff (channelOf (guitar), muteKey), offsetOf (muteEnd));
                    else
                        addOff ({ guitar, muteKey, -1, muteEnd });
                }
            }
        }

        quarter = q1;
        if (countingIn && quarter >= countInUntil)
            countInUntil = none;
    }

    kit.render (buffer.getWritePointer (0), buffer.getWritePointer (1), n);
    bassVoice.render (buffer.getWritePointer (2), n);
    guitarVoice.render (buffer.getWritePointer (3), n);

    // the block's first sample is heard after the output latency
    const auto heard = started + (int64_t) (outputLatency.load() * (double) SharedClock::ticksPerSecond());
    clock.publish (blockStart, heard, playing ? rate : 0.0, playing, countInUntil);
    shownPosition.store (quarter);
    shownPlaying.store (playing);
}

} // namespace gt
