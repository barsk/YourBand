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
    juce::Label songLabel { {}, "Song" }, inputLabel { {}, "Guitar input" }, presetLabel { {}, "Tone preset" };
    juce::ComboBox songBox, inputBox, presetBox;  // songBox: the game's songs, the one open; choosing one asks the game to open it
    juce::TextButton importButton { "Import new..." };  // next to the song list: opens the import window
    std::unique_ptr<ImportWindow> importWindow;
    juce::TextButton guitarButton { "Guitar tone..." };
    struct BandChoice { juce::File file; juce::String name; bool silentUntilChosen; float level; bool added; };
    struct BandPart  // a band part's plugin, and its window
    {
        juce::ComboBox box;
        juce::TextButton windowButton { "Plugin window..." };
        std::vector<BandChoice> choices;  // the plugins it can play on
    };
    std::map<EngineCore::Slot, BandPart> bandParts;
    /** The band's instruments (docs/instrument-tracks.md), a row each: the song's track it plays (chosen here, kept by
        the game), then its plugin and the plugin's window (the keyboards play on Numa Player, not chosen here). */
    struct InstrumentRow
    {
        juce::String id;  // as the game knows it: "rhythm", "bass", "keys", "drums", "lead", "instrument1"
        bool hasSlot = false;
        EngineCore::Slot slot = EngineCore::Slot::bandGuitar;
        juce::Label label;
        juce::ComboBox track;
    };
    std::array<InstrumentRow, 6> instrumentRows;
    static constexpr int pickedTrackIds = 1000;  // a track list's ids from here: the track picked note by note (the rhythm guitar)
    juce::Label keysPluginLabel { {}, "Numa Player" };
    void setUpInstrumentRows();
    /** The game's "tracks": the song's tracks to choose from in each row, and the one each instrument plays; without a
        game, or for a song with no tracks, only the default part (and no choosing). */
    void showTracks (const juce::var& message);
    std::unique_ptr<juce::FileChooser> pluginChooser;
    std::map<EngineCore::Slot, juce::File> bandPlugins;  // the band instrument plugins loaded
    std::map<EngineCore::Slot, juce::MemoryBlock> blankStates;  // an instrument plugin's state before a sound is chosen
    juce::Label latencyLabel, cpuLabel, footLabel, gameLabel, bandLabel;
    // the band's mixer: drums, bass, keys, guitar, lead 1, lead 2
    std::array<juce::Slider, 6> faders;
    std::array<juce::Label, 6> faderLabels;
    juce::TextEditor logView;

    std::map<EngineCore::Slot, std::unique_ptr<juce::DocumentWindow>> windows;
    std::vector<Preset> presetList;
    int ticks = 0;
};

} // namespace gt
