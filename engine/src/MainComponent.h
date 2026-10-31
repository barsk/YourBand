#pragma once

// The engine's window, the control panel: audio device settings, the TONE3000 windows, preset switching, the connection to the
// game and what the band is doing, with live latency, CPU and footswitch readouts. Settings and tones are
// remembered in %APPDATA%\GuitarTutor\GuitarTutor Engine.settings, next to a log (engine.log).

#include <juce_audio_utils/juce_audio_utils.h>

#include "EngineCore.h"
#include "Footswitch.h"
#include "GameLink.h"
#include "ImportWindow.h"
#include "ParentWatch.h"
#include "SoundStore.h"

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
    juce::String bandBass;     // --band-bass <plugin file name, e.g. ABPL | synthesized>: instead of your choice
    juce::String bandGuitar;   // --band-guitar <plugin file name, e.g. "Kontakt 8" | synthesized>: the same
    juce::String bandLead1, bandLead2;  // --lead1 / --lead2 <plugin file name, e.g. "Numa Player" | synthesized (off)>: the same
    juce::File snapshot;       // --snapshot <file.png>: a picture of the control panel 3 s after the start (tests); with --import-test, of the import window too
    juce::String importTest;   // --import-test <UG chords page URL>: opens the import window and imports that page (tests)
    juce::String connectTest;  // --connect-test <instrument>:<track index>: picks that track for the instrument, as you would (tests); -1 its default part
    juce::String styleTest;  // --style-test <playstyle>: chooses it in the Band style list 6 s after the start, as you would (tests)
    bool playstylesTest = false;  // --playstyles-test: opens the band playstyle window 8 s after the start; with --snapshot, a picture of it

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
    // the band's drums, bass and guitar: each its built-in sound or an instrument plugin, chosen in a dropdown
    void setUpBandPart (EngineCore::Slot);
    void refreshBandPart (EngineCore::Slot);
    void bandBoxChanged (EngineCore::Slot);
    void chooseOtherPlugin (EngineCore::Slot);
    /** The part on one of its plugins (an index into its choices) or its built-in sound (-1); with `remember`,
        also next time; `fresh`: a new instance without its sound. False if the plugin cannot be loaded. */
    bool chooseBandInstrument (EngineCore::Slot, int choice, bool remember, bool fresh);
    /** The part's plugin with this path or file name ("ABPL"), as an index into its choices; -1 if none. */
    int choiceFor (EngineCore::Slot, const juce::String& plugin) const;
    int currentChoice (EngineCore::Slot) const;
    /** Loads a band instrument plugin, with its sound from last time if `restoreSound`; it plays for the band if
        it has a sound (or plays from the start). Returns false (and logs why) if it cannot be loaded. */
    bool startBandInstrument (EngineCore::Slot, const juce::File& plugin, bool silentUntilChosen, bool restoreSound);
    void keepBandState (EngineCore::Slot);
    void refreshInputs();
    void refreshPresets();
    void stepPreset (int step);
    void selectPreset (int index);
    /** Whether the band guitarist plays the rhythm: from the game (its G key), kept for next time and told to the
        game. */
    void setBandGuitarPlays (bool on);
    void sendBandGuitar();
    /** The import window (made the first time), and a song it has written: the game is asked to open it. */
    void showImport();
    void songImported (const juce::File& song);
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
    juce::Label songLabel { {}, "Song" }, inputLabel { {}, "Guitar input" }, styleLabel { {}, "Band style" };
    juce::ComboBox styleBox;  // the game's band playstyles, the one playing; choosing one asks the game to play it
    juce::ComboBox songBox, inputBox, presetBox;  // songBox: the game's songs, the one open; choosing one asks the game to open it
    juce::TextButton importButton { "Import new..." };  // next to the song list: opens the import window
    std::unique_ptr<ImportWindow> importWindow;
    juce::TextButton guitarButton { "Guitar tone..." };
    struct BandChoice { juce::File file; juce::String name; bool silentUntilChosen; float level; bool added; };
    struct BandPart  // a band part's plugin, and its window
    {
        juce::ComboBox box;
        juce::TextButton windowButton { "..." };  // the plugin's window
        std::vector<BandChoice> choices;  // the plugins it can play on
    };
    std::map<EngineCore::Slot, BandPart> bandParts;
    std::unique_ptr<juce::LookAndFeel> knobLook;  // the mixer's knobs (before the rows: it outlives them)
    /** The band's instruments (docs/instrument-tracks.md), a row each: the song's track it plays (chosen here, kept by
        the game), then its plugin and the plugin's window. */
    struct InstrumentRow
    {
        juce::String id;  // as the game knows it: "rhythm", "bass", "keys", "drums", "lead", "instrument1"
        EngineCore::Slot slot = EngineCore::Slot::bandGuitar;
        bool lead = false;  // the lead guitar and instrument 1: no part of their own, only ever a track
        bool playsTrack = false;  // a band member playing one of the song's tracks (else its playstyle part)
        juce::String part;  // its part in the engine's mixer ("guitar", "lead1"...)
        juce::Label label;
        juce::ComboBox track;
        juce::TextButton mute { "M" }, solo { "S" };  // its mixer channel (step 8): kept with the song, but solo
        juce::Slider volume, pan;
    };
    std::array<InstrumentRow, 6> instrumentRows;
    static constexpr int pickedTrackIds = 1000;  // a track list's ids from here: the track picked note by note (the rhythm guitar)
    static constexpr int stringTrackIds = 2000;  // and from here: the track string by string on its held chord (an experiment)
    void setUpInstrumentRows();
    /** The game's "tracks": the song's tracks to choose from in each row, and the one each instrument plays; without a
        game, or for a song with no tracks, only the band's playstyle part (a lead: none) and no choosing. */
    void showTracks (const juce::var& message);

    // the band's sounds per song (docs/instrument-tracks.md, step 7): per playstyle the band members', and the leads'
    std::unique_ptr<SoundStore> sounds;
    juce::String currentSong, currentStyle;  // the song the game has open (its file name) and its playstyle
    SongSounds songSounds;  // the open song's (the saved defaults, copied in the first time it is opened)
    /** The sound an instrument plays in the song: on its track the song's sound for that (a lead's always), else its
        playstyle's; null if none. */
    juce::StringArray styleNames;  // the game's playstyles ("Ballad", "Rock", "Pop")
    std::map<juce::String, juce::String> styleKeys;  // and each one's keyboard sound in Numa Player
    std::map<EngineCore::Slot, Sound> appliedSounds;  // per slot: the kept sound put on it last (only what differs is loaded)
    std::map<EngineCore::Slot, juce::MemoryBlock> appliedStates;  // and its plugin's state then (a window closed with another: changed)
    std::map<EngineCore::Slot, juce::String> editingStyle;  // a plugin window opened from the playstyle window: for that playstyle
    juce::TextButton playstylesButton { "Band playstyle defaults..." };
    std::unique_ptr<juce::DocumentWindow> playstyleWindow;
    std::function<void()> refreshPlaystyles;  // the playstyle window shows the song's sounds again
    Sound* keptSound (const InstrumentRow&, const juce::String& style);
    /** A band member's sound for its part in a playstyle (the playstyle window's), or null. */
    Sound* playstyleSound (const InstrumentRow&, const juce::String& style);
    /** The sound an instrument plays now: its plugin, and the plugin's state once it plays for the band. */
    Sound currentSound (EngineCore::Slot) const;
    /** The game's "load": the song's sounds (the saved defaults the first time it is opened), and the band on its
        playstyle's. */
    void applySounds (const juce::String& song, const juce::String& style);
    /** Puts an instrument on a kept sound, loading only what differs from what plays (a playstyle's keys on Numa Player
        with no state of their own: that playstyle's keyboard sound). */
    void applySound (EngineCore::Slot, const Sound&, const juce::String& style);
    /** Keeps what an instrument plays now as the open song's sound for what it plays (its track, or its part in the
        playstyle playing). */
    void keepSongSound (const InstrumentRow&);
    /** Keeps it as the sound of a band member's part in a playstyle. */
    void keepPlaystyleSound (const InstrumentRow&, const juce::String& style);
    void saveSongSounds();
    void showPlaystyles();
    /** In the playstyle window: a playstyle's plugin for an instrument (an index into its choices; -1 built-in or off).
        The band changes at once if it plays that part. */
    void choosePlaystylePlugin (const InstrumentRow&, const juce::String& style, int choice);
    /** In the playstyle window: the band instrument on a playstyle's sound, and its plugin window; closed, the sound is
        the playstyle's and the instrument is back on what it plays. */
    void editPlaystyleSound (const InstrumentRow&, const juce::String& style);
    /** The song's playstyle sounds as the ones a song gets the first time it is opened. */
    void savePlaystyleDefaults();
    /** In the playstyle window: a MONSTER plugin's playstyle sound on a new instance, its window open to pick another
        sound (a MONSTER plugin shows its sound picker only while it has none); closed, as editPlaystyleSound. */
    void pickPlaystyleSound (const InstrumentRow&, const juce::String& style);
    /** The Band style list: the game's playstyles, the one playing selected. */
    void showStyles();
    /** A plugin file added to a part's choices ("Other plugin..."); its index. */
    int addPluginChoice (EngineCore::Slot, const juce::File& plugin);
    const InstrumentRow* rowFor (EngineCore::Slot) const;
    /** Whether the command line chose the slot's plugin (tests): kept sounds leave it alone. */
    bool chosenByOptions (EngineCore::Slot) const;
    std::unique_ptr<juce::FileChooser> pluginChooser;
    std::map<EngineCore::Slot, juce::File> bandPlugins;  // the band instrument plugins loaded
    std::map<EngineCore::Slot, juce::MemoryBlock> blankStates;  // an instrument plugin's state before a sound is chosen
    juce::Label latencyLabel, cpuLabel, footLabel, gameLabel, bandLabel;
    // the band's mixer (docs/instrument-tracks.md, step 8): a channel per instrument row, kept with the song
    std::map<juce::String, float> stagePans;  // per instrument: the pan of its musician's place on stage (the game's)
    juce::TextButton stagePanButton { "Pan from stage" };
    std::array<juce::Label, 4> mixHeads;  // above the rows' mute, solo, volume and pan
    bool mixChanged = false;  // the song's mix to keep (from the timer: not at every step of a knob)
    void setUpMixer (InstrumentRow&);
    /** An instrument's mix in the open song (a song not mixed yet: at the level its fader had before, from stage). */
    Mix& songMix (const InstrumentRow&);
    /** The open song's mix on the rows' controls and in the engine. */
    void showMix();
    /** An instrument's volume and pan in the engine (its pan from its place on stage, unless set). */
    void applyMix (const InstrumentRow&);
    /** Mute and solo: an instrument is silent when muted, or while another is soloed and it is not. */
    void applyMutes();
    /** The pan knob's text: "L 40", "Centre", "R 25", and "(stage)" while it follows its place on stage. */
    juce::String panText (const InstrumentRow&, double pan) const;
    juce::TextEditor logView;

    std::map<EngineCore::Slot, std::unique_ptr<juce::DocumentWindow>> windows;
    std::vector<Preset> presetList;
    int ticks = 0;
};

} // namespace gt
