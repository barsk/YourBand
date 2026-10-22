#include "WindowFront.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

namespace gt
{

void bringToFront (juce::Component& window)
{
    window.setVisible (true);
    window.toFront (true);
    if (auto* hwnd = (HWND) window.getWindowHandle())
    {
        if (IsIconic (hwnd))
            ShowWindow (hwnd, SW_RESTORE);
        SetForegroundWindow (hwnd);
    }
}

} // namespace gt
