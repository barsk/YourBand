#pragma once

// The connection to the game (schema/engine-protocol.md): the engine listens on 127.0.0.1:47800, the game
// connects; messages are JSON, one per line. One game at a time.

#include <juce_core/juce_core.h>
#include <juce_events/juce_events.h>

#include <atomic>
#include <functional>
#include <memory>

namespace gt
{

class GameLink final : private juce::Thread
{
public:
    static constexpr int port = 47800;

    /** Called on the message thread with each message from the game. */
    std::function<void (const juce::var& message)> onMessage;
    /** Called on the message thread when the game connects or goes. */
    std::function<void (bool connected)> onConnection;

    GameLink();
    ~GameLink() override;

    void start() { startThread(); }
    /** Sends a message to the game, if one is connected (any thread). */
    void send (const juce::var& message);
    bool isConnected() const { return connected.load(); }
    bool isListening() const { return listening.load(); }

private:
    void run() override;
    void post (std::function<void()>);

    juce::StreamingSocket server;
    std::unique_ptr<juce::StreamingSocket> client;
    juce::CriticalSection sending;
    std::atomic<bool> connected { false }, listening { false };
    // posted callbacks check this, so none runs after the link is gone
    std::shared_ptr<std::atomic<bool>> alive = std::make_shared<std::atomic<bool>> (true);
};

} // namespace gt
