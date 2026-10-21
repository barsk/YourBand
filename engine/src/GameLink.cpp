#include "GameLink.h"

namespace gt
{

GameLink::GameLink()
    : juce::Thread ("Game link")
{
}

GameLink::~GameLink()
{
    alive->store (false);
    signalThreadShouldExit();
    server.close();  // wakes a waiting accept
    {
        const juce::ScopedLock lock (sending);
        if (client != nullptr)
            client->close();
    }
    stopThread (2000);
}

void GameLink::post (std::function<void()> f)
{
    juce::MessageManager::callAsync ([flag = alive, f = std::move (f)] { if (flag->load()) f(); });
}

void GameLink::send (const juce::var& message)
{
    const auto line = juce::JSON::toString (message, true) + "\n";
    const juce::ScopedLock lock (sending);
    if (client != nullptr && client->isConnected())
        client->write (line.toRawUTF8(), (int) line.getNumBytesAsUTF8());
}

void GameLink::run()
{
    if (! server.createListener (port, "127.0.0.1"))
    {
        post ([this] { if (onConnection) onConnection (false); });
        return;
    }
    listening = true;

    while (! threadShouldExit())
    {
        std::unique_ptr<juce::StreamingSocket> accepted (server.waitForNextConnection());
        if (accepted == nullptr)
            continue;
        {
            const juce::ScopedLock lock (sending);
            client = std::move (accepted);
        }
        connected = true;
        post ([this] { if (onConnection) onConnection (true); });

        juce::MemoryBlock pending;
        char chunk[16384];
        while (! threadShouldExit())
        {
            const auto ready = client->waitUntilReady (true, 200);
            if (ready < 0)
                break;
            if (ready == 0)
                continue;
            const auto n = client->read (chunk, (int) sizeof (chunk), false);
            if (n <= 0)
                break;
            pending.append (chunk, (size_t) n);
            // every complete line is a message
            for (;;)
            {
                const auto* data = static_cast<const char*> (pending.getData());
                const auto size = pending.getSize();
                size_t end = 0;
                while (end < size && data[end] != '\n')
                    ++end;
                if (end == size)
                    break;
                const auto line = juce::String::fromUTF8 (data, (int) end).trim();
                pending.removeSection (0, end + 1);
                if (line.isEmpty())
                    continue;
                juce::var message;
                if (juce::JSON::parse (line, message).wasOk() && message.isObject())
                    post ([this, message] { if (onMessage) onMessage (message); });
            }
        }

        connected = false;
        {
            const juce::ScopedLock lock (sending);
            client.reset();
        }
        post ([this] { if (onConnection) onConnection (false); });
    }
}

} // namespace gt
