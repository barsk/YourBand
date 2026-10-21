#include "Instruments.h"

#include <cmath>

namespace gt
{

namespace
{
    constexpr float twoPi = 6.283185307f;

    /** Per-sample multiplier that takes a level down by 60 dB in `seconds`. */
    float decayFor (double rate, double seconds)
    {
        return (float) std::exp (std::log (0.001) / (seconds * rate));
    }
}

//==============================================================================
void Biquad::bandPass (double rate, double frequency, double q)
{
    const auto w = 2.0 * juce::MathConstants<double>::pi * frequency / rate, alpha = std::sin (w) / (2.0 * q), a0 = 1.0 + alpha;
    b0 = (float) (alpha / a0);
    b1 = 0.0f;
    b2 = (float) (-alpha / a0);
    a1 = (float) (-2.0 * std::cos (w) / a0);
    a2 = (float) ((1.0 - alpha) / a0);
    z1 = z2 = 0.0f;
}

void Biquad::highPass (double rate, double frequency, double q)
{
    const auto w = 2.0 * juce::MathConstants<double>::pi * frequency / rate, alpha = std::sin (w) / (2.0 * q), c = std::cos (w), a0 = 1.0 + alpha;
    b0 = (float) ((1.0 + c) / 2.0 / a0);
    b1 = (float) (-(1.0 + c) / a0);
    b2 = b0;
    a1 = (float) (-2.0 * c / a0);
    a2 = (float) ((1.0 - alpha) / a0);
    z1 = z2 = 0.0f;
}

void Biquad::lowPass (double rate, double frequency, double q)
{
    const auto w = 2.0 * juce::MathConstants<double>::pi * frequency / rate, alpha = std::sin (w) / (2.0 * q), c = std::cos (w), a0 = 1.0 + alpha;
    b0 = (float) ((1.0 - c) / 2.0 / a0);
    b1 = (float) ((1.0 - c) / a0);
    b2 = b0;
    a1 = (float) (-2.0 * c / a0);
    a2 = (float) ((1.0 - alpha) / a0);
    z1 = z2 = 0.0f;
}

//==============================================================================
void DrumKit::prepare (double sampleRate)
{
    rate = sampleRate;
    silence();
}

void DrumKit::silence()
{
    for (auto& v : voices)
        v.note = -1;
}

float DrumKit::noise()
{
    seed ^= seed << 13;
    seed ^= seed >> 17;
    seed ^= seed << 5;
    return (float) (int32_t) seed / 2147483648.0f;
}

DrumKit::Voice& DrumKit::freeVoice()
{
    Voice* oldest = &voices[0];
    for (auto& v : voices)
    {
        if (v.note < 0)
            return v;
        if (v.age > oldest->age)
            oldest = &v;
    }
    return *oldest;
}

void DrumKit::trigger (int note, float velocity, int offset)
{
    // a closed or pedal hat chokes an open one
    if (note == drum::closedHat || note == drum::pedalHat)
        for (auto& v : voices)
            if (v.note == drum::openHat)
                v.noiseDecay = decayFor (rate, 0.03);

    Voice v;
    v.note = note;
    v.delay = juce::jmax (0, offset);
    v.gain = std::pow (juce::jlimit (0.0f, 1.0f, velocity), 1.6f);
    v.env = v.noiseEnv = 1.0f;
    auto tone = [&] (float from, float to, double sweepSeconds, double seconds, float level)
    {
        v.startFrequency = from;
        v.endFrequency = to;
        v.sweep = (float) std::exp (-1.0 / (sweepSeconds * rate));
        v.decay = decayFor (rate, seconds);
        v.toneLevel = level;
    };
    auto noisy = [&] (double seconds, float level)
    {
        v.noiseDecay = decayFor (rate, seconds);
        v.noiseLevel = level;
    };

    switch (note)
    {
        case drum::kick:
            tone (165.0f, 48.0f, 0.025, 0.45, 1.0f);
            noisy (0.012, 0.35f);
            v.filter.bandPass (rate, 2500.0, 0.7);
            v.gain *= 0.95f;
            break;
        case drum::snare:
            tone (205.0f, 180.0f, 0.01, 0.16, 0.45f);
            noisy (0.26, 0.75f);
            v.filter.highPass (rate, 1500.0, 0.7);
            v.filter2.lowPass (rate, 9000.0, 0.7);
            v.gain *= 0.62f;
            break;
        case drum::sideStick:
            tone (900.0f, 800.0f, 0.004, 0.05, 0.4f);
            noisy (0.04, 0.8f);
            v.filter.bandPass (rate, 1800.0, 2.0);
            v.gain *= 0.9f;
            v.pan = 0.05f;
            break;
        case drum::closedHat:
        case drum::pedalHat:
            noisy (note == drum::closedHat ? 0.06 : 0.04, note == drum::closedHat ? 0.5f : 0.35f);
            v.filter.highPass (rate, 7000.0, 0.7);
            v.gain *= 1.0f;
            v.pan = 0.3f;
            break;
        case drum::openHat:
            noisy (0.5, 0.45f);
            v.filter.highPass (rate, 6500.0, 0.7);
            v.gain *= 0.8f;
            v.pan = 0.3f;
            break;
        case drum::crash:
            noisy (1.8, 0.6f);
            v.filter.highPass (rate, 3500.0, 0.6);
            v.filter2.lowPass (rate, 14000.0, 0.7);
            v.gain *= 0.45f;
            v.pan = -0.35f;
            break;
        case drum::ride:
            tone (3200.0f, 3150.0f, 0.1, 0.6, 0.12f);
            noisy (0.9, 0.5f);
            v.filter.bandPass (rate, 7000.0, 1.5);
            v.gain *= 0.7f;
            v.pan = 0.4f;
            break;
        case drum::highTom:
        case drum::midTom:
        case drum::lowTom:
        {
            const auto f = note == drum::highTom ? 190.0f : note == drum::midTom ? 150.0f : 115.0f;
            tone (f, f * 0.78f, 0.05, 0.5, 1.0f);
            noisy (0.06, 0.15f);
            v.filter.bandPass (rate, 1000.0, 1.0);
            v.gain *= 0.75f;
            v.pan = note == drum::highTom ? -0.2f : note == drum::midTom ? 0.05f : 0.25f;
            break;
        }
        case drum::sticks:
            tone (2500.0f, 2400.0f, 0.01, 0.03, 0.4f);
            noisy (0.025, 0.9f);
            v.filter.bandPass (rate, 3000.0, 3.0);
            v.gain *= 0.55f;
            break;
        default:
            return;
    }
    freeVoice() = v;  // filter2 passes through unless a hit set it
}

void DrumKit::render (float* left, float* right, int numSamples)
{
    for (auto& v : voices)
    {
        if (v.note < 0)
            continue;
        const auto l = std::cos ((v.pan + 1.0f) * 0.25f * juce::MathConstants<float>::pi);
        const auto r = std::sin ((v.pan + 1.0f) * 0.25f * juce::MathConstants<float>::pi);
        for (int i = 0; i < numSamples; ++i)
        {
            if (v.delay > 0)
            {
                --v.delay;
                continue;
            }
            float s = 0.0f;
            if (v.toneLevel > 0.0f)
            {
                v.startFrequency = v.endFrequency + (v.startFrequency - v.endFrequency) * v.sweep;
                v.phase += v.startFrequency / (float) rate;
                if (v.phase >= 1.0f)
                    v.phase -= 1.0f;
                s += std::sin (twoPi * v.phase) * v.env * v.toneLevel;
                v.env *= v.decay;
            }
            if (v.noiseLevel > 0.0f)
            {
                s += v.filter2.process (v.filter.process (noise())) * v.noiseEnv * v.noiseLevel;
                v.noiseEnv *= v.noiseDecay;
            }
            s *= v.gain;
            left[i] += s * l;
            right[i] += s * r;
            v.age += 1.0;
        }
        if ((v.toneLevel <= 0.0f || v.env < 1.0e-4f) && (v.noiseLevel <= 0.0f || v.noiseEnv < 1.0e-4f) && v.delay == 0)
            v.note = -1;
    }
}

//==============================================================================
void BassVoice::prepare (double sampleRate)
{
    rate = sampleRate;
    silence();
}

void BassVoice::silence()
{
    line.fill (0.0f);
    numEvents = 0;
    damping = 0.98f;
    tone = 0.0f;
}

void BassVoice::noteOn (int note, float velocity, int offset)
{
    if (numEvents < (int) events.size())
        events[(size_t) numEvents++] = { true, note, velocity, offset };
}

void BassVoice::noteOff (int offset)
{
    if (numEvents < (int) events.size())
        events[(size_t) numEvents++] = { false, 0, 0.0f, offset };
}

void BassVoice::pluck (int note, float velocity)
{
    const auto frequency = 440.0 * std::pow (2.0, (note - 69) / 12.0);
    length = juce::jlimit<size_t> (2, line.size(), (size_t) std::round (rate / frequency));
    pos = 0;
    // a soft pluck: noise smoothed by a one-pole low-pass, so the attack is round like fingers on a bass
    float s = 0.0f;
    const auto amount = 0.35f + 0.45f * velocity;
    for (size_t i = 0; i < length; ++i)
    {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        s += 0.35f * ((float) (int32_t) seed / 2147483648.0f - s);
        line[i] = amount * s;
    }
    damping = 0.997f;
}

void BassVoice::render (float* out, int numSamples)
{
    // in time order, a note's end before the next note's start at the same moment
    for (int i = 1; i < numEvents; ++i)
        for (int j = i; j > 0; --j)
        {
            auto& a = events[(size_t) j - 1];
            auto& b = events[(size_t) j];
            if (a.offset < b.offset || (a.offset == b.offset && (! a.on || b.on)))
                break;
            std::swap (a, b);
        }
    int next = 0;
    for (int i = 0; i < numSamples; ++i)
    {
        while (next < numEvents && events[(size_t) next].offset <= i)
        {
            const auto& e = events[(size_t) next++];
            if (e.on)
                pluck (e.note, e.velocity);
            else
                damping = 0.985f;  // the player mutes the string
        }
        const auto after = (pos + 1) % length;
        const auto value = 0.5f * (line[pos] + line[after]) * damping;
        tone += 0.5f * (value - tone);  // keeps the top end of a bass DI signal soft
        line[pos] = value;
        pos = after;
        out[i] += tone;
    }
    numEvents = 0;
}

} // namespace gt
