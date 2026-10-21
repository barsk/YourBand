#pragma once

// The band: plays the parts the game arranged (schema/engine-protocol.md, "load") on the song's tempo map.
// The band owns the song's transport: the game asks it to play, pause, seek and change speed, and the band
// publishes where the music is in the shared clock after every block.
//
//   outputs 0-1   drums (stereo)
//   output  2     bass DI, into the bass amp (a TONE3000 instance)
//   MIDI out      keys, into the keyboard plugin (Numa Player)
//
// Rendered in the audio callback for now; docs/plan.md has it moving to its own thread, ahead of time, as the
// band grows.

#include "Instruments.h"
#include "Processors.h"
#include "SharedClock.h"

#include <array>
#include <atomic>
#include <memory>
#include <vector>

namespace gt
{

struct BandNote
{
    int part = 0, pitch = 0;
    double at = 0.0, length = 0.0;  // quarter notes
    float velocity = 0.0f;          // 0..1
};

/** What the band plays: the song's tempo map and the notes in time order. Read-only once the band has it. */
struct Score
{
    struct Tempo { double at, bpm; };
    struct Meter { double at; int numerator, denominator; };  // at: where the bar starts, in quarter notes

    juce::String title, style, keys;
    std::vector<Tempo> tempos;
    std::vector<Meter> meters;
    std::vector<BandNote> notes;

    double bpmAt (double quarter) const;
    /** A beat's length in quarter notes at a position (1 in 4/4, 0.5 in 6/8). */
    double beatAt (double quarter) const;

    /** Reads a "load" message; null with an error if it cannot. */
    static std::unique_ptr<Score> fromMessage (const juce::var& message, juce::String& error);
};

class BandProcessor final : public SimpleProcessor
{
public:
    enum Part { drums = 0, bass = 1, keys = 2 };

    explicit BandProcessor (SharedClock&);

    const juce::String getName() const override { return "Band"; }
    bool producesMidi() const override { return true; }
    void prepareToPlay (double sampleRate, int samplesPerBlock) override;
    void processBlock (juce::AudioBuffer<float>&, juce::MidiBuffer&) override;

    // message thread: the transport (each takes effect at the start of the next block)
    void load (std::unique_ptr<Score>);
    void play (double from, double countIn);
    void pause();
    void seek (double to);
    void setSpeed (double speed);
    const Score* score() const { return latest; }

    void setOutputLatency (double seconds) { outputLatency.store (seconds); }
    /** The drums' volume (the bass and keys have theirs after their plugins). */
    void setDrumsGain (float g) { drumsGain.store (juce::jlimit (0.0f, 4.0f, g)); }
    float getDrumsGain() const { return drumsGain.load(); }

    // any thread: for the window
    double position() const { return shownPosition.load(); }
    bool isPlaying() const { return shownPlaying.load(); }

private:
    struct Command
    {
        enum Type { play, pause, seek, speed } type = pause;
        double a = 0.0, b = 0.0;
    };
    struct Off
    {
        int part, pitch, serial;
        double at;
    };

    void push (Command);
    void apply (const Command&, juce::MidiBuffer&);
    void releaseAll (juce::MidiBuffer&);
    size_t firstNoteFrom (double quarter) const;
    void addOff (Off);
    void collectGarbage();

    SharedClock& clock;
    DrumKit kit;
    BassVoice bassVoice;
    double sampleRate = 48000.0;

    // message thread -> audio thread
    juce::AbstractFifo fifo { 64 };
    std::array<Command, 64> commands;
    std::atomic<const Score*> pending { nullptr }, inUse { nullptr };
    std::vector<std::unique_ptr<Score>> owned;  // message thread; freed once neither pending nor in use
    const Score* latest = nullptr;
    std::atomic<double> outputLatency { 0.0 };
    std::atomic<float> drumsGain { 0.8f };
    float drumsApplied = 0.8f;

    // audio thread
    const Score* playingScore = nullptr;
    bool playing = false;
    double quarter = 0.0, speed = 1.0, countInUntil = 0.0, countInLength = 0.0;
    size_t cursor = 0;
    std::array<Off, 256> offs;
    int numOffs = 0, bassSerial = 0;

    // audio thread -> window
    std::atomic<double> shownPosition { 0.0 };
    std::atomic<bool> shownPlaying { false };
};

} // namespace gt
