#pragma once

#include <juce_gui_basics/juce_gui_basics.h>

namespace gt
{

/** Shows a window and makes it the one in front, over the game. JUCE's toFront only raises it among the
    engine's own windows: Windows keeps a program in the background from taking the front unless the program
    in front allows it (the game does, just before it asks for a window). */
void bringToFront (juce::Component& window);

} // namespace gt
