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

/** A volume control in the graph: one channel of the band's mixer. Changes are ramped over a block. With
    `limit`, peaks above 0.7 are bent softly towards 1 instead of clipping (the band's sum, to spare your ears). */
class GainProcessor final : public SimpleProcessor
{
public:
    GainProcessor (const juce::String& channelName, float initialGain, bool limit = false)
        : SimpleProcessor (BusesProperties().withInput ("In", juce::AudioChannelSet::stereo(), true)
                                            .withOutput ("Out", juce::AudioChannelSet::stereo(), true)),
          name (channelName), gain (initialGain), applied (initialGain), limiting (limit)
    {
    }

    const juce::String getName() const override { return name; }
    void prepareToPlay (double, int) override { applied = gain.load(); }
    void processBlock (juce::AudioBuffer<float>& buffer, juce::MidiBuffer&) override
    {
        const auto target = gain.load();
        buffer.applyGainRamp (0, buffer.getNumSamples(), applied, target);
        applied = target;
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

private:
    static constexpr float knee = 0.7f;
    juce::String name;
    std::atomic<float> gain;
    float applied;
    bool limiting;
};

} // namespace gt
