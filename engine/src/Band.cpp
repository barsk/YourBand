#include "Band.h"

#include <algorithm>
#include <cmath>
#include <limits>

namespace gt
{

namespace
{
    const double none = std::numeric_limits<double>::quiet_NaN();
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
        if (note.part >= 0 && note.part <= 2 && note.pitch >= 0 && note.pitch < 128)
            score->notes.push_back (note);
    }
    std::stable_sort (score->notes.begin(), score->notes.end(), [] (const BandNote& a, const BandNote& b) { return a.at < b.at; });
    return score;
}

//==============================================================================
BandProcessor::BandProcessor (SharedClock& sharedClock)
    : SimpleProcessor (BusesProperties().withOutput ("Drums", juce::AudioChannelSet::stereo(), true)
                                        .withOutput ("Bass", juce::AudioChannelSet::mono(), true)),
      clock (sharedClock)
{
    countInUntil = none;
}

void BandProcessor::prepareToPlay (double newSampleRate, int)
{
    sampleRate = newSampleRate;
    kit.prepare (sampleRate);
    bassVoice.prepare (sampleRate);
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

void BandProcessor::addOff (Off off)
{
    if (numOffs < (int) offs.size())
        offs[(size_t) numOffs++] = off;
}

void BandProcessor::releaseAll (juce::MidiBuffer& midi)
{
    for (int i = 0; i < numOffs; ++i)
        midi.addEvent (juce::MidiMessage::noteOff (channelOf (offs[(size_t) i].part), offs[(size_t) i].pitch), 0);
    for (auto part : { keys, bass, drums })
        midi.addEvent (juce::MidiMessage::allNotesOff (channelOf (part)), 0);
    numOffs = 0;
    bassVoice.noteOff (0);
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
            break;
        case Command::speed:
            speed = c.a;
            break;
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
                offs[(size_t) i] = offs[(size_t) --numOffs];
            }
            else
                ++i;
        }

        const auto countingIn = ! std::isnan (countInUntil);
        while (cursor < s->notes.size() && s->notes[cursor].at < q1)
        {
            const auto& note = s->notes[cursor++];
            if (note.at < q0 - 1.0e-9 || (countingIn && note.at < countInUntil - 1.0e-9))
                continue;
            const auto at = offsetOf (note.at);
            const auto onPlugin = note.part == keys || (note.part == drums && drumsOnPlugin.load()) || (note.part == bass && bassOnPlugin.load());
            if (onPlugin)
            {
                // a drum hit is short; a bass note lets go just before the next, as a player's would
                const auto length = note.part == drums ? juce::jmax (0.1, note.length) : note.part == bass ? note.length * 0.95 : note.length;
                midi.addEvent (juce::MidiMessage::noteOn (channelOf (note.part), note.pitch, note.velocity), at);
                addOff ({ note.part, note.pitch, -1, note.at + length });
            }
            else if (note.part == drums)
                kit.trigger (note.pitch, note.velocity, at);
            else if (note.part == bass)
            {
                bassVoice.noteOn (note.pitch, note.velocity, at);
                addOff ({ bass, note.pitch, ++bassSerial, note.at + note.length * 0.95 });
            }
        }

        quarter = q1;
        if (countingIn && quarter >= countInUntil)
            countInUntil = none;
    }

    kit.render (buffer.getWritePointer (0), buffer.getWritePointer (1), n);
    bassVoice.render (buffer.getWritePointer (2), n);

    // the block's first sample is heard after the output latency
    const auto heard = started + (int64_t) (outputLatency.load() * (double) SharedClock::ticksPerSecond());
    clock.publish (blockStart, heard, playing ? rate : 0.0, playing, countInUntil);
    shownPosition.store (quarter);
    shownPlaying.store (playing);
}

} // namespace gt
