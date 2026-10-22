#include "MainComponent.h"

namespace gt
{

namespace
{
    const char* const defaultPlugin = "C:\\Program Files\\Common Files\\VST3\\TONE3000.vst3";
    const char* const defaultKeysPlugin = "C:\\Program Files\\Common Files\\VST3\\Numa Player.vst3";
    const char* const defaultKeysSound = "2 - E_PIANO/1 - EP-Mark I";

    const char* slotKey (EngineCore::Slot slot)
    {
        switch (slot)
        {
            case EngineCore::Slot::guitar:    return "guitarAmp";
            case EngineCore::Slot::bass:      return "bassAmp";
            case EngineCore::Slot::bandDrums: return "bandDrums";
            case EngineCore::Slot::bandBass:  return "bandBass";
        }
        return "";
    }

    /** An instrument plugin for the band: the path in the settings, the VST3 folder, or the folder you
        downloaded it to next to the repository (monster_plugins), found by looking upwards from the engine. */
    juce::File findInstrument (const juce::String& setting, const juce::String& pattern)
    {
        if (setting.isNotEmpty())
            return juce::File (setting);
        juce::Array<juce::File> places { juce::File ("C:\\Program Files\\Common Files\\VST3") };
        for (auto dir = juce::File::getSpecialLocation (juce::File::currentExecutableFile).getParentDirectory(); dir.exists() && ! dir.isRoot();
             dir = dir.getParentDirectory())
            places.add (dir.getChildFile ("monster_plugins").getChildFile ("Library files"));
        for (const auto& place : places)
            for (const auto& f : place.findChildFiles (juce::File::findFilesAndDirectories, false, pattern))
                return f;
        return {};
    }

    /** A window holding a plugin's own editor; closing it only hides the editor, the plugin keeps running. */
    class PluginWindow final : public juce::DocumentWindow
    {
    public:
        PluginWindow (const juce::String& title, juce::AudioProcessorEditor* editor, std::function<void()> closed)
            : DocumentWindow (title, juce::Colours::darkgrey, DocumentWindow::closeButton), onClosed (std::move (closed))
        {
            setUsingNativeTitleBar (true);
            setContentOwned (editor, true);
            setResizable (editor->isResizable(), false);
            centreWithSize (getWidth(), getHeight());
            setVisible (true);
        }

        void closeButtonPressed() override { juce::MessageManager::callAsync (onClosed); }

    private:
        std::function<void()> onClosed;
    };
}

EngineOptions EngineOptions::fromCommandLine (const juce::StringArray& args)
{
    EngineOptions o;
    for (int i = 0; i < args.size(); ++i)
    {
        if (args[i] == "--silent")
            o.silent = true;
        else if (args[i] == "--record" && i + 1 < args.size())
            o.recording = juce::File::getCurrentWorkingDirectory().getChildFile (args[++i].unquoted());
        else if (args[i] == "--run-for" && i + 1 < args.size())
            o.runFor = args[++i].getDoubleValue();
        else if (args[i] == "--parent" && i + 1 < args.size())
            o.parent = args[++i].getIntValue();
    }
    return o;
}

MainComponent::MainComponent (const EngineOptions& startOptions)
    : options (startOptions)
{
    juce::PropertiesFile::Options fileOptions;
    fileOptions.applicationName = "GuitarTutor Engine";
    fileOptions.folderName = "GuitarTutor";
    fileOptions.filenameSuffix = ".settings";
    settings = std::make_unique<juce::PropertiesFile> (fileOptions);
    settings->getFile().getParentDirectory().createDirectory();
    settings->getFile().getSiblingFile ("engine.log").appendText ("\n--- " + juce::Time::getCurrentTime().toString (true, true) + "\n");

    logView.setMultiLine (true);
    logView.setReadOnly (true);
    logView.setFont (juce::Font (juce::FontOptions (13.0f)));
    addAndMakeVisible (logView);

    // audio device
    auto saved = settings->getXmlValue ("devices");
    if (const auto error = options.silent ? engine.openSilentDevice (options.recording) : engine.openDevice (saved.get()); error.isNotEmpty())
        log ("Audio device: " + error);
    if (options.silent)
        log ("Silent: no audio interface" + (options.recording != juce::File() ? ", recording to " + options.recording.getFullPathName() : juce::String()));
    log (engine.deviceSummary());
    selector = std::make_unique<juce::AudioDeviceSelectorComponent> (engine.devices(), 1, 2, 2, 2, false, false, false, false);
    addAndMakeVisible (*selector);

    // guitar input channel
    addAndMakeVisible (inputLabel);
    addAndMakeVisible (inputBox);
    engine.setGuitarInput (settings->getIntValue ("guitarInput", 1));
    refreshInputs();
    inputBox.onChange = [this] { engine.setGuitarInput (inputBox.getSelectedId() - 1); };
    engine.onDeviceChanged = [this]
    {
        log (engine.deviceSummary());
        refreshInputs();
    };

    // the two TONE3000 instances, with the state they had last time (an older tone A becomes the guitar's)
    const juce::File pluginFile (settings->getValue ("plugin", defaultPlugin));
    if (const auto error = engine.loadPlugins (pluginFile); error.isNotEmpty())
        log (error);
    else if (auto* p = engine.plugin (EngineCore::Slot::guitar))
        log ("Loaded 2 x " + p->getName() + " (" + juce::String (p->getTotalNumInputChannels()) + " in, "
             + juce::String (p->getTotalNumOutputChannels()) + " out, latency "
             + juce::String (p->getLatencySamples()) + " samples)");
    for (auto slot : { EngineCore::Slot::guitar, EngineCore::Slot::bass })
    {
        auto value = settings->getValue (slotKey (slot));
        if (value.isEmpty() && slot == EngineCore::Slot::guitar)
            value = settings->getValue ("toneA");
        juce::MemoryBlock state;
        if (state.fromBase64Encoding (value))
            engine.setPluginState (slot, state);
    }

    // the band's drums and bass on instrument plugins, if you have them; the band plays them once you have
    // chosen a kit and a bass in their windows (until then, and without them, it plays its synthesized ones)
    for (auto [slot, pattern, what] : { std::tuple { EngineCore::Slot::bandDrums, "MONSTER Drums*.vst3", "drums" },
                                        std::tuple { EngineCore::Slot::bandBass, "MONSTER Bass*.vst3", "bass" } })
    {
        const auto file = findInstrument (settings->getValue (juce::String (slotKey (slot)) + "Plugin"), pattern);
        if (file == juce::File())
        {
            log (juce::String ("Band ") + what + ": synthesized (no instrument plugin found)");
            continue;
        }
        if (const auto error = engine.loadBandInstrument (slot, file); error.isNotEmpty())
        {
            log (juce::String ("Band ") + what + ": synthesized (" + error + ")");
            continue;
        }
        blankStates[slot] = engine.pluginState (slot);
        juce::MemoryBlock state;
        const auto chosen = state.fromBase64Encoding (settings->getValue (slotKey (slot))) && state.getSize() > 0;
        if (chosen)
            engine.setPluginState (slot, state);
        engine.useBandInstrument (slot, chosen);
        log (juce::String ("Band ") + what + ": " + engine.plugin (slot)->getName()
             + (chosen ? juce::String() : ", choose its sound with \"Band " + juce::String (what) + "...\" (synthesized until then)"));
    }

    // the band's keys
    if (const auto error = engine.loadKeys (juce::File (settings->getValue ("keysPlugin", defaultKeysPlugin))); error.isNotEmpty())
        log (error + " (the band plays without keys)");
    else if (const auto soundError = engine.setKeysSound (settings->getValue ("keysSound", defaultKeysSound)); soundError.isNotEmpty())
        log ("Keys: " + soundError);
    else
        log ("Keys: Numa Player, " + engine.keysSound());

    for (auto* c : std::initializer_list<juce::Component*> { &guitarButton, &bassButton, &bandDrumsButton, &bandBassButton,
                                                             &presetLabel, &presetBox, &previousButton, &nextButton })
        addAndMakeVisible (*c);
    guitarButton.onClick = [this] { showPlugin (EngineCore::Slot::guitar, "Guitar tone"); };
    bassButton.onClick = [this] { showPlugin (EngineCore::Slot::bass, "Bass amp"); };
    bandDrumsButton.onClick = [this] { showPlugin (EngineCore::Slot::bandDrums, "Band drums"); };
    bandBassButton.onClick = [this] { showPlugin (EngineCore::Slot::bandBass, "Band bass"); };
    bandDrumsButton.setEnabled (engine.plugin (EngineCore::Slot::bandDrums) != nullptr);
    bandBassButton.setEnabled (engine.plugin (EngineCore::Slot::bandBass) != nullptr);
    // the bass plugin has its own amp and effects: straight to the mixer unless you want the bass amp too
    engine.setBassPluginThroughAmp (settings->getBoolValue ("bassPluginThroughAmp", false));
    bassThroughAmp.setToggleState (engine.bassPluginThroughAmp(), juce::dontSendNotification);
    bassThroughAmp.setEnabled (bandBassButton.isEnabled());
    bassThroughAmp.onClick = [this]
    {
        engine.setBassPluginThroughAmp (bassThroughAmp.getToggleState());
        log (juce::String ("Band bass plugin ") + (bassThroughAmp.getToggleState() ? "through the bass amp" : "straight to the mixer"));
        if (! engine.isSilent())
            settings->setValue ("bassPluginThroughAmp", bassThroughAmp.getToggleState());
    };
    addAndMakeVisible (bassThroughAmp);
    previousButton.onClick = [this] { stepPreset (-1); };
    nextButton.onClick = [this] { stepPreset (1); };
    presetBox.onChange = [this] { selectPreset (presetBox.getSelectedId() - 1); };
    refreshPresets();
    log (juce::String ((int) presetList.size()) + " presets saved in TONE3000");

    for (auto* l : { &latencyLabel, &cpuLabel, &footLabel, &gameLabel, &bandLabel })
    {
        l->setFont (juce::Font (juce::FontOptions (15.0f)));
        addAndMakeVisible (*l);
    }
    footLabel.setText ("Footswitch: none found (any HID game controller works)", juce::dontSendNotification);

    // the band's mixer: your faders, on top of the level the game gives each sound; remembered between runs
    for (size_t i = 0; i < faders.size(); ++i)
    {
        const juce::String part = i == 0 ? "drums" : i == 1 ? "bass" : "keys";
        faderLabels[i].setText (part.substring (0, 1).toUpperCase() + part.substring (1), juce::dontSendNotification);
        addAndMakeVisible (faderLabels[i]);
        auto& f = faders[i];
        f.setSliderStyle (juce::Slider::LinearHorizontal);
        f.setTextBoxStyle (juce::Slider::TextBoxRight, false, 64, 20);
        f.setRange (0.0, 2.0, 0.01);
        f.setDoubleClickReturnValue (true, 1.0);  // back to the balanced mix
        f.textFromValueFunction = [] (double v)
        {
            return v < 0.001 ? juce::String ("off") : juce::String (juce::Decibels::gainToDecibels (v), 1) + " dB";
        };
        f.valueFromTextFunction = [] (const juce::String& text)
        {
            return text.trim() == "off" ? 0.0 : juce::Decibels::decibelsToGain (text.getDoubleValue());
        };
        const auto level = (float) settings->getDoubleValue ("fader." + part, 1.0);
        engine.setFader (part, level);
        f.setValue (level, juce::dontSendNotification);
        f.updateText();
        f.onValueChange = [this, &f, part]
        {
            engine.setFader (part, (float) f.getValue());
            if (! engine.isSilent())  // a test run leaves your settings as they are
                settings->setValue ("fader." + part, f.getValue());
        };
        addAndMakeVisible (f);
    }
    log ("Faders: drums " + faders[0].getTextFromValue (faders[0].getValue()) + ", bass " + faders[1].getTextFromValue (faders[1].getValue())
         + ", keys " + faders[2].getTextFromValue (faders[2].getValue()));

    // footswitch: buttons 1 and 2 step through the tone presets here; the others are the game's to act on
    footswitch.onDevice = [this] (const juce::String& name)
    {
        footLabel.setText ("Footswitch: " + (name.isEmpty() ? juce::String ("none found") : name), juce::dontSendNotification);
        log (name.isEmpty() ? "Footswitch disconnected" : "Footswitch: " + name);
    };
    footswitch.onButton = [this] (int button, bool down)
    {
        log ("Footswitch button " + juce::String (button) + (down ? " down" : " up"));
        if (! down)
            return;
        if (button == 1)
            stepPreset (1);
        else if (button == 2)
            stepPreset (-1);
        else if (button == 3)
            sendCommand ("playPause");
        else if (button == 4)
            sendCommand ("sectionPrevious");
        else if (button == 5)
            sendCommand ("sectionNext");
        else if (button == 6)
            sendCommand ("restart");
    };
    footswitch.start();

    // the game
    gameLabel.setText ("Game: waiting on port " + juce::String (GameLink::port), juce::dontSendNotification);
    game.onConnection = [this] (bool connected)
    {
        if (! connected && ! game.isListening())
            log ("Cannot listen for the game on port " + juce::String (GameLink::port) + " (another engine running?)");
        gameLabel.setText (connected ? "Game: connected" : "Game: waiting on port " + juce::String (GameLink::port), juce::dontSendNotification);
        log (connected ? "Game connected" : "Game gone");
        if (! connected)
            engine.band().pause();
    };
    game.onMessage = [this] (const juce::var& message) { handle (message); };
    game.start();

    if (options.runFor > 0.0)
        juce::Timer::callAfterDelay ((int) (options.runFor * 1000.0), [] { juce::JUCEApplication::getInstance()->systemRequestedQuit(); });

    // started by the game: it lives as long as the game does
    if (options.parent > 0)
    {
        parentWatch = std::make_unique<ParentWatch> (options.parent, [this]
        {
            log ("The game that started the engine is gone");
            juce::JUCEApplication::getInstance()->systemRequestedQuit();
        });
        log ("Started by the game (process " + juce::String (options.parent) + ")"
             + (parentWatch->isWatching() ? juce::String() : ", which is already gone"));
        if (! parentWatch->isWatching())
            juce::MessageManager::callAsync ([] { juce::JUCEApplication::getInstance()->systemRequestedQuit(); });
    }

    setSize (760, 760);
    startTimerHz (4);
}

MainComponent::~MainComponent()
{
    stopTimer();
    saveSettings();
    windows.clear();  // editors go before their plugins
    footswitch.stop();
    engine.shutdown();
}

void MainComponent::handle (const juce::var& message)
{
    const auto type = message["type"].toString();
    auto& band = engine.band();
    if (type == "hello")
    {
        auto* answer = new juce::DynamicObject();
        answer->setProperty ("type", "hello");
        answer->setProperty ("engine", JUCE_APPLICATION_NAME_STRING);
        answer->setProperty ("protocol", 1);
        answer->setProperty ("sampleRate", engine.latency().sampleRate);
        answer->setProperty ("clock", "Local\\GuitarTutorClock");
        game.send (juce::var (answer));
    }
    else if (type == "load")
    {
        juce::String error;
        auto score = Score::fromMessage (message, error);
        if (score == nullptr)
        {
            log ("Game sent a song the band cannot read: " + error);
            return;
        }
        if (score->keys.isNotEmpty())
            if (const auto keysError = engine.setKeysSound (score->keys); keysError.isNotEmpty())
                log ("Keys: " + keysError);
        log ("Song: " + score->title + ", " + score->style + ", " + juce::String ((int) score->notes.size()) + " notes");
        const auto notes = (int) score->notes.size();
        band.load (std::move (score));
        auto* answer = new juce::DynamicObject();
        answer->setProperty ("type", "loaded");
        answer->setProperty ("notes", notes);
        answer->setProperty ("keys", engine.keysSound());
        game.send (juce::var (answer));
    }
    else if (type == "play")
    {
        log ("Play from quarter " + juce::String ((double) message["from"], 2) + ", count-in " + juce::String ((double) message["countIn"], 2));
        band.play ((double) message["from"], (double) message["countIn"]);
    }
    else if (type == "pause")
    {
        log ("Pause at quarter " + juce::String (band.position(), 2));
        band.pause();
    }
    else if (type == "seek")
    {
        log ("Go to quarter " + juce::String ((double) message["to"], 2));
        band.seek ((double) message["to"]);
    }
    else if (type == "speed")
    {
        log ("Speed " + juce::String ((double) message["value"], 2));
        band.setSpeed ((double) message["value"]);
    }
    else if (type == "mix")
        engine.setSoundLevel (message["part"].toString(), (float) (double) message["gain"]);
    else if (type == "window")
    {
        const auto which = message["show"].toString();
        if (which == "guitar")
            showPlugin (EngineCore::Slot::guitar, "Guitar tone");
        else if (which == "bass")
            showPlugin (EngineCore::Slot::bass, "Bass amp");
        else if (which == "drums")
            showPlugin (EngineCore::Slot::bandDrums, "Band drums");
        else if (which == "bandBass")
            showPlugin (EngineCore::Slot::bandBass, "Band bass");
        else if (auto* window = getTopLevelComponent())  // "controlPanel": this window
        {
            window->setVisible (true);
            window->toFront (true);
        }
    }
    else if (type == "quit")
    {
        log ("The game asked the engine to close");
        juce::JUCEApplication::getInstance()->systemRequestedQuit();
    }
}

void MainComponent::sendCommand (const juce::String& command)
{
    auto* message = new juce::DynamicObject();
    message->setProperty ("type", "command");
    message->setProperty ("command", command);
    game.send (juce::var (message));
}

void MainComponent::saveSettings()
{
    if (engine.isSilent())
        return;  // a test run leaves your settings as they are
    if (auto xml = engine.deviceState())
        settings->setValue ("devices", xml.get());
    settings->setValue ("guitarInput", engine.guitarInput());
    for (auto slot : { EngineCore::Slot::guitar, EngineCore::Slot::bass })
    {
        const auto state = engine.pluginState (slot);
        if (state.getSize() > 0)
            settings->setValue (slotKey (slot), state.toBase64Encoding());
    }
    // the band's instruments once a sound is chosen in them
    for (auto slot : { EngineCore::Slot::bandDrums, EngineCore::Slot::bandBass })
        if (engine.usesBandInstrument (slot))
            settings->setValue (slotKey (slot), engine.pluginState (slot).toBase64Encoding());
    settings->removeValue ("toneA");
    settings->removeValue ("toneB");
    settings->saveIfNeeded();
}

void MainComponent::refreshInputs()
{
    inputBox.clear (juce::dontSendNotification);
    const auto names = engine.inputChannelNames();
    for (int i = 0; i < names.size(); ++i)
        inputBox.addItem (juce::String (i + 1) + ": " + names[i], i + 1);
    inputBox.setSelectedId (engine.guitarInput() + 1, juce::dontSendNotification);
}

void MainComponent::refreshPresets()
{
    presetList = engine.presets();
    presetBox.clear (juce::dontSendNotification);
    for (const auto& p : presetList)
        presetBox.addItem (juce::String (p.index + 1) + ": " + p.name, p.index + 1);
    presetBox.setTextWhenNothingSelected (presetList.empty() ? "No presets saved in TONE3000 yet" : "Choose a preset");
    presetBox.setSelectedId (engine.currentPreset() + 1, juce::dontSendNotification);
}

void MainComponent::selectPreset (int index)
{
    if (index < 0)
        return;
    engine.selectPreset (index);
    presetBox.setSelectedId (index + 1, juce::dontSendNotification);
    for (const auto& p : presetList)
        if (p.index == index)
            log ("Preset " + juce::String (index + 1) + ": " + p.name);
}

void MainComponent::stepPreset (int step)
{
    refreshPresets();  // presets saved in the plugin window since the last look
    if (presetList.empty())
    {
        log ("No presets saved in TONE3000 yet");
        return;
    }
    const auto current = engine.currentPreset();
    int at = 0;
    for (int i = 0; i < (int) presetList.size(); ++i)
        if (presetList[(size_t) i].index == current)
            at = i;
    const auto n = (int) presetList.size();
    selectPreset (presetList[(size_t) (((at + step) % n + n) % n)].index);
}

void MainComponent::showPlugin (EngineCore::Slot slot, const juce::String& title)
{
    if (auto it = windows.find (slot); it != windows.end())
    {
        it->second->toFront (true);
        return;
    }
    auto* p = engine.plugin (slot);
    if (p == nullptr)
    {
        log ("No plugin loaded");
        return;
    }
    if (auto* editor = p->createEditorAndMakeActive())
        windows[slot] = std::make_unique<PluginWindow> (title, editor, [this, slot, title]
        {
            windows.erase (slot);
            refreshPresets();
            // a band instrument plays for the band once its sound has been chosen
            auto blank = blankStates.find (slot);
            if (blank != blankStates.end() && ! engine.usesBandInstrument (slot) && engine.pluginState (slot) != blank->second)
            {
                engine.useBandInstrument (slot, true);
                log (title + ": the band plays it now");
            }
        });
}

void MainComponent::timerCallback()
{
    const auto l = engine.latency();
    const auto latency = juce::String::formatted ("Latency: input %.1f ms + output %.1f ms + plugin %.1f ms = %.1f ms round trip "
                                                  "(buffer %d at %.0f Hz)",
                                                  l.ms (l.input), l.ms (l.output), l.ms (l.plugin), l.roundTripMs(),
                                                  l.buffer, l.sampleRate);
    const auto cpu = juce::String::formatted ("Audio CPU: %.0f %%", 100.0 * engine.cpuLoad());
    latencyLabel.setText (latency, juce::dontSendNotification);
    cpuLabel.setText (cpu, juce::dontSendNotification);
    auto& band = engine.band();
    const auto* score = band.score();
    bandLabel.setText (score == nullptr ? juce::String ("Band: no song yet")
                                        : "Band: " + score->title + " (" + score->style + "), " + (band.isPlaying() ? "playing" : "stopped")
                                              + " at quarter " + juce::String (band.position(), 1),
                       juce::dontSendNotification);
    // into the log file too: two seconds after start, then every ten seconds while the band plays
    if (++ticks == 8 || (band.isPlaying() && ticks % 40 == 0))
        log (latency + ", " + cpu);
}

void MainComponent::log (const juce::String& line)
{
    const auto text = juce::Time::getCurrentTime().toString (false, true, true, true) + "  " + line + "\n";
    logView.moveCaretToEnd();
    logView.insertTextAtCaret (text);
    settings->getFile().getSiblingFile ("engine.log").appendText (text);  // also kept next to the settings
}

void MainComponent::paint (juce::Graphics& g)
{
    g.fillAll (getLookAndFeel().findColour (juce::ResizableWindow::backgroundColourId));
}

void MainComponent::resized()
{
    auto r = getLocalBounds().reduced (10);
    selector->setBounds (r.removeFromTop (300));
    r.removeFromTop (6);

    auto row = r.removeFromTop (28);
    inputLabel.setBounds (row.removeFromLeft (100));
    inputBox.setBounds (row.removeFromLeft (260));
    r.removeFromTop (8);

    row = r.removeFromTop (30);
    const auto fifth = row.getWidth() / 5;
    guitarButton.setBounds (row.removeFromLeft (fifth).reduced (3, 0));
    bassButton.setBounds (row.removeFromLeft (fifth).reduced (3, 0));
    bandDrumsButton.setBounds (row.removeFromLeft (fifth).reduced (3, 0));
    bandBassButton.setBounds (row.removeFromLeft (fifth).reduced (3, 0));
    bassThroughAmp.setBounds (row.reduced (3, 0));
    r.removeFromTop (6);

    row = r.removeFromTop (30);
    presetLabel.setBounds (row.removeFromLeft (60));
    nextButton.setBounds (row.removeFromRight (90).reduced (3, 0));
    previousButton.setBounds (row.removeFromRight (90).reduced (3, 0));
    presetBox.setBounds (row.reduced (3, 0));
    r.removeFromTop (6);

    r.removeFromTop (4);

    latencyLabel.setBounds (r.removeFromTop (24));
    cpuLabel.setBounds (r.removeFromTop (24));
    footLabel.setBounds (r.removeFromTop (24));
    gameLabel.setBounds (r.removeFromTop (24));
    bandLabel.setBounds (r.removeFromTop (24));
    r.removeFromTop (4);
    row = r.removeFromTop (26);
    const auto third = row.getWidth() / 3;
    for (size_t i = 0; i < faders.size(); ++i)
    {
        auto cell = row.removeFromLeft (third);
        faderLabels[i].setBounds (cell.removeFromLeft (50));
        faders[i].setBounds (cell.reduced (2, 0));
    }
    r.removeFromTop (6);
    logView.setBounds (r);
}

} // namespace gt
