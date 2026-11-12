#pragma once

// The band's sounds per song (docs/instrument-tracks.md, step 7). A sound is what an instrument plays on: a plugin (its
// file) or its built-in sound, and the plugin's state. A song keeps, per band playstyle (Ballad, Rock, Pop), the sound
// each band member ("rhythm", "bass", "keys", "drums") plays its playstyle part on, and one sound per instrument for
// playing one of the song's tracks (the two leads, "lead" and "instrument1", only ever play a track).
//   %APPDATA%\YourBand\songs\<song>.sounds      a song's sounds (JSON)
//   %APPDATA%\YourBand\band-defaults.sounds     the playstyles' sounds a song starts with ("Save defaults")

#include <juce_core/juce_core.h>

#include <map>
#include <optional>

namespace gt
{

struct Sound
{
    static constexpr const char* builtIn = "built-in";
    juce::String plugin;      // the plugin's file, or builtIn (the keys and leads: off)
    juce::MemoryBlock state;  // the plugin's state (empty: the plugin's sound from last time; Numa Player keys: the playstyle's)

    bool isBuiltIn() const { return plugin == builtIn; }
    bool operator== (const Sound& other) const { return plugin == other.plugin && state == other.state; }
    bool operator!= (const Sound& other) const { return ! (*this == other); }
};

using SoundSet = std::map<juce::String, Sound>;  // by instrument id

/** An instrument's place in a song's mix: its volume (1: as the band plays it, 0 dB), its pan (-1 left to 1 right;
    none: from its musician's place on stage), and whether it is muted. */
struct Mix
{
    float volume = 1.0f;
    std::optional<float> pan;
    bool muted = false;
};

/** A song's sounds: per playstyle the band members', and each instrument's for playing a track; and its mix. */
struct SongSounds
{
    std::map<juce::String, SoundSet> styles;  // by playstyle ("Ballad")
    SoundSet tracks;
    std::map<juce::String, Mix> mix;  // by instrument id

    bool empty() const { return styles.empty() && tracks.empty() && mix.empty(); }
};

class SoundStore
{
public:
    /** The store in this folder (the engine's settings folder). */
    explicit SoundStore (const juce::File& folder) : root (folder) {}

    juce::File songFile (const juce::String& song) const;
    juce::File defaultsFile() const { return root.getChildFile ("band-defaults.sounds"); }

    /** A file's sounds (none if it is not there, cannot be read, or is of an older kind). */
    static SongSounds read (const juce::File&);
    /** Writes the sounds to the file. False if it cannot be written. */
    static bool write (const juce::File&, const SongSounds&);

private:
    juce::File root;
};

} // namespace gt
