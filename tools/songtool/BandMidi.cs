using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YourBand.Core.Band;
using YourBand.Core.Chart;
using YourBand.Core.Songs;

namespace YourBand.SongTool
{
    /// <summary>
    /// The band guitarist's part as the engine sends it to Shreddage (strumming mode), written as a standard MIDI
    /// file for checking in a DAW: F#5 held (the strumming mode), the held chord's strings, the strum keyswitches
    /// (G#5 down, A5 up, C6 for a muted stroke) or the string keys (F#6 up: a string of the held chord picked on its
    /// own: a track's part), and the single-string notes with the strumming mode let go around them; a part with
    /// neither as plain notes, with no strumming mode. The tempo map is
    /// in the file. This repeats the engine's rules (engine/src/Band.cpp) offline, without the latency advance; keep
    /// the two in step.
    /// </summary>
    static class BandMidi
    {
        const int Ppq = 960, Channel = 2;  // MIDI channel 3, where the engine plays the band guitar
        const int StrumMode = 78, DownKey = 80, UpKey = 81, MuteKey = 84, FirstStringKey = 90;
        const int LowestNote = 39;  // the plugin's lowest note (Shreddage 3 Stratus FREE: D#2): a lower one, a drop D's D2, goes an octave up
        static int Fold(int pitch)
        {
            while (pitch < LowestNote)
                pitch += 12;
            return pitch;
        }
        const double HoldMin = 0.080, HoldMax = 1.2, ChordGap = 0.07, MuteHold = 0.12, MuteTail = 0.040;

        sealed class Ev
        {
            public long Tick;
            public int Order;  // at one tick: offs (0) before ons (1)
            public byte[] Bytes;
        }

        /// <summary>The engine's gap before a key struck again at once (Band.cpp, retriggerGapSec).</summary>
        const double RetriggerGap = 0.06;

        /// <summary>
        /// Writes the guitarist's part: its default part, or (with <paramref name="connections"/>) the part the song's
        /// tracks give it.
        /// </summary>
        public static void Write(Song song, string path, double fromQuarter, double toQuarter, BandConnections connections = null)
        {
            var chart = new SongChart(song);
            var a = Arranger.Arrange(chart, null, connections);
            var events = new List<Ev>();
            long Tick(double q) => Math.Max(0, (long)Math.Round((q - fromQuarter) * Ppq));
            void On(double q, int key, int vel, int tickShift = 0) => events.Add(new Ev { Tick = Math.Max(0, Tick(q) + tickShift), Order = 1, Bytes = new byte[] { (byte)(0x90 | Channel), (byte)key, (byte)vel } });
            void Off(double q, int key, int tickShift = 0) => events.Add(new Ev { Tick = Math.Max(0, Tick(q) + tickShift), Order = 0, Bytes = new byte[] { (byte)(0x80 | Channel), (byte)key, 0 } });
            bool InRange(double q) => q >= fromQuarter - 1e-9 && q < toQuarter;

            // the chords: the strings of each, as the arranger wrote them
            var chords = a.Voicings.GroupBy(v => (v.AtQuarter, v.Quarters))
                .Select(g => (At: g.Key.AtQuarter, End: g.Key.AtQuarter + g.Key.Quarters, Pitches: g.Select(v => Fold(v.Pitch)).Distinct().ToList()))
                .OrderBy(c => c.At).ToList();
            var spans = a.SingleSpans;

            // a note the same key follows at once lets go early, as the engine does it (to the plugin a repeat with no gap
            // is the same note held on, with no new attack)
            double EndOf(BandNote n)
            {
                var end = n.AtQuarter + n.Quarters;
                var next = a.Notes.Where(m => m.Part == n.Part && m.Pitch == n.Pitch && m.AtQuarter > n.AtQuarter + 1e-9 && m.AtQuarter <= end + 1.0)
                                  .Select(m => m.AtQuarter).DefaultIfEmpty(double.PositiveInfinity).Min();
                var gap = RetriggerGap * song.Tempo.BpmAt(n.AtQuarter) / 60.0;
                return next - end < gap ? n.AtQuarter + Math.Max(0.02, next - n.AtQuarter - gap) : end;
            }

            // no strokes or picks: the engine plays every note as a note, with no strumming mode
            if (a.Strokes.Count == 0 && a.Picks.Count == 0)
            {
                foreach (var n in a.Notes.Where(n => n.Part == BandPart.Guitar && InRange(n.AtQuarter)))
                {
                    On(n.AtQuarter, Fold(n.Pitch), n.Velocity);
                    Off(RepeatsEarly(n) ? EndOf(n) : n.AtQuarter + n.Quarters, Fold(n.Pitch), -4);
                }
                WriteFile(song, path, events, fromQuarter, toQuarter, Tick);
                return;
            }

            // the strumming mode: F#5 held, let go for each single-string stretch and taken again at its end
            var modeFrom = fromQuarter;
            foreach (var (from, to) in spans.Where(s => s.To > fromQuarter && s.From < toQuarter))
            {
                if (from > modeFrom)
                {
                    On(modeFrom, StrumMode, 100);
                    Off(from, StrumMode, -1);
                }
                modeFrom = to - 0.001;
            }
            On(modeFrom, StrumMode, 100);
            Off(toQuarter, StrumMode);

            // the held chords, in pieces between the single-string stretches (struck up again at a stretch's end)
            foreach (var chord in chords.Where(c => c.End > fromQuarter && c.At < toQuarter))
            {
                var pieces = new List<(double From, double To)>();
                var start = chord.At;
                foreach (var (from, to) in spans.Where(s => s.To > chord.At && s.From < chord.End))
                {
                    if (from > start)
                        pieces.Add((start, from));
                    start = Math.Max(start, to - 0.001);
                }
                pieces.Add((start, chord.End - 0.001));
                foreach (var (from, to) in pieces.Where(p => p.To > p.From && p.To > fromQuarter && p.From < toQuarter))
                    foreach (var pitch in chord.Pitches)
                    {
                        On(from, pitch, 96, from == chord.At ? -2 : 2);
                        Off(to, pitch, -4);  // (before the next chord's on, which can be the same key: an off after it would cut the new note)
                    }
            }

            // the strokes: a strum keyswitch on its beat, held until the next stroke starts (back to back), the last of a chord a little before
            // the chord ends, 1.2 s at most; a muted stroke's key 120 ms, with the Mute keyswitch around it
            var strokes = a.Strokes.OrderBy(s => s.AtQuarter).ToList();
            for (int i = 0; i < strokes.Count; ++i)
            {
                var st = strokes[i];
                if (!InRange(st.AtQuarter))
                    continue;
                var at = st.AtQuarter;
                var bpm = song.Tempo.BpmAt(st.AtQuarter);
                var rate = bpm / 60.0;
                var hold = HoldMax;
                var chord = chords.FirstOrDefault(c => c.At <= st.AtQuarter + 1e-9 && c.End > st.AtQuarter + 1e-9);
                if (chord.Pitches != null)
                    hold = Math.Min(hold, (chord.End - st.AtQuarter) / rate - ChordGap);
                if (st.Muted)
                    hold = Math.Min(hold, MuteHold);
                hold = Math.Max(HoldMin, hold);
                var key = st.Up ? UpKey : DownKey;
                var end = at + hold * rate;
                if (i + 1 < strokes.Count)
                    end = Math.Min(end, strokes[i + 1].AtQuarter);  // the next stroke starts as this key goes
                var cut = spans.Where(s => s.From > st.AtQuarter && s.From < end).Select(s => s.From).DefaultIfEmpty(double.NaN).First();
                if (!double.IsNaN(cut))
                    end = cut;  // the stretch lets the key go
                if (st.Muted)
                {
                    On(at, MuteKey, 100, -1);
                    Off(at + MuteTail * rate, MuteKey);
                }
                On(at, key, st.Velocity);
                Off(end, key);
            }

            // the strings picked on their own: the string key of the note's place in the held chord, held as long as the string rings
            foreach (var p in a.Picks.Where(p => InRange(p.AtQuarter)))
            {
                On(p.AtQuarter, FirstStringKey + Math.Min(5, p.Rank), p.Velocity);
                Off(p.AtQuarter + p.Quarters, FirstStringKey + Math.Min(5, p.Rank));
            }

            // the single-string notes: as notes, the strumming mode being let go
            foreach (var n in a.Notes.Where(n => n.Single && InRange(n.AtQuarter)))
            {
                On(n.AtQuarter, Fold(n.Pitch), n.Velocity);
                Off(EndOf(n), Fold(n.Pitch), -4);  // (as the chords' offs: before an on of the same key just after it)
            }

            WriteFile(song, path, events, fromQuarter, toQuarter, Tick);
        }

        /// <summary>Whether the engine lets this guitar note go early when its key is struck again at once (Band.cpp).</summary>
        static bool RepeatsEarly(BandNote n) => n.Part == BandPart.Guitar;

        static void WriteFile(Song song, string path, List<Ev> events, double fromQuarter, double toQuarter, Func<double, long> tick)
        {
            var track = new List<byte>();
            long last = 0;
            void Add(long at, params byte[] bytes)
            {
                Var(track, (int)(at - last));
                track.AddRange(bytes);
                last = at;
            }
            var all = new List<Ev>();
            // the name, the time signature, the tempo map
            all.Add(new Ev { Tick = 0, Order = -3, Bytes = Meta(0x03, System.Text.Encoding.UTF8.GetBytes("Band guitar (Shreddage strumming)")) });
            var sig = song.Tempo.Meters.FirstOrDefault();
            all.Add(new Ev { Tick = 0, Order = -2, Bytes = Meta(0x58, new byte[] { (byte)(sig?.Numerator ?? 4), (byte)Math.Log(sig?.Denominator ?? 4, 2), 24, 8 }) });
            var tempos = song.Tempo.Tempos.Where(t => t.AtQuarter <= toQuarter).OrderBy(t => t.AtQuarter).ToList();
            var current = tempos.LastOrDefault(t => t.AtQuarter <= fromQuarter);
            all.Add(new Ev { Tick = 0, Order = -1, Bytes = Meta(0x51, Micros(current?.Bpm ?? song.Tempo.BpmAt(fromQuarter))) });
            foreach (var t in tempos.Where(t => t.AtQuarter > fromQuarter))
                all.Add(new Ev { Tick = tick(t.AtQuarter), Order = -1, Bytes = Meta(0x51, Micros(t.Bpm)) });
            all.AddRange(events);
            foreach (var e in all.OrderBy(e => e.Tick).ThenBy(e => e.Order))
                Add(e.Tick, e.Bytes);
            Add(last + 1, 0xFF, 0x2F, 0x00);

            using var file = new BinaryWriter(File.Create(path));
            file.Write(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1, (byte)(Ppq >> 8), (byte)(Ppq & 0xFF) });
            file.Write(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' });
            file.Write(new[] { (byte)(track.Count >> 24), (byte)(track.Count >> 16), (byte)(track.Count >> 8), (byte)track.Count });
            file.Write(track.ToArray());
        }

        static byte[] Micros(double bpm)
        {
            var us = (int)Math.Round(60_000_000.0 / bpm);
            return new[] { (byte)(us >> 16), (byte)(us >> 8), (byte)us };
        }

        static byte[] Meta(byte type, byte[] data)
        {
            var bytes = new List<byte> { 0xFF, type };
            Var(bytes, data.Length);
            bytes.AddRange(data);
            return bytes.ToArray();
        }

        static void Var(List<byte> bytes, int value)
        {
            var buffer = new Stack<byte>();
            buffer.Push((byte)(value & 0x7F));
            while ((value >>= 7) > 0)
                buffer.Push((byte)((value & 0x7F) | 0x80));
            bytes.AddRange(buffer);
        }
    }
}
