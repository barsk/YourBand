#pragma once

// When the game starts the engine (--parent <process id>), the engine lives as long as the game: this watches
// the game's process and calls back once it is gone, closed or crashed.

#include <juce_core/juce_core.h>

#include <atomic>
#include <functional>
#include <memory>

namespace gt
{

class ParentWatch final : private juce::Thread
{
public:
    /** `gone` is called on the message thread. */
    ParentWatch (int processId, std::function<void()> gone);
    ~ParentWatch() override;

    /** False if the process could not be found (it may already be gone). */
    bool isWatching() const { return handle != nullptr; }

private:
    void run() override;

    void* handle = nullptr;
    std::function<void()> onGone;
    std::shared_ptr<std::atomic<bool>> alive = std::make_shared<std::atomic<bool>> (true);
};

} // namespace gt
