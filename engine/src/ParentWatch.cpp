#include "ParentWatch.h"

#include <juce_events/juce_events.h>

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

namespace gt
{

ParentWatch::ParentWatch (int processId, std::function<void()> gone)
    : juce::Thread ("Parent watch"), onGone (std::move (gone))
{
    handle = OpenProcess (SYNCHRONIZE, FALSE, (DWORD) processId);
    if (handle != nullptr)
        startThread();
}

ParentWatch::~ParentWatch()
{
    alive->store (false);
    stopThread (1000);
    if (handle != nullptr)
        CloseHandle (handle);
}

void ParentWatch::run()
{
    // in short waits, so the thread can be stopped
    while (! threadShouldExit())
        if (WaitForSingleObject (handle, 250) == WAIT_OBJECT_0)
        {
            juce::MessageManager::callAsync ([flag = alive, f = onGone] { if (flag->load() && f) f(); });
            return;
        }
}

} // namespace gt
