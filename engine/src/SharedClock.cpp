#include "SharedClock.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <atomic>
#include <cstring>
#include <limits>

namespace gt
{

namespace
{
    constexpr uint32_t magic = 0x4B435447;  // "GTCK"
    constexpr uint32_t version = 1;
    constexpr size_t size = 64;

    template <typename T>
    void put (unsigned char* memory, size_t offset, T value)
    {
        std::memcpy (memory + offset, &value, sizeof (T));
    }
}

SharedClock::SharedClock()
{
    mapping = CreateFileMappingW (INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, (DWORD) size, name);
    if (mapping == nullptr)
        return;
    memory = static_cast<unsigned char*> (MapViewOfFile (mapping, FILE_MAP_ALL_ACCESS, 0, 0, size));
    if (memory == nullptr)
        return;
    std::memset (memory, 0, size);
    put (memory, 0, magic);
    put (memory, 4, version);
    put (memory, 48, std::numeric_limits<double>::quiet_NaN());
    put (memory, 56, ticksPerSecond());
}

SharedClock::~SharedClock()
{
    if (memory != nullptr)
        UnmapViewOfFile (memory);
    if (mapping != nullptr)
        CloseHandle (mapping);
}

int64_t SharedClock::now()
{
    LARGE_INTEGER t;
    QueryPerformanceCounter (&t);
    return t.QuadPart;
}

int64_t SharedClock::ticksPerSecond()
{
    LARGE_INTEGER f;
    QueryPerformanceFrequency (&f);
    return f.QuadPart;
}

void SharedClock::publish (double quarter, int64_t ticks, double quartersPerSecond, bool playing, double countInUntil)
{
    if (memory == nullptr)
        return;
    auto* seq = reinterpret_cast<volatile int64_t*> (memory + 8);
    *seq = ++sequence;  // odd: writing
    std::atomic_thread_fence (std::memory_order_release);
    put (memory, 16, quarter);
    put (memory, 24, ticks);
    put (memory, 32, quartersPerSecond);
    put (memory, 40, (int32_t) (playing ? 1 : 0));
    put (memory, 48, countInUntil);
    std::atomic_thread_fence (std::memory_order_release);
    *seq = ++sequence;  // even: done
}

} // namespace gt
