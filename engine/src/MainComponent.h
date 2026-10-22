#pragma once

// The engine's window, the control panel: audio device settings, the TONE3000 windows, preset switching, the connection to the
// game and what the band is doing, with live latency, CPU and footswitch readouts. Settings and tones are
// remembered in %APPDATA%\GuitarTutor\GuitarTutor Engine.settings, next to a log (engine.log).

#include <juce_audio_utils/juce_audio_utils.h>

#include "EngineCore.h"
#include "Footswitch.h"
#include "GameLink.h"
#include "ParentWatch.h"

#include <array>
#include <map>

namespace gt
{

/** How the engine was started (command line). */
struct EngineOptions
{
    bool silent = false;       // --silent: no audio interface, real time kept by a pretend one
    juce::File recording;      // --record <file.wav>: with --silent, what the engine plays
    double runFor = 0.0;       // --run-for <seconds>: quit after that long (0: run until closed)
    int parent = 0;            // --parent <process id>: started by the game; hidden, and gone with the game

    static EngineOptions fromCommandLine (const juce::StringArray& args);
};

class MainComponent final : public juce::Component, private juce::Timer
{
public:
    explicit MainComponent (const EngineOptions&);
    ~MainComponent() override;

    void resized() override;
    void paint (juce::Graphics&) override;

private:
    void timerCallback() override;
    void log (const juce::String&);
    void showPlugin (EngineCore::Slot, const juce::String& title);
    void refreshInputs();
    void refreshPresets();
    void stepPreset (int step);
    void selectPreset (int index);
    void handle (const juce::var& message);
    void sendCommand (const juce::String& command);
    void saveSettings();

    std::unique_ptr<juce::PropertiesFile> settings;
    EngineOptions options;
    EngineCore engine;
    Footswitch footswitch;
    GameLink game;
    std::unique_ptr<ParentWatch> parentWatch;

    std::unique_ptr<juce::AudioDeviceSelectorComponent> selector;
    juce::Label inputLabel { {}, "Guitar input" }, presetLabel { {}, "Preset" };
    juce::ComboBox inputBox, presetBox;
    juce::TextButton guitarButton { "Guitar tone..." }, bassButton { "Bass amp..." };
    juce::TextButton previousButton { "< Previous" }, nextButton { "Next >" };
    juce::Label latencyLabel, cpuLabel, footLabel, gameLabel, bandLabel;
    // the band's mixer: drums, bass, keys
    std::array<juce::Slider, 3> faders;
    std::array<juce::Label, 3> faderLabels;
    juce::TextEditor logView;

    std::map<EngineCore::Slot, std::unique_ptr<juce::DocumentWindow>> windows;
    std::vector<Preset> presetList;
    int ticks = 0;
};

} // namespace gt
