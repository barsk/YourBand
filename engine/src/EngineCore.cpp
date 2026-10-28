#include "EngineCore.h"

#include "NumaKeys.h"
#include "SilentDevice.h"

namespace gt
{

using IO = juce::AudioProcessorGraph::AudioGraphIOProcessor;
using UpdateKind = juce::AudioProcessorGraph::UpdateKind;

EngineCore::EngineCore()
    : graph (std::make_unique<juce::AudioProcessorGraph>())
{
    juce::addDefaultFormatsToManager (formats);

    input = graph->addNode (std::make_unique<IO> (IO::audioInputNode));
    output = graph->addNode (std::make_unique<IO> (IO::audioOutputNode));

    auto band = std::make_unique<BandProcessor> (clock);
    bandProcessor = band.get();
    bandNode = graph->addNode (std::move (band));
    // the band's mixer: the keys sit a little under the drums and bass
    drumsMix = graph->addNode (std::make_unique<GainProcessor> ("Drums", 0.8f));
    bassMix = graph->addNode (std::make_unique<GainProcessor> ("Bass", 1.75f));
    keysMix = graph->addNode (std::make_unique<GainProcessor> ("Keys", 1.3f));
    guitarMix = graph->addNode (std::make_unique<GainProcessor> ("Guitar", 0.8f));
    lead1Mix = graph->addNode (std::make_unique<GainProcessor> ("Lead 1", 0.8f));
    lead2Mix = graph->addNode (std::make_unique<GainProcessor> ("Lead 2", 0.8f));
    guitarTrim = graph->addNode (std::make_unique<GainProcessor> ("Guitar plugin into the amp", 1.0f));
    bandBus = graph->addNode (std::make_unique<GainProcessor> ("Band", 1.0f, true));
    // each instrument plugin hears its own MIDI channel of the band
    keysChannel = graph->addNode (std::make_unique<MidiChannelFilter> (BandProcessor::channelOf (BandProcessor::keys)));
    drumsChannel = graph->addNode (std::make_unique<MidiChannelFilter> (BandProcessor::channelOf (BandProcessor::drums)));
    bassChannel = graph->addNode (std::make_unique<MidiChannelFilter> (BandProcessor::channelOf (BandProcessor::bass)));
    guitarChannel = graph->addNode (std::make_unique<MidiChannelFilter> (BandProcessor::channelOf (BandProcessor::guitar)));
    lead1Channel = graph->addNode (std::make_unique<MidiChannelFilter> (BandProcessor::channelOf (BandProcessor::lead1)));
    lead2Channel = graph->addNode (std::make_unique<MidiChannelFilter> (BandProcessor::channelOf (BandProcessor::lead2)));
    spareNode = graph->addNode (std::make_unique<MidiChannelFilter> (16));  // nothing plays on channel 16

    player.setProcessor (graph.get());
}

EngineCore::~EngineCore()
{
    shutdown();
}

juce::String EngineCore::openDevice (const juce::XmlElement* saved)
{
    auto error = deviceManager.initialise (2, 2, saved, true);

    if (saved == nullptr)
    {
        deviceManager.setCurrentAudioDeviceType ("ASIO", true);
        if (auto* type = deviceManager.getCurrentDeviceTypeObject(); type != nullptr && type->getTypeName() == "ASIO")
        {
            type->scanForDevices();
            const auto names = type->getDeviceNames();
            juce::String pick;
            for (auto preferred : { "Focusrite USB ASIO", "ASIO4ALL v2" })
                if (names.contains (preferred))
                {
                    pick = preferred;
                    break;
                }
            if (pick.isEmpty() && ! names.isEmpty())
                pick = names[0];

            auto setup = deviceManager.getAudioDeviceSetup();
            setup.inputDeviceName = setup.outputDeviceName = pick;
            setup.sampleRate = 48000.0;
            setup.bufferSize = 64;  // 7.8 ms round trip on the Scarlett Solo (32: 5.5 ms, 128: 14.5 ms)
            setup.useDefaultInputChannels = setup.useDefaultOutputChannels = true;
            error = deviceManager.setAudioDeviceSetup (setup, true);
        }
    }

    deviceManager.addChangeListener (this);
    deviceManager.addAudioCallback (&player);
    connect();
    return error;
}

juce::String EngineCore::openSilentDevice (const juce::File& recording)
{
    silent = true;
    deviceManager.addAudioDeviceType (std::make_unique<SilentDeviceType> (recording));
    deviceManager.setCurrentAudioDeviceType (SilentDeviceType::typeName, true);
    auto setup = deviceManager.getAudioDeviceSetup();
    setup.inputDeviceName = setup.outputDeviceName = SilentDeviceType::deviceName;
    setup.sampleRate = 48000.0;
    setup.bufferSize = 64;
    setup.useDefaultInputChannels = setup.useDefaultOutputChannels = false;
    setup.inputChannels.setRange (0, 2, true);
    setup.outputChannels.setRange (0, 2, true);
    const auto error = deviceManager.setAudioDeviceSetup (setup, true);
    deviceManager.addChangeListener (this);
    deviceManager.addAudioCallback (&player);
    connect();
    return error;
}

juce::String EngineCore::deviceSummary() const
{
    auto* d = deviceManager.getCurrentAudioDevice();
    if (d == nullptr)
        return "No audio device open";
    return deviceManager.getCurrentAudioDeviceType() + ": " + d->getName() + ", "
         + juce::String (d->getCurrentSampleRate(), 0) + " Hz, buffer " + juce::String (d->getCurrentBufferSizeSamples())
         + ", inputs [" + d->getInputChannelNames().joinIntoString (", ") + "], buffer sizes offered ["
         + [d] { juce::StringArray s; for (auto b : d->getAvailableBufferSizes()) s.add (juce::String (b)); return s.joinIntoString (" "); }()
         + "]";
}

juce::String EngineCore::loadPlugins (const juce::File& pluginFile)
{
    if (! pluginFile.exists())
        return "Plugin not found: " + pluginFile.getFullPathName();

    juce::OwnedArray<juce::PluginDescription> types;
    for (auto* format : formats.getFormats())
        if (format->fileMightContainThisPluginType (pluginFile.getFullPathName()))
            format->findAllTypesForFile (types, pluginFile.getFullPathName());
    if (types.isEmpty())
        return "No plugin found in " + pluginFile.getFullPathName();

    auto* d = deviceManager.getCurrentAudioDevice();
    const auto rate = d != nullptr ? d->getCurrentSampleRate() : 48000.0;
    const auto block = d != nullptr ? d->getCurrentBufferSizeSamples() : 128;

    for (auto* slot : { &guitarTone, &bandAmp })
    {
        juce::String error;
        auto instance = formats.createPluginInstance (*types[0], rate, block, error);
        if (instance == nullptr)
            return "Could not load " + types[0]->name + ": " + error;
        instance->enableAllBuses();
        *slot = graph->addNode (std::move (instance));
    }

    connect();
    return {};
}

juce::String EngineCore::loadKeys (const juce::File& pluginFile)
{
    if (! pluginFile.exists())
        return "Keyboard plugin not found: " + pluginFile.getFullPathName();
    juce::OwnedArray<juce::PluginDescription> types;
    for (auto* format : formats.getFormats())
        if (format->fileMightContainThisPluginType (pluginFile.getFullPathName()))
            format->findAllTypesForFile (types, pluginFile.getFullPathName());
    if (types.isEmpty())
        return "No plugin found in " + pluginFile.getFullPathName();

    auto* d = deviceManager.getCurrentAudioDevice();
    juce::String error;
    auto instance = formats.createPluginInstance (*types[0], d != nullptr ? d->getCurrentSampleRate() : 48000.0,
                                                  d != nullptr ? d->getCurrentBufferSizeSamples() : 64, error);
    if (instance == nullptr)
        return "Could not load " + types[0]->name + ": " + error;
    instance->enableAllBuses();
    keysNode = graph->addNode (std::move (instance));
    connect();
    return {};
}

int EngineCore::partIndex (const juce::String& part)
{
    return part == "drums" ? 0 : part == "bass" ? 1 : part == "keys" ? 2 : part == "guitar" ? 3 : part == "lead1" ? 4 : part == "lead2" ? 5 : -1;
}

int EngineCore::partOf (Slot slot)
{
    return slot == Slot::bandDrums ? BandProcessor::drums : slot == Slot::bandBass ? BandProcessor::bass
         : slot == Slot::bandLead1 ? BandProcessor::lead1 : slot == Slot::bandLead2 ? BandProcessor::lead2 : BandProcessor::guitar;
}

void EngineCore::applyMix (int part)
{
    const auto onPlugin = part != BandProcessor::keys && bandProcessor->usesPluginFor ((BandProcessor::Part) part);
    const auto sound = onPlugin ? pluginLevels[(size_t) part] : soundLevels[(size_t) part];
    const auto gain = sound * faderLevels[(size_t) part];
    const Node::Ptr mixes[] { drumsMix, bassMix, keysMix, guitarMix, lead1Mix, lead2Mix };
    if (const auto& node = mixes[part]; node != nullptr)  // none after shutdown
        static_cast<GainProcessor*> (node->getProcessor())->setGain (gain);
}

bool EngineCore::setSoundLevel (const juce::String& part, float gain)
{
    const auto i = partIndex (part);
    if (i < 0)
        return false;
    soundLevels[(size_t) i] = juce::jlimit (0.0f, 4.0f, gain);
    applyMix (i);
    return true;
}

bool EngineCore::setFader (const juce::String& part, float level)
{
    const auto i = partIndex (part);
    if (i < 0)
        return false;
    faderLevels[(size_t) i] = juce::jlimit (0.0f, 2.0f, level);
    bandProcessor->setPartMuted ((BandProcessor::Part) i, faderLevels[(size_t) i] <= 0.0f);
    applyMix (i);
    return true;
}

float EngineCore::fader (const juce::String& part) const
{
    const auto i = partIndex (part);
    return i < 0 ? 0.0f : faderLevels[(size_t) i];
}

juce::String EngineCore::loadBandInstrument (Slot slot, const juce::File& pluginFile)
{
    if (! pluginFile.exists())
        return "not found: " + pluginFile.getFullPathName();
    juce::OwnedArray<juce::PluginDescription> types;
    for (auto* format : formats.getFormats())
        if (format->fileMightContainThisPluginType (pluginFile.getFullPathName()))
            format->findAllTypesForFile (types, pluginFile.getFullPathName());
    if (types.isEmpty())
        return "no plugin in " + pluginFile.getFullPathName();
    auto* d = deviceManager.getCurrentAudioDevice();
    juce::String error;
    auto instance = formats.createPluginInstance (*types[0], d != nullptr ? d->getCurrentSampleRate() : 48000.0,
                                                  d != nullptr ? d->getCurrentBufferSizeSamples() : 64, error);
    if (instance == nullptr)
        return "could not load " + types[0]->name + ": " + error;
    if (! instance->acceptsMidi())
        return types[0]->name + " takes no notes: it is not an instrument";
    instance->enableAllBuses();
    unloadBandInstrument (slot);
    instrumentNode (slot) = graph->addNode (std::move (instance), std::nullopt, UpdateKind::none);
    connect();
    return {};
}

void EngineCore::unloadBandInstrument (Slot slot)
{
    auto& node = instrumentNode (slot);
    if (node == nullptr)
        return;
    useBandInstrument (slot, false);
    ++stateChanges[node->getProcessor()];  // a resume still waiting from a state change no longer applies
    // The plugin is deleted here, before anything else happens. Left to the graph, it would be deleted later,
    // while the graph holds the lock the audio thread needs to take a newer graph (MONSTER Bass takes 5 s to
    // go), and perhaps while another plugin is being made (MONSTER Bass crashed on that). So: out of the graph;
    // the audio thread takes the graph without it at its next block; one more rebuild (the spare connection
    // changed) makes the graph let go of the one played before, and ours is the last hold on the plugin.
    auto plugin = graph->removeNode (node->nodeID, UpdateKind::none);
    node = nullptr;
    connect();
    juce::Thread::sleep (50);
    spareLinked = ! spareLinked;
    connect();
    plugin = nullptr;
}

void EngineCore::useBandInstrument (Slot slot, bool on)
{
    bandWants[(size_t) partOf (slot)] = on;
    playBandInstrument (slot);
}

void EngineCore::playBandInstrument (Slot slot)
{
    // a plugin taking a new state is paused, and silent: the band plays the built-in sound until it is back
    const auto node = slotNode (slot);
    const auto part = partOf (slot);
    const auto plays = bandWants[(size_t) part] && node != nullptr && ! node->getProcessor()->isSuspended();
    bandProcessor->setPluginFor ((BandProcessor::Part) part, plays);
    applyMix (part);
}

void EngineCore::setBandPluginLevel (Slot slot, float gain)
{
    const auto part = partOf (slot);
    pluginLevels[(size_t) part] = juce::jlimit (0.0f, 4.0f, gain);
    applyMix (part);
}

void EngineCore::setGuitarPluginTrim (float gain)
{
    static_cast<GainProcessor*> (guitarTrim->getProcessor())->setGain (gain);
}

void EngineCore::setGuitarPluginThroughAmp (bool on)
{
    if (on == guitarThroughAmp)
        return;
    guitarThroughAmp = on;
    connect();
}

bool EngineCore::usesBandInstrument (Slot slot) const
{
    return bandWants[(size_t) partOf (slot)] && slotNode (slot) != nullptr;
}

juce::String EngineCore::setKeysSound (const juce::String& sound)
{
    if (keysNode == nullptr)
        return "no keyboard plugin";
    if (sound == currentKeysSound)
        return {};
    juce::String error;
    const auto state = numa::stateFor (sound, error);
    if (state.isEmpty())
        return error;
    // the keys are silent while Numa Player loads the sound: it takes up to a quarter of a second
    // (measured with plugin_probe --load-time), so half a second
    changeState (*keysNode->getProcessor(), state, 500);
    currentKeysSound = sound;
    return {};
}

EngineCore::Node::Ptr EngineCore::slotNode (Slot slot) const
{
    switch (slot)
    {
        case Slot::guitar:     return guitarTone;
        case Slot::bandAmp:    return bandAmp;
        case Slot::bandDrums:  return drumsPlugin;
        case Slot::bandBass:   return bassPlugin;
        case Slot::bandGuitar: return guitarPlugin;
        case Slot::bandLead1:  return lead1Plugin;
        case Slot::bandLead2:  return lead2Plugin;
    }
    return nullptr;
}

EngineCore::Node::Ptr& EngineCore::instrumentNode (Slot slot)
{
    return slot == Slot::bandDrums ? drumsPlugin : slot == Slot::bandBass ? bassPlugin : slot == Slot::bandLead1 ? lead1Plugin
         : slot == Slot::bandLead2 ? lead2Plugin : guitarPlugin;
}

juce::AudioProcessor* EngineCore::plugin (Slot slot) const
{
    auto node = slotNode (slot);
    return node != nullptr ? node->getProcessor() : nullptr;
}

juce::MemoryBlock EngineCore::pluginState (Slot slot) const
{
    juce::MemoryBlock state;
    if (auto* p = plugin (slot))
        p->getStateInformation (state);
    return state;
}

void EngineCore::setPluginState (Slot slot, const juce::MemoryBlock& state)
{
    if (auto* p = plugin (slot); p != nullptr && state.getSize() > 0)
        changeState (*p, state, 2000);
}

void EngineCore::changeState (juce::AudioProcessor& p, const juce::MemoryBlock& state, int settleMs)
{
    // waits for the block being played to finish; from then on the graph plays silence for this plugin
    p.suspendProcessing (true);
    for (auto slot : { Slot::bandDrums, Slot::bandBass, Slot::bandGuitar, Slot::bandLead1, Slot::bandLead2 })
        playBandInstrument (slot);
    p.setStateInformation (state.getData(), (int) state.getSize());
    const auto change = ++stateChanges[&p];
    juce::Timer::callAfterDelay (settleMs, [this, flag = alive, plugin = &p, change]
    {
        if (*flag && stateChanges[plugin] == change)
        {
            plugin->suspendProcessing (false);
            for (auto slot : { Slot::bandDrums, Slot::bandBass, Slot::bandGuitar, Slot::bandLead1, Slot::bandLead2 })
                playBandInstrument (slot);
        }
    });
}

std::vector<Preset> EngineCore::presets (Slot slot) const
{
    std::vector<Preset> list;
    if (auto* p = plugin (slot))
        for (int i = 0; i < p->getNumPrograms(); ++i)
            if (const auto name = p->getProgramName (i); name.isNotEmpty() && name != "(empty)")
                list.push_back ({ i, name });
    return list;
}

void EngineCore::selectPreset (int index, Slot slot)
{
    if (auto* p = plugin (slot); p != nullptr && index >= 0 && index < p->getNumPrograms())
        p->setCurrentProgram (index);
}

int EngineCore::currentPreset (Slot slot) const
{
    auto* p = plugin (slot);
    return p != nullptr ? p->getCurrentProgram() : -1;
}

void EngineCore::setGuitarInput (int channel)
{
    inputChannel = juce::jmax (0, channel);
    connect();
}

juce::StringArray EngineCore::inputChannelNames() const
{
    if (auto* d = deviceManager.getCurrentAudioDevice())
        return d->getInputChannelNames();
    return {};
}

void EngineCore::connect()
{
    // all connections anew, in one rebuild at the end: the audio thread goes from the whole old graph to the
    // whole new one (a rebuild per connection let it play graphs half connected, silent in places)
    for (auto& c : graph->getConnections())
        graph->removeConnection (c, UpdateKind::none);

    auto link = [this] (const Node::Ptr& from, int fromChannel, const Node::Ptr& to, int toChannel)
    {
        if (from != nullptr && to != nullptr)
            graph->addConnection ({ { from->nodeID, fromChannel }, { to->nodeID, toChannel } }, UpdateKind::none);
    };
    // a node's first two outputs (a mono output feeds both) into `to`, from channel `base`
    auto stereo = [&] (const Node::Ptr& from, const Node::Ptr& to, int base, int count)
    {
        if (from == nullptr)
            return;
        const auto n = from->getProcessor()->getTotalNumOutputChannels();
        for (int c = 0; c < count && n > 0; ++c)
            link (from, juce::jmin (c, n - 1), to, base + c);
    };
    // every output of a multi-output instrument (a drum kit with a stereo pair per drum) into one stereo pair
    auto allOutputs = [&] (const Node::Ptr& from, const Node::Ptr& to)
    {
        if (from == nullptr)
            return;
        const auto n = from->getProcessor()->getTotalNumOutputChannels();
        for (int c = 0; c < n; ++c)
            link (from, c, to, n == 1 ? 0 : c % 2);
        if (n == 1)
            link (from, 0, to, 1);
    };

    const auto ins = graph->getTotalNumInputChannels();
    const auto outs = juce::jmin (2, graph->getTotalNumOutputChannels());

    // guitar: the instrument input into its amp
    if (inputChannel < ins && guitarTone != nullptr)
        for (int c = 0; c < guitarTone->getProcessor()->getTotalNumInputChannels(); ++c)
            link (input, inputChannel, guitarTone, c);
    stereo (guitarTone, output, 0, outs);

    // the band: each part's MIDI channel to its instrument plugin
    auto midi = [this] (const Node::Ptr& from, const Node::Ptr& to)
    {
        if (from != nullptr && to != nullptr)
            graph->addConnection ({ { from->nodeID, juce::AudioProcessorGraph::midiChannelIndex },
                                    { to->nodeID, juce::AudioProcessorGraph::midiChannelIndex } }, UpdateKind::none);
    };
    for (auto [channel, instrument] : { std::pair { keysChannel, keysNode }, std::pair { drumsChannel, drumsPlugin },
                                        std::pair { bassChannel, bassPlugin }, std::pair { guitarChannel, guitarPlugin },
                                        std::pair { lead1Channel, lead1Plugin }, std::pair { lead2Channel, lead2Plugin } })
        if (instrument != nullptr)
        {
            midi (bandNode, channel);
            midi (channel, instrument);
        }
    if (spareLinked)
        midi (bandNode, spareNode);
    // drums: the synthesized kit (and the count-in sticks) and the drum plugin
    stereo (bandNode, drumsMix, 0, 2);
    allOutputs (drumsPlugin, drumsMix);
    stereo (drumsMix, bandBus, 0, 2);
    // bass: the synthesized one (a DI sound) and the bass plugin (with its own amp) straight to the mixer
    link (bandNode, 2, bassMix, 0);
    link (bandNode, 2, bassMix, 1);
    allOutputs (bassPlugin, bassMix);
    stereo (bassMix, bandBus, 0, 2);
    // guitar: the synthesized DI into the band guitar's amp; the guitar plugin into it too (every output, as one
    // guitar), or straight to the mixer
    if (bandAmp != nullptr)
    {
        const auto ampInputs = bandAmp->getProcessor()->getTotalNumInputChannels();
        for (int c = 0; c < ampInputs; ++c)
            link (bandNode, 3, bandAmp, c);
        if (guitarThroughAmp && guitarPlugin != nullptr)
        {
            allOutputs (guitarPlugin, guitarTrim);
            for (int c = 0; c < ampInputs; ++c)
            {
                link (guitarTrim, 0, bandAmp, c);
                link (guitarTrim, 1, bandAmp, c);
            }
        }
    }
    if (! guitarThroughAmp || bandAmp == nullptr)
        allOutputs (guitarPlugin, guitarMix);
    stereo (bandAmp, guitarMix, 0, 2);
    stereo (guitarMix, bandBus, 0, 2);
    // keys
    stereo (keysNode, keysMix, 0, 2);
    stereo (keysMix, bandBus, 0, 2);
    // the two lead channels: a plugin each (a stand-in for the harmonica, the lead guitar...), no built-in sound
    allOutputs (lead1Plugin, lead1Mix);
    stereo (lead1Mix, bandBus, 0, 2);
    allOutputs (lead2Plugin, lead2Mix);
    stereo (lead2Mix, bandBus, 0, 2);
    stereo (bandBus, output, 0, outs);
    graph->rebuild();

    if (auto* d = deviceManager.getCurrentAudioDevice())
        bandProcessor->setOutputLatency (d->getOutputLatencyInSamples() / d->getCurrentSampleRate());
}

void EngineCore::changeListenerCallback (juce::ChangeBroadcaster*)
{
    connect();
    if (onDeviceChanged)
        onDeviceChanged();
}

Latency EngineCore::latency() const
{
    Latency l;
    if (auto* d = deviceManager.getCurrentAudioDevice())
    {
        l.sampleRate = d->getCurrentSampleRate();
        l.buffer = d->getCurrentBufferSizeSamples();
        l.input = d->getInputLatencyInSamples();
        l.output = d->getOutputLatencyInSamples();
    }
    if (auto* p = plugin (Slot::guitar))
        l.plugin = p->getLatencySamples();
    return l;
}

void EngineCore::shutdown()
{
    *alive = false;  // no plugin's audio is resumed after this
    if (graph == nullptr)
        return;
    deviceManager.removeChangeListener (this);
    deviceManager.removeAudioCallback (&player);
    player.setProcessor (nullptr);
    deviceManager.closeAudioDevice();
    guitarTone = bandAmp = guitarTrim = bandNode = keysNode = drumsMix = bassMix = keysMix = guitarMix = bandBus = input = output = nullptr;
    drumsPlugin = bassPlugin = guitarPlugin = keysChannel = drumsChannel = bassChannel = guitarChannel = spareNode = nullptr;
    lead1Plugin = lead2Plugin = lead1Mix = lead2Mix = lead1Channel = lead2Channel = nullptr;
    bandProcessor = nullptr;
    graph->clear();
    graph.reset();
}

} // namespace gt
