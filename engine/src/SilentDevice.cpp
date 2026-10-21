#include "SilentDevice.h"

namespace gt
{

namespace
{
    class SilentDevice final : public juce::AudioIODevice, private juce::Thread
    {
    public:
        SilentDevice (juce::File file)
            : juce::AudioIODevice (SilentDeviceType::deviceName, SilentDeviceType::typeName),
              juce::Thread ("Silent audio device"),
              recording (std::move (file))
        {
        }

        ~SilentDevice() override { close(); }

        juce::StringArray getOutputChannelNames() override { return { "Left", "Right" }; }
        juce::StringArray getInputChannelNames() override { return { "Input 1", "Input 2" }; }
        juce::Array<double> getAvailableSampleRates() override { return { 48000.0 }; }
        juce::Array<int> getAvailableBufferSizes() override { return { 64 }; }
        int getDefaultBufferSize() override { return 64; }

        juce::String open (const juce::BigInteger& inputs, const juce::BigInteger& outputs, double, int) override
        {
            activeInputs = inputs;
            activeInputs.setRange (2, activeInputs.getHighestBit() + 1, false);
            activeOutputs = outputs;
            activeOutputs.setRange (2, activeOutputs.getHighestBit() + 1, false);
            isOpenNow = true;
            return {};
        }

        void close() override
        {
            stop();
            isOpenNow = false;
        }

        bool isOpen() override { return isOpenNow; }

        void start (juce::AudioIODeviceCallback* newCallback) override
        {
            if (newCallback == nullptr || isThreadRunning())
                return;
            callback = newCallback;
            callback->audioDeviceAboutToStart (this);
            if (recording != juce::File())
            {
                recording.getParentDirectory().createDirectory();
                recording.deleteFile();  // an output stream on an existing file appends to it
                if (std::unique_ptr<juce::OutputStream> stream = recording.createOutputStream())
                    writer = juce::WavAudioFormat().createWriterFor (stream, juce::AudioFormatWriterOptions{}.withSampleRate (rate)
                                                                                                         .withNumChannels (2)
                                                                                                         .withBitsPerSample (24));
            }
            startThread (juce::Thread::Priority::highest);
        }

        void stop() override
        {
            if (! isThreadRunning())
                return;
            stopThread (2000);
            if (callback != nullptr)
                callback->audioDeviceStopped();
            callback = nullptr;
            writer.reset();  // finishes the WAV file
        }

        bool isPlaying() override { return isThreadRunning(); }
        juce::String getLastError() override { return {}; }
        int getCurrentBufferSizeSamples() override { return block; }
        double getCurrentSampleRate() override { return rate; }
        int getCurrentBitDepth() override { return 32; }
        juce::BigInteger getActiveOutputChannels() const override { return activeOutputs; }
        juce::BigInteger getActiveInputChannels() const override { return activeInputs; }
        int getOutputLatencyInSamples() override { return block; }
        int getInputLatencyInSamples() override { return block; }

    private:
        void run() override
        {
            juce::AudioBuffer<float> inputs (2, block), outputs (2, block);
            inputs.clear();
            const float* in[] = { inputs.getReadPointer (0), inputs.getReadPointer (1) };
            float* out[] = { outputs.getWritePointer (0), outputs.getWritePointer (1) };
            const auto began = juce::Time::getMillisecondCounterHiRes();
            int64_t samples = 0;
            const juce::AudioIODeviceCallbackContext context;

            while (! threadShouldExit())
            {
                // keep up with the wall clock: as many blocks as are due, then rest
                const auto due = (juce::Time::getMillisecondCounterHiRes() - began) / 1000.0 * rate;
                if ((double) samples >= due)
                {
                    juce::Thread::sleep (1);
                    continue;
                }
                outputs.clear();
                // a device hands over its active channels only, packed from the first
                callback->audioDeviceIOCallbackWithContext (in, juce::jmin (2, activeInputs.countNumberOfSetBits()), out,
                                                            juce::jmin (2, activeOutputs.countNumberOfSetBits()), block, context);
                if (writer != nullptr)
                    writer->writeFromAudioSampleBuffer (outputs, 0, block);
                samples += block;
            }
        }

        static constexpr int block = 64;
        static constexpr double rate = 48000.0;
        juce::File recording;
        std::unique_ptr<juce::AudioFormatWriter> writer;
        juce::AudioIODeviceCallback* callback = nullptr;
        juce::BigInteger activeInputs, activeOutputs;
        bool isOpenNow = false;
    };
}

SilentDeviceType::SilentDeviceType (juce::File file)
    : juce::AudioIODeviceType (typeName), recording (std::move (file))
{
}

juce::AudioIODevice* SilentDeviceType::createDevice (const juce::String&, const juce::String&)
{
    return new SilentDevice (recording);
}

} // namespace gt
