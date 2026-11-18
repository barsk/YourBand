#pragma once

// The audio engine's core: the audio interface, and a processing graph with the hosted plugins and the band.
//
//   instrument input --> TONE3000 (guitar) ------------------------------------+
//   band: drums ---------------------------------------> volume --+            +--> outputs
//         bass ----------------------------------------> volume --+            |
//         keys MIDI ---> keys plugin (Numa Player) ----> volume --+--> band bus --+
//         guitar ----------------------------------------> volume --+    (limiter)
//
// The drums, bass and guitar play synthesized, or on instrument plugins (MIDI) chosen in the control panel; the keys and
// the two leads only on a plugin (they have no built-in sound). A
// guitar plugin (Kontakt with Shreddage) has an amp of its own: it goes straight to the mixer, as the built-in guitar
// does (a DI sound).
//
// Tones are switched with TONE3000's own presets: it builds the new model in the background and fades its
// output across the swap, so a switch in a pause between phrases is not heard.

#include <juce_audio_utils/juce_audio_utils.h>

#include "Band.h"
#include "Processors.h"
#include "SharedClock.h"

#include <array>
#include <map>
#include <memory>
#include <vector>

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

/** One of the plugin's presets (its program number and name). */
struct Preset
{
    int index;
    juce::String name;
};

class EngineCore final : private juce::ChangeListener
{
public:
    /** The plugins: your guitar's amp, and the band's instruments (drums, bass, guitar and the two lead channels). */
    enum class Slot { guitar, bandDrums, bandBass, bandGuitar, bandLead1, bandLead2, bandKeys };
    /** The band's instrument slots. */
    static constexpr Slot bandSlots[] { Slot::bandDrums, Slot::bandBass, Slot::bandGuitar, Slot::bandLead1, Slot::bandLead2, Slot::bandKeys };

    EngineCore();
    ~EngineCore() override;

    /** Opens the audio interface from saved settings, or the first ASIO driver found (Focusrite's own first,
        then ASIO4ALL). Returns an error message, or empty on success. */
    juce::String openDevice (const juce::XmlElement* saved);
    /** Instead of an audio interface: a silent one that keeps real time, recording to `recording` if given. */
    juce::String openSilentDevice (const juce::File& recording);
    std::unique_ptr<juce::XmlElement> deviceState() const { return deviceManager.createStateXml(); }
    juce::AudioDeviceManager& devices() { return deviceManager; }
    juce::String deviceSummary() const;
    bool isSilent() const { return silent; }

    /** Loads the amp plugin (TONE3000) for your guitar. Returns an error, or empty. */
    juce::String loadPlugins (const juce::File& pluginFile);
    /** Loads an instrument plugin for one of the band's parts (MONSTER Drums, MONSTER Bass, Ample Bass, MONSTER Guitar,
        Kontakt, Numa Player), in place of the one there before. The band plays it once told to (see
        useBandInstrument). Returns an error, or empty. */
    juce::String loadBandInstrument (Slot, const juce::File& pluginFile);
    /** Takes a band instrument plugin out (the band plays that part synthesized). Close its window first. */
    void unloadBandInstrument (Slot);
    /** Whether the band plays the drums, bass or guitar on its instrument plugin, or synthesized. */
    void useBandInstrument (Slot, bool on);
    bool usesBandInstrument (Slot slot) const;
    /** A band instrument plugin's sound level in the mix, used instead of the part's while the band plays on it. */
    void setBandPluginLevel (Slot, float gain);
    /** The band guitar's notes go to its plugin this early (seconds), to make up for a sound that builds slowly. */
    void setBandGuitarAdvance (double seconds) { bandProcessor->setGuitarAdvance (seconds); }
    /** The highest note a band instrument's plugin plays (127: no limit); higher notes are played octaves lower. */
    /** The keyboards or a lead on a drum plugin: the drum notes it plays (see BandProcessor::setKitNotes). */
    void setBandKitNotes (Slot slot, const std::vector<int>& notes) { bandProcessor->setKitNotes ((BandProcessor::Part) partOf (slot), notes); }
    void setBandNoteCeiling (Slot slot, int highest) { bandProcessor->setNoteCeiling ((BandProcessor::Part) partOf (slot), highest); }
    /** The band guitar's plugin is strum-capable (Shreddage's strumming mode): its strokes go to it as strum
        keyswitches on its held voicing, instead of its per-string notes. */
    void setBandGuitarStrummingMode (bool on) { bandProcessor->setStrummingMode (on); }
    juce::AudioProcessor* plugin (Slot) const;
    /** A plugin's state; while it takes a new one (setPluginState: paused until it has settled), that one, without
        asking the plugin (the engine stopped in MONSTER Drums when asked for its state as it loaded the kit it was given). */
    juce::MemoryBlock pluginState (Slot) const;
    void setPluginState (Slot, const juce::MemoryBlock&);

    /** The saved presets of your guitar's TONE3000, and switching between them (message thread). */
    std::vector<Preset> presets() const;
    void selectPreset (int index);
    int currentPreset() const;

    /** The device input channel the guitar is plugged into (0-based; on a Scarlett Solo the instrument
        jack is input 2, channel 1). */
    void setGuitarInput (int channel);
    int guitarInput() const { return inputChannel; }
    juce::StringArray inputChannelNames() const;

    BandProcessor& band() { return *bandProcessor; }

    /** The band's mixer, per part ("drums", "bass", "keys", "guitar", "lead1" or "lead2"). A part plays at the level the game gives its
        sound (the keys per style: an organ sits lower than a piano) times your fader (1: the balanced mix).
        False if there is no such part. */
    bool setSoundLevel (const juce::String& part, float gain);
    bool setFader (const juce::String& part, float level);
    /** A part's place, -1 all left to 1 all right, and whether it is silent (mute, or another part's solo). */
    bool setPan (const juce::String& part, float pan);
    bool setMuted (const juce::String& part, bool muted);
    float fader (const juce::String& part) const;

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
    Node::Ptr& instrumentNode (Slot);
    /** The band part a band instrument slot plays (BandProcessor::Part, also the mixer's index). */
    static int partOf (Slot);
    /** The band on a drums or bass plugin if you want it there and the plugin is not paused for a new state. */
    void playBandInstrument (Slot);
    /** Gives a plugin in the graph a new state with its audio paused: from before the change until it has had
        `settleMs` to load what the state names (Numa Player loads a sound in the background, and crashed when it
        played while doing so). */
    void changeState (juce::AudioProcessor&, const juce::MemoryBlock& state, int settleMs);
    static int partIndex (const juce::String& part);
    void applyMix (int part);

    juce::AudioDeviceManager deviceManager;
    juce::AudioPluginFormatManager formats;
    std::unique_ptr<juce::AudioProcessorGraph> graph;
    juce::AudioProcessorPlayer player;

    SharedClock clock;
    Node::Ptr input, output, guitarTone, bandNode, keysPlugin, drumsMix, bassMix, keysMix, guitarMix, bandBus;
    Node::Ptr drumsPlugin, bassPlugin, guitarPlugin, keysChannel, drumsChannel, bassChannel, guitarChannel;
    Node::Ptr lead1Plugin, lead2Plugin, lead1Mix, lead2Mix, lead1Channel, lead2Channel;  // the two lead channels
    BandProcessor* bandProcessor = nullptr;
    // drums, bass, keys, guitar: the built-in bass a DI sound (as loud as the bass plugins at 1.75, measured), the
    // built-in guitar a DI sound too (at the bass's level, not measured)
    std::array<float, 6> soundLevels { 0.8f, 1.75f, 1.3f, 1.75f, 0.8f, 0.8f }, faderLevels { 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f };
    std::array<float, 6> pans {};
    std::array<bool, 6> mutedParts {};
    std::array<float, 6> pluginLevels { 0.8f, 0.8f, 0.8f, 0.8f, 0.8f, 0.8f };  // on their plugins (the keys always are)
    int inputChannel = 1;
    bool silent = false;
    std::array<bool, 6> bandWants {};  // per part: on its plugin (useBandInstrument)
    Node::Ptr spareNode;  // a connection to it or not: a different graph, for one more rebuild (unloadBandInstrument)
    bool spareLinked = false;
    std::map<juce::AudioProcessor*, int> stateChanges;  // the latest change per plugin: only it resumes the audio
    std::map<juce::AudioProcessor*, juce::MemoryBlock> settling;  // the state a paused plugin is taking (see pluginState)
    std::shared_ptr<bool> alive = std::make_shared<bool> (true);
};

} // namespace gt
