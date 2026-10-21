#pragma once

// Small audio processors that live in the engine's graph next to the hosted plugins.

#include <juce_audio_processors/juce_audio_processors.h>

#include <atomic>
#include <vector>

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

/** A test bass line for the band's bass: plucked strings (Karplus-Strong) playing quarter-note roots of
    Bm - A - E - E at a fixed tempo. It stands in for sampled clean bass notes until the band engine exists,
    and feeds a TONE3000 instance loaded with a bass amp capture. */
class TestBassLine final : public SimpleProcessor
{
public:
    TestBassLine();

    const juce::String getName() const override { return "Test bass line"; }
    void prepareToPlay (double sampleRate, int samplesPerBlock) override;
    void processBlock (juce::AudioBuffer<float>&, juce::MidiBuffer&) override;

    void setPlaying (bool on) { playing.store (on); }
    bool isPlaying() const { return playing.load(); }
    void setTempo (double bpm) { tempo.store (bpm); }

private:
    void pluck (double frequency);

    std::atomic<bool> playing { false };
    std::atomic<double> tempo { 112.0 };
    double sampleRate = 48000.0;
    double samplesToNextBeat = 0.0;
    int beat = 0;
    std::vector<float> line;  // the string's delay line
    size_t length = 1, pos = 0;
    float damping = 0.996f, tone = 0.0f;
    juce::Random random;
};

} // namespace gt
