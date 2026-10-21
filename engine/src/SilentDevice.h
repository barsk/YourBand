#pragma once

// A pretend audio interface for testing without making a sound: it runs the engine's audio callback at
// real-time pace (48 kHz, 64-sample blocks, as on the Scarlett), feeds it silence, and can record what the engine
// plays to a WAV file. Started with --silent [--record <file.wav>].

#include <juce_audio_devices/juce_audio_devices.h>
#include <juce_audio_formats/juce_audio_formats.h>

namespace gt
{

class SilentDeviceType final : public juce::AudioIODeviceType
{
public:
    static constexpr const char* typeName = "Silent";
    static constexpr const char* deviceName = "Silent (no sound)";

    /** Where the device records its output (none if empty). */
    explicit SilentDeviceType (juce::File recording);

    void scanForDevices() override {}
    juce::StringArray getDeviceNames (bool) const override { return { deviceName }; }
    int getDefaultDeviceIndex (bool) const override { return 0; }
    int getIndexOfDevice (juce::AudioIODevice*, bool) const override { return 0; }
    bool hasSeparateInputsAndOutputs() const override { return false; }
    juce::AudioIODevice* createDevice (const juce::String& outputName, const juce::String& inputName) override;

private:
    juce::File recording;
};

} // namespace gt
