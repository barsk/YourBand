#pragma once

// The band's own instruments, synthesized: a drum kit and a plucked bass. They need no downloads; a sampled kit
// and sampled bass can take their place later (docs/plan.md, "Audio").
//
// Both are driven from the audio thread: events carry a sample offset into the current block, rendering never
// allocates, and voices are taken from fixed pools.

#include <juce_audio_basics/juce_audio_basics.h>

#include <array>
#include <cstdint>

namespace gt
{

/** A second-order filter (RBJ cookbook), enough for drum noise shaping. */
struct Biquad
{
    float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0, z1 = 0, z2 = 0;

    void bandPass (double rate, double frequency, double q);
    void highPass (double rate, double frequency, double q);
    void lowPass (double rate, double frequency, double q);

    float process (float x)
    {
        const auto y = b0 * x + z1;
        z1 = b1 * x - a1 * y + z2;
        z2 = b2 * x - a2 * y;
        return y;
    }
};

/** General MIDI drum notes the kit plays (as the game's arranger writes them), plus sticks for count-ins. */
namespace drum
{
    constexpr int sticks = 31, kick = 36, sideStick = 37, snare = 38, lowTom = 45, closedHat = 42, pedalHat = 44,
                  openHat = 46, midTom = 47, crash = 49, highTom = 50, ride = 51;
}

/** A synthesized drum kit: sine-sweep kick and toms, tone-and-noise snare, filtered-noise hats and cymbals. */
class DrumKit
{
public:
    void prepare (double sampleRate);
    /** Starts a hit `offset` samples into the next block rendered. Unknown notes are ignored. */
    void trigger (int note, float velocity, int offset);
    /** Adds the kit into two channels (left, right). */
    void render (float* left, float* right, int numSamples);
    void silence();

private:
    struct Voice
    {
        int note = -1, delay = 0;
        double age = 0;           // samples since the hit
        float gain = 0, pan = 0;  // pan -1 left .. 1 right
        float phase = 0, phase2 = 0;
        float decay = 0, noiseDecay = 0, toneLevel = 0, noiseLevel = 0, env = 0, noiseEnv = 0;
        float startFrequency = 0, endFrequency = 0, sweep = 0;
        Biquad filter, filter2;
    };

    float noise();
    Voice& freeVoice();

    double rate = 48000.0;
    std::array<Voice, 32> voices;
    uint32_t seed = 0x1234567u;
};

/** A plucked bass string (Karplus-Strong), one note at a time, as a clean DI signal for a bass amp. */
class BassVoice
{
public:
    void prepare (double sampleRate);
    void noteOn (int note, float velocity, int offset);
    void noteOff (int offset);
    void render (float* out, int numSamples);
    void silence();

private:
    struct Event { bool on; int note; float velocity; int offset; };
    void pluck (int note, float velocity);

    double rate = 48000.0;
    std::array<float, 4096> line {};  // room for notes down to 12 Hz at 48 kHz
    size_t length = 2, pos = 0;
    float damping = 0.98f, tone = 0.0f, level = 0.0f;
    std::array<Event, 16> events {};
    int numEvents = 0;
    uint32_t seed = 0x9e3779b9u;
};

} // namespace gt
