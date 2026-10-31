#pragma once

// Small audio processors that live in the engine's graph next to the hosted plugins.

#include <juce_audio_processors/juce_audio_processors.h>

#include <atomic>
#include <cmath>


namespace gt
{

/** Boilerplate for processors without an editor, programs or state. */
class SimpleProcessor : public juce::AudioProcessor
{
public:
    using juce::AudioProcessor::AudioProcessor;

    void releaseResources() override {}
    bool hasEditor() const override { return false; }
    juce::AudioProcessorEditor* createEditor() override { return nullptr; }
    bool acceptsMidi() const override { return false; }
    bool producesMidi() const override { return false; }
    double getTailLengthSeconds() const override { return 0.0; }
    int getNumPrograms() override { return 1; }
    int getCurrentProgram() override { return 0; }
    void setCurrentProgram (int) override {}
    const juce::String getProgramName (int) override { return {}; }
    void changeProgramName (int, const juce::String&) override {}
    void getStateInformation (juce::MemoryBlock&) override {}
    void setStateInformation (const void*, int) override {}
};

/** Passes on the MIDI of one channel only: each instrument plugin hears its own part of the band. */
class MidiChannelFilter final : public SimpleProcessor
{
public:
    explicit MidiChannelFilter (int midiChannel)
        : SimpleProcessor (BusesProperties()), channel (midiChannel)
    {
        kept.ensureSize (8192);
    }

    const juce::String getName() const override { return "MIDI channel " + juce::String (channel); }
    bool acceptsMidi() const override { return true; }
    bool producesMidi() const override { return true; }
    bool isMidiEffect() const override { return true; }
    void prepareToPlay (double, int) override {}
    void processBlock (juce::AudioBuffer<float>&, juce::MidiBuffer& midi) override
    {
        kept.clear();
        for (const auto event : midi)
            if (event.getMessage().getChannel() == channel)
                kept.addEvent (event.getMessage(), event.samplePosition);
        midi.swapWith (kept);
    }

private:
    int channel;
    juce::MidiBuffer kept;
};

/** A volume control in the graph: one channel of the band's mixer. Changes are ramped over a block. With
    `limit`, peaks above 0.7 are bent softly towards 1 instead of clipping (the band's sum, to spare your ears). */
class GainProcessor final : public SimpleProcessor
{
public:
    GainProcessor (const juce::String& channelName, float initialGain, bool limit = false)
        : SimpleProcessor (BusesProperties().withInput ("In", juce::AudioChannelSet::stereo(), true)
                                            .withOutput ("Out", juce::AudioChannelSet::stereo(), true)),
          name (channelName), gain (initialGain), applied { initialGain, initialGain }, limiting (limit)
    {
    }

    const juce::String getName() const override { return name; }
    void prepareToPlay (double, int) override
    {
        for (int ch = 0; ch < 2; ++ch)
            applied[ch] = gain.load() * side (ch);
    }
    void processBlock (juce::AudioBuffer<float>& buffer, juce::MidiBuffer&) override
    {
        for (int ch = 0; ch < juce::jmin (2, buffer.getNumChannels()); ++ch)
        {
            const auto target = gain.load() * side (ch);
            buffer.applyGainRamp (ch, 0, buffer.getNumSamples(), applied[ch], target);
            applied[ch] = target;
        }
        if (! limiting)
            return;
        for (int ch = 0; ch < buffer.getNumChannels(); ++ch)
        {
            auto* x = buffer.getWritePointer (ch);
            for (int i = 0; i < buffer.getNumSamples(); ++i)
                if (const auto a = std::abs (x[i]); a > knee)
                    x[i] = std::copysign (knee + (1.0f - knee) * std::tanh ((a - knee) / (1.0f - knee)), x[i]);
        }
    }

    void setGain (float g) { gain.store (juce::jlimit (0.0f, 4.0f, g)); }
    float getGain() const { return gain.load(); }
    /** Where the channel sits, -1 all left to 1 all right: equal power, at its level in the middle (a side up to +3 dB). */
    void setPan (float p) { pan.store (juce::jlimit (-1.0f, 1.0f, p)); }

private:
    float side (int channel) const
    {
        const auto angle = (pan.load() + 1.0f) * juce::MathConstants<float>::pi / 4.0f;
        return (channel == 0 ? std::cos (angle) : std::sin (angle)) * juce::MathConstants<float>::sqrt2;
    }

    static constexpr float knee = 0.7f;
    juce::String name;
    std::atomic<float> gain, pan { 0.0f };
    float applied[2];
    bool limiting;
};

} // namespace gt
