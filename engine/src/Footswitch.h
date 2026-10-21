#pragma once

// The footswitch: an Arduino (32U4) that shows up as a HID joystick, or any game controller for testing.
// Read by the engine itself so tone switches react instantly, whatever the game is doing.

#include <juce_core/juce_core.h>

#include <atomic>
#include <functional>
#include <memory>

namespace gt
{

class Footswitch final : private juce::Thread
{
public:
    /** Called on the message thread: button number from 1, and whether it went down. */
    std::function<void (int button, bool down)> onButton;
    /** Called on the message thread when a controller is found or lost (empty name = none). */
    std::function<void (const juce::String& name)> onDevice;

    Footswitch();
    ~Footswitch() override;

    void start() { startThread (juce::Thread::Priority::high); }
    void stop() { stopThread (500); }

private:
    void run() override;
    int findDevice (juce::String& name) const;

    // posted callbacks check this, so none runs after the footswitch is gone
    std::shared_ptr<std::atomic<bool>> alive = std::make_shared<std::atomic<bool>> (true);
};

} // namespace gt
