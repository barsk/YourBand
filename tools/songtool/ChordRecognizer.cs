using System;
using System.Collections.Generic;
using System.Linq;

namespace YourBand.SongTool
{
    /// <summary>
    /// The chords of a track from its notes, for a Guitar Pro file that writes no chord names. Every note counts, not
    /// only the strummed ones: a fingerpicked part (a bass note, then the strings one by one) has no simultaneous
    /// notes at all. The song is cut into the chord sequence that explains the notes best (dynamic programming over an
    /// eighth-note grid): each stretch is scored by how many of its notes the chord holds, how few it does not, and
    /// whether its bass notes are the chord's root; a chord change costs something, and costs less at the start of a bar
    /// or a beat, which is where chords change.
    /// </summary>
    static class ChordRecognizer
    {
        public sealed class Note
        {
            public double At, Quarters;
            public int Pitch;        // MIDI pitch (as written: the capo is not added)
            public int StringIndex;  // 0 = lowest string
            public int Fret;
        }

        public sealed class Region
        {
            public double Start, Quarters;
            public string Name;
            public int[] Frets;  // per string, lowest first (-1 not played)
        }

        /// <summary>The grid, in quarters.</summary>
        const double Slot = 0.5;
        /// <summary>The longest stretch one chord covers without a restart (slots): two bars of 4/4 make 16, four 32.</summary>
        const int MaxSlots = 32;
        const int MinSlots = 3;
        /// <summary>Silence (slots) that ends a run of music: the chord ends too.</summary>
        const int RunGap = 2;

        const double ChangeCost = 4.5;
        const double StrayWeight = 1.6;
        const double MissingWeight = 0.4;
        const double BassRoot = 1.2, BassTone = 0.4, BassStray = 1.0;

        /// <summary>The qualities to choose from, simplest first, with what each costs per unit of sound (a plain triad nothing).</summary>
        static readonly (string Name, int[] Intervals, double Cost)[] Qualities =
        {
            ("5",     new[] { 0, 7 },        0.00),
            ("",      new[] { 0, 4, 7 },     0.00),
            ("m",     new[] { 0, 3, 7 },     0.00),
            ("sus2",  new[] { 0, 2, 7 },     0.06),
            ("sus4",  new[] { 0, 5, 7 },     0.06),
            ("7",     new[] { 0, 4, 7, 10 }, 0.25),
            ("m7",    new[] { 0, 3, 7, 10 }, 0.25),
            ("maj7",  new[] { 0, 4, 7, 11 }, 0.28),
            ("6",     new[] { 0, 4, 7, 9 },  0.30),
            ("m6",    new[] { 0, 3, 7, 9 },  0.30),
            ("add9",  new[] { 0, 2, 4, 7 },  0.28),
            ("dim",   new[] { 0, 3, 6 },     0.25),
            ("m7b5",  new[] { 0, 3, 6, 10 }, 0.25),
        };

        static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        public static List<Region> Recognize(List<Note> notes, IReadOnlyList<double> barStarts, int strings)
        {
            var regions = new List<Region>();
            if (notes.Count == 0)
                return regions;
            var end = notes.Max(n => n.At + n.Quarters);
            var count = (int)Math.Ceiling(end / Slot) + 1;

            // per slot: how much each pitch class sounds, and which pitch classes are struck as the lowest note
            var weight = new double[count + 1, 12];
            var bass = new double[count + 1, 12];
            var lowest = new int[count];
            Array.Fill(lowest, int.MaxValue);
            foreach (var n in notes)
            {
                if (n.Pitch < 0)
                    continue;
                var pc = n.Pitch % 12;
                var onset = (int)Math.Floor(n.At / Slot + 1e-6);
                var last = Math.Max(onset, (int)Math.Ceiling((n.At + n.Quarters) / Slot - 1e-6) - 1);
                for (int s = onset; s <= last && s < count; ++s)
                {
                    var overlap = Math.Min(n.At + n.Quarters, (s + 1) * Slot) - Math.Max(n.At, s * Slot);
                    weight[s, pc] += Math.Max(0, overlap) / Slot;
                }
                weight[onset, pc] += 0.5;   // a struck note counts more than one ringing on
                if (onset < count && n.Pitch < lowest[onset])
                    lowest[onset] = n.Pitch;
            }
            for (int s = 0; s < count; ++s)
                if (lowest[s] != int.MaxValue)
                    bass[s, lowest[s] % 12] += 1;

            // prefix sums, so any stretch's totals are two lookups
            var sumWeight = new double[count + 2, 12];
            var sumBass = new double[count + 2, 12];
            for (int s = 0; s <= count; ++s)
                for (int pc = 0; pc < 12; ++pc)
                {
                    sumWeight[s + 1, pc] = sumWeight[s, pc] + weight[s, pc];
                    sumBass[s + 1, pc] = sumBass[s, pc] + bass[s, pc];
                }

            bool Active(int s) { for (int pc = 0; pc < 12; ++pc) if (weight[s, pc] > 0) return true; return false; }

            // runs of music, apart by silence
            var runs = new List<(int From, int To)>();
            for (int s = 0; s < count;)
            {
                if (!Active(s)) { ++s; continue; }
                var from = s;
                var to = s + 1;
                for (int t = s + 1; t < count && t <= to + RunGap; ++t)
                    if (Active(t))
                        to = t + 1;
                runs.Add((from, to));
                s = to;
            }

            foreach (var (from, to) in runs)
            {
                var length = to - from;
                var best = new double[length + 1];
                var back = new int[length + 1];
                var pick = new (int Root, int Quality)[length + 1];
                Array.Fill(best, double.NegativeInfinity);
                best[0] = 0;
                for (int j = 1; j <= length; ++j)
                    for (int i = Math.Max(0, j - MaxSlots); i < j; ++i)
                    {
                        if (double.IsNegativeInfinity(best[i]))
                            continue;
                        if (j - i < MinSlots && j != length)
                            continue;
                        var (score, root, quality) = Score(sumWeight, sumBass, from + i, from + j);
                        var total = best[i] + score - ChangeCost - PositionCost((from + i) * Slot, barStarts);
                        if (total > best[j])
                        {
                            best[j] = total;
                            back[j] = i;
                            pick[j] = (root, quality);
                        }
                    }

                var cuts = new List<(int I, int J, int Root, int Quality)>();
                for (int j = length; j > 0; j = back[j])
                    cuts.Add((back[j], j, pick[j].Root, pick[j].Quality));
                cuts.Reverse();
                foreach (var (i, j, root, quality) in cuts)
                    regions.Add(Describe(notes, sumBass, from + i, from + j, root, quality, strings));
            }
            return regions;
        }

        /// <summary>What a chord change costs at this time: nothing extra at a bar line, a little at a beat, more elsewhere.</summary>
        static double PositionCost(double at, IReadOnlyList<double> barStarts)
        {
            double d = at;
            foreach (var b in barStarts)
            {
                if (b > at + 1e-6)
                    break;
                d = at - b;
            }
            if (d < 1e-6) return 0;
            if (Math.Abs(d - Math.Round(d)) < 1e-6) return 1.0;
            if (Math.Abs(d * 2 - Math.Round(d * 2)) < 1e-6) return 1.8;
            return 3.0;
        }

        /// <summary>The best chord for a stretch of slots [i, j) and how well it explains the notes there.</summary>
        static (double Score, int Root, int Quality) Score(double[,] sumWeight, double[,] sumBass, int i, int j)
        {
            var w = new double[12];
            var b = new double[12];
            double total = 0, bassTotal = 0;
            for (int pc = 0; pc < 12; ++pc)
            {
                w[pc] = sumWeight[j, pc] - sumWeight[i, pc];
                b[pc] = sumBass[j, pc] - sumBass[i, pc];
                total += w[pc];
                bassTotal += b[pc];
            }
            if (total <= 0)
                return (-0.5 * (j - i), 0, 1);

            double bestScore = double.NegativeInfinity;
            int bestRoot = 0, bestQuality = 1;
            for (int root = 0; root < 12; ++root)
                for (int q = 0; q < Qualities.Length; ++q)
                {
                    var (_, intervals, cost) = Qualities[q];
                    double held = 0, bassTones = 0;
                    int missing = 0;
                    var inChord = new bool[12];
                    foreach (var iv in intervals)
                        inChord[(root + iv) % 12] = true;
                    for (int pc = 0; pc < 12; ++pc)
                    {
                        if (inChord[pc])
                        {
                            held += w[pc];
                            if (pc != root) bassTones += b[pc];
                            if (w[pc] < 0.15 * total / intervals.Length) ++missing;
                        }
                    }
                    var strayBass = bassTotal - b[root] - bassTones;
                    var score = held - StrayWeight * (total - held)
                                - MissingWeight * total * missing / intervals.Length
                                - cost * total
                                + BassRoot * b[root] + BassTone * bassTones - BassStray * strayBass;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestRoot = root;
                        bestQuality = q;
                    }
                }
            return (bestScore, bestRoot, bestQuality);
        }

        /// <summary>A chosen stretch as a region: its name (a slash chord when the bass is mostly another chord tone) and shape.</summary>
        static Region Describe(List<Note> notes, double[,] sumBass, int i, int j, int root, int quality, int strings)
        {
            var (suffix, intervals, _) = Qualities[quality];
            var name = Names[root] + suffix;
            var inChord = new bool[12];
            foreach (var iv in intervals)
                inChord[(root + iv) % 12] = true;

            double bassTotal = 0, bassTop = 0;
            int bassPc = root;
            for (int pc = 0; pc < 12; ++pc)
            {
                var v = sumBass[j, pc] - sumBass[i, pc];
                bassTotal += v;
                if (v > bassTop) { bassTop = v; bassPc = pc; }
            }
            if (bassPc != root && inChord[bassPc] && bassTotal >= 2 && bassTop / bassTotal >= 0.7)
                name += "/" + Names[bassPc];

            var from = i * Slot - 1e-6;
            var to = j * Slot - 1e-6;
            var inside = notes.Where(n => n.At >= from && n.At < to).ToList();
            var start = inside.Count > 0 ? Math.Max(i * Slot, inside.Min(n => n.At)) : i * Slot;
            var finish = Math.Min(j * Slot, inside.Count > 0 ? inside.Max(n => n.At + n.Quarters) : j * Slot);

            // the shape: each string's most common fret among the chord's own notes
            var frets = new int[strings];
            Array.Fill(frets, -1);
            var chordNotes = inside.Where(n => inChord[n.Pitch % 12] && n.StringIndex >= 0 && n.StringIndex < strings).ToList();
            var strikes = chordNotes.GroupBy(n => n.StringIndex).ToDictionary(g => g.Key, g => g.Count());
            var most = strikes.Count > 0 ? strikes.Values.Max() : 0;
            foreach (var g in chordNotes.GroupBy(n => n.StringIndex))
                if (g.Count() >= 0.25 * most)
                    frets[g.Key] = g.GroupBy(n => n.Fret).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key;

            return new Region { Start = start, Quarters = Math.Max(0, finish - start), Name = name, Frets = frets };
        }
    }
}
