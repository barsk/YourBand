#include "EngineCore.h"

namespace gt
{

using IO = juce::AudioProcessorGraph::AudioGraphIOProcessor;

EngineCore::EngineCore()
    : graph (std::make_unique<juce::AudioProcessorGraph>())
{
    juce::addDefaultFormatsToManager (formats);

    input = graph->addNode (std::make_unique<IO> (IO::audioInputNode));
    output = graph->addNode (std::make_unique<IO> (IO::audioOutputNode));

    auto bassProcessor = std::make_unique<TestBassLine>();
    bass = bassProcessor.get();
    bassLine = graph->addNode (std::move (bassProcessor));

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

    // the band's test bass through its own amp
    if (bassTone != nullptr)
        for (int c = 0; c < bassTone->getProcessor()->getTotalNumInputChannels(); ++c)
            link (bassLine, 0, bassTone, c);
    stereo (bassTone, output, 0, outs);
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
    guitarTone = bassTone = bassLine = input = output = nullptr;
    bass = nullptr;
    graph->clear();
    graph.reset();
}

} // namespace gt
