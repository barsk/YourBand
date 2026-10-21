#pragma once

// The band's keyboard sound in Numa Player (Studiologic, free, installed separately). Numa Player has no program
// list for a host: a sound is chosen by giving the plugin a state whose zone names it. The state is made from
// the standalone app's last state, with the zone's sound swapped (as tools/numa_state.py does offline).

#include <juce_audio_processors/juce_audio_processors.h>

namespace gt::numa
{

/** Where Numa Player keeps its settings and sounds (%APPDATA%\Studiologic\Numa Player). */
juce::File folder();

/** The installed sounds, as "<library>/<sound>" ("2 - E_PIANO/1 - EP-Mark I"). */
juce::StringArray sounds();

/** A state for the VST3 plugin (JUCE's VST3 host format) that plays `sound`; empty with an error if it cannot. */
juce::MemoryBlock stateFor (const juce::String& sound, juce::String& error);

} // namespace gt::numa
