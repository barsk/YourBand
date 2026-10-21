#include "MainComponent.h"

namespace gt
{

namespace
{
    const char* const defaultPlugin = "C:\\Program Files\\Common Files\\VST3\\TONE3000.vst3";

    const char* slotKey (EngineCore::Slot slot)
    {
        return slot == EngineCore::Slot::guitar ? "guitarAmp" : "bassAmp";
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

MainComponent::MainComponent()
{
    juce::PropertiesFile::Options options;
    options.applicationName = "GuitarTutor Engine";
    options.folderName = "GuitarTutor";
    options.filenameSuffix = ".settings";
    settings = std::make_unique<juce::PropertiesFile> (options);
    settings->getFile().getParentDirectory().createDirectory();
    settings->getFile().getSiblingFile ("engine.log").appendText ("\n--- " + juce::Time::getCurrentTime().toString (true, true) + "\n");

    logView.setMultiLine (true);
    logView.setReadOnly (true);
    logView.setFont (juce::Font (juce::FontOptions (13.0f)));
    addAndMakeVisible (logView);

    // audio device
    auto saved = settings->getXmlValue ("devices");
    if (const auto error = engine.openDevice (saved.get()); error.isNotEmpty())
        log ("Audio device: " + error);
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

    for (auto* c : std::initializer_list<juce::Component*> { &guitarButton, &bassButton, &presetLabel, &presetBox,
                                                             &previousButton, &nextButton, &bassSwitch })
        addAndMakeVisible (*c);
    guitarButton.onClick = [this] { showPlugin (EngineCore::Slot::guitar, "Guitar tone"); };
    bassButton.onClick = [this] { showPlugin (EngineCore::Slot::bass, "Bass amp"); };
    previousButton.onClick = [this] { stepPreset (-1); };
    nextButton.onClick = [this] { stepPreset (1); };
    presetBox.onChange = [this] { selectPreset (presetBox.getSelectedId() - 1); };
    bassSwitch.onClick = [this] { setBass (! engine.bassLinePlaying()); };
    refreshPresets();
    log (juce::String ((int) presetList.size()) + " presets saved in TONE3000");
    setBass (false);

    for (auto* l : { &latencyLabel, &cpuLabel, &footLabel })
    {
        l->setFont (juce::Font (juce::FontOptions (15.0f)));
        addAndMakeVisible (*l);
    }
    footLabel.setText ("Footswitch: none found (any HID game controller works)", juce::dontSendNotification);

    // footswitch: button 1 next preset, button 2 previous preset, button 3 starts and stops the bass line
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
            setBass (! engine.bassLinePlaying());
    };
    footswitch.start();

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

void MainComponent::saveSettings()
{
    if (auto xml = engine.deviceState())
        settings->setValue ("devices", xml.get());
    settings->setValue ("guitarInput", engine.guitarInput());
    for (auto slot : { EngineCore::Slot::guitar, EngineCore::Slot::bass })
    {
        const auto state = engine.pluginState (slot);
        if (state.getSize() > 0)
            settings->setValue (slotKey (slot), state.toBase64Encoding());
    }
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
        windows[slot] = std::make_unique<PluginWindow> (title, editor, [this, slot]
        {
            windows.erase (slot);
            refreshPresets();
        });
}

void MainComponent::setBass (bool on)
{
    engine.setBassLine (on);
    bassSwitch.setButtonText (on ? "Stop test bass line" : "Start test bass line (Bm A E E, 112 BPM)");
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
    if (++ticks == 8)  // once, two seconds after start: into the log file too
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
    guitarButton.setBounds (row.removeFromLeft (row.getWidth() / 2).reduced (3, 0));
    bassButton.setBounds (row.reduced (3, 0));
    r.removeFromTop (6);

    row = r.removeFromTop (30);
    presetLabel.setBounds (row.removeFromLeft (60));
    nextButton.setBounds (row.removeFromRight (90).reduced (3, 0));
    previousButton.setBounds (row.removeFromRight (90).reduced (3, 0));
    presetBox.setBounds (row.reduced (3, 0));
    r.removeFromTop (6);

    bassSwitch.setBounds (r.removeFromTop (30).reduced (3, 0));
    r.removeFromTop (10);

    latencyLabel.setBounds (r.removeFromTop (24));
    cpuLabel.setBounds (r.removeFromTop (24));
    footLabel.setBounds (r.removeFromTop (24));
    r.removeFromTop (6);
    logView.setBounds (r);
}

} // namespace gt
