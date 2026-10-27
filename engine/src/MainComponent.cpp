#include "MainComponent.h"
#include "WindowFront.h"

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
            case EngineCore::Slot::guitar:     return "guitarAmp";
            case EngineCore::Slot::bandAmp:    return "bandGuitarAmp";
            case EngineCore::Slot::bandDrums:  return "bandDrums";
            case EngineCore::Slot::bandBass:   return "bandBass";
            case EngineCore::Slot::bandGuitar: return "bandGuitar";
        }
        return "";
    }

    /** Where a band instrument plugin's sound is kept: one per plugin ("bandBass.ABPL"). */
    juce::String stateKey (EngineCore::Slot slot, const juce::File& plugin)
    {
        return juce::String (slotKey (slot)) + "." + plugin.getFileNameWithoutExtension();
    }

    juce::String bandTitle (EngineCore::Slot slot)
    {
        return slot == EngineCore::Slot::bandDrums ? "Band drums" : slot == EngineCore::Slot::bandBass ? "Band bass" : "Band guitar";
    }

    /** The instrument plugins the band knows, offered when you have them; any other can be added with "Other
        plugin...". A MONSTER plugin (and Kontakt) is silent until you choose a sound in it, Ample Bass plays its
        own from the start. Each has a sound level that puts it as loud as the band's built-in sound, measured
        straight to the mixer with their default sounds; a guitar also has a trim into the band guitar's amp, to
        a guitar's level (MONSTER Guitar plays about 30 dB louder than the built-in guitar; Kontakt not measured
        yet). Through the amp a guitar plugin then plays at the built-in guitar's level. */
    struct KnownPlugin { EngineCore::Slot slot; const char* pattern; const char* name; bool silentUntilChosen; float level, trim; };
    const KnownPlugin knownPlugins[] { { EngineCore::Slot::bandDrums, "MONSTER Drums*.vst3", "MONSTER Drums", true, 0.8f, 1.0f },
                                       { EngineCore::Slot::bandBass, "MONSTER Bass*.vst3", "MONSTER Bass", true, 0.16f, 1.0f },
                                       { EngineCore::Slot::bandBass, "ABPL*.vst3", "Ample Bass P Lite", false, 0.21f, 1.0f },
                                       { EngineCore::Slot::bandGuitar, "MONSTER Guitar*.vst3", "MONSTER Guitar", true, 0.03f, 0.03f },
                                       { EngineCore::Slot::bandGuitar, "Kontakt*.vst3", "Kontakt (Shreddage)", true, 0.8f, 1.0f } };
    constexpr float guitarLevelThroughAmp = 0.8f;  // as the built-in guitar's

    /** How early (seconds) the band guitar's notes go to a plugin: Kontakt's strummed guitars sound a little late (the
        first strum is on the beat, its loud part builds about 25-40 ms later, measured with a recorded session), so
        they go 20 ms ahead. The setting bandGuitarAdvanceMs overrides it (0 for none). */
    double guitarAdvanceSeconds (const juce::File& plugin, const juce::String& setting)
    {
        if (setting.isNotEmpty())
            return juce::jlimit (0.0, 200.0, setting.getDoubleValue()) / 1000.0;
        return plugin.getFileName().startsWithIgnoreCase ("Kontakt") ? 0.02 : 0.0;
    }

    /** A plugin you add: drums and guitar at the built-in sound's level, a bass at about the known basses' (they
        play loud). */
    float addedPluginLevel (EngineCore::Slot slot) { return slot == EngineCore::Slot::bandBass ? 0.2f : 0.8f; }

    // a band part's dropdown: its built-in sound, then its plugins (ids from 2), then these
    constexpr int builtInId = 1, newSoundId = 900, otherPluginId = 901;

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
            bringToFront (*this);
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
        else if (args[i] == "--band-bass" && i + 1 < args.size())
            o.bandBass = args[++i].unquoted();
        else if (args[i] == "--band-guitar" && i + 1 < args.size())
            o.bandGuitar = args[++i].unquoted();
        else if (args[i] == "--snapshot" && i + 1 < args.size())
            o.snapshot = juce::File::getCurrentWorkingDirectory().getChildFile (args[++i].unquoted());
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

    // the two TONE3000 instances, yours and the band guitarist's, with the state they had last time (an older
    // tone A becomes yours; the band guitarist starts with your tone)
    const juce::File pluginFile (settings->getValue ("plugin", defaultPlugin));
    if (const auto error = engine.loadPlugins (pluginFile); error.isNotEmpty())
        log (error);
    else if (auto* p = engine.plugin (EngineCore::Slot::guitar))
        log ("Loaded 2 x " + p->getName() + " (" + juce::String (p->getTotalNumInputChannels()) + " in, "
             + juce::String (p->getTotalNumOutputChannels()) + " out, latency "
             + juce::String (p->getLatencySamples()) + " samples)");
    for (auto slot : { EngineCore::Slot::guitar, EngineCore::Slot::bandAmp })
    {
        auto value = settings->getValue (slotKey (slot));
        if (value.isEmpty())
            value = settings->getValue (slot == EngineCore::Slot::guitar ? "toneA" : slotKey (EngineCore::Slot::guitar));
        juce::MemoryBlock state;
        if (state.fromBase64Encoding (value))
            engine.setPluginState (slot, state);
    }

    // the band's drums, bass and guitar: their built-in sounds, or instrument plugins chosen in the control panel
    setUpBandPart (EngineCore::Slot::bandDrums);
    setUpBandPart (EngineCore::Slot::bandBass);
    setUpBandPart (EngineCore::Slot::bandGuitar);
    engine.setBandGuitarPlays (settings->getBoolValue ("bandGuitarPlays", false));
    guitarPlays.setToggleState (engine.bandGuitarPlays(), juce::dontSendNotification);
    guitarPlays.onClick = [this] { setBandGuitarPlays (guitarPlays.getToggleState()); };
    addAndMakeVisible (guitarPlays);

    // the band's keys
    if (const auto error = engine.loadKeys (juce::File (settings->getValue ("keysPlugin", defaultKeysPlugin))); error.isNotEmpty())
        log (error + " (the band plays without keys)");
    else if (const auto soundError = engine.setKeysSound (settings->getValue ("keysSound", defaultKeysSound)); soundError.isNotEmpty())
        log ("Keys: " + soundError);
    else
        log ("Keys: Numa Player, " + engine.keysSound());

    for (auto* c : std::initializer_list<juce::Component*> { &guitarButton, &presetLabel, &presetBox, &previousButton,
                                                             &nextButton, &bandToneLabel, &bandToneBox, &bandToneButton })
        addAndMakeVisible (*c);
    guitarButton.onClick = [this] { showPlugin (EngineCore::Slot::guitar, "Guitar tone"); };
    bandToneButton.onClick = [this] { showPlugin (EngineCore::Slot::bandAmp, "Band guitar tone"); };
    // a guitar plugin with an amp of its own can skip the band guitar's
    engine.setGuitarPluginThroughAmp (settings->getBoolValue ("bandGuitarThroughAmp", true));
    guitarThroughAmp.setToggleState (engine.guitarPluginThroughAmp(), juce::dontSendNotification);
    guitarThroughAmp.onClick = [this]
    {
        engine.setGuitarPluginThroughAmp (guitarThroughAmp.getToggleState());
        if (const auto current = currentChoice (EngineCore::Slot::bandGuitar); current >= 0)
            engine.setBandPluginLevel (EngineCore::Slot::bandGuitar, guitarThroughAmp.getToggleState() ? guitarLevelThroughAmp
                                                                    : bandParts[EngineCore::Slot::bandGuitar].choices[(size_t) current].level);
        log (juce::String ("Band guitar plugin ") + (guitarThroughAmp.getToggleState() ? "through the band guitar tone" : "straight to the mixer"));
        if (! engine.isSilent())
            settings->setValue ("bandGuitarThroughAmp", guitarThroughAmp.getToggleState());
    };
    addAndMakeVisible (guitarThroughAmp);
    previousButton.onClick = [this] { stepPreset (-1); };
    nextButton.onClick = [this] { stepPreset (1); };
    presetBox.onChange = [this] { selectPreset (presetBox.getSelectedId() - 1); };
    bandToneBox.onChange = [this]
    {
        engine.selectPreset (bandToneBox.getSelectedId() - 1, EngineCore::Slot::bandAmp);
        log ("Band guitar tone: " + bandToneBox.getText());
    };
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
        const juce::String part = i == 0 ? "drums" : i == 1 ? "bass" : i == 2 ? "keys" : "guitar";
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
         + ", keys " + faders[2].getTextFromValue (faders[2].getValue()) + ", guitar " + faders[3].getTextFromValue (faders[3].getValue()));

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

    setSize (760, 820);
    startTimerHz (4);

    if (options.snapshot != juce::File())
        juce::Timer::callAfterDelay (3000, [self = juce::Component::SafePointer<MainComponent> (this)]
        {
            if (self == nullptr)
                return;
            const auto image = self->createComponentSnapshot (self->getLocalBounds());
            self->options.snapshot.deleteFile();
            if (juce::FileOutputStream out (self->options.snapshot); out.openedOk())
                juce::PNGImageFormat().writeImageToStream (image, out);
        });
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
        sendBandGuitar();  // the game shows whether the band guitarist plays
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
        log ("Song: " + score->title + ", " + score->style + ", " + juce::String ((int) score->notes.size()) + " notes"
             + ", " + juce::String ((int) score->voicings.size()) + " voicing notes"
             + ", " + juce::String ((int) score->strokes.size()) + " strokes"
             + (score->origin.isNotEmpty() ? ", from " + score->origin : juce::String()));
        const auto notes = (int) score->notes.size();
        const auto keys = score->keys.isNotEmpty() ? score->keys : engine.keysSound();
        band.load (std::move (score));
        // a new keys sound once the band has let go of the old one's notes (on its next block)
        if (keys != engine.keysSound())
            juce::Timer::callAfterDelay (50, [self = juce::Component::SafePointer<MainComponent> (this), keys]
            {
                if (self != nullptr)
                    if (const auto keysError = self->engine.setKeysSound (keys); keysError.isNotEmpty())
                        self->log ("Keys: " + keysError);
            });
        auto* answer = new juce::DynamicObject();
        answer->setProperty ("type", "loaded");
        answer->setProperty ("notes", notes);
        answer->setProperty ("keys", keys);
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
    else if (type == "bandBass")
    {
        const auto plugin = message["plugin"].toString();
        const auto choice = choiceFor (EngineCore::Slot::bandBass, plugin);
        if (choice >= 0 || plugin == "synthesized")
            chooseBandInstrument (EngineCore::Slot::bandBass, choice, true, false);
        else
            log ("The game asked for a bass plugin not found: " + plugin);
    }
    else if (type == "bandGuitar")
        setBandGuitarPlays ((bool) message["plays"]);
    else if (type == "window")
    {
        const auto which = message["show"].toString();
        if (which == "guitar")
            showPlugin (EngineCore::Slot::guitar, "Guitar tone");
        else if (which == "bandGuitarTone")
            showPlugin (EngineCore::Slot::bandAmp, "Band guitar tone");
        else if (which == "drums")
            showPlugin (EngineCore::Slot::bandDrums, "Band drums");
        else if (which == "bandBass")
            showPlugin (EngineCore::Slot::bandBass, "Band bass");
        else if (which == "bandGuitar")
            showPlugin (EngineCore::Slot::bandGuitar, "Band guitar");
        else if (auto* window = getTopLevelComponent())  // "controlPanel": this window
            bringToFront (*window);
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
    for (auto slot : { EngineCore::Slot::guitar, EngineCore::Slot::bandAmp })
    {
        const auto state = engine.pluginState (slot);
        if (state.getSize() > 0)
            settings->setValue (slotKey (slot), state.toBase64Encoding());
    }
    for (auto slot : { EngineCore::Slot::bandDrums, EngineCore::Slot::bandBass, EngineCore::Slot::bandGuitar })
        keepBandState (slot);
    settings->removeValue ("toneA");
    settings->removeValue ("toneB");
    settings->saveIfNeeded();
}

void MainComponent::setUpBandPart (EngineCore::Slot slot)
{
    auto& part = bandParts[slot];
    // the plugins the band knows that you have, then the ones you added
    for (const auto& known : knownPlugins)
        if (known.slot == slot)
            if (const auto file = findInstrument ({}, known.pattern); file != juce::File())
                part.choices.push_back ({ file, known.name, known.silentUntilChosen, known.level, known.trim, false });
    for (const auto& path : juce::StringArray::fromLines (settings->getValue (juce::String (slotKey (slot)) + "Added")))
        if (path.isNotEmpty() && juce::File (path).exists() && choiceFor (slot, path) < 0)
            part.choices.push_back ({ juce::File (path), juce::File (path).getFileNameWithoutExtension(), false, addedPluginLevel (slot), 1.0f, true });

    // your choice from last time: a plugin's path, or "synthesized" (else the first plugin found)
    auto last = settings->getValue (juce::String (slotKey (slot)) + "Plugin");
    if (slot == EngineCore::Slot::bandBass && options.bandBass.isNotEmpty())
        last = options.bandBass;
    if (slot == EngineCore::Slot::bandGuitar && options.bandGuitar.isNotEmpty())
        last = options.bandGuitar;
    auto choice = choiceFor (slot, last);
    if (choice < 0 && last != "synthesized" && ! part.choices.empty())
        choice = 0;

    part.label.setText (bandTitle (slot), juce::dontSendNotification);
    part.box.onChange = [this, slot] { bandBoxChanged (slot); };
    part.windowButton.onClick = [this, slot] { showPlugin (slot, bandTitle (slot)); };
    for (auto* c : std::initializer_list<juce::Component*> { &part.label, &part.box, &part.windowButton })
        addAndMakeVisible (*c);
    chooseBandInstrument (slot, choice, false, false);
}

void MainComponent::refreshBandPart (EngineCore::Slot slot)
{
    auto& part = bandParts[slot];
    const auto current = currentChoice (slot);
    part.box.clear (juce::dontSendNotification);
    part.box.addItem ("Built-in sound", builtInId);
    for (size_t i = 0; i < part.choices.size(); ++i)
        part.box.addItem (part.choices[i].name, (int) i + 2);
    part.box.addSeparator();
    // a MONSTER plugin shows its sound picker only while it has no sound: a new instance of it has none
    if (current >= 0 && part.choices[(size_t) current].silentUntilChosen)
        part.box.addItem ("Pick another sound in " + part.choices[(size_t) current].name + "...", newSoundId);
    part.box.addItem ("Other plugin...", otherPluginId);
    part.box.setSelectedId (current + 2, juce::dontSendNotification);  // the built-in sound when none
    part.windowButton.setEnabled (current >= 0);
    if (slot == EngineCore::Slot::bandGuitar)
        guitarThroughAmp.setEnabled (current >= 0);
}

void MainComponent::bandBoxChanged (EngineCore::Slot slot)
{
    const auto id = bandParts[slot].box.getSelectedId();
    if (id == newSoundId)
    {
        if (const auto current = currentChoice (slot); current >= 0 && chooseBandInstrument (slot, current, false, true))
            showPlugin (slot, bandTitle (slot));
        else
            refreshBandPart (slot);
    }
    else if (id == otherPluginId)
    {
        refreshBandPart (slot);  // the dropdown shows what plays until a plugin is chosen
        chooseOtherPlugin (slot);
    }
    else if (id >= builtInId && id - 2 < (int) bandParts[slot].choices.size())
        chooseBandInstrument (slot, id - 2, true, false);
}

void MainComponent::chooseOtherPlugin (EngineCore::Slot slot)
{
    pluginChooser = std::make_unique<juce::FileChooser> ("An instrument plugin for the " + bandTitle (slot).toLowerCase(),
                                                         juce::File ("C:\\Program Files\\Common Files\\VST3"), "*.vst3");
    pluginChooser->launchAsync (juce::FileBrowserComponent::openMode | juce::FileBrowserComponent::canSelectFiles,
                                [this, slot] (const juce::FileChooser& chooser)
    {
        auto file = chooser.getResult();
        if (file == juce::File())
            return;
        // a file inside a plugin's folder (ABPL.vst3\Contents\x86_64-win\ABPL.vst3): the plugin is the folder
        for (auto dir = file.getParentDirectory(); dir != dir.getParentDirectory(); dir = dir.getParentDirectory())
            if (dir.hasFileExtension ("vst3"))
                file = dir;
        auto& part = bandParts[slot];
        auto choice = choiceFor (slot, file.getFullPathName());
        const auto added = choice < 0;
        if (added)
        {
            part.choices.push_back ({ file, file.getFileNameWithoutExtension(), false, addedPluginLevel (slot), 1.0f, true });
            choice = (int) part.choices.size() - 1;
        }
        if (! chooseBandInstrument (slot, choice, true, false) && added)
        {
            part.choices.pop_back();  // not an instrument the band can play
            refreshBandPart (slot);
            return;
        }
        // the plugins you added stay in the dropdown
        juce::StringArray paths;
        for (const auto& c : part.choices)
            if (c.added)
                paths.add (c.file.getFullPathName());
        if (! engine.isSilent())
            settings->setValue (juce::String (slotKey (slot)) + "Added", paths.joinIntoString ("\n"));
    });
}

bool MainComponent::chooseBandInstrument (EngineCore::Slot slot, int choice, bool remember, bool fresh)
{
    auto& part = bandParts[slot];
    if (! fresh && choice >= 0 && choice == currentChoice (slot))
        return true;
    windows.erase (slot);  // the editor goes before its plugin
    keepBandState (slot);
    engine.unloadBandInstrument (slot);
    bandPlugins.erase (slot);
    blankStates.erase (slot);
    // a strum-capable guitar (Shreddage in strumming mode) hears the strokes as strum keyswitches on its
    // held voicing, instead of the per-string notes
    if (slot == EngineCore::Slot::bandGuitar)
        engine.setBandGuitarStrummingMode (choice >= 0
                                           && part.choices[(size_t) choice].file.getFileName().startsWithIgnoreCase ("Kontakt"));
    auto playsPlugin = false;
    if (choice >= 0)
    {
        auto& c = part.choices[(size_t) choice];
        engine.setBandPluginLevel (slot, slot == EngineCore::Slot::bandGuitar && engine.guitarPluginThroughAmp() ? guitarLevelThroughAmp : c.level);
        if (slot == EngineCore::Slot::bandGuitar)
        {
            engine.setGuitarPluginTrim (c.trim);
            const auto advance = guitarAdvanceSeconds (c.file, settings->getValue ("bandGuitarAdvanceMs"));
            engine.setBandGuitarAdvance (advance);
            if (advance > 0.0)
                log ("Band guitar plugin plays " + juce::String (juce::roundToInt (advance * 1000)) + " ms ahead");
        }
        playsPlugin = startBandInstrument (slot, c.file, c.silentUntilChosen, ! fresh);
        if (playsPlugin && c.added)
            c.name = engine.plugin (slot)->getName();
    }
    else
        log (bandTitle (slot) + ": built-in sound");
    if (remember && ! engine.isSilent())
        settings->setValue (juce::String (slotKey (slot)) + "Plugin",
                            playsPlugin ? part.choices[(size_t) choice].file.getFullPathName() : juce::String ("synthesized"));
    refreshBandPart (slot);
    return playsPlugin || choice < 0;
}

int MainComponent::choiceFor (EngineCore::Slot slot, const juce::String& plugin) const
{
    if (const auto part = bandParts.find (slot); part != bandParts.end())
        for (size_t i = 0; i < part->second.choices.size(); ++i)
            if (const auto& f = part->second.choices[i].file; f.getFullPathName() == plugin || f.getFileNameWithoutExtension() == plugin)
                return (int) i;
    return -1;
}

int MainComponent::currentChoice (EngineCore::Slot slot) const
{
    const auto loaded = bandPlugins.find (slot);
    return loaded == bandPlugins.end() ? -1 : choiceFor (slot, loaded->second.getFullPathName());
}

bool MainComponent::startBandInstrument (EngineCore::Slot slot, const juce::File& file, bool silentUntilChosen, bool restoreSound)
{
    if (const auto error = engine.loadBandInstrument (slot, file); error.isNotEmpty())
    {
        log (bandTitle (slot) + ": built-in sound (" + error + ")");
        return false;
    }
    bandPlugins[slot] = file;
    blankStates[slot] = engine.pluginState (slot);
    // its sound from last time (a MONSTER plugin's from before each plugin kept its own: under the slot's name)
    auto value = restoreSound ? settings->getValue (stateKey (slot, file)) : juce::String();
    if (restoreSound && value.isEmpty() && file.getFileName().startsWith ("MONSTER"))
        value = settings->getValue (slotKey (slot));
    juce::MemoryBlock state;
    const auto saved = state.fromBase64Encoding (value) && state.getSize() > 0;
    if (saved)
    {
        // the host wrapper only takes states in its own XML format and refuses anything else without a word
        if (juce::AudioProcessor::getXmlFromBinary (state.getData(), (int) state.getSize()) == nullptr)
            log (bandTitle (slot) + ": saved sound not in the host's state format: not restored");
        else
        {
            engine.setPluginState (slot, state);
            log (bandTitle (slot) + ": restored saved sound (" + juce::String (state.getSize()) + " bytes)");
        }
    }
    const auto plays = saved || ! silentUntilChosen;
    engine.useBandInstrument (slot, plays);
    log (bandTitle (slot) + ": " + engine.plugin (slot)->getName()
         + (plays ? juce::String() : ", choose its sound in its window (the built-in sound plays until then)"));
    return true;
}

void MainComponent::keepBandState (EngineCore::Slot slot)
{
    // a band instrument's sound, once it plays for the band (a test run leaves your settings as they are)
    if (const auto it = bandPlugins.find (slot); it != bandPlugins.end() && engine.usesBandInstrument (slot) && ! engine.isSilent())
    {
        const auto state = engine.pluginState (slot);
        settings->setValue (stateKey (slot, it->second), state.toBase64Encoding());
        log (bandTitle (slot) + ": saved sound (" + juce::String (state.getSize()) + " bytes)");
    }
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
    // the band guitarist's tone: one of the same presets, in the other TONE3000
    bandToneBox.clear (juce::dontSendNotification);
    for (const auto& p : engine.presets (EngineCore::Slot::bandAmp))
        bandToneBox.addItem (juce::String (p.index + 1) + ": " + p.name, p.index + 1);
    bandToneBox.setTextWhenNothingSelected (bandToneBox.getNumItems() == 0 ? "No presets saved in TONE3000 yet" : "Choose a preset");
    bandToneBox.setSelectedId (engine.currentPreset (EngineCore::Slot::bandAmp) + 1, juce::dontSendNotification);
}

void MainComponent::setBandGuitarPlays (bool on)
{
    engine.setBandGuitarPlays (on);
    guitarPlays.setToggleState (on, juce::dontSendNotification);
    log (on ? "The band guitarist plays the rhythm" : "The band guitarist rests");
    if (! engine.isSilent())
        settings->setValue ("bandGuitarPlays", on);
    sendBandGuitar();
}

void MainComponent::sendBandGuitar()
{
    auto* message = new juce::DynamicObject();
    message->setProperty ("type", "bandGuitar");
    message->setProperty ("plays", engine.bandGuitarPlays());
    game.send (juce::var (message));
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
        bringToFront (*it->second);
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

    // your guitar: its input and tone, the tone's presets
    auto row = r.removeFromTop (28);
    inputLabel.setBounds (row.removeFromLeft (100));
    inputBox.setBounds (row.removeFromLeft (260));
    guitarButton.setBounds (row.removeFromLeft (140).reduced (6, 0));
    r.removeFromTop (6);

    row = r.removeFromTop (28);
    presetLabel.setBounds (row.removeFromLeft (100));
    presetBox.setBounds (row.removeFromLeft (260));
    previousButton.setBounds (row.removeFromLeft (140).reduced (6, 0));
    nextButton.setBounds (row.removeFromLeft (140).reduced (6, 0));
    r.removeFromTop (10);

    // the band's drums, bass and guitar, and the band guitarist's tone
    for (auto slot : { EngineCore::Slot::bandDrums, EngineCore::Slot::bandBass, EngineCore::Slot::bandGuitar })
    {
        auto& part = bandParts[slot];
        row = r.removeFromTop (28);
        part.label.setBounds (row.removeFromLeft (100));
        part.box.setBounds (row.removeFromLeft (260));
        part.windowButton.setBounds (row.removeFromLeft (140).reduced (6, 0));
        if (slot == EngineCore::Slot::bandGuitar)
            guitarThroughAmp.setBounds (row.reduced (6, 0));
        r.removeFromTop (6);
    }
    row = r.removeFromTop (28);
    bandToneLabel.setBounds (row.removeFromLeft (100));
    bandToneBox.setBounds (row.removeFromLeft (260));
    bandToneButton.setBounds (row.removeFromLeft (140).reduced (6, 0));
    guitarPlays.setBounds (row.reduced (6, 0));
    r.removeFromTop (6);

    r.removeFromTop (4);

    latencyLabel.setBounds (r.removeFromTop (24));
    cpuLabel.setBounds (r.removeFromTop (24));
    footLabel.setBounds (r.removeFromTop (24));
    gameLabel.setBounds (r.removeFromTop (24));
    bandLabel.setBounds (r.removeFromTop (24));
    r.removeFromTop (4);
    row = r.removeFromTop (26);
    const auto quarter = row.getWidth() / (int) faders.size();
    for (size_t i = 0; i < faders.size(); ++i)
    {
        auto cell = row.removeFromLeft (quarter);
        faderLabels[i].setBounds (cell.removeFromLeft (50));
        faders[i].setBounds (cell.reduced (2, 0));
    }
    r.removeFromTop (6);
    logView.setBounds (r);
}

} // namespace gt
