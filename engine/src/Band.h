#pragma once

// The band: plays the parts the game arranged (schema/engine-protocol.md, "load") on the song's tempo map.
// The band owns the song's transport: the game asks it to play, pause, seek and change speed, and the band
// publishes where the music is in the shared clock after every block.
//
//   outputs 0-1   drums (stereo): the synthesized kit, and the count-in sticks
//   output  2     bass, synthesized (a DI sound)
//   output  3     rhythm guitar DI, synthesized, into the band guitar's amp (a TONE3000 instance)
//   MIDI out      keys on channel 1 (Numa Player); with instrument plugins for them, the bass on channel 2, the
//                 guitar on channel 3 and the drums on channel 10 (General MIDI drum notes) instead of the
//                 synthesized ones
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
    std::vector<size_t> guitar;  // the indices of the guitar's notes, in time order (the guitar has its own cursor)

    double bpmAt (double quarter) const;
    /** A beat's length in quarter notes at a position (1 in 4/4, 0.5 in 6/8). */
    double beatAt (double quarter) const;

    /** Reads a "load" message; null with an error if it cannot. */
    static std::unique_ptr<Score> fromMessage (const juce::var& message, juce::String& error);
};

class BandProcessor final : public SimpleProcessor
{
public:
    enum Part { drums = 0, bass = 1, keys = 2, guitar = 3 };
    /** The MIDI channel each part plays on when it goes to a plugin. */
    static int channelOf (int part) { return part == drums ? 10 : part == bass ? 2 : part == guitar ? 3 : 1; }

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
    /** Plays a part (drums, bass, guitar) on an instrument plugin (as MIDI) instead of the synthesized one. */
    void setPluginFor (Part part, bool on) { onPlugin[(size_t) part].store (on); }
    bool usesPluginFor (Part part) const { return onPlugin[(size_t) part].load(); }
    /** Whether the band's guitarist plays (the rhythm: the song's strumming on its chords). */
    void setGuitarPlays (bool on) { guitarPlays.store (on); }
    /** How early (seconds) the guitar's notes go to its plugin, when it plays on one: a plugin whose sound builds
        slowly (Kontakt's strummed guitars) is heard late by about that much. */
    void setGuitarAdvance (double seconds) { guitarAdvance.store (juce::jlimit (0.0, 0.2, seconds)); }
    bool guitarIsPlaying() const { return guitarPlays.load(); }

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
    size_t firstGuitarNoteFrom (double quarter) const;
    void emit (const BandNote&, double dueAt, int sample, juce::MidiBuffer&);
    void addOff (Off);
    void collectGarbage();

    SharedClock& clock;
    DrumKit kit;
    BassVoice bassVoice;
    GuitarVoice guitarVoice;
    double sampleRate = 48000.0;

    // message thread -> audio thread
    juce::AbstractFifo fifo { 64 };
    std::array<Command, 64> commands;
    std::atomic<const Score*> pending { nullptr }, inUse { nullptr };
    std::vector<std::unique_ptr<Score>> owned;  // message thread; freed once neither pending nor in use
    const Score* latest = nullptr;
    std::atomic<double> outputLatency { 0.0 };
    std::array<std::atomic<bool>, 4> onPlugin {};  // per part; the keys are always on their plugin
    std::atomic<bool> guitarPlays { false };
    std::atomic<double> guitarAdvance { 0.0 };

    // audio thread
    const Score* playingScore = nullptr;
    bool playing = false;
    double quarter = 0.0, speed = 1.0, countInUntil = 0.0, countInLength = 0.0;
    size_t cursor = 0, guitarCursor = 0;
    std::array<Off, 256> offs;
    int numOffs = 0, bassSerial = 0;

    // audio thread -> window
    std::atomic<double> shownPosition { 0.0 };
    std::atomic<bool> shownPlaying { false };
};

} // namespace gt
