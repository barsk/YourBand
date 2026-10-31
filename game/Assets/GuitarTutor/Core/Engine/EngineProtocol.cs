using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GuitarTutor.Core.Band;
using GuitarTutor.Core.Music;
using GuitarTutor.Core.Songs;
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
        public static readonly BandPart[] Parts = { BandPart.Drums, BandPart.Bass, BandPart.Keys, BandPart.Guitar, BandPart.Lead1, BandPart.Lead2 };

        static string Line(JObject message) => message.ToString(Formatting.None);

        public static string Hello() => Line(new JObject { ["type"] = "hello", ["client"] = "GuitarTutor", ["protocol"] = Version });

        /// <summary>The song's tempo map and the band's notes, as [part, pitch, at, length, velocity]; the
        /// guitarist's held voicings as [pitch, at, length], its strokes as [at, direction, velocity, muted] and its
        /// strings picked on their own as [at, length, place in the held chord, velocity], and its parts' pitch wheels as
        /// [part, at, semitones] (the wheel moves evenly between a part's points). <paramref name="song"/> names the
        /// song (its file name) for the sounds the engine keeps per song.</summary>
        public static string Load(string title, TempoMap tempo, Arrangement arrangement, string keysSound, string origin = "", string song = "")
        {
            var notes = new JArray();
            foreach (var n in arrangement.Notes)
            {
                var note = new JArray(Array.IndexOf(Parts, n.Part), n.Pitch, Math.Round(n.AtQuarter, 6), Math.Round(n.Quarters, 6), n.Velocity);
                if (n.Single)
                    note.Add(1);  // a single-string note of the guitarist's
                notes.Add(note);
            }
            var spans = new JArray();
            foreach (var (from, to) in arrangement.SingleSpans)
                spans.Add(new JArray(Math.Round(from, 6), Math.Round(to, 6)));
            var voicings = new JArray();
            foreach (var v in arrangement.Voicings)
                voicings.Add(new JArray(v.Pitch, Math.Round(v.AtQuarter, 6), Math.Round(v.Quarters, 6)));
            var strokes = new JArray();
            foreach (var s in arrangement.Strokes)
                strokes.Add(new JArray(Math.Round(s.AtQuarter, 6), s.Up ? 1 : 0, s.Velocity, s.Muted ? 1 : 0));
            var picks = new JArray();
            foreach (var p in arrangement.Picks)
                picks.Add(new JArray(Math.Round(p.AtQuarter, 6), Math.Round(p.Quarters, 6), p.Rank, p.Velocity));
            var wheel = new JArray();
            foreach (var w in arrangement.Wheel)
                wheel.Add(new JArray(Array.IndexOf(Parts, w.Part), Math.Round(w.AtQuarter, 6), Math.Round(w.Semitones, 3)));
            return Line(new JObject
            {
                ["type"] = "load",
                ["title"] = title,
                ["style"] = arrangement.Style,
                ["tempos"] = new JArray(tempo.Tempos.Select(t => new JArray(t.AtQuarter, t.Bpm))),
                ["meters"] = new JArray(tempo.Meters.Select(m => new JArray(m.AtBar, m.Numerator, m.Denominator))),
                ["notes"] = notes,
                ["voicings"] = voicings,
                ["strokes"] = strokes,
                ["picks"] = picks,
                ["wheel"] = wheel,
                ["spans"] = spans,
                ["leads"] = new JArray(arrangement.LeadNames),
                ["keys"] = keysSound ?? "",
                ["origin"] = origin ?? "",
                ["song"] = song ?? "",
                // the playstyles, each with its keyboard sound: the engine keeps a song's sounds per playstyle
                ["styles"] = new JArray(BandStyle.All.Select(s => new JObject { ["name"] = s.Name, ["keys"] = s.KeysSound })),
            });
        }

        /// <summary>
        /// Where the band's musicians stand, as each instrument's pan (by instrument id, -1 all left to 1 all right, as
        /// the audience hears them): the engine pans an instrument from its place on stage until you turn its pan knob.
        /// </summary>
        public static string Stage(IReadOnlyDictionary<string, double> pans)
        {
            var values = new JObject();
            foreach (var kv in pans)
                values[kv.Key] = Math.Round(kv.Value, 3);
            return Line(new JObject { ["type"] = "stage", ["pans"] = values });
        }

        public static string Play(double fromQuarter, double countInQuarters) =>
            Line(new JObject { ["type"] = "play", ["from"] = fromQuarter, ["countIn"] = countInQuarters });

        public static string Pause() => Line(new JObject { ["type"] = "pause" });

        public static string Seek(double quarter) => Line(new JObject { ["type"] = "seek", ["to"] = quarter });

        public static string Speed(double speed) => Line(new JObject { ["type"] = "speed", ["value"] = speed });

        /// <summary>Brings up one of the engine's windows: "controlPanel", "guitar" (your tone), "drums", "bandBass"
        /// or "bandGuitar" (the band's plugins).</summary>
        public static string ShowWindow(string which) => Line(new JObject { ["type"] = "window", ["show"] = which });

        /// <summary>Whether the band's guitarist plays the rhythm (the engine answers with the same message).</summary>
        public static string BandGuitar(bool plays) => Line(new JObject { ["type"] = "bandGuitar", ["plays"] = plays });

        public static string Quit() => Line(new JObject { ["type"] = "quit" });

        /// <summary>
        /// The song's tracks the band's instruments can play (one per doubled pair: its label names both) and, per
        /// instrument, the track it plays (-1: its default part), whether it plays it picked or string by string (the rhythm guitar), and
        /// whether that was chosen or connected automatically.
        /// </summary>
        public static string Tracks(Song song, BandConnections connections)
        {
            var tracks = new JArray();
            foreach (var t in song.Tracks.Where(t => t.SameAs == null && t.Notes.Count > 0))
            {
                var doubles = song.Tracks.Where(d => d.SameAs == t.Name).Select(d => d.Name).ToList();
                var name = t.Name + (doubles.Count > 0 ? " = " + string.Join(" = ", doubles) : "");
                tracks.Add(new JObject { ["index"] = t.Index, ["name"] = $"{name} ({t.Kind}, {t.Notes.Count} notes)", ["kind"] = t.Kind });
            }
            var instruments = new JArray();
            foreach (Instrument i in Enum.GetValues(typeof(Instrument)))
                instruments.Add(new JObject
                {
                    ["id"] = BandConnections.Ids[i],
                    ["name"] = BandConnections.Names[i],
                    ["track"] = connections.TrackFor(song, i)?.Index ?? -1,
                    ["picked"] = connections.PlaysPicked(song, i),
                    ["strings"] = connections.PlaysStringKeys(song, i),
                    ["chosen"] = connections.IsChosen(i),
                });
            return Line(new JObject { ["type"] = "tracks", ["tracks"] = tracks, ["instruments"] = instruments });
        }

        /// <summary>The songs the game can open (their names) and which one is open, for the engine's song list.</summary>
        public static string Songs(IEnumerable<string> names, int current) =>
            Line(new JObject { ["type"] = "songs", ["names"] = new JArray(names), ["current"] = current });

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
