using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuitarTutor.Core.Band;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Songs;

namespace GuitarTutor.SongTool
{
    /// <summary>
    /// The band guitarist's part as the engine sends it to Shreddage (strumming mode), written as a standard MIDI
    /// file for checking in a DAW: F#5 held (the strumming mode), the held chord's strings, the strum keyswitches
    /// (G#5 down, A5 up, C6 for a muted stroke), and the single-string notes with the strumming mode let go around
    /// them. The tempo map is in the file. This repeats the engine's rules (engine/src/Band.cpp) offline, without the
    /// latency advance; keep the two in step.
    /// </summary>
    static class BandMidi
    {
        const int Ppq = 960, Channel = 2;  // MIDI channel 3, where the engine plays the band guitar
        const int StrumMode = 78, DownKey = 80, UpKey = 81, MuteKey = 84;
        const double HoldMin = 0.080, HoldMax = 1.2, ReleaseLead = 0.05, ChordGap = 0.07, MuteHold = 0.12, MuteTail = 0.040, Lag = 0.015;

        sealed class Ev
        {
            public long Tick;
            public int Order;  // at one tick: offs (0) before ons (1)
            public byte[] Bytes;
        }

        public static void Write(Song song, string path, double fromQuarter, double toQuarter)
        {
            var chart = new SongChart(song);
            var a = Arranger.Arrange(chart);
            var events = new List<Ev>();
            long Tick(double q) => Math.Max(0, (long)Math.Round((q - fromQuarter) * Ppq));
            double Quarters(double seconds, double at) => seconds * song.Tempo.BpmAt(at) / 60.0;
            void On(double q, int key, int vel, int tickShift = 0) => events.Add(new Ev { Tick = Math.Max(0, Tick(q) + tickShift), Order = 1, Bytes = new byte[] { (byte)(0x90 | Channel), (byte)key, (byte)vel } });
            void Off(double q, int key, int tickShift = 0) => events.Add(new Ev { Tick = Math.Max(0, Tick(q) + tickShift), Order = 0, Bytes = new byte[] { (byte)(0x80 | Channel), (byte)key, 0 } });
            bool InRange(double q) => q >= fromQuarter - 1e-9 && q < toQuarter;

            // the chords: the strings of each, as the arranger wrote them
            var chords = a.Voicings.GroupBy(v => (v.AtQuarter, v.Quarters))
                .Select(g => (At: g.Key.AtQuarter, End: g.Key.AtQuarter + g.Key.Quarters, Pitches: g.Select(v => v.Pitch).ToList()))
                .OrderBy(c => c.At).ToList();
            var spans = a.SingleSpans;

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
                        Off(to, pitch);
                    }
            }

            // the strokes: a strum keyswitch held until the next stroke (50 ms short of it, so each attack is heard), a little before its chord ends,
            // 1.2 s at most; a muted stroke's key 120 ms, with the Mute keyswitch around it
            var strokes = a.Strokes.OrderBy(s => s.AtQuarter).ToList();
            for (int i = 0; i < strokes.Count; ++i)
            {
                var st = strokes[i];
                if (!InRange(st.AtQuarter))
                    continue;
                var at = st.AtQuarter + Quarters(Lag, st.AtQuarter);
                var bpm = song.Tempo.BpmAt(st.AtQuarter);
                var rate = bpm / 60.0;
                var hold = HoldMax;
                var chord = chords.FirstOrDefault(c => c.At <= st.AtQuarter + 1e-9 && c.End > st.AtQuarter + 1e-9);
                if (chord.Pitches != null)
                    hold = Math.Min(hold, (chord.End - st.AtQuarter) / rate - ChordGap);
                if (st.Muted)
                    hold = Math.Min(hold, MuteHold);
                hold = Math.Max(HoldMin, hold);
                if (i + 1 < strokes.Count)
                {
                    var gap = (strokes[i + 1].AtQuarter - st.AtQuarter) / rate;  // seconds to the next stroke
                    hold = Math.Min(hold, Math.Max(0, gap - Math.Min(ReleaseLead, gap * 0.4)));
                }
                var key = st.Up ? UpKey : DownKey;
                var end = at + hold * rate;
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

            // the single-string notes: as notes, the strumming mode being let go
            foreach (var n in a.Notes.Where(n => n.Single && InRange(n.AtQuarter)))
            {
                On(n.AtQuarter, n.Pitch, n.Velocity);
                Off(n.AtQuarter + n.Quarters, n.Pitch);
            }

            WriteFile(song, path, events, fromQuarter, toQuarter, Tick);
        }

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
