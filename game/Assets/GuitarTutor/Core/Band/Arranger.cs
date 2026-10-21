using System;
using System.Collections.Generic;
using System.Linq;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Music;
using GuitarTutor.Core.Songs;

namespace GuitarTutor.Core.Band
{
    public enum BandPart
    {
        Drums,
        Bass,
        Keys,
    }

    /// <summary>A note the band plays: a MIDI note number (drums: the General MIDI drum map), when, how long, how hard.</summary>
    public readonly struct BandNote
    {
        public BandPart Part { get; }
        public int Pitch { get; }
        public double AtQuarter { get; }
        public double Quarters { get; }
        public int Velocity { get; }

        public BandNote(BandPart part, int pitch, double atQuarter, double quarters, int velocity)
        {
            Part = part;
            Pitch = pitch;
            AtQuarter = atQuarter;
            Quarters = quarters;
            Velocity = velocity;
        }

        public override string ToString() => $"{Part} {Pitch} at {AtQuarter} for {Quarters} ({Velocity})";
    }

    /// <summary>General MIDI drum notes the arranger uses.</summary>
    public static class Drum
    {
        public const int Kick = 36, SideStick = 37, Snare = 38, ClosedHat = 42, LowTom = 45, PedalHat = 44,
            OpenHat = 46, MidTom = 47, Crash = 49, HighTom = 50, Ride = 51;
    }

    /// <summary>The band's parts for a whole song.</summary>
    public sealed class Arrangement
    {
        public string Style { get; set; } = "";
        public List<BandNote> Notes { get; } = new List<BandNote>();
        public IEnumerable<BandNote> Part(BandPart part) => Notes.Where(n => n.Part == part);
    }

    /// <summary>
    /// A band style: per 4/4 bar, the drum hits, the bass rhythm (which chord tone on each hit) and the keys
    /// rhythm. Hits are at quarter-note offsets in the bar.
    /// </summary>
    public sealed class BandStyle
    {
        public enum Tone { Root, Fifth, Octave, Approach }

        public string Name { get; set; } = "";
        public List<(int piece, double at, int velocity)> Drums { get; } = new List<(int, double, int)>();
        /// <summary>Drum hits for quiet sections (intro, verse, outro) where they differ: a side stick for the snare.</summary>
        public bool SideStickWhenQuiet { get; set; }
        public List<(double at, double length, Tone tone)> Bass { get; } = new List<(double, double, Tone)>();
        public List<(double at, double length)> Keys { get; } = new List<(double, double)>();
        public int KeysVelocity { get; set; } = 64;
        /// <summary>The keyboard sound that suits the style, as the engine's keyboard plugin names it (Numa Player).</summary>
        public string KeysSound { get; set; } = "";
        /// <summary>The keys' volume in the engine's mixer for that sound (an organ sustains: it sits lower).</summary>
        public double KeysGain { get; set; } = 1.3;

        public static readonly IReadOnlyList<BandStyle> All = new[] { Ballad(), Rock(), Pop() };

        public static BandStyle Named(string name) => All.FirstOrDefault(s => s.Name == name) ?? All[0];

        /// <summary>Slow and soft: eighth-note hats, side stick in the quiet sections, a bass that walks into each change.</summary>
        static BandStyle Ballad()
        {
            var s = new BandStyle { Name = "Ballad", SideStickWhenQuiet = true, KeysVelocity = 60, KeysSound = "2 - E_PIANO/1 - EP-Mark I" };
            for (int i = 0; i < 8; ++i)
                s.Drums.Add((Drum.ClosedHat, i * 0.5, i % 2 == 0 ? 72 : 50));
            s.Drums.Add((Drum.Kick, 0, 100));
            s.Drums.Add((Drum.Kick, 1.5, 70));
            s.Drums.Add((Drum.Kick, 2, 88));
            s.Drums.Add((Drum.Snare, 1, 90));
            s.Drums.Add((Drum.Snare, 3, 92));
            s.Bass.Add((0, 1.5, Tone.Root));
            s.Bass.Add((1.5, 0.5, Tone.Root));
            s.Bass.Add((2, 1, Tone.Fifth));
            s.Bass.Add((3, 1, Tone.Approach));
            s.Keys.Add((0, 2));
            s.Keys.Add((2, 2));
            return s;
        }

        /// <summary>Driving eighths: louder hats, kick on 1, 3 and the and of 3, eighth-note bass.</summary>
        static BandStyle Rock()
        {
            var s = new BandStyle { Name = "Rock", KeysVelocity = 78, KeysSound = "5 - ORGANS/4 - ROCK B3", KeysGain = 0.45 };
            for (int i = 0; i < 8; ++i)
                s.Drums.Add((Drum.ClosedHat, i * 0.5, i % 2 == 0 ? 96 : 72));
            s.Drums.Add((Drum.Kick, 0, 110));
            s.Drums.Add((Drum.Kick, 2, 104));
            s.Drums.Add((Drum.Kick, 2.5, 90));
            s.Drums.Add((Drum.Snare, 1, 110));
            s.Drums.Add((Drum.Snare, 3, 112));
            for (int i = 0; i < 8; ++i)
                s.Bass.Add((i * 0.5, 0.45, i == 7 ? Tone.Approach : Tone.Root));
            s.Keys.Add((0, 1.5));
            s.Keys.Add((1.5, 2.5));
            return s;
        }

        /// <summary>Lighter and syncopated: kick on the and of 2, keys pushed ahead of the beat.</summary>
        static BandStyle Pop()
        {
            var s = new BandStyle { Name = "Pop", KeysVelocity = 70, KeysSound = "1 - A_PIANO/1 - Model D 1983", KeysGain = 2.0 };
            for (int i = 0; i < 8; ++i)
                s.Drums.Add((Drum.ClosedHat, i * 0.5, i % 2 == 0 ? 84 : 60));
            s.Drums.Add((Drum.Kick, 0, 104));
            s.Drums.Add((Drum.Kick, 1.5, 84));
            s.Drums.Add((Drum.Kick, 2, 96));
            s.Drums.Add((Drum.Snare, 1, 100));
            s.Drums.Add((Drum.Snare, 3, 102));
            s.Bass.Add((0, 1, Tone.Root));
            s.Bass.Add((1.5, 0.5, Tone.Octave));
            s.Bass.Add((2, 1, Tone.Root));
            s.Bass.Add((3.5, 0.5, Tone.Approach));
            s.Keys.Add((0, 1.5));
            s.Keys.Add((1.5, 1.5));
            s.Keys.Add((3, 1));
            return s;
        }
    }

    /// <summary>
    /// Writes the band's parts from the chart (see docs/plan.md, "Song data": the arrangement layer): drums from
    /// the style with a crash at each section's start and a fill into the next section, bass on the chord roots
    /// walking into each change, keys voiced close around middle C, each chord moving as little as it can from
    /// the one before.
    /// </summary>
    public static class Arranger
    {
        /// <summary>The style a song gets unless you pick one: ballad below 120 BPM, rock from there.</summary>
        public static BandStyle DefaultStyle(Song song) => BandStyle.Named(song.Tempo.BpmAt(0) < 120 ? "Ballad" : "Rock");

        public const int BassLow = 28, BassHigh = 40;      // roots from E1 up to E2
        public const int KeysLow = 55, KeysHigh = 77;      // G3 to F5
        const int KeysCentre = 64;

        public static Arrangement Arrange(SongChart chart, BandStyle style = null)
        {
            style = style ?? DefaultStyle(chart.Song);
            var arrangement = new Arrangement { Style = style.Name };
            var notes = arrangement.Notes;
            var random = new Random(1);  // the same small variations every time
            int Humanise(int velocity) => Math.Max(1, Math.Min(127, velocity + random.Next(-5, 6)));
            var chords = chart.Song.Chords.Where(c => Chord.TryParse(c.Name, out _)).OrderBy(c => c.AtQuarter).ToList();
            List<int> lastVoicing = null;

            foreach (var bar in chart.Bars)
            {
                var last = bar.Index == chart.Bars.Count - 1;
                var fourFour = bar.Signature.Equals(TimeSignature.FourFour);
                var nextBar = last ? null : chart.Bars[bar.Index + 1];
                var fill = nextBar != null && nextBar.StartsSection && bar.Quarters >= 2;
                var quiet = style.SideStickWhenQuiet && IsQuiet(bar.Section);

                // ------------------------------------------------ drums
                if (bar.StartsSection || bar.Index == 0 || last)
                    notes.Add(new BandNote(BandPart.Drums, Drum.Crash, bar.AtQuarter, 2, Humanise(bar.Index == 0 ? 96 : 108)));
                if (last)
                    notes.Add(new BandNote(BandPart.Drums, Drum.Kick, bar.AtQuarter, 0.5, 110));
                else
                {
                    var hits = fourFour ? style.Drums : GenericBar(bar.Signature);
                    var fillFrom = fill ? bar.Quarters - 1 : double.MaxValue;
                    foreach (var (piece, at, velocity) in hits)
                    {
                        if (at >= bar.Quarters || at >= fillFrom && piece != Drum.Kick)
                            continue;
                        if (piece == Drum.ClosedHat && at == 0 && bar.StartsSection)
                            continue;  // the crash takes the first hat
                        var p = piece == Drum.Snare && quiet ? Drum.SideStick : piece;
                        notes.Add(new BandNote(BandPart.Drums, p, bar.AtQuarter + at, 0.25, Humanise(velocity)));
                    }
                    if (fill)
                        for (int i = 0; i < 4; ++i)
                        {
                            var piece = i < 2 ? Drum.Snare : i == 2 ? Drum.HighTom : Drum.LowTom;
                            notes.Add(new BandNote(BandPart.Drums, piece, bar.AtQuarter + fillFrom + i * 0.25, 0.25, Humanise(76 + i * 10)));
                        }
                }

                // ------------------------------------------------ bass and keys, chord by chord
                var bassHits = last ? new List<(double at, double length, BandStyle.Tone tone)> { (0, bar.Quarters, BandStyle.Tone.Root) }
                    : fourFour ? style.Bass : GenericBass(bar.Signature);
                var keysHits = last ? new List<(double at, double length)> { (0, bar.Quarters) }
                    : fourFour ? style.Keys : new List<(double at, double length)> { (0, bar.Quarters) };
                foreach (var (at, length, tone) in WithChordChanges(bassHits, bar, chords))
                {
                    var chord = ChordAt(chords, bar.AtQuarter + at, out var span);
                    if (chord == null)
                        continue;
                    // walk into the next chord only if it comes at the end of this bar
                    var endsHere = span.AtQuarter + span.Quarters <= bar.AtQuarter + bar.Quarters + 1e-6;
                    var next = endsHere ? chords.FirstOrDefault(c => c.AtQuarter >= span.AtQuarter + span.Quarters - 1e-6) : null;
                    var pitch = BassPitch(chord, tone, next != null ? Chord.Parse(next.Name) : null);
                    var end = Math.Min(bar.AtQuarter + at + length, span.AtQuarter + span.Quarters);
                    notes.Add(new BandNote(BandPart.Bass, pitch, bar.AtQuarter + at, end - bar.AtQuarter - at, Humanise(at == 0 ? 100 : 86)));
                }
                foreach (var (at, length, _) in WithChordChanges(keysHits.Select(k => (k.at, k.length, BandStyle.Tone.Root)).ToList(), bar, chords))
                {
                    var chord = ChordAt(chords, bar.AtQuarter + at, out var span);
                    if (chord == null)
                        continue;
                    var voicing = Voice(chord, lastVoicing);
                    lastVoicing = voicing;
                    var end = Math.Min(bar.AtQuarter + at + length, span.AtQuarter + span.Quarters);
                    var velocity = Humanise(style.KeysVelocity + (at == 0 ? 6 : 0));
                    foreach (var pitch in voicing)
                        notes.Add(new BandNote(BandPart.Keys, pitch, bar.AtQuarter + at, end - bar.AtQuarter - at, velocity));
                }
            }
            notes.Sort((a, b) => a.AtQuarter != b.AtQuarter ? a.AtQuarter.CompareTo(b.AtQuarter) : a.Part.CompareTo(b.Part));
            return arrangement;
        }

        static bool IsQuiet(Section section)
        {
            var name = section?.Name.ToLowerInvariant() ?? "";
            return name.StartsWith("intro") || name.StartsWith("verse") || name.StartsWith("outro") || name.StartsWith("bridge");
        }

        /// <summary>Bars not in 4/4: hats on eighths, kick on 1, snare on the last beat.</summary>
        static List<(int, double, int)> GenericBar(TimeSignature signature)
        {
            var hits = new List<(int, double, int)>();
            var beat = 4.0 / signature.Denominator;
            for (double at = 0; at < signature.QuartersPerBar - 1e-6; at += beat / 2)
                hits.Add((Drum.ClosedHat, at, Math.Abs(at % beat) < 1e-6 ? 72 : 50));
            hits.Add((Drum.Kick, 0, 100));
            hits.Add((Drum.Snare, signature.QuartersPerBar - beat, 90));
            return hits;
        }

        static List<(double, double, BandStyle.Tone)> GenericBass(TimeSignature signature) =>
            new List<(double, double, BandStyle.Tone)> { (0, signature.QuartersPerBar, BandStyle.Tone.Root) };

        /// <summary>
        /// A bar's rhythm with a hit added where a chord changes between hits, and every hit cut short where the
        /// next one starts.
        /// </summary>
        static IEnumerable<(double at, double length, BandStyle.Tone tone)> WithChordChanges(
            IReadOnlyList<(double at, double length, BandStyle.Tone tone)> hits, ChartBar bar, List<ChordEvent> chords)
        {
            var all = hits.Where(h => h.at < bar.Quarters - 1e-6).ToList();
            foreach (var c in bar.Chords)
                if (!c.CarriedIn && all.All(h => Math.Abs(h.at - c.Offset) > 1e-6))
                    all.Add((c.Offset, bar.Quarters - c.Offset, BandStyle.Tone.Root));
            all.Sort((a, b) => a.at.CompareTo(b.at));
            for (int i = 0; i < all.Count; ++i)
            {
                var limit = i + 1 < all.Count ? all[i + 1].at : bar.Quarters;
                // a hit right at a chord change plays the new chord's root
                var tone = bar.Chords.Any(c => !c.CarriedIn && Math.Abs(c.Offset - all[i].at) < 1e-6 && c.Offset > 0) ? BandStyle.Tone.Root : all[i].tone;
                yield return (all[i].at, Math.Min(all[i].length, limit - all[i].at), tone);
            }
        }

        static Chord ChordAt(List<ChordEvent> chords, double quarter, out ChordEvent span)
        {
            span = chords.LastOrDefault(c => c.AtQuarter <= quarter + 1e-6 && quarter < c.AtQuarter + c.Quarters - 1e-6);
            return span != null ? Chord.Parse(span.Name) : null;
        }

        /// <summary>The bass note for a chord tone: roots (and slash basses) between E1 and E2.</summary>
        public static int BassPitch(Chord chord, BandStyle.Tone tone, Chord next)
        {
            int Low(int pitchClass) => BassLow + ((pitchClass - BassLow % 12) % 12 + 12) % 12;
            var root = Low(chord.Bass);
            switch (tone)
            {
                case BandStyle.Tone.Fifth:
                    return chord.Intervals.Contains(7) || chord.Intervals.Contains(6) || chord.Intervals.Contains(8)
                        ? Low(chord.Root) + chord.Intervals.First(i => i >= 6 && i <= 8)
                        : root;
                case BandStyle.Tone.Octave:
                    return root + 12;
                case BandStyle.Tone.Approach:
                    if (next == null || next.Bass == chord.Bass)
                        return root;
                    // a half step towards the next root, from the side it is nearer
                    var target = Low(next.Bass);
                    return target > root ? target - 1 : target + 1;
                default:
                    return root;
            }
        }

        /// <summary>
        /// Close voicings of the chord (three or four notes) between G3 and F5; the one that moves least from the
        /// last voicing (or, for the first chord, sits nearest E4).
        /// </summary>
        public static List<int> Voice(Chord chord, List<int> previous)
        {
            var classes = chord.Intervals.Select(i => (chord.Root + i) % 12).Distinct().ToList();
            if (classes.Count > 4)
                classes = classes.Where(pc => pc != (chord.Root + 7) % 12).Take(4).ToList();  // drop the fifth
            List<int> best = null;
            var bestCost = double.MaxValue;
            foreach (var bottom in classes)
                for (int octave = 3; octave <= 5; ++octave)
                {
                    var notes = new List<int>();
                    var start = 12 * (octave + 1) + bottom;
                    foreach (var pc in classes.OrderBy(pc => (pc - bottom + 12) % 12))
                        notes.Add(start + (pc - bottom + 12) % 12);
                    if (notes[0] < KeysLow || notes[notes.Count - 1] > KeysHigh)
                        continue;
                    var cost = previous == null || previous.Count == 0
                        ? Math.Abs(notes.Average() - KeysCentre)
                        : Movement(notes, previous);
                    if (cost < bestCost)
                    {
                        best = notes;
                        bestCost = cost;
                    }
                }
            return best ?? classes.Select(pc => 60 + pc).ToList();
        }

        // how far a voicing moves from the last: each note to the nearest note of the other
        static double Movement(List<int> a, List<int> b) =>
            a.Sum(x => b.Min(y => Math.Abs(x - y))) + b.Sum(y => a.Min(x => Math.Abs(x - y))) + 0.1 * Math.Abs(a.Average() - KeysCentre);
    }
}
