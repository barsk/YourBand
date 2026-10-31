#include "MainComponent.h"
#include "NumaKeys.h"
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
            case EngineCore::Slot::bandDrums:  return "bandDrums";
            case EngineCore::Slot::bandBass:   return "bandBass";
            case EngineCore::Slot::bandGuitar: return "bandGuitar";
            case EngineCore::Slot::bandLead1:  return "bandLead1";
            case EngineCore::Slot::bandLead2:  return "bandLead2";
            case EngineCore::Slot::bandKeys:   return "bandKeys";
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
        return slot == EngineCore::Slot::bandDrums ? "Drums" : slot == EngineCore::Slot::bandBass ? "Bass"
             : slot == EngineCore::Slot::bandLead1 ? "Lead guitar" : slot == EngineCore::Slot::bandLead2 ? "Instrument 1"
             : slot == EngineCore::Slot::bandKeys ? "Keyboards" : "Rhythm guitar";
    }

    /** A picture of a component (tests). */
    void writeSnapshot (juce::Component& c, const juce::File& file)
    {
        const auto image = c.createComponentSnapshot (c.getLocalBounds());
        file.deleteFile();
        if (juce::FileOutputStream out (file); out.openedOk())
            juce::PNGImageFormat().writeImageToStream (image, out);
    }

    /** The lead channels play the song's other instruments (a harmonica, a lead guitar) on a plugin of your choice, as
        stand-ins: there is no built-in sound for them. */
    bool isLead (EngineCore::Slot slot) { return slot == EngineCore::Slot::bandLead1 || slot == EngineCore::Slot::bandLead2; }

    /** A MONSTER plugin shows its sound picker only while it has no sound: another sound needs a new instance of it
        ("Pick another sound in ..."). Kontakt and Numa Player choose theirs in their window at any time. */
    bool picksSoundOnlyWhenNew (const juce::File& plugin) { return plugin.getFileName().startsWithIgnoreCase ("MONSTER"); }

    /** The keys and the leads play only on a plugin: their dropdown's first choice is off, not a built-in sound. */
    bool playsOnlyOnPlugin (EngineCore::Slot slot) { return isLead (slot) || slot == EngineCore::Slot::bandKeys; }

    /** A file inside a plugin's folder (ABPL.vst3\Contents\x86_64-win\ABPL.vst3): the plugin is the folder. */
    juce::File pluginOf (juce::File file)
    {
        for (auto dir = file.getParentDirectory(); dir != dir.getParentDirectory(); dir = dir.getParentDirectory())
            if (dir.hasFileExtension ("vst3"))
                file = dir;
        return file;
    }

    /** The instrument plugins the band knows, offered when you have them; any other can be added with "Other
        plugin...". A MONSTER plugin (and Kontakt) is silent until you choose a sound in it, Ample Bass plays its
        own from the start. Each has a sound level that puts it as loud as the band's built-in sound, measured
        straight to the mixer with their default sounds (a guitar plugin has an amp of its own). */
    struct KnownPlugin { EngineCore::Slot slot; const char* pattern; const char* name; bool silentUntilChosen; float level; };
    const KnownPlugin knownPlugins[] { { EngineCore::Slot::bandDrums, "MONSTER Drums*.vst3", "MONSTER Drums", true, 0.8f },
                                       { EngineCore::Slot::bandBass, "MONSTER Bass*.vst3", "MONSTER Bass", true, 0.16f },
                                       { EngineCore::Slot::bandBass, "ABPL*.vst3", "Ample Bass P Lite", false, 0.21f },
                                       { EngineCore::Slot::bandGuitar, "MONSTER Guitar*.vst3", "MONSTER Guitar", true, 0.03f },
                                       { EngineCore::Slot::bandGuitar, "Kontakt*.vst3", "Kontakt (Shreddage)", true, 0.8f },
                                       { EngineCore::Slot::bandLead1, "Numa Player*.vst3", "Numa Player", true, 0.8f },
                                       { EngineCore::Slot::bandLead1, "Kontakt*.vst3", "Kontakt", true, 0.8f },
                                       { EngineCore::Slot::bandLead2, "Numa Player*.vst3", "Numa Player", true, 0.8f },
                                       { EngineCore::Slot::bandLead2, "Kontakt*.vst3", "Kontakt", true, 0.8f },
                                       { EngineCore::Slot::bandKeys, "Numa Player*.vst3", "Numa Player", true, 0.8f } };

    /** The General MIDI drum notes a drum plugin plays, if it does not play them all (empty: all): MONSTER Drums v3,
        measured with plugin_probe --map on a standard kit, plays 35 to 57 but the high tom (50), the tambourine (54),
        the cowbell (56) and ride 2 (59). The band plays a drum a kit lacks on the nearest one it has. */
    std::vector<int> drumPluginNotes (const juce::File& plugin)
    {
        if (plugin.getFileName().startsWithIgnoreCase ("MONSTER Drums"))
            return { 30, 32, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 51, 52, 53, 55, 57 };
        return {};
    }

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

    /** A kept sound in the log: its plugin, or the built-in sound (the keys and a lead: off). */
    juce::String describe (const Sound& sound, bool onlyOnPlugin)
    {
        if (sound.isBuiltIn())
            return onlyOnPlugin ? "off" : "the built-in sound";
        return juce::File (sound.plugin).getFileNameWithoutExtension();
    }

    /** The band playstyle window: the open song's sound per playstyle (a column each) for each band member (a row
        each), a plugin dropdown and its window ("...") in every cell, as in the control panel's rows. "Save defaults"
        keeps them for the songs opened the first time; "Ok" closes (the song keeps them either way). */
    class PlaystyleView final : public juce::Component
    {
    public:
        struct Cell
        {
            juce::ComboBox box;
            juce::TextButton window { "..." };
        };
        std::function<void (int row, int column)> onPick;    // a cell's dropdown changed
        std::function<void (int row, int column)> onWindow;  // a cell's "..." pressed
        std::function<void()> onSave, onOk;

        PlaystyleView (const juce::StringArray& rowNames, const juce::StringArray& columnNames)
            : columns (columnNames)
        {
            note.setJustificationType (juce::Justification::topLeft);
            addAndMakeVisible (note);
            for (const auto& c : columns)
                addAndMakeVisible (heads.add (new juce::Label ({}, c)));
            for (int i = 0; i < rowNames.size(); ++i)
            {
                addAndMakeVisible (names.add (new juce::Label ({}, rowNames[i])));
                for (int c = 0; c < columns.size(); ++c)
                {
                    auto* cell = cells.add (new Cell());
                    cell->box.onChange = [this, i, c] { if (onPick) onPick (i, c); };
                    cell->window.onClick = [this, i, c] { if (onWindow) onWindow (i, c); };
                    addAndMakeVisible (cell->box);
                    addAndMakeVisible (cell->window);
                }
            }
            save.onClick = [this] { if (onSave) onSave(); };
            ok.onClick = [this] { if (onOk) onOk(); };
            addAndMakeVisible (save);
            addAndMakeVisible (ok);
            setSize (120 + 250 * columns.size() + 20, 130 + 32 * rowNames.size());
        }

        Cell& cell (int row, int column) { return *cells[row * columns.size() + column]; }
        void setNote (const juce::String& text) { note.setText (text, juce::dontSendNotification); }

        void paint (juce::Graphics& g) override
        {
            g.fillAll (getLookAndFeel().findColour (juce::ResizableWindow::backgroundColourId));
        }

        void resized() override
        {
            auto r = getLocalBounds().reduced (10);
            note.setBounds (r.removeFromTop (40));
            auto buttons = r.removeFromBottom (28);
            ok.setBounds (buttons.removeFromRight (90));
            buttons.removeFromRight (8);
            save.setBounds (buttons.removeFromRight (130));
            r.removeFromTop (4);
            const auto column = (r.getWidth() - 120) / juce::jmax (1, columns.size());
            auto row = r.removeFromTop (24);
            row.removeFromLeft (120);
            for (auto* head : heads)
                head->setBounds (row.removeFromLeft (column));
            for (int i = 0; i < names.size(); ++i)
            {
                row = r.removeFromTop (32);
                names[i]->setBounds (row.removeFromLeft (120));
                for (int c = 0; c < columns.size(); ++c)
                {
                    auto area = row.removeFromLeft (column).reduced (2, 3);
                    cell (i, c).window.setBounds (area.removeFromRight (36));
                    cell (i, c).box.setBounds (area.withTrimmedRight (4));
                }
            }
        }

    private:
        juce::StringArray columns;
        juce::Label note;
        juce::OwnedArray<juce::Label> heads, names;
        juce::OwnedArray<Cell> cells;
        juce::TextButton save { "Save defaults" }, ok { "Ok" };
    };

    class PlaystyleWindow final : public juce::DocumentWindow
    {
    public:
        explicit PlaystyleWindow (PlaystyleView* view)
            : DocumentWindow ("Band playstyle defaults", juce::Colours::darkgrey, DocumentWindow::closeButton)
        {
            setUsingNativeTitleBar (true);
            setContentOwned (view, true);
            centreWithSize (getWidth(), getHeight());
        }

        void closeButtonPressed() override { setVisible (false); }
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
        else if (args[i] == "--lead1" && i + 1 < args.size())
            o.bandLead1 = args[++i].unquoted();
        else if (args[i] == "--lead2" && i + 1 < args.size())
            o.bandLead2 = args[++i].unquoted();
        else if (args[i] == "--import-test" && i + 1 < args.size())
            o.importTest = args[++i].unquoted();
        else if (args[i] == "--connect-test" && i + 1 < args.size())
            o.connectTest = args[++i].unquoted();
        else if (args[i] == "--playstyles-test")
            o.playstylesTest = true;
        else if (args[i] == "--style-test" && i + 1 < args.size())
            o.styleTest = args[++i].unquoted();
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
    sounds = std::make_unique<SoundStore> (settings->getFile().getParentDirectory());
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

    // the game's songs (it sends the list when it connects and whenever it opens one)
    addAndMakeVisible (songLabel);
    addAndMakeVisible (songBox);
    songBox.setTextWhenNothingSelected ("Waiting for the game");
    songBox.setEnabled (false);
    songBox.onChange = [this]
    {
        auto* message = new juce::DynamicObject();
        message->setProperty ("type", "openSong");
        message->setProperty ("index", songBox.getSelectedId() - 1);
        game.send (juce::var (message));
    };
    addAndMakeVisible (importButton);
    importButton.onClick = [this] { showImport(); };
    // the band's playstyle (the game's): choosing one asks the game to play the song in it
    addAndMakeVisible (styleLabel);
    addAndMakeVisible (styleBox);
    styleBox.setTextWhenNothingSelected ("Waiting for the game");
    styleBox.setEnabled (false);
    styleBox.onChange = [this]
    {
        if (! game.isConnected() || styleBox.getSelectedId() <= 0)
            return;
        auto* message = new juce::DynamicObject();
        message->setProperty ("type", "style");
        message->setProperty ("name", styleBox.getText());
        game.send (juce::var (message));
        log ("Band style: " + styleBox.getText());
    };
    addAndMakeVisible (playstylesButton);
    playstylesButton.onClick = [this] { showPlaystyles(); };

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

    // your guitar's TONE3000, with the state it had last time (an older tone A becomes it)
    const juce::File pluginFile (settings->getValue ("plugin", defaultPlugin));
    if (const auto error = engine.loadPlugins (pluginFile); error.isNotEmpty())
        log (error);
    else if (auto* p = engine.plugin (EngineCore::Slot::guitar))
        log ("Loaded " + p->getName() + " (" + juce::String (p->getTotalNumInputChannels()) + " in, "
             + juce::String (p->getTotalNumOutputChannels()) + " out, latency "
             + juce::String (p->getLatencySamples()) + " samples)");
    {
        auto value = settings->getValue (slotKey (EngineCore::Slot::guitar));
        if (value.isEmpty())
            value = settings->getValue ("toneA");
        juce::MemoryBlock state;
        if (state.fromBase64Encoding (value))
            engine.setPluginState (EngineCore::Slot::guitar, state);
    }

    // the band's drums, bass and guitar: their built-in sounds, or instrument plugins chosen in the control panel; the
    // keys and the song's other instruments: a plugin each, or silent (each song then puts them on its own sounds)
    setUpBandPart (EngineCore::Slot::bandDrums);
    setUpBandPart (EngineCore::Slot::bandBass);
    setUpBandPart (EngineCore::Slot::bandGuitar);
    setUpBandPart (EngineCore::Slot::bandKeys);
    setUpBandPart (EngineCore::Slot::bandLead1);
    setUpBandPart (EngineCore::Slot::bandLead2);
    setUpInstrumentRows();
    engine.setBandGuitarPlays (settings->getBoolValue ("bandGuitarPlays", false));  // (the game's G switches it)

    for (auto* c : std::initializer_list<juce::Component*> { &guitarButton, &presetBox })
        addAndMakeVisible (*c);
    guitarButton.onClick = [this] { showPlugin (EngineCore::Slot::guitar, "Guitar tone"); };
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
        const juce::String part = i == 0 ? "drums" : i == 1 ? "bass" : i == 2 ? "keys" : i == 3 ? "guitar" : i == 4 ? "lead1" : "lead2";
        const char* const names[] { "Drums", "Bass", "Keys", "Rhythm", "Lead", "Inst. 1" };
        faderLabels[i].setText (names[i], juce::dontSendNotification);
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
        {
            engine.band().pause();
            songBox.setEnabled (false);  // (its list is the game's)
            styleBox.setEnabled (false);  // (and so are the playstyles)
            showTracks ({});             // (and so are the tracks)
        }
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

    setSize (760, 964);
    startTimerHz (4);

    if (options.importTest.isNotEmpty())
        juce::Timer::callAfterDelay (5000, [self = juce::Component::SafePointer<MainComponent> (this)]  // (the game has connected by then)
        {
            if (self == nullptr)
                return;
            self->showImport();
            self->importWindow->startTest (self->options.importTest);
        });

    if (options.styleTest.isNotEmpty())
        juce::Timer::callAfterDelay (6000, [self = juce::Component::SafePointer<MainComponent> (this)]  // (the game has sent its song by then)
        {
            if (self != nullptr)
                self->styleBox.setSelectedId (self->styleNames.indexOf (self->options.styleTest) + 1, juce::sendNotificationSync);
        });

    if (options.playstylesTest)
        juce::Timer::callAfterDelay (8000, [self = juce::Component::SafePointer<MainComponent> (this)]  // (the game has sent its song by then)
        {
            if (self == nullptr)
                return;
            self->showPlaystyles();
            if (self->options.snapshot != juce::File() && self->playstyleWindow != nullptr)
                writeSnapshot (*self->playstyleWindow->getContentComponent(), self->options.snapshot.withFileExtension ("playstyles.png"));
        });

    if (options.connectTest.isNotEmpty())
        juce::Timer::callAfterDelay (6000, [self = juce::Component::SafePointer<MainComponent> (this)]  // (the game has sent the tracks by then)
        {
            if (self == nullptr)
                return;
            const auto id = self->options.connectTest.upToFirstOccurrenceOf (":", false, false);
            const auto track = self->options.connectTest.fromFirstOccurrenceOf (":", false, false).getIntValue();
            for (auto& row : self->instrumentRows)
                if (row.id == id)
                    row.track.setSelectedId (track + 2, juce::sendNotificationSync);
            if (self->options.snapshot != juce::File())
                juce::Timer::callAfterDelay (2500, [self]
                {
                    if (self != nullptr)
                        writeSnapshot (*self, self->options.snapshot.withFileExtension ("connect.png"));
                });
        });

    if (options.snapshot != juce::File())
        juce::Timer::callAfterDelay (3000, [self = juce::Component::SafePointer<MainComponent> (this)]
        {
            if (self == nullptr)
                return;
            writeSnapshot (*self, self->options.snapshot);
            if (self->importWindow != nullptr)
                writeSnapshot (*self->importWindow->getContentComponent(), self->options.snapshot.withFileExtension ("import.png"));
        });
}

MainComponent::~MainComponent()
{
    stopTimer();
    saveSettings();
    importWindow.reset();  // (stops a running import)
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
             + (score->picks.empty() ? juce::String() : ", " + juce::String ((int) score->picks.size()) + " picked strings")
             + ", " + juce::String ((int) score->spans.size()) + " single-note stretches"
             + (score->leads[0].isNotEmpty() || score->leads[1].isNotEmpty()
                    ? ", leads: " + (score->leads[0].isNotEmpty() ? score->leads[0] : juce::String ("-"))
                          + " / " + (score->leads[1].isNotEmpty() ? score->leads[1] : juce::String ("-"))
                    : juce::String())
             + (score->origin.isNotEmpty() ? ", from " + score->origin : juce::String()));
        const auto notes = (int) score->notes.size();
        const auto style = score->style;
        // the game's playstyles, and each one's keyboard sound (a game that does not send them: this one's)
        styleNames.clear();
        styleKeys.clear();
        if (auto* styles = message["styles"].getArray())
            for (const auto& s : *styles)
            {
                styleNames.add (s["name"].toString());
                styleKeys[s["name"].toString()] = s["keys"].toString();
            }
        if (! styleNames.contains (style))
        {
            styleNames.add (style);
            styleKeys[style] = score->keys;
        }
        const auto keys = styleKeys[style].isNotEmpty() ? styleKeys[style] : juce::String (defaultKeysSound);
        band.load (std::move (score));
        // the song's sounds (loading a plugin or a sound can take seconds: the game waits for "loaded")
        applySounds (message["song"].toString(), style);
        showStyles();
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
    else if (type == "tracks")
        showTracks (message);
    else if (type == "songs")
    {
        songBox.clear (juce::dontSendNotification);
        if (auto* names = message["names"].getArray())
            for (int i = 0; i < names->size(); ++i)
                songBox.addItem ((*names)[i].toString(), i + 1);
        songBox.setSelectedId ((int) message["current"] + 1, juce::dontSendNotification);
        songBox.setEnabled (songBox.getNumItems() > 0);
    }
    else if (type == "window")
    {
        const auto which = message["show"].toString();
        if (which == "guitar")
            showPlugin (EngineCore::Slot::guitar, "Guitar tone");
        else if (which == "drums")
            showPlugin (EngineCore::Slot::bandDrums, bandTitle (EngineCore::Slot::bandDrums));
        else if (which == "bandBass")
            showPlugin (EngineCore::Slot::bandBass, bandTitle (EngineCore::Slot::bandBass));
        else if (which == "bandGuitar")
            showPlugin (EngineCore::Slot::bandGuitar, bandTitle (EngineCore::Slot::bandGuitar));
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
    if (const auto state = engine.pluginState (EngineCore::Slot::guitar); state.getSize() > 0)
        settings->setValue (slotKey (EngineCore::Slot::guitar), state.toBase64Encoding());
    for (auto* gone : { "bandGuitarAmp", "bandGuitarThroughAmp" })  // the band guitar's amp, no longer there
        settings->removeValue (gone);
    for (auto slot : EngineCore::bandSlots)
        keepBandState (slot);
    settings->removeValue ("toneA");
    settings->removeValue ("toneB");
    settings->saveIfNeeded();
}

void MainComponent::setUpInstrumentRows()
{
    struct Spec { const char* id; EngineCore::Slot slot; };
    const Spec specs[] { { "rhythm", EngineCore::Slot::bandGuitar }, { "bass", EngineCore::Slot::bandBass },
                         { "keys", EngineCore::Slot::bandKeys }, { "drums", EngineCore::Slot::bandDrums },
                         { "lead", EngineCore::Slot::bandLead1 }, { "instrument1", EngineCore::Slot::bandLead2 } };
    for (size_t i = 0; i < instrumentRows.size(); ++i)
    {
        auto& row = instrumentRows[i];
        row.id = specs[i].id;
        row.slot = specs[i].slot;
        row.lead = isLead (row.slot);
        row.label.setText (bandTitle (row.slot), juce::dontSendNotification);
        row.track.onChange = [this, i]
        {
            auto& r = instrumentRows[i];
            if (! game.isConnected() || r.track.getSelectedId() <= 0)
                return;
            const auto id = r.track.getSelectedId();
            const auto strings = id >= stringTrackIds;
            const auto picked = ! strings && id >= pickedTrackIds;
            auto* message = new juce::DynamicObject();
            message->setProperty ("type", "connect");
            message->setProperty ("instrument", r.id);
            message->setProperty ("track", (strings ? id - stringTrackIds : picked ? id - pickedTrackIds : id) - 2);  // -1: the default part
            message->setProperty ("picked", picked);
            message->setProperty ("strings", strings);
            game.send (juce::var (message));
            log (r.label.getText() + " plays " + (r.track.getSelectedId() != 1 ? r.track.getText()
                                                  : r.lead ? juce::String ("no track") : juce::String ("the band playstyle's part")));
        };
        addAndMakeVisible (row.label);
        addAndMakeVisible (row.track);
    }
    showTracks ({});
}

void MainComponent::showTracks (const juce::var& message)
{
    const auto* tracks = message["tracks"].getArray();
    const auto* instruments = message["instruments"].getArray();
    for (auto& row : instrumentRows)
    {
        row.track.clear (juce::dontSendNotification);
        row.track.addItem (row.lead ? "None" : "Band playstyle", 1);  // (a lead has no part of its own)
        if (tracks != nullptr)
            for (const auto& t : *tracks)
                row.track.addItem (t["name"].toString(), (int) t["index"] + 2);
        // the rhythm guitar can also play a guitar track picked, note by note (a fingerpicked part), not strummed, or
        // string by string on its held chord (an experiment: the strings ring until struck again)
        if (tracks != nullptr && row.id == "rhythm")
            for (const auto& t : *tracks)
                if (t["kind"].toString() == "guitar")
                {
                    const auto name = t["name"].toString().upToFirstOccurrenceOf (" (", false, false);
                    row.track.addItem (name + ", picked note by note", pickedTrackIds + (int) t["index"] + 2);
                    row.track.addItem (name + ", string by string (experiment)", stringTrackIds + (int) t["index"] + 2);
                }
        auto playing = -1;
        auto picked = false, strings = false;
        if (instruments != nullptr)
            for (const auto& i : *instruments)
                if (i["id"].toString() == row.id)
                {
                    playing = (int) i["track"];
                    picked = (bool) i["picked"];
                    strings = (bool) i["strings"];
                }
        const auto ids = playing < 0 ? 0 : strings ? stringTrackIds : picked ? pickedTrackIds : 0;
        row.track.setSelectedId (ids + playing + 2, juce::dontSendNotification);
        if (row.track.getSelectedId() == 0)
            row.track.setSelectedId (1, juce::dontSendNotification);
        row.track.setEnabled (tracks != nullptr && ! tracks->isEmpty());
        // a band member on its playstyle part plays the playstyle's sound (Band playstyle defaults...): its plugin is
        // chosen here only when it plays a track (a lead always does)
        row.playsTrack = ! row.lead && playing >= 0;
        auto& part = bandParts[row.slot];
        part.box.setVisible (row.lead || row.playsTrack);
        part.windowButton.setVisible (row.lead || row.playsTrack);
    }
}

void MainComponent::setUpBandPart (EngineCore::Slot slot)
{
    auto& part = bandParts[slot];
    // the plugins the band knows that you have, then the ones you added
    for (const auto& known : knownPlugins)
        if (known.slot == slot)
            if (const auto file = findInstrument ({}, known.pattern); file != juce::File())
                part.choices.push_back ({ file, known.name, known.silentUntilChosen, known.level, false });
    for (const auto& path : juce::StringArray::fromLines (settings->getValue (juce::String (slotKey (slot)) + "Added")))
        if (path.isNotEmpty() && juce::File (path).exists() && choiceFor (slot, path) < 0)
            part.choices.push_back ({ juce::File (path), juce::File (path).getFileNameWithoutExtension(), false, addedPluginLevel (slot), true });

    // your choice from last time: a plugin's path, or "synthesized" (else the first plugin found)
    auto last = settings->getValue (juce::String (slotKey (slot)) + "Plugin");
    if (slot == EngineCore::Slot::bandKeys && last.isEmpty())
        last = settings->getValue ("keysPlugin", defaultKeysPlugin);  // (the keys' one plugin, before they could be chosen)
    if (slot == EngineCore::Slot::bandBass && options.bandBass.isNotEmpty())
        last = options.bandBass;
    if (slot == EngineCore::Slot::bandGuitar && options.bandGuitar.isNotEmpty())
        last = options.bandGuitar;
    if (slot == EngineCore::Slot::bandLead1 && options.bandLead1.isNotEmpty())
        last = options.bandLead1;
    if (slot == EngineCore::Slot::bandLead2 && options.bandLead2.isNotEmpty())
        last = options.bandLead2;
    if (isLead (slot) && last.isEmpty())
        last = "synthesized";  // no lead plugin until you choose one (each is another instance to load)
    auto choice = choiceFor (slot, last);
    if (choice < 0 && last != "synthesized" && ! part.choices.empty())
        choice = 0;

    part.box.onChange = [this, slot] { bandBoxChanged (slot); };
    part.windowButton.onClick = [this, slot] { showPlugin (slot, bandTitle (slot)); };
    for (auto* c : std::initializer_list<juce::Component*> { &part.box, &part.windowButton })
        addAndMakeVisible (*c);
    chooseBandInstrument (slot, choice, false, false);
}

void MainComponent::refreshBandPart (EngineCore::Slot slot)
{
    auto& part = bandParts[slot];
    const auto current = currentChoice (slot);
    part.box.clear (juce::dontSendNotification);
    part.box.addItem (playsOnlyOnPlugin (slot) ? "Off (no sound)" : "Built-in sound", builtInId);
    for (size_t i = 0; i < part.choices.size(); ++i)
        part.box.addItem (part.choices[i].name, (int) i + 2);
    part.box.addSeparator();
    if (current >= 0 && picksSoundOnlyWhenNew (part.choices[(size_t) current].file))
        part.box.addItem ("Pick another sound in " + part.choices[(size_t) current].name + "...", newSoundId);
    part.box.addItem ("Other plugin...", otherPluginId);
    part.box.setSelectedId (current + 2, juce::dontSendNotification);  // the built-in sound when none
    part.windowButton.setEnabled (current >= 0);
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
    {
        if (chooseBandInstrument (slot, id - 2, true, false))
            if (const auto* row = rowFor (slot))
                keepSongSound (*row);  // the plugin you choose plays in this song
    }
}

void MainComponent::chooseOtherPlugin (EngineCore::Slot slot)
{
    pluginChooser = std::make_unique<juce::FileChooser> ("An instrument plugin for the " + bandTitle (slot).toLowerCase(),
                                                         juce::File ("C:\\Program Files\\Common Files\\VST3"), "*.vst3");
    pluginChooser->launchAsync (juce::FileBrowserComponent::openMode | juce::FileBrowserComponent::canSelectFiles,
                                [this, slot] (const juce::FileChooser& chooser)
    {
        const auto file = pluginOf (chooser.getResult());
        if (file == juce::File())
            return;
        auto& part = bandParts[slot];
        auto choice = choiceFor (slot, file.getFullPathName());
        const auto added = choice < 0;
        if (added)
        {
            part.choices.push_back ({ file, file.getFileNameWithoutExtension(), false, addedPluginLevel (slot), true });
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
        if (currentChoice (slot) == choice)
            if (const auto* row = rowFor (slot))
                keepSongSound (*row);
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
    if (slot == EngineCore::Slot::bandDrums)
        engine.band().setDrumPluginNotes (choice >= 0 ? drumPluginNotes (part.choices[(size_t) choice].file) : std::vector<int> {});
    if (slot == EngineCore::Slot::bandGuitar)
        engine.setBandGuitarStrummingMode (choice >= 0
                                           && part.choices[(size_t) choice].file.getFileName().startsWithIgnoreCase ("Kontakt"));
    auto playsPlugin = false;
    if (choice >= 0)
    {
        auto& c = part.choices[(size_t) choice];
        engine.setBandPluginLevel (slot, c.level);
        if (slot == EngineCore::Slot::bandGuitar)
        {
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
        log (bandTitle (slot) + (isLead (slot) ? ": off" : ": built-in sound"));
    if (remember && ! engine.isSilent())
        settings->setValue (juce::String (slotKey (slot)) + "Plugin",
                            playsPlugin ? part.choices[(size_t) choice].file.getFullPathName() : juce::String ("synthesized"));
    refreshBandPart (slot);
    return playsPlugin || choice < 0;
}

int MainComponent::choiceFor (EngineCore::Slot slot, const juce::String& plugin) const
{
    // by its path, its name ("ABPL"), else its file's name: a plugin kept from another folder (a kept sound) is the same one
    const auto fileName = plugin.fromLastOccurrenceOf ("\\", false, false);
    if (const auto part = bandParts.find (slot); part != bandParts.end())
    {
        for (size_t i = 0; i < part->second.choices.size(); ++i)
            if (const auto& f = part->second.choices[i].file; f.getFullPathName() == plugin || f.getFileNameWithoutExtension() == plugin)
                return (int) i;
        for (size_t i = 0; i < part->second.choices.size(); ++i)
            if (part->second.choices[i].file.getFileName().equalsIgnoreCase (fileName))
                return (int) i;
    }
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
    auto saved = state.fromBase64Encoding (value) && state.getSize() > 0;
    // a new Numa Player is silent until a sound is picked in its window: the keys (and a lead) on it start with the keys'
    // sound from before (a song's playstyle then gives the keys theirs)
    if (! saved && playsOnlyOnPlugin (slot) && file.getFileName().startsWithIgnoreCase ("Numa Player"))
    {
        juce::String soundError;
        state = numa::stateFor (settings->getValue ("keysSound", defaultKeysSound), soundError);
        if (state.getSize() > 0)
        {
            engine.setPluginState (slot, state);
            log (bandTitle (slot) + ": Numa Player starts with " + settings->getValue ("keysSound", defaultKeysSound) + " (change it in its window)");
            saved = true;
            value = {};
        }
        else
            log (bandTitle (slot) + ": " + soundError);
    }
    if (saved && value.isNotEmpty())
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

Sound* MainComponent::keptSound (const InstrumentRow& row, const juce::String& style)
{
    if (! row.lead && ! row.playsTrack)
        return playstyleSound (row, style);
    const auto it = songSounds.tracks.find (row.id);
    return it != songSounds.tracks.end() ? &it->second : nullptr;
}

Sound* MainComponent::playstyleSound (const InstrumentRow& row, const juce::String& style)
{
    auto& set = songSounds.styles[style];
    const auto it = set.find (row.id);
    return it != set.end() ? &it->second : nullptr;
}

Sound MainComponent::currentSound (EngineCore::Slot slot) const
{
    Sound sound;
    const auto loaded = bandPlugins.find (slot);
    sound.plugin = loaded == bandPlugins.end() ? juce::String (Sound::builtIn) : loaded->second.getFullPathName();
    if (loaded != bandPlugins.end() && engine.usesBandInstrument (slot))
        sound.state = engine.pluginState (slot);
    return sound;
}

void MainComponent::applySounds (const juce::String& song, const juce::String& style)
{
    if (song != currentSong || songSounds.empty())
    {
        currentSong = song;
        songSounds = song.isEmpty() ? SongSounds() : SoundStore::read (sounds->songFile (song));
    }
    currentStyle = style;
    // a song opened the first time: the saved playstyle defaults (none saved: the built-in sounds; the keys on Numa
    // Player with each playstyle's sound); an instrument playing a track the first time: a band member the sound of its
    // part, a lead what it was on last
    auto made = false;
    const auto defaults = SoundStore::read (sounds->defaultsFile());
    const auto numa = choiceFor (EngineCore::Slot::bandKeys, "Numa Player");
    for (const auto& name : styleNames)
        for (const auto& row : instrumentRows)
            if (! row.lead && playstyleSound (row, name) == nullptr)
            {
                Sound sound;
                if (const auto d = defaults.styles.find (name); d != defaults.styles.end() && d->second.count (row.id) > 0)
                    sound = d->second.at (row.id);
                else if (row.slot == EngineCore::Slot::bandKeys && numa >= 0)
                    sound.plugin = bandParts[row.slot].choices[(size_t) numa].file.getFullPathName();
                else
                    sound.plugin = Sound::builtIn;
                songSounds.styles[name][row.id] = sound;
                made = true;
            }
    for (const auto& row : instrumentRows)
        if ((row.lead || row.playsTrack) && keptSound (row, style) == nullptr)
        {
            const auto* part = row.lead ? nullptr : playstyleSound (row, style);
            songSounds.tracks[row.id] = part != nullptr ? *part : currentSound (row.slot);
            made = true;
        }
    if (made)
        saveSongSounds();
    for (const auto& row : instrumentRows)
        if (! chosenByOptions (row.slot))
            if (const auto* sound = keptSound (row, style))
                applySound (row.slot, *sound, style);
    if (refreshPlaystyles)
        refreshPlaystyles();
}

void MainComponent::applySound (EngineCore::Slot slot, const Sound& sound, const juce::String& style)
{
    const auto choice = sound.isBuiltIn() ? -1 : choiceFor (slot, sound.plugin);
    if (! sound.isBuiltIn() && choice < 0)
    {
        log (bandTitle (slot) + ": its kept sound's plugin is not here (" + sound.plugin + ")");
        return;
    }
    // the keys on Numa Player with no state of their own: the playstyle's keyboard sound
    auto state = sound.state;
    if (state.getSize() == 0 && slot == EngineCore::Slot::bandKeys && choice >= 0
        && bandParts[slot].choices[(size_t) choice].file.getFileName().startsWithIgnoreCase ("Numa Player"))
    {
        juce::String error;
        const auto keys = styleKeys[style].isNotEmpty() ? styleKeys[style] : juce::String (defaultKeysSound);
        state = numa::stateFor (keys, error);
        if (state.getSize() == 0)
            log ("Keys: " + error);
    }
    Sound wanted { sound.plugin, state };
    const auto applied = appliedSounds.find (slot);
    if (choice == currentChoice (slot) && applied != appliedSounds.end() && applied->second == wanted)
        return;  // it plays that already
    const auto hasState = state.getSize() > 0;
    if (choice != currentChoice (slot))
        chooseBandInstrument (slot, choice, true, hasState);  // with a kept state, a fresh instance: it gets that state below
    if (choice >= 0 && hasState && currentChoice (slot) == choice && engine.pluginState (slot) != state)
    {
        if (juce::AudioProcessor::getXmlFromBinary (state.getData(), (int) state.getSize()) == nullptr)
            log (bandTitle (slot) + ": its kept sound is not in the host's state format: not loaded");
        else
        {
            engine.setPluginState (slot, state);
            engine.useBandInstrument (slot, true);
            log (bandTitle (slot) + ": the song's sound (" + juce::String (state.getSize()) + " bytes)");
        }
    }
    appliedSounds[slot] = wanted;
    appliedStates[slot] = engine.pluginState (slot);
}

void MainComponent::keepSongSound (const InstrumentRow& row)
{
    if (! row.lead && ! row.playsTrack)
    {
        keepPlaystyleSound (row, currentStyle);
        return;
    }
    if (currentSong.isEmpty())
        return;  // (no song from the game yet)
    auto& sound = songSounds.tracks[row.id];
    sound = currentSound (row.slot);
    appliedSounds[row.slot] = sound;
    appliedStates[row.slot] = engine.pluginState (row.slot);
    saveSongSounds();
    log (row.label.getText() + ": " + describe (sound, playsOnlyOnPlugin (row.slot)) + " kept for " + currentSong + ", its track");
}

void MainComponent::keepPlaystyleSound (const InstrumentRow& row, const juce::String& style)
{
    if (currentSong.isEmpty() || row.lead)
        return;
    auto& sound = songSounds.styles[style][row.id];
    sound = currentSound (row.slot);
    appliedSounds[row.slot] = sound;
    appliedStates[row.slot] = engine.pluginState (row.slot);
    saveSongSounds();
    log (row.label.getText() + ": " + describe (sound, playsOnlyOnPlugin (row.slot)) + " kept for " + currentSong + ", " + style);
}

void MainComponent::saveSongSounds()
{
    if (currentSong.isEmpty() || engine.isSilent())
        return;  // (a test run keeps nothing)
    const auto file = sounds->songFile (currentSong);
    if (! SoundStore::write (file, songSounds))
        log ("Could not keep the song's sounds in " + file.getFullPathName());
}

void MainComponent::showPlaystyles()
{
    if (currentSong.isEmpty() || styleNames.isEmpty())
    {
        log ("Band playstyle defaults: open a song in the game first (each song keeps its own)");
        return;
    }
    std::vector<int> members;  // the rows the window shows: the band members, not the leads
    juce::StringArray names;
    for (int i = 0; i < (int) instrumentRows.size(); ++i)
        if (! instrumentRows[(size_t) i].lead)
        {
            members.push_back (i);
            names.add (instrumentRows[(size_t) i].label.getText());
        }
    playstyleWindow.reset();  // (made anew: the playstyles may have changed)
    auto* view = new PlaystyleView (names, styleNames);
    const auto columns = styleNames;
    refreshPlaystyles = [this, view, members, columns]
    {
        view->setNote ("The sounds of " + currentSong + " in each band playstyle (" + currentStyle + " plays now). "
                       "\"Save defaults\" keeps them for the songs you open the first time; the song keeps them either way.");
        for (int i = 0; i < (int) members.size(); ++i)
        {
            const auto& row = instrumentRows[(size_t) members[(size_t) i]];
            const auto& part = bandParts[row.slot];
            for (int c = 0; c < columns.size(); ++c)
            {
                auto& cell = view->cell (i, c);
                cell.box.clear (juce::dontSendNotification);
                cell.box.addItem (playsOnlyOnPlugin (row.slot) ? "Off (no sound)" : "Built-in sound", builtInId);
                for (size_t k = 0; k < part.choices.size(); ++k)
                    cell.box.addItem (part.choices[k].name, (int) k + 2);
                cell.box.addSeparator();
                const auto* sound = playstyleSound (row, columns[c]);
                const auto choice = sound == nullptr || sound->isBuiltIn() ? -1 : choiceFor (row.slot, sound->plugin);
                if (choice >= 0 && picksSoundOnlyWhenNew (part.choices[(size_t) choice].file))
                    cell.box.addItem ("Pick another sound in " + part.choices[(size_t) choice].name + "...", newSoundId);
                cell.box.addItem ("Other plugin...", otherPluginId);
                cell.box.setSelectedId (choice + 2, juce::dontSendNotification);
                cell.window.setEnabled (choice >= 0);
            }
        }
    };
    view->onPick = [this, view, members, columns] (int i, int c)
    {
        const auto& row = instrumentRows[(size_t) members[(size_t) i]];
        const auto id = view->cell (i, c).box.getSelectedId();
        const auto style = columns[c];
        if (id == otherPluginId)
        {
            pluginChooser = std::make_unique<juce::FileChooser> ("An instrument plugin for the " + bandTitle (row.slot).toLowerCase(),
                                                                 juce::File ("C:\\Program Files\\Common Files\\VST3"), "*.vst3");
            const auto slot = row.slot;
            pluginChooser->launchAsync (juce::FileBrowserComponent::openMode | juce::FileBrowserComponent::canSelectFiles,
                                        [this, slot, style] (const juce::FileChooser& chooser)
            {
                const auto file = pluginOf (chooser.getResult());
                if (file != juce::File())
                    if (const auto* r = rowFor (slot))
                        choosePlaystylePlugin (*r, style, addPluginChoice (slot, file));
                if (refreshPlaystyles)
                    refreshPlaystyles();
            });
            return;
        }
        if (id == newSoundId)
            pickPlaystyleSound (row, style);
        else
            choosePlaystylePlugin (row, style, id - 2);
        refreshPlaystyles();
    };
    view->onWindow = [this, members, columns] (int i, int c)
    {
        editPlaystyleSound (instrumentRows[(size_t) members[(size_t) i]], columns[c]);
    };
    view->onSave = [this] { savePlaystyleDefaults(); };
    view->onOk = [this] { playstyleWindow->setVisible (false); };
    playstyleWindow = std::make_unique<PlaystyleWindow> (view);
    refreshPlaystyles();
    playstyleWindow->setVisible (true);
    bringToFront (*playstyleWindow);
}

void MainComponent::choosePlaystylePlugin (const InstrumentRow& row, const juce::String& style, int choice)
{
    const auto& part = bandParts[row.slot];
    if (choice >= (int) part.choices.size())
        return;
    Sound sound;  // (its state: the plugin's last sound, until you choose one in its window)
    sound.plugin = choice < 0 ? juce::String (Sound::builtIn) : part.choices[(size_t) choice].file.getFullPathName();
    if (style == currentStyle && ! row.playsTrack)
    {
        applySound (row.slot, sound, style);  // the part it plays: the band changes now
        keepPlaystyleSound (row, style);
        return;
    }
    songSounds.styles[style][row.id] = sound;
    saveSongSounds();
    log (row.label.getText() + ": " + describe (sound, playsOnlyOnPlugin (row.slot)) + " for " + currentSong + ", " + style);
}

void MainComponent::editPlaystyleSound (const InstrumentRow& row, const juce::String& style)
{
    const auto* sound = playstyleSound (row, style);
    if (sound == nullptr || sound->isBuiltIn())
        return;
    windows.erase (row.slot);  // (a window open for another sound: it goes before this one comes)
    const auto copy = *sound;
    applySound (row.slot, copy, style);  // you hear the playstyle's sound while you change it
    editingStyle[row.slot] = style;
    showPlugin (row.slot, bandTitle (row.slot) + " (" + style + ")");
}

void MainComponent::pickPlaystyleSound (const InstrumentRow& row, const juce::String& style)
{
    const auto* sound = playstyleSound (row, style);
    const auto choice = sound == nullptr || sound->isBuiltIn() ? -1 : choiceFor (row.slot, sound->plugin);
    if (choice < 0)
        return;
    windows.erase (row.slot);
    if (! chooseBandInstrument (row.slot, choice, true, true))  // a new instance: its sound picker shows
        return;
    appliedSounds.erase (row.slot);  // (none of the kept sounds plays now: closed, the one it plays is loaded again)
    editingStyle[row.slot] = style;
    showPlugin (row.slot, bandTitle (row.slot) + " (" + style + ")");
}

void MainComponent::showStyles()
{
    styleBox.clear (juce::dontSendNotification);
    for (int i = 0; i < styleNames.size(); ++i)
        styleBox.addItem (styleNames[i], i + 1);
    styleBox.setSelectedId (styleNames.indexOf (currentStyle) + 1, juce::dontSendNotification);
    styleBox.setEnabled (! styleNames.isEmpty() && game.isConnected());
}

void MainComponent::savePlaystyleDefaults()
{
    if (engine.isSilent())
        return;
    SongSounds defaults;
    defaults.styles = songSounds.styles;
    if (SoundStore::write (sounds->defaultsFile(), defaults))
        log ("Band playstyle defaults: " + currentSong + "'s sounds are the ones a song gets the first time it is opened");
    else
        log ("Could not save the band playstyle defaults in " + sounds->defaultsFile().getFullPathName());
}

int MainComponent::addPluginChoice (EngineCore::Slot slot, const juce::File& plugin)
{
    if (const auto known = choiceFor (slot, plugin.getFullPathName()); known >= 0)
        return known;
    auto& part = bandParts[slot];
    part.choices.push_back ({ plugin, plugin.getFileNameWithoutExtension(), false, addedPluginLevel (slot), true });
    juce::StringArray paths;  // the plugins you added stay in the dropdown
    for (const auto& c : part.choices)
        if (c.added)
            paths.add (c.file.getFullPathName());
    if (! engine.isSilent())
        settings->setValue (juce::String (slotKey (slot)) + "Added", paths.joinIntoString ("\n"));
    refreshBandPart (slot);
    return (int) part.choices.size() - 1;
}

const MainComponent::InstrumentRow* MainComponent::rowFor (EngineCore::Slot slot) const
{
    for (const auto& row : instrumentRows)
        if (row.slot == slot)
            return &row;
    return nullptr;
}

bool MainComponent::chosenByOptions (EngineCore::Slot slot) const
{
    return (slot == EngineCore::Slot::bandBass && options.bandBass.isNotEmpty())
        || (slot == EngineCore::Slot::bandGuitar && options.bandGuitar.isNotEmpty())
        || (slot == EngineCore::Slot::bandLead1 && options.bandLead1.isNotEmpty())
        || (slot == EngineCore::Slot::bandLead2 && options.bandLead2.isNotEmpty());
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

void MainComponent::setBandGuitarPlays (bool on)
{
    engine.setBandGuitarPlays (on);
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
            // a sound changed in the window is the song's for what the instrument plays (and the plugin's last sound, for
            // another song that chooses it); opened from the playstyle window, that playstyle's, and the instrument goes
            // back to what it plays
            const auto* row = rowFor (slot);
            const auto editing = editingStyle.find (slot);
            const auto fromPlaystyles = editing != editingStyle.end();
            const auto style = fromPlaystyles ? editing->second : currentStyle;
            if (fromPlaystyles)
                editingStyle.erase (editing);
            if (row != nullptr && engine.usesBandInstrument (slot))
            {
                const auto applied = appliedStates.find (slot);
                if (fromPlaystyles || applied == appliedStates.end() || engine.pluginState (slot) != applied->second)
                {
                    keepBandState (slot);
                    if (fromPlaystyles)
                        keepPlaystyleSound (*row, style);
                    else
                        keepSongSound (*row);
                }
            }
            if (row != nullptr && fromPlaystyles)
                if (const auto* playing = keptSound (*row, currentStyle))
                    applySound (slot, *playing, currentStyle);  // (only if it differs)
            if (refreshPlaystyles)
                refreshPlaystyles();
        });
}

void MainComponent::showImport()
{
    if (importWindow == nullptr)
        importWindow = std::make_unique<ImportWindow> (*settings, [this] (const juce::File& song) { songImported (song); });
    else
    {
        importWindow->setVisible (true);
        bringToFront (*importWindow);
    }
}

void MainComponent::songImported (const juce::File& song)
{
    log ("Imported " + song.getFileName());
    if (! game.isConnected())
    {
        log ("The game is not connected: start it and choose the song in the list");
        return;
    }
    auto* message = new juce::DynamicObject();
    message->setProperty ("type", "importedSong");
    message->setProperty ("path", song.getFullPathName());
    game.send (juce::var (message));  // the game reads its list again, sends it, and opens the song
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

    // the song
    auto row = r.removeFromTop (28);
    songLabel.setBounds (row.removeFromLeft (100));
    songBox.setBounds (row.removeFromLeft (260));
    importButton.setBounds (row.removeFromLeft (140).reduced (6, 0));
    r.removeFromTop (6);

    // your guitar: its input, its tone, the tone's presets
    row = r.removeFromTop (28);
    inputLabel.setBounds (row.removeFromLeft (100));
    inputBox.setBounds (row.removeFromLeft (260));
    guitarButton.setBounds (row.removeFromLeft (140).reduced (6, 0));
    presetBox.setBounds (row.withTrimmedLeft (6));
    r.removeFromTop (10);

    // the band's playstyle, and its sounds per playstyle (in the columns of the instruments' tracks and plugins)
    row = r.removeFromTop (28);
    styleLabel.setBounds (row.removeFromLeft (100));
    styleBox.setBounds (row.removeFromLeft (300));
    row.removeFromLeft (6);
    playstylesButton.setBounds (row.removeFromLeft (220));
    r.removeFromTop (6);

    // the band's instruments: the track each plays, its plugin, the plugin's window ("...")
    for (auto& instrument : instrumentRows)
    {
        row = r.removeFromTop (28);
        instrument.label.setBounds (row.removeFromLeft (100));
        instrument.track.setBounds (row.removeFromLeft (300));
        row.removeFromLeft (6);
        auto& part = bandParts[instrument.slot];
        part.windowButton.setBounds (row.removeFromRight (40));
        part.box.setBounds (row.withTrimmedRight (6));
        r.removeFromTop (6);
    }

    r.removeFromTop (4);

    latencyLabel.setBounds (r.removeFromTop (24));
    cpuLabel.setBounds (r.removeFromTop (24));
    footLabel.setBounds (r.removeFromTop (24));
    gameLabel.setBounds (r.removeFromTop (24));
    bandLabel.setBounds (r.removeFromTop (24));
    r.removeFromTop (4);
    // the faders: drums, bass, keys in a row, then guitar and the two leads
    for (size_t first = 0; first < faders.size(); first += 3)
    {
        row = r.removeFromTop (26);
        const auto third = row.getWidth() / 3;
        for (size_t i = first; i < first + 3 && i < faders.size(); ++i)
        {
            auto cell = row.removeFromLeft (third);
            faderLabels[i].setBounds (cell.removeFromLeft (50));
            faders[i].setBounds (cell.reduced (2, 0));
        }
        r.removeFromTop (2);
    }
    r.removeFromTop (6);
    logView.setBounds (r);
}

} // namespace gt
