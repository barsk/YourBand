#pragma once

// The control panel's "Import new..." window: a Guitar Pro file (with an Ultimate Guitar page for its strumming), or Ultimate Guitar
// chords and/or tabs pages, imported into a song by tools/songtool (`songtool import`), which this runs and shows the progress of.
// A finished import is handed to the panel, which has the game open it.

#include <juce_gui_basics/juce_gui_basics.h>

#include <functional>

namespace gt
{

class ImportWindow final : public juce::DocumentWindow
{
public:
    /** `imported` gets the song file once an import has written it; `settings` keeps the folder the Guitar Pro files are chosen from. */
    ImportWindow (juce::PropertiesFile& settings, std::function<void (const juce::File& song)> imported);
    ~ImportWindow() override;

    void closeButtonPressed() override { setVisible (false); }

    /** For tests: the Ultimate Guitar chords page URL typed in and Import pressed. */
    void startTest (const juce::String& chordsUrl);

    /** songtool: next to the engine (songtool\songtool.exe), or the repository's build of it (tools\songtool\bin\Release\net8.0). */
    static juce::File findSongtool();
};

} // namespace gt
