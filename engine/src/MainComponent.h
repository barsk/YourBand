#pragma once

// The engine's window for experiment E1: audio device settings, the TONE3000 windows, tone A/B and the
// test bass line, with live latency, CPU and footswitch readouts. Settings and tones are remembered in
// %APPDATA%\GuitarTutor\GuitarTutor Engine.settings.

#include <juce_audio_utils/juce_audio_utils.h>

#include "EngineCore.h"
#include "Footswitch.h"

#include <map>

namespace gt
{

class MainComponent final : public juce::Component, private juce::Timer
{
public:
    MainComponent();
    ~MainComponent() override;

    void resized() override;
    void paint (juce::Graphics&) override;

private:
    void timerCallback() override;
    void log (const juce::String&);
    void showPlugin (EngineCore::Slot, const juce::String& title);
    void refreshInputs();
    void setTone (int tone);
    void setBass (bool on);
    void saveSettings();

    std::unique_ptr<juce::PropertiesFile> settings;
    EngineCore engine;
    Footswitch footswitch;

    std::unique_ptr<juce::AudioDeviceSelectorComponent> selector;
    juce::Label inputLabel { {}, "Guitar input" };
    juce::ComboBox inputBox;
    juce::TextButton toneAButton { "Tone A..." }, toneBButton { "Tone B..." }, bassButton { "Bass amp..." };
    juce::TextButton toneSwitch, bassSwitch;
    juce::Label latencyLabel, cpuLabel, footLabel;
    juce::TextEditor logView;

    std::map<EngineCore::Slot, std::unique_ptr<juce::DocumentWindow>> windows;
    int ticks = 0;
};

} // namespace gt
