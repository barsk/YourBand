#include "EngineCore.h"

#include "NumaKeys.h"
#include "SilentDevice.h"

namespace gt
{

using IO = juce::AudioProcessorGraph::AudioGraphIOProcessor;

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
    bassMix = graph->addNode (std::make_unique<GainProcessor> ("Bass", 0.8f));
    keysMix = graph->addNode (std::make_unique<GainProcessor> ("Keys", 1.3f));
    bandBus = graph->addNode (std::make_unique<GainProcessor> ("Band", 1.0f, true));
    // each instrument plugin hears its own MIDI channel of the band
    keysChannel = graph->addNode (std::make_unique<MidiChannelFilter> (BandProcessor::channelOf (BandProcessor::keys)));
    drumsChannel = graph->addNode (std::make_unique<MidiChannelFilter> (BandProcessor::channelOf (BandProcessor::drums)));
    bassChannel = graph->addNode (std::make_unique<MidiChannelFilter> (BandProcessor::channelOf (BandProcessor::bass)));

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

    for (auto* slot : { &guitarTone, &bassTone })
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
    return part == "drums" ? 0 : part == "bass" ? 1 : part == "keys" ? 2 : -1;
}

void EngineCore::applyMix (int part)
{
    const auto gain = soundLevels[(size_t) part] * faderLevels[(size_t) part];
    static_cast<GainProcessor*> ((part == 0 ? drumsMix : part == 1 ? bassMix : keysMix)->getProcessor())->setGain (gain);
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
    instance->enableAllBuses();
    (slot == Slot::bandDrums ? drumsPlugin : bassPlugin) = graph->addNode (std::move (instance));
    connect();
    return {};
}

void EngineCore::useBandInstrument (Slot slot, bool on)
{
    const auto part = slot == Slot::bandDrums ? BandProcessor::drums : BandProcessor::bass;
    bandProcessor->setPluginFor (part, on && slotNode (slot) != nullptr);
}

void EngineCore::setBassPluginThroughAmp (bool on)
{
    if (on == bassThroughAmp)
        return;
    bassThroughAmp = on;
    connect();
}

bool EngineCore::usesBandInstrument (Slot slot) const
{
    return bandProcessor->usesPluginFor (slot == Slot::bandDrums ? BandProcessor::drums : BandProcessor::bass);
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
    keysNode->getProcessor()->setStateInformation (state.getData(), (int) state.getSize());
    currentKeysSound = sound;
    return {};
}

EngineCore::Node::Ptr EngineCore::slotNode (Slot slot) const
{
    switch (slot)
    {
        case Slot::guitar:    return guitarTone;
        case Slot::bass:      return bassTone;
        case Slot::bandDrums: return drumsPlugin;
        case Slot::bandBass:  return bassPlugin;
    }
    return nullptr;
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
        p->setStateInformation (state.getData(), (int) state.getSize());
}

std::vector<Preset> EngineCore::presets() const
{
    std::vector<Preset> list;
    if (auto* p = plugin (Slot::guitar))
        for (int i = 0; i < p->getNumPrograms(); ++i)
            if (const auto name = p->getProgramName (i); name.isNotEmpty() && name != "(empty)")
                list.push_back ({ i, name });
    return list;
}

void EngineCore::selectPreset (int index)
{
    if (auto* p = plugin (Slot::guitar); p != nullptr && index >= 0 && index < p->getNumPrograms())
        p->setCurrentProgram (index);
}

int EngineCore::currentPreset() const
{
    auto* p = plugin (Slot::guitar);
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
    for (auto& c : graph->getConnections())
        graph->removeConnection (c);

    auto link = [this] (const Node::Ptr& from, int fromChannel, const Node::Ptr& to, int toChannel)
    {
        if (from != nullptr && to != nullptr)
            graph->addConnection ({ { from->nodeID, fromChannel }, { to->nodeID, toChannel } });
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
                                    { to->nodeID, juce::AudioProcessorGraph::midiChannelIndex } });
    };
    for (auto [channel, instrument] : { std::pair { keysChannel, keysNode }, std::pair { drumsChannel, drumsPlugin }, std::pair { bassChannel, bassPlugin } })
        if (instrument != nullptr)
        {
            midi (bandNode, channel);
            midi (channel, instrument);
        }
    // drums: the synthesized kit (and the count-in sticks) and the drum plugin
    stereo (bandNode, drumsMix, 0, 2);
    stereo (drumsPlugin, drumsMix, 0, 2);
    stereo (drumsMix, bandBus, 0, 2);
    // bass: the synthesized DI through the bass amp; the bass plugin through it too, or straight to the mixer
    if (bassTone != nullptr)
    {
        for (int c = 0; c < bassTone->getProcessor()->getTotalNumInputChannels(); ++c)
            link (bandNode, 2, bassTone, c);
        if (bassThroughAmp)
            stereo (bassPlugin, bassTone, 0, bassTone->getProcessor()->getTotalNumInputChannels());
    }
    if (! bassThroughAmp || bassTone == nullptr)
        stereo (bassPlugin, bassMix, 0, 2);
    stereo (bassTone, bassMix, 0, 2);
    stereo (bassMix, bandBus, 0, 2);
    // keys
    stereo (keysNode, keysMix, 0, 2);
    stereo (keysMix, bandBus, 0, 2);
    stereo (bandBus, output, 0, outs);

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
    if (graph == nullptr)
        return;
    deviceManager.removeChangeListener (this);
    deviceManager.removeAudioCallback (&player);
    player.setProcessor (nullptr);
    deviceManager.closeAudioDevice();
    guitarTone = bassTone = bandNode = keysNode = drumsMix = bassMix = keysMix = bandBus = input = output = nullptr;
    drumsPlugin = bassPlugin = keysChannel = drumsChannel = bassChannel = nullptr;
    bandProcessor = nullptr;
    graph->clear();
    graph.reset();
}

} // namespace gt
