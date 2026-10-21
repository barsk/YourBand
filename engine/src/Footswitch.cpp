#include "Footswitch.h"

#include <juce_events/juce_events.h>

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <mmsystem.h>

namespace gt
{

Footswitch::Footswitch() : juce::Thread ("Footswitch") {}

Footswitch::~Footswitch()
{
    stop();
    alive->store (false);
}

int Footswitch::findDevice (juce::String& name) const
{
    // joyGetNumDevs() is the number of slots the driver supports, not of connected devices
    for (UINT id = 0; id < joyGetNumDevs(); ++id)
    {
        JOYINFOEX info {};
        info.dwSize = sizeof (info);
        info.dwFlags = JOY_RETURNBUTTONS;
        if (joyGetPosEx (id, &info) == JOYERR_NOERROR)
        {
            JOYCAPSW caps {};
            if (joyGetDevCapsW (id, &caps, sizeof (caps)) == JOYERR_NOERROR)
                name = juce::String (caps.szPname);
            if (name.isEmpty())
                name = "Joystick " + juce::String ((int) id + 1);
            return (int) id;
        }
    }
    return -1;
}

void Footswitch::run()
{
    int device = -1;
    DWORD previous = 0;

    auto post = [flag = alive] (std::function<void()> f)
    {
        juce::MessageManager::callAsync ([flag, f = std::move (f)] { if (flag->load()) f(); });
    };

    while (! threadShouldExit())
    {
        if (device < 0)
        {
            juce::String name;
            device = findDevice (name);
            if (device >= 0)
            {
                previous = 0;
                post ([this, name] { if (onDevice) onDevice (name); });
            }
            else
            {
                wait (1000);  // nothing plugged in: look again in a second
                continue;
            }
        }

        JOYINFOEX info {};
        info.dwSize = sizeof (info);
        info.dwFlags = JOY_RETURNBUTTONS;
        if (joyGetPosEx ((UINT) device, &info) != JOYERR_NOERROR)
        {
            device = -1;
            post ([this] { if (onDevice) onDevice ({}); });
            continue;
        }

        if (const auto changed = info.dwButtons ^ previous)
        {
            for (int b = 0; b < 32; ++b)
            {
                if ((changed >> b) & 1u)
                {
                    const bool down = ((info.dwButtons >> b) & 1u) != 0;
                    post ([this, b, down] { if (onButton) onButton (b + 1, down); });
                }
            }
            previous = info.dwButtons;
        }

        wait (2);  // 500 Hz polling: a press is seen within a few milliseconds
    }
}

} // namespace gt
