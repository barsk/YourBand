using System;
using System.Runtime.InteropServices;
using System.Threading;
using YourBand.Core.Engine;

namespace YourBand.Runtime.Engine
{
    /// <summary>
    /// Reads the engine's clock from shared memory (schema/engine-protocol.md, "Clock"), through the Windows API
    /// directly, so the counter and the mapping are the same ones the engine uses.
    /// </summary>
    public sealed class EngineClock : IDisposable
    {
        const uint FileMapRead = 0x0004;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr OpenFileMappingW(uint access, bool inherit, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

        [DllImport("kernel32.dll")]
        static extern bool UnmapViewOfFile(IntPtr view);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        static extern bool QueryPerformanceCounter(out long ticks);

        IntPtr mapping, view;

        /// <summary>The performance counter now.</summary>
        public static long Now()
        {
            QueryPerformanceCounter(out var t);
            return t;
        }

        /// <summary>The engine's latest clock, or null if the engine is not running (or the read kept tearing).</summary>
        public ClockSnapshot? Read()
        {
            if (view == IntPtr.Zero && !Open())
                return null;
            for (int attempt = 0; attempt < 16; ++attempt)
            {
                var before = Marshal.ReadInt64(view, 8);
                if ((before & 1) != 0)
                    continue;
                Thread.MemoryBarrier();
                var magic = (uint)Marshal.ReadInt32(view, 0);
                var quarter = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(view, 16));
                var ticks = Marshal.ReadInt64(view, 24);
                var rate = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(view, 32));
                var playing = Marshal.ReadInt32(view, 40) != 0;
                var countIn = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(view, 48));
                var perSecond = Marshal.ReadInt64(view, 56);
                Thread.MemoryBarrier();
                if (Marshal.ReadInt64(view, 8) != before)
                    continue;
                if (magic != ClockSnapshot.Magic)
                    return null;
                return new ClockSnapshot(quarter, ticks, rate, playing, countIn, perSecond);
            }
            return null;
        }

        bool Open()
        {
            mapping = OpenFileMappingW(FileMapRead, false, EngineProtocol.ClockName);
            if (mapping == IntPtr.Zero)
                return false;
            view = MapViewOfFile(mapping, FileMapRead, 0, 0, (UIntPtr)ClockSnapshot.Size);
            if (view != IntPtr.Zero)
                return true;
            CloseHandle(mapping);
            mapping = IntPtr.Zero;
            return false;
        }

        /// <summary>Lets go of the mapping (a new engine makes a new one).</summary>
        public void Dispose()
        {
            if (view != IntPtr.Zero)
                UnmapViewOfFile(view);
            if (mapping != IntPtr.Zero)
                CloseHandle(mapping);
            view = mapping = IntPtr.Zero;
        }
    }
}
