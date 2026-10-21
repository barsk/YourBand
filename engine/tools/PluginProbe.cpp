// plugin_probe: checks whether a plugin can serve as one of the band's instruments.
//
//   plugin_probe <plugin.vst3> [out-folder] [program-name-filter ... | --state <file> ...]
//
// With --state, each file holds the plugin's own state (as a JUCE-built plugin's standalone app saves it)
// and gives one take; this is how sounds are chosen in plugins without a program list (Numa Player).
//
// Loads the plugin without a window and reports what it offers (inputs, outputs, MIDI, programs, latency).
// For each program whose name contains one of the filters (all programs if none are given), it plays a
// keyboard comping phrase (Bm - A - E - E at 112 BPM, two bars each), rendered in 64-sample blocks as the
// engine would, and reports the processing time as a share of each block's duration (the engine's
// "audio CPU"). Each take is written as a WAV file to listen to.

#include <juce_audio_formats/juce_audio_formats.h>
#include <juce_audio_processors/juce_audio_processors.h>
#include <juce_gui_basics/juce_gui_basics.h>

#include <algorithm>
#include <iostream>

namespace
{
constexpr double rate = 48000.0;
constexpr int block = 64;
constexpr double bpm = 112.0;

/** The phrase: per bar a chord, left hand root and fifth, right hand a close triad, struck on every beat. */
juce::MidiBuffer phrase (int& totalSamples)
{
    struct Chord { int left[2]; int right[3]; };
    // MIDI notes: Bm (B1 F#2 | D4 F#4 B4), A (A1 E2 | C#4 E4 A4), E (E2 B2 | B3 E4 G#4)
    const Chord bm { { 35, 42 }, { 62, 66, 71 } }, a { { 33, 40 }, { 61, 64, 69 } }, e { { 40, 47 }, { 59, 64, 68 } };
    const Chord* bars[] = { &bm, &bm, &a, &a, &e, &e, &e, &e };
    const auto beat = rate * 60.0 / bpm;

    juce::MidiBuffer midi;
    int t = 0;
    for (int bar = 0; bar < 8; ++bar)
        for (int b = 0; b < 4; ++b)
        {
            t = (int) std::round ((bar * 4 + b) * beat);
            const auto off = (int) std::round (t + 0.85 * beat);
            const auto* c = bars[bar];
            const auto velocity = (juce::uint8) (b == 0 ? 100 : 80);
            if (b % 2 == 0)
                for (auto n : c->left)
                {
                    midi.addEvent (juce::MidiMessage::noteOn (1, n, velocity), t);
                    midi.addEvent (juce::MidiMessage::noteOff (1, n), (int) std::round (t + 1.85 * beat));
                }
            for (auto n : c->right)
            {
                midi.addEvent (juce::MidiMessage::noteOn (1, n, velocity), t);
                midi.addEvent (juce::MidiMessage::noteOff (1, n), off);
            }
        }
    totalSamples = (int) std::round (32 * beat + 3.0 * rate);  // plus room for the release
    return midi;
}

struct Take
{
    double meanLoad = 0, peakLoad = 0;
    float peak = 0, rms = 0;
};

Take render (juce::AudioPluginInstance& plugin, const juce::File& wav)
{
    int total = 0;
    const auto midi = phrase (total);
    const auto outs = juce::jmax (1, plugin.getTotalNumOutputChannels());
    juce::AudioBuffer<float> buffer (juce::jmax (outs, plugin.getTotalNumInputChannels()), block);
    juce::AudioBuffer<float> recording (outs, total);

    plugin.reset();
    Take take;
    double sumLoad = 0;
    int blocks = 0;
    const auto blockSeconds = block / rate;
    const auto began = juce::Time::getHighResolutionTicks();

    for (int start = 0; start < total; start += block)
    {
        const auto n = juce::jmin (block, total - start);
        juce::MidiBuffer events;
        for (const auto m : midi)
            if (m.samplePosition >= start && m.samplePosition < start + n)
                events.addEvent (m.getMessage(), m.samplePosition - start);
        buffer.clear();

        const auto t0 = juce::Time::getHighResolutionTicks();
        plugin.processBlock (buffer, events);
        const auto seconds = juce::Time::highResolutionTicksToSeconds (juce::Time::getHighResolutionTicks() - t0);

        // real-time pace, as in the engine (sample players stream from disk in the background); the waits
        // run the message loop, which some plugins need while they play
        const auto due = (start + n) / rate;
        const auto elapsed = juce::Time::highResolutionTicksToSeconds (juce::Time::getHighResolutionTicks() - began);
        if (due - elapsed > 0.002)
            juce::MessageManager::getInstance()->runDispatchLoopUntil ((int) ((due - elapsed) * 1000.0));

        const auto load = seconds / blockSeconds;
        sumLoad += load;
        take.peakLoad = std::max (take.peakLoad, load);
        ++blocks;
        for (int ch = 0; ch < outs; ++ch)
            recording.copyFrom (ch, start, buffer, ch, 0, n);
    }

    take.meanLoad = sumLoad / std::max (1, blocks);
    for (int ch = 0; ch < outs; ++ch)
    {
        take.peak = std::max (take.peak, recording.getMagnitude (ch, 0, total));
        take.rms = std::max (take.rms, recording.getRMSLevel (ch, 0, total));
    }

    wav.getParentDirectory().createDirectory();
    wav.deleteFile();  // an output stream on an existing file appends to it
    juce::WavAudioFormat format;
    if (std::unique_ptr<juce::OutputStream> stream = wav.createOutputStream())
    {
        auto options = juce::AudioFormatWriterOptions{}.withSampleRate (rate)
                                                       .withNumChannels (outs)
                                                       .withBitsPerSample (24);
        if (auto writer = format.createWriterFor (stream, options))
        {
            writer->writeFromAudioSampleBuffer (recording, 0, total);
        }
    }
    return take;
}
}

int main (int argc, char* argv[])
{
    juce::ScopedJuceInitialiser_GUI juce;  // plugins expect a message thread

    if (argc < 2)
    {
        std::cout << "usage: plugin_probe <plugin.vst3> [out-folder] [program-name-filter ... | --state <file> ...]\n";
        return 1;
    }
    const juce::File pluginFile { juce::String (argv[1]) };
    const juce::File outFolder (argc > 2 ? juce::String (argv[2]) : juce::File::getCurrentWorkingDirectory().getFullPathName());
    juce::StringArray filters;
    juce::Array<juce::File> states;
    for (int i = 3; i < argc; ++i)
    {
        if (juce::String (argv[i]) == "--state" && i + 1 < argc)
            states.add (juce::File (juce::String (argv[++i])));
        else
            filters.add (argv[i]);
    }

    juce::AudioPluginFormatManager formats;
    juce::addDefaultFormatsToManager (formats);
    juce::OwnedArray<juce::PluginDescription> types;
    for (auto* format : formats.getFormats())
        if (format->fileMightContainThisPluginType (pluginFile.getFullPathName()))
            format->findAllTypesForFile (types, pluginFile.getFullPathName());
    if (types.isEmpty())
    {
        std::cout << "No plugin found in " << pluginFile.getFullPathName() << "\n";
        return 1;
    }

    const auto& d = *types[0];
    std::cout << "Plugin: " << d.name << " " << d.version << " by " << d.manufacturerName << " (" << d.pluginFormatName
              << ", " << (d.isInstrument ? "instrument" : "effect") << ", category " << d.category << ")\n";

    juce::String error;
    auto plugin = formats.createPluginInstance (d, rate, block, error);
    if (plugin == nullptr)
    {
        std::cout << "Could not load: " << error << "\n";
        return 1;
    }
    plugin->enableAllBuses();
    plugin->setRateAndBufferSizeDetails (rate, block);
    plugin->prepareToPlay (rate, block);
    // some plugins only finish setting up once their editor exists: make one (never shown)
    std::unique_ptr<juce::AudioProcessorEditor> editor (plugin->hasEditor() ? plugin->createEditorAndMakeActive() : nullptr);
    std::cout << "Editor " << (editor != nullptr ? "created" : "none") << "; waiting 5 s for the plugin to load its sounds...\n";
    juce::MessageManager::getInstance()->runDispatchLoopUntil (5000);

    // what the plugin keeps in its state (how a host would select its sounds)
    juce::MemoryBlock state;
    plugin->getStateInformation (state);
    juce::String printable;
    for (size_t i = 0; i < juce::jmin<size_t> (state.getSize(), 1500); ++i)
    {
        const auto c = (char) state[i];
        printable << ((c >= 32 && c < 127) ? juce::String::charToString (c) : juce::String ("."));
    }
    std::cout << "State: " << (int) state.getSize() << " bytes, starting: " << printable.substring (0, 120) << "\n";
    // a VST3 state wraps the plugin's own data, encoded, in an <IComponent> element
    if (auto xml = juce::parseXML (printable.fromFirstOccurrenceOf ("<?xml", true, false)
                                   + juce::String::createStringFromData ((const char*) state.getData() + juce::jmin<size_t> (state.getSize(), 1500),
                                                                         (int) state.getSize() - juce::jmin<int> ((int) state.getSize(), 1500))))
        if (auto* component = xml->getChildByName ("IComponent"))
        {
            juce::MemoryBlock inner;
            if (inner.fromBase64Encoding (component->getAllSubText().trim()))
            {
                juce::String text;
                for (size_t i = 0; i < inner.getSize(); ++i)
                {
                    const auto c = (char) inner[i];
                    text << ((c >= 32 && c < 127) ? juce::String::charToString (c) : juce::String ("."));
                }
                // skip the long run of MIDI-mapping slots at the start, if any
                const auto body = text.contains ("<MASTER_TREE") ? text.fromFirstOccurrenceOf ("<MASTER_TREE", true, false) : text;
                std::cout << "Plugin's own state (" << (int) inner.getSize() << " bytes): " << body.substring (0, 4000) << "\n";
            }
        }

    std::cout << "Inputs " << plugin->getTotalNumInputChannels() << ", outputs " << plugin->getTotalNumOutputChannels()
              << ", accepts MIDI " << (plugin->acceptsMidi() ? "yes" : "no") << ", latency " << plugin->getLatencySamples()
              << " samples, parameters " << plugin->getParameters().size() << ", programs " << plugin->getNumPrograms() << "\n";

    // parameters that look like sound selection
    const auto& parameters = plugin->getParameters();
    for (int i = 0; i < parameters.size(); ++i)
    {
        const auto name = parameters[i]->getName (64);
        if (i < 12 || name.containsIgnoreCase ("sound") || name.containsIgnoreCase ("instr")
            || name.containsIgnoreCase ("preset") || name.containsIgnoreCase ("program") || name.containsIgnoreCase ("library")
            || name.containsIgnoreCase ("layer") || name.containsIgnoreCase ("select"))
            std::cout << "  parameter " << i << ": " << name << " = " << parameters[i]->getCurrentValueAsText() << "\n";
    }

    juce::Array<int> chosen;
    for (int i = 0; i < plugin->getNumPrograms(); ++i)
    {
        const auto name = plugin->getProgramName (i);
        std::cout << "  program " << i << ": " << name << "\n";
        bool match = filters.isEmpty();
        for (const auto& f : filters)
            match = match || name.containsIgnoreCase (f);
        if (match && name.isNotEmpty())
            chosen.add (i);
    }
    if (plugin->getNumPrograms() <= 1)
        chosen = { -1 };  // no program list: play whatever is loaded

    auto report = [&] (const juce::String& name)
    {
        const auto file = outFolder.getChildFile (juce::File::createLegalFileName (d.name + " - " + name) + ".wav");
        const auto take = render (*plugin, file);
        std::cout << "Take '" << name << "': audio CPU mean " << juce::String (100.0 * take.meanLoad, 1) << " %, peak "
                  << juce::String (100.0 * take.peakLoad, 1) << " % of a " << block << "-sample block; level peak "
                  << juce::String (take.peak, 2) << ", RMS " << juce::String (take.rms, 3) << " -> "
                  << file.getFullPathName() << "\n";
    };

    if (! states.isEmpty())
    {
        // one take per state file: the plugin's own state, as a JUCE-built plugin's standalone app saves it
        for (const auto& stateFile : states)
        {
            juce::MemoryBlock component;
            if (! stateFile.loadFileAsData (component))
            {
                std::cout << "Cannot read " << stateFile.getFullPathName() << "\n";
                continue;
            }
            juce::XmlElement wrapper ("VST3PluginState");  // how JUCE's VST3 host stores a plugin's state
            wrapper.createNewChildElement ("IComponent")->addTextElement (component.toBase64Encoding());
            juce::MemoryBlock wrapped;
            juce::AudioProcessor::copyXmlToBinary (wrapper, wrapped);
            plugin->setStateInformation (wrapped.getData(), (int) wrapped.getSize());
            juce::MessageManager::getInstance()->runDispatchLoopUntil (4000);  // the sound loads in the background
            report (stateFile.getFileNameWithoutExtension());
        }
    }
    else
    {
        for (auto program : chosen)
        {
            juce::String name = "current sound";
            if (program >= 0)
            {
                plugin->setCurrentProgram (program);
                name = plugin->getProgramName (program);
                // give a program change time to load samples (message-thread work in many plugins)
                juce::MessageManager::getInstance()->runDispatchLoopUntil (1500);
            }
            report (name);
        }
    }

    editor.reset();  // the editor goes before its plugin
    plugin->releaseResources();
    return 0;
}
