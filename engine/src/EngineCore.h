#pragma once

// The audio engine's core: the audio interface, and a processing graph with the hosted TONE3000 instances.
//
//   instrument input --+--> TONE3000 (tone A) --+
//                      |                        +--> crossfade --+
//                      +--> TONE3000 (tone B) --+                +--> outputs
//   test bass line ------> TONE3000 (bass amp) ------------------+
//
// Both guitar tones run all the time, so the footswitch switches between them instantly.

#include <juce_audio_utils/juce_audio_utils.h>

#include "Processors.h"

namespace gt
{

/** Latency of the audio path, in samples as reported by the driver and the plugin. */
struct Latency
{
    double sampleRate = 0.0;
    int buffer = 0, input = 0, output = 0, plugin = 0;

    double ms (int samples) const { return sampleRate > 0.0 ? 1000.0 * samples / sampleRate : 0.0; }
    double roundTripMs() const { return ms (input + output + plugin); }
};

class EngineCore final : private juce::ChangeListener
{
public:
    enum class Slot { toneA, toneB, bass };

    EngineCore();
    ~EngineCore() override;

    /** Opens the audio interface from saved settings, or the first ASIO driver found (Focusrite's own first,
        then ASIO4ALL). Returns an error message, or empty on success. */
    juce::String openDevice (const juce::XmlElement* saved);
    std::unique_ptr<juce::XmlElement> deviceState() const { return deviceManager.createStateXml(); }
    juce::AudioDeviceManager& devices() { return deviceManager; }
    juce::String deviceSummary() const;

    /** Loads three instances of the plugin (two guitar tones and the bass amp). Returns an error message,
        or empty on success. */
    juce::String loadPlugins (const juce::File& pluginFile);
    juce::AudioProcessor* plugin (Slot) const;
    juce::MemoryBlock pluginState (Slot) const;
    void setPluginState (Slot, const juce::MemoryBlock&);

    /** The device input channel the guitar is plugged into (0-based; on a Scarlett Solo the instrument
        jack is input 2, channel 1). */
    void setGuitarInput (int channel);
    int guitarInput() const { return inputChannel; }
    juce::StringArray inputChannelNames() const;

    void selectTone (int tone) { if (fade != nullptr) fade->select (tone); }
    int selectedTone() const { return fade != nullptr ? fade->selected() : 0; }
    void setBassLine (bool on) { if (bass != nullptr) bass->setPlaying (on); }
    bool bassLinePlaying() const { return bass != nullptr && bass->isPlaying(); }

    Latency latency() const;
    double cpuLoad() const { return deviceManager.getCpuUsage(); }

    /** Called when the device changes (channels, rate, buffer), after the graph is reconnected. */
    std::function<void()> onDeviceChanged;

    void shutdown();

private:
    using Node = juce::AudioProcessorGraph::Node;

    void changeListenerCallback (juce::ChangeBroadcaster*) override;
    void connect();
    Node::Ptr slotNode (Slot) const;

    juce::AudioDeviceManager deviceManager;
    juce::AudioPluginFormatManager formats;
    std::unique_ptr<juce::AudioProcessorGraph> graph;
    juce::AudioProcessorPlayer player;

    Node::Ptr input, output, toneA, toneB, crossfade, bassLine, bassTone;
    ToneCrossfade* fade = nullptr;
    TestBassLine* bass = nullptr;
    int inputChannel = 1;
};

} // namespace gt
