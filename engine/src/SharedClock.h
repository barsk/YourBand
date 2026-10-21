#pragma once

// The engine's clock in shared memory (schema/engine-protocol.md, "Clock"): after every audio block the band
// writes where the song is and when that is heard; the game reads it every frame. Written from the audio thread
// without locks (a sequence number tells the reader when it caught a write half-done).

#include <cstdint>

namespace gt
{

class SharedClock
{
public:
    static constexpr const wchar_t* name = L"Local\\GuitarTutorClock";

    SharedClock();
    ~SharedClock();

    bool isOpen() const { return memory != nullptr; }

    /** The performance counter now (Windows QueryPerformanceCounter), and its ticks per second. */
    static int64_t now();
    static int64_t ticksPerSecond();

    /** Audio thread: the song is at `quarter` when the performance counter reads `ticks`, moving at
        `quartersPerSecond` (0 when stopped). `countInUntil` is NaN when there is no count-in. */
    void publish (double quarter, int64_t ticks, double quartersPerSecond, bool playing, double countInUntil);

private:
    void* mapping = nullptr;
    unsigned char* memory = nullptr;
    int64_t sequence = 0;
};

} // namespace gt
