#include "Processors.h"

namespace gt
{

//==============================================================================
TestBassLine::TestBassLine()
    : SimpleProcessor (BusesProperties().withOutput ("Bass", juce::AudioChannelSet::mono(), true))
{
}

void TestBassLine::prepareToPlay (double newSampleRate, int)
{
    sampleRate = newSampleRate;
    line.assign ((size_t) (sampleRate / 30.0) + 2, 0.0f);  // room for notes down to 30 Hz
    samplesToNextBeat = 0.0;
    beat = 0;
}

void TestBassLine::pluck (double frequency)
{
    length = juce::jlimit<size_t> (2, line.size(), (size_t) std::round (sampleRate / frequency));
    pos = 0;
    // a soft pluck: noise smoothed by a one-pole low-pass, so the attack is round like fingers on a bass
    float s = 0.0f;
    for (size_t i = 0; i < length; ++i)
    {
        s += 0.35f * ((random.nextFloat() * 2.0f - 1.0f) - s);
        line[i] = 0.6f * s;
    }
    damping = 0.997f;
}

void TestBassLine::processBlock (juce::AudioBuffer<float>& buffer, juce::MidiBuffer&)
{
    auto* out = buffer.getWritePointer (0);
    // roots per bar, one per beat: B1, A1, E1, E1
    static constexpr double roots[] = { 61.735, 55.0, 41.203, 41.203 };
    const auto beatLength = sampleRate * 60.0 / tempo.load();
    const auto on = playing.load();

    for (int i = 0; i < buffer.getNumSamples(); ++i)
    {
        if (on)
        {
            samplesToNextBeat -= 1.0;
            if (samplesToNextBeat <= 0.0)
            {
                pluck (roots[(beat / 4) % 4]);
                ++beat;
                samplesToNextBeat += beatLength;
            }
            // damp the note just before the next one, as a player mutes the string
            if (samplesToNextBeat < 0.06 * sampleRate)
                damping = 0.985f;
        }
        else
        {
            damping = 0.98f;
            samplesToNextBeat = 0.0;
            beat = 0;
        }

        const auto next = (pos + 1) % length;
        const auto value = 0.5f * (line[pos] + line[next]) * damping;
        tone += 0.5f * (value - tone);  // keeps the top end of a bass DI signal soft
        line[pos] = value;
        pos = next;
        out[i] = tone;
    }

    for (int ch = 1; ch < buffer.getNumChannels(); ++ch)
        buffer.clear (ch, 0, buffer.getNumSamples());
}

} // namespace gt
