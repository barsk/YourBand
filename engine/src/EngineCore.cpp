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
    bassMix = graph->addNode (std::make_unique<GainProcessor> ("Bass", 0.8f));
    keysMix = graph->addNode (std::make_unique<GainProcessor> ("Keys", 1.3f));
    bandBus = graph->addNode (std::make_unique<GainProcessor> ("Band", 1.0f, true));

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

bool EngineCore::setPartGain (const juce::String& part, float gain)
{
    if (part == "drums")
        bandProcessor->setDrumsGain (gain);
    else if (part == "bass" || part == "keys")
        static_cast<GainProcessor*> ((part == "bass" ? bassMix : keysMix)->getProcessor())->setGain (gain);
    else
        return false;
    return true;
}

float EngineCore::partGain (const juce::String& part) const
{
    if (part == "drums")
        return bandProcessor->getDrumsGain();
    if (part == "bass" || part == "keys")
        return static_cast<GainProcessor*> ((part == "bass" ? bassMix : keysMix)->getProcessor())->getGain();
    return 0.0f;
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
        case Slot::guitar: return guitarTone;
        case Slot::bass:   return bassTone;
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

    // the band: drums straight out, the bass DI through its amp, the keys' MIDI into the keyboard plugin
    stereo (bandNode, bandBus, 0, 2);
    if (bassTone != nullptr)
        for (int c = 0; c < bassTone->getProcessor()->getTotalNumInputChannels(); ++c)
            link (bandNode, 2, bassTone, c);
    stereo (bassTone, bassMix, 0, 2);
    stereo (bassMix, bandBus, 0, 2);
    if (keysNode != nullptr)
    {
        graph->addConnection ({ { bandNode->nodeID, juce::AudioProcessorGraph::midiChannelIndex },
                                { keysNode->nodeID, juce::AudioProcessorGraph::midiChannelIndex } });
        stereo (keysNode, keysMix, 0, 2);
        stereo (keysMix, bandBus, 0, 2);
    }
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
    guitarTone = bassTone = bandNode = keysNode = bassMix = keysMix = bandBus = input = output = nullptr;
    bandProcessor = nullptr;
    graph->clear();
    graph.reset();
}

} // namespace gt
