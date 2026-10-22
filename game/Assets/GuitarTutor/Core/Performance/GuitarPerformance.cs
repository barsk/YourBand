using System;
using System.Collections.Generic;
using System.Linq;
using GuitarTutor.Core.Band;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Music;
using GuitarTutor.Core.Songs;

namespace GuitarTutor.Core.Performance
{
    /// <summary>A chord shape held from a moment: per string from the lowest, the fret (-1 not played, 0 open)
    /// and the finger (0 none, 1 index ... 4 little).</summary>
    public sealed class Grip
    {
        public double AtQuarter { get; set; }
        public double Quarters { get; set; }
        public string Chord { get; set; } = "";
        public int[] Frets { get; set; } = new int[0];
        public int[] Fingers { get; set; } = new int[0];
        /// <summary>The index finger across several strings (a barre), or null.</summary>
        public (int fret, int fromString, int toString)? Barre { get; set; }

        /// <summary>Where along the neck the hand is: the lowest fretted fret (1 for open chords).</summary>
        public int Position => Frets.Where(f => f > 0).DefaultIfEmpty(1).Min();
    }

    /// <summary>A stroke of the strumming hand: when, which way, across which strings (from the lowest), how hard.</summary>
    public readonly struct Strum
    {
        public double AtQuarter { get; }
        public bool Down { get; }
        public int LowString { get; }
        public int HighString { get; }
        public int Velocity { get; }
        public bool Muted { get; }

        public Strum(double atQuarter, bool down, int lowString, int highString, int velocity, bool muted)
        {
            AtQuarter = atQuarter;
            Down = down;
            LowString = lowString;
            HighString = highString;
            Velocity = velocity;
            Muted = muted;
        }

        public override string ToString() => $"{(Down ? "down" : "up")} {LowString}-{HighString} at {AtQuarter:0.###}{(Muted ? " muted" : "")}";
    }

    /// <summary>
    /// The band guitarist's playing as the hands do it: the chord shapes held (the song's fingerings, as the
    /// chart shows them, else Arranger.GuitarShape with fingers found for it) and the strokes, read back from
    /// the guitar part's notes (Arranger.Strum spreads each stroke across the strings).
    /// </summary>
    public static class GuitarPerformance
    {
        /// <summary>Notes of one stroke follow each other closer than this (quarter notes).</summary>
        const double SameStroke = 0.15;

        public static List<Grip> Grips(SongChart chart)
        {
            var song = chart.Song;
            var grips = new List<Grip>();
            foreach (var c in song.Chords.OrderBy(c => c.AtQuarter))
            {
                if (!Chord.TryParse(c.Name, out var chord))
                    continue;
                var voicing = song.VoicingFor(c);
                int[] frets, fingers;
                if (voicing != null && voicing.Frets.Count == 6)
                {
                    frets = voicing.Frets.ToArray();
                    fingers = voicing.Fingers.Count == 6 && voicing.Fingers.Any(f => f > 0) ? voicing.Fingers.ToArray() : FingersFor(frets);
                }
                else
                {
                    frets = Arranger.GuitarShape(chord).ToArray();
                    fingers = FingersFor(frets);
                }
                grips.Add(new Grip { AtQuarter = c.AtQuarter, Quarters = c.Quarters, Chord = c.Name, Frets = frets, Fingers = fingers, Barre = BarreOf(frets, fingers) });
            }
            return grips;
        }

        /// <summary>
        /// Fingers for a shape that has none: a barre with the index across the strings at the lowest fret when
        /// several share it, the others from the index up by fret, then by string (as guitarists finger E and A
        /// shapes).
        /// </summary>
        public static int[] FingersFor(int[] frets)
        {
            var fingers = new int[frets.Length];
            var fretted = Enumerable.Range(0, frets.Length).Where(s => frets[s] > 0).ToList();
            if (fretted.Count == 0)
                return fingers;
            var lowest = fretted.Min(s => frets[s]);
            var atLowest = fretted.Where(s => frets[s] == lowest).ToList();
            var barre = atLowest.Count >= 2 && lowest > 0 && fretted.Count > 3;
            var next = 1;
            if (barre)
            {
                foreach (var s in atLowest)
                    fingers[s] = 1;
                next = 2;
            }
            foreach (var s in fretted.Where(s => fingers[s] == 0).OrderBy(s => frets[s]).ThenBy(s => s))
            {
                fingers[s] = Math.Min(4, Math.Max(next, 1 + frets[s] - lowest));
                next = fingers[s] + 1;
            }
            return fingers;
        }

        static (int, int, int)? BarreOf(int[] frets, int[] fingers)
        {
            var index = Enumerable.Range(0, frets.Length).Where(s => fingers[s] == 1 && frets[s] > 0).ToList();
            if (index.Count < 2)
                return null;
            var fret = frets[index[0]];
            if (index.Any(s => frets[s] != fret))
                return null;
            return (fret, index.Min(), Enumerable.Range(0, frets.Length).Where(s => frets[s] >= 0).Max());
        }

        /// <summary>The strokes in the guitar part: its notes grouped by stroke, the way told by their order.</summary>
        public static List<Strum> Strums(SongChart chart, IEnumerable<BandNote> notes)
        {
            var open = Arranger.OpenStrings(chart.Song.Info);
            int StringOf(int pitch, double at)
            {
                // the string the shape plays this pitch on (the chord sounding then), else the nearest open string below
                var chord = chart.Song.ChordAt(at);
                var voicing = chord != null ? chart.Song.VoicingFor(chord) : null;
                var frets = voicing != null && voicing.Frets.Count == 6 ? voicing.Frets.ToArray()
                    : chord != null && Chord.TryParse(chord.Name, out var c) ? Arranger.GuitarShape(c).ToArray() : null;
                if (frets != null)
                    for (int s = 0; s < 6; ++s)
                        if (frets[s] >= 0 && open[s] + frets[s] == pitch)
                            return s;
                for (int s = 5; s >= 0; --s)
                    if (pitch >= open[s])
                        return s;
                return 0;
            }

            var strums = new List<Strum>();
            var list = notes.Where(n => n.Part == BandPart.Guitar).OrderBy(n => n.AtQuarter).ToList();
            for (int i = 0; i < list.Count;)
            {
                var j = i + 1;
                while (j < list.Count && list[j].AtQuarter - list[j - 1].AtQuarter < SameStroke && list[j].AtQuarter - list[i].AtQuarter < 2 * SameStroke)
                    ++j;
                var stroke = list.GetRange(i, j - i);
                var strings = stroke.Select(n => StringOf(n.Pitch, n.AtQuarter + 1e-6)).ToList();
                var down = strings.Count < 2 || strings[strings.Count - 1] >= strings[0];
                var muted = stroke.All(n => n.Quarters <= 0.08 + 1e-6);
                strums.Add(new Strum(stroke[0].AtQuarter, down, strings.Min(), strings.Max(), stroke.Max(n => n.Velocity), muted));
                i = j;
            }
            return strums;
        }
    }

    /// <summary>The bassist's notes on the strings: which string and fret each is played at.</summary>
    public readonly struct BassNote
    {
        public double AtQuarter { get; }
        public double Quarters { get; }
        public int String { get; }  // 0 the low E ... 3 the G
        public int Fret { get; }
        public int Velocity { get; }

        public BassNote(double atQuarter, double quarters, int @string, int fret, int velocity)
        {
            AtQuarter = atQuarter;
            Quarters = quarters;
            String = @string;
            Fret = fret;
            Velocity = velocity;
        }

        public override string ToString() => $"string {String} fret {Fret} at {AtQuarter}";
    }

    public static class BassPerformance
    {
        /// <summary>The bass's open strings (E1 A1 D2 G2), lowest first.</summary>
        public static readonly int[] Open = { 28, 33, 38, 43 };
        const int Frets = 18;

        /// <summary>
        /// Each note on a string and fret, the hand moving as little as it can: fewest position shifts (dynamic
        /// programming over the choices), open strings welcome, low positions preferred.
        /// </summary>
        public static List<BassNote> Plan(IEnumerable<BandNote> notes)
        {
            var list = notes.Where(n => n.Part == BandPart.Bass).OrderBy(n => n.AtQuarter).ToList();
            var choices = list.Select(n => Enumerable.Range(0, 4).Where(s => n.Pitch >= Open[s] && n.Pitch - Open[s] <= Frets)
                .Select(s => (s, f: n.Pitch - Open[s])).ToList()).ToList();
            var result = new List<BassNote>();
            if (list.Count == 0)
                return result;
            // cost of being at a choice: the fret's height; of moving: how far the hand shifts
            double Place((int s, int f) c) => c.f == 0 ? 0 : 0.15 * c.f;
            double Move((int s, int f) a, (int s, int f) b) => (a.f == 0 || b.f == 0 ? 0 : Math.Abs(a.f - b.f)) + 0.3 * Math.Abs(a.s - b.s);
            var cost = new List<double[]>();
            var from = new List<int[]>();
            for (int i = 0; i < list.Count; ++i)
            {
                var c = choices[i];
                if (c.Count == 0)
                    c.Add((0, Math.Max(0, list[i].Pitch - Open[0])));  // out of range: the low string
                var here = new double[c.Count];
                var back = new int[c.Count];
                for (int k = 0; k < c.Count; ++k)
                {
                    if (i == 0)
                    {
                        here[k] = Place(c[k]);
                        continue;
                    }
                    var best = double.MaxValue;
                    for (int p = 0; p < choices[i - 1].Count; ++p)
                    {
                        var v = cost[i - 1][p] + Move(choices[i - 1][p], c[k]);
                        if (v < best)
                        {
                            best = v;
                            back[k] = p;
                        }
                    }
                    here[k] = best + Place(c[k]);
                }
                cost.Add(here);
                from.Add(back);
            }
            var at = Array.IndexOf(cost[cost.Count - 1], cost[cost.Count - 1].Min());
            var picks = new int[list.Count];
            for (int i = list.Count - 1; i >= 0; --i)
            {
                picks[i] = at;
                if (i > 0)
                    at = from[i][at];
            }
            for (int i = 0; i < list.Count; ++i)
            {
                var (s, f) = choices[i][picks[i]];
                result.Add(new BassNote(list[i].AtQuarter, list[i].Quarters, s, f, list[i].Velocity));
            }
            return result;
        }
    }

    /// <summary>A key pressed: which finger of which hand, from when, how long.</summary>
    public readonly struct KeyPress
    {
        public int Pitch { get; }
        public double AtQuarter { get; }
        public double Quarters { get; }
        public bool Right { get; }
        public int Finger { get; }  // 1 thumb ... 5 little

        public KeyPress(int pitch, double atQuarter, double quarters, bool right, int finger)
        {
            Pitch = pitch;
            AtQuarter = atQuarter;
            Quarters = quarters;
            Right = right;
            Finger = finger;
        }
    }

    public static class KeysPerformance
    {
        /// <summary>
        /// The keys part's chords with the right hand (thumb on the lowest note, the little finger on the
        /// highest, the fingers between in order: 1-3-5 for three notes, 1-2-3-5 for four); notes below the
        /// chord's span of a hand (more than a ninth under its top) with the left.
        /// </summary>
        public static List<KeyPress> Plan(IEnumerable<BandNote> notes)
        {
            var presses = new List<KeyPress>();
            foreach (var chord in notes.Where(n => n.Part == BandPart.Keys).GroupBy(n => Math.Round(n.AtQuarter, 6)).OrderBy(g => g.Key))
            {
                var sorted = chord.OrderBy(n => n.Pitch).ToList();
                var top = sorted[sorted.Count - 1].Pitch;
                var right = sorted.Where(n => top - n.Pitch <= 14).ToList();
                var left = sorted.Where(n => top - n.Pitch > 14).ToList();
                int[] FingersFor(int count) => count == 1 ? new[] { 2 } : count == 2 ? new[] { 1, 5 } : count == 3 ? new[] { 1, 3, 5 } : new[] { 1, 2, 3, 5 }.Take(count).ToArray();
                var rf = FingersFor(Math.Min(4, right.Count));
                for (int i = 0; i < right.Count && i < rf.Length; ++i)
                    presses.Add(new KeyPress(right[i].Pitch, right[i].AtQuarter, right[i].Quarters, true, rf[i]));
                var lf = FingersFor(Math.Min(4, left.Count)).Reverse().ToArray();  // the left hand's little finger lowest
                for (int i = 0; i < left.Count && i < lf.Length; ++i)
                    presses.Add(new KeyPress(left[i].Pitch, left[i].AtQuarter, left[i].Quarters, false, lf[i]));
            }
            return presses;
        }
    }
}
