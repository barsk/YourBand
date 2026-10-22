using System;
using System.Globalization;
using System.Linq;
using GuitarTutor.Core.Band;
using GuitarTutor.Core.Music;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GuitarTutor.Core.Engine
{
    /// <summary>
    /// The messages between the game and the audio engine (schema/engine-protocol.md): JSON, one message per
    /// line, over a TCP connection on this computer. The engine's clock does not go through messages: it is
    /// in shared memory (<see cref="ClockSnapshot"/>).
    /// </summary>
    public static class EngineProtocol
    {
        public const int Version = 1;
        public const int Port = 47800;
        public const string ClockName = "Local\\GuitarTutorClock";

        /// <summary>The parts in the order the engine numbers them in "notes".</summary>
        public static readonly BandPart[] Parts = { BandPart.Drums, BandPart.Bass, BandPart.Keys, BandPart.Guitar };

        static string Line(JObject message) => message.ToString(Formatting.None);

        public static string Hello() => Line(new JObject { ["type"] = "hello", ["client"] = "GuitarTutor", ["protocol"] = Version });

        /// <summary>The song's tempo map and the band's notes, as [part, pitch, at, length, velocity].</summary>
        public static string Load(string title, TempoMap tempo, Arrangement arrangement, string keysSound)
        {
            var notes = new JArray();
            foreach (var n in arrangement.Notes)
                notes.Add(new JArray(Array.IndexOf(Parts, n.Part), n.Pitch, Math.Round(n.AtQuarter, 6), Math.Round(n.Quarters, 6), n.Velocity));
            return Line(new JObject
            {
                ["type"] = "load",
                ["title"] = title,
                ["style"] = arrangement.Style,
                ["tempos"] = new JArray(tempo.Tempos.Select(t => new JArray(t.AtQuarter, t.Bpm))),
                ["meters"] = new JArray(tempo.Meters.Select(m => new JArray(m.AtBar, m.Numerator, m.Denominator))),
                ["notes"] = notes,
                ["keys"] = keysSound ?? "",
            });
        }

        public static string Play(double fromQuarter, double countInQuarters) =>
            Line(new JObject { ["type"] = "play", ["from"] = fromQuarter, ["countIn"] = countInQuarters });

        public static string Pause() => Line(new JObject { ["type"] = "pause" });

        public static string Seek(double quarter) => Line(new JObject { ["type"] = "seek", ["to"] = quarter });

        public static string Speed(double speed) => Line(new JObject { ["type"] = "speed", ["value"] = speed });

        /// <summary>Brings up one of the engine's windows: "controlPanel", "guitar" (your tone), "bandGuitarTone",
        /// "drums", "bandBass" or "bandGuitar" (the band's plugins).</summary>
        public static string ShowWindow(string which) => Line(new JObject { ["type"] = "window", ["show"] = which });

        /// <summary>Whether the band's guitarist plays the rhythm (the engine answers with the same message).</summary>
        public static string BandGuitar(bool plays) => Line(new JObject { ["type"] = "bandGuitar", ["plays"] = plays });

        public static string Quit() => Line(new JObject { ["type"] = "quit" });

        /// <summary>A band part's volume (1: as played).</summary>
        public static string Mix(BandPart part, double gain) =>
            Line(new JObject { ["type"] = "mix", ["part"] = part.ToString().ToLowerInvariant(), ["gain"] = gain });

        /// <summary>A message from the engine: its type and the whole message (null if the line is not JSON).</summary>
        public static (string type, JObject message) Read(string line)
        {
            try
            {
                var message = JObject.Parse(line);
                return ((string)message["type"] ?? "", message);
            }
            catch (JsonException)
            {
                return (null, null);
            }
        }

        public static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The engine's clock as published in shared memory: the song position (in quarter notes) heard at a moment of
    /// the computer's performance counter, and how fast the position moves. The game reads it each frame and
    /// works out the position now, so pictures and sound stay together.
    /// </summary>
    /// <remarks>
    /// Layout (64 bytes, little-endian): 0 uint32 magic "GTCK", 4 uint32 version, 8 int64 sequence (odd while
    /// the engine writes), 16 double quarter, 24 int64 ticks, 32 double quarters per second (0 when paused),
    /// 40 int32 playing, 44 int32 reserved, 48 double count-in end (NaN: none), 56 int64 ticks per second.
    /// </remarks>
    public readonly struct ClockSnapshot
    {
        public const uint Magic = 0x4B435447;  // "GTCK"
        public const int Size = 64;

        public double Quarter { get; }
        public long Ticks { get; }
        public double QuartersPerSecond { get; }
        public bool Playing { get; }
        public double CountInUntil { get; }
        public long TicksPerSecond { get; }

        public ClockSnapshot(double quarter, long ticks, double quartersPerSecond, bool playing, double countInUntil, long ticksPerSecond)
        {
            Quarter = quarter;
            Ticks = ticks;
            QuartersPerSecond = quartersPerSecond;
            Playing = playing;
            CountInUntil = countInUntil;
            TicksPerSecond = ticksPerSecond;
        }

        /// <summary>The position at a moment of the performance counter.</summary>
        public double QuarterAt(long ticks)
        {
            if (!Playing || TicksPerSecond <= 0)
                return Quarter;
            return Quarter + (ticks - Ticks) / (double)TicksPerSecond * QuartersPerSecond;
        }
    }
}
