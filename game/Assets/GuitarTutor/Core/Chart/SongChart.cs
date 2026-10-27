using System;
using System.Collections.Generic;
using System.Linq;
using GuitarTutor.Core.Music;
using GuitarTutor.Core.Songs;

namespace GuitarTutor.Core.Chart
{
    /// <summary>A chord as one bar shows it: where in the bar it starts and how much of the bar it fills.</summary>
    public readonly struct BarChord
    {
        public ChordEvent Chord { get; }
        /// <summary>Quarter notes from the start of the bar (0 if the chord carries in from an earlier bar).</summary>
        public double Offset { get; }
        public double Quarters { get; }
        /// <summary>True if the chord started before this bar.</summary>
        public bool CarriedIn { get; }

        public BarChord(ChordEvent chord, double offset, double quarters, bool carriedIn)
        {
            Chord = chord;
            Offset = offset;
            Quarters = quarters;
            CarriedIn = carriedIn;
        }
    }

    /// <summary>A lyric word as one bar shows it.</summary>
    public readonly struct BarWord
    {
        public LyricWord Word { get; }
        public double Offset { get; }
        /// <summary>Part of a backing vocal line.</summary>
        public bool Backing { get; }

        public BarWord(LyricWord word, double offset, bool backing = false)
        {
            Word = word;
            Offset = offset;
            Backing = backing;
        }
    }

    /// <summary>A tab note as one bar shows it.</summary>
    public readonly struct BarNote
    {
        public TabNote Note { get; }
        public double Offset { get; }
        public Riff Riff { get; }

        public BarNote(TabNote note, double offset, Riff riff)
        {
            Note = note;
            Offset = offset;
            Riff = riff;
        }
    }

    /// <summary>One bar of the chart: its chords, the words sung in it, and the tab notes played in it.</summary>
    public sealed class ChartBar
    {
        /// <summary>Numbered from 0 (the chart shows it from 1).</summary>
        public int Index { get; internal set; }
        public double AtQuarter { get; internal set; }
        public TimeSignature Signature { get; internal set; }
        public double Quarters => Signature.QuartersPerBar;
        public Section Section { get; internal set; }
        /// <summary>True for the first bar of a section.</summary>
        public bool StartsSection { get; internal set; }
        public List<BarChord> Chords { get; } = new List<BarChord>();
        public List<BarWord> Words { get; } = new List<BarWord>();
        public List<BarNote> Notes { get; } = new List<BarNote>();
    }

    /// <summary>A section as the section map shows it: which bars it covers.</summary>
    public readonly struct SectionSpan
    {
        public Section Section { get; }
        public int FirstBar { get; }
        public int Bars { get; }

        public SectionSpan(Section section, int firstBar, int bars)
        {
            Section = section;
            FirstBar = firstBar;
            Bars = bars;
        }
    }

    /// <summary>A row of the chart: up to <see cref="SongChart.BarsPerRow"/> bars; each section starts a new row.</summary>
    public readonly struct ChartRow
    {
        public int Index { get; }
        public int FirstBar { get; }
        public int Bars { get; }
        public double AtQuarter { get; }
        public double Quarters { get; }
        /// <summary>The section the row is in (null if the song has none there).</summary>
        public Section Section { get; }
        /// <summary>True for a section's first row.</summary>
        public bool StartsSection { get; }

        public ChartRow(int index, int firstBar, int bars, double atQuarter, double quarters, Section section, bool startsSection)
        {
            Index = index;
            FirstBar = firstBar;
            Bars = bars;
            AtQuarter = atQuarter;
            Quarters = quarters;
            Section = section;
            StartsSection = startsSection;
        }
    }

    /// <summary>
    /// A song laid out as the chart shows it (see docs/plan.md, "Desktop mode"): bar by bar, with what to show at a
    /// position (the bar, the lyric line, the strumming stroke, the next chord change). Built once per song; the
    /// views only ask it questions.
    /// </summary>
    public sealed class SongChart
    {
        /// <summary>How long a lyric line stays current after its last word starts, at most (in quarter notes).</summary>
        public const double LineTail = 4;

        /// <summary>The most bars a row of the chart holds.</summary>
        public const int BarsPerRow = 4;

        public Song Song { get; }
        public IReadOnlyList<ChartBar> Bars => bars;
        public IReadOnlyList<SectionSpan> Sections => sections;
        public IReadOnlyList<ChartRow> Rows => rows;
        /// <summary>The length of the song's most common bar (quarter notes): <see cref="BarsPerRow"/> of them fill a row.</summary>
        public double TypicalBarQuarters { get; }
        /// <summary>The song's chords by name, in the order they first appear.</summary>
        public IReadOnlyList<string> ChordNames { get; }
        public double EndQuarter { get; }

        readonly List<ChartBar> bars = new List<ChartBar>();
        readonly List<SectionSpan> sections = new List<SectionSpan>();
        readonly List<ChartRow> rows = new List<ChartRow>();
        readonly List<int> rowOfBar = new List<int>();
        readonly List<LyricLine> lines;
        readonly List<ChordEvent> chords;

        public SongChart(Song song)
        {
            Song = song ?? throw new ArgumentNullException(nameof(song));
            chords = song.Chords.OrderBy(c => c.AtQuarter).ToList();
            // the lead lines are the song's lines; backing vocals only add their words to the bars
            lines = song.Lyrics.Where(l => l.Words.Count > 0 && !l.Backing).OrderBy(l => l.AtQuarter).ToList();
            ChordNames = chords.Select(c => c.Name).Distinct().ToList();

            var ends = new List<double> { 0 };
            ends.AddRange(chords.Select(c => c.AtQuarter + c.Quarters));
            ends.AddRange(song.Sections.Select(s => s.AtQuarter + s.Quarters));
            ends.AddRange(song.Lyrics.SelectMany(l => l.Words).Select(w => w.AtQuarter + 1));
            ends.AddRange(song.Riffs.SelectMany(r => r.Notes).Select(n => n.AtQuarter + Math.Max(n.Quarters, 0.25)));
            var end = ends.Max();

            var tempo = song.Tempo;
            Section previous = null;
            for (int index = 0; ; ++index)
            {
                var at = tempo.QuarterOfBar(index);
                if (at >= end - 1e-6 && index > 0)
                    break;
                var bar = new ChartBar { Index = index, AtQuarter = at, Signature = tempo.SignatureAtBar(index) };
                bar.Section = song.SectionAt(at + 1e-6);
                bar.StartsSection = bar.Section != null && bar.Section != previous;
                previous = bar.Section;
                bars.Add(bar);
            }
            EndQuarter = bars.Count > 0 ? bars[bars.Count - 1].AtQuarter + bars[bars.Count - 1].Quarters : 0;

            foreach (var c in chords)
                foreach (var bar in BarsOver(c.AtQuarter, c.AtQuarter + c.Quarters))
                {
                    var from = Math.Max(c.AtQuarter, bar.AtQuarter);
                    var to = Math.Min(c.AtQuarter + c.Quarters, bar.AtQuarter + bar.Quarters);
                    bar.Chords.Add(new BarChord(c, from - bar.AtQuarter, to - from, c.AtQuarter < bar.AtQuarter - 1e-6));
                }
            foreach (var l in song.Lyrics)
                foreach (var w in l.Words)
                {
                    var bar = BarOf(w.AtQuarter);
                    bar?.Words.Add(new BarWord(w, w.AtQuarter - bar.AtQuarter, l.Backing));
                }
            foreach (var r in song.Riffs)
                foreach (var n in r.Notes)
                {
                    var bar = BarOf(n.AtQuarter);
                    bar?.Notes.Add(new BarNote(n, n.AtQuarter - bar.AtQuarter, r));
                }
            foreach (var bar in bars)
            {
                bar.Words.Sort((a, b) => a.Offset.CompareTo(b.Offset));
                bar.Notes.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            }

            for (int i = 0; i < bars.Count;)
            {
                int j = i + 1;
                while (j < bars.Count && !bars[j].StartsSection && bars[j].Section == bars[i].Section)
                    ++j;
                if (bars[i].Section != null)
                    sections.Add(new SectionSpan(bars[i].Section, i, j - i));
                i = j;
            }

            // rows: up to four bars, a new one at each section
            TypicalBarQuarters = bars.Count == 0 ? 4 : bars.GroupBy(b => b.Quarters).OrderByDescending(g => g.Count()).First().Key;
            for (int i = 0; i < bars.Count;)
            {
                int j = i + 1;
                while (j < bars.Count && j - i < BarsPerRow && !bars[j].StartsSection)
                    ++j;
                var rowEnd = bars[j - 1].AtQuarter + bars[j - 1].Quarters;
                rows.Add(new ChartRow(rows.Count, i, j - i, bars[i].AtQuarter, rowEnd - bars[i].AtQuarter, bars[i].Section, bars[i].StartsSection));
                for (int k = i; k < j; ++k)
                    rowOfBar.Add(rows.Count - 1);
                i = j;
            }
        }

        // ------------------------------------------------------------ rows
        /// <summary>The row a bar is in.</summary>
        public int RowOf(int bar) => rowOfBar.Count == 0 ? 0 : rowOfBar[Math.Max(0, Math.Min(bar, rowOfBar.Count - 1))];

        /// <summary>
        /// Where the chart's rows stand at a position, as a row number with a fraction: the number of the row
        /// being played, rising smoothly to the next row's over the last <paramref name="lead"/> quarter notes of
        /// the row, so the next row is in place when the music gets there.
        /// </summary>
        public double RowPosition(double quarter, double lead)
        {
            if (rows.Count == 0)
                return 0;
            var bar = BarOf(Math.Max(0, quarter));
            var r = bar == null ? (quarter < 0 ? 0 : rows.Count - 1) : RowOf(bar.Index);
            if (r >= rows.Count - 1 || lead <= 0)
                return r;
            var end = rows[r].AtQuarter + rows[r].Quarters;
            var t = (quarter - (end - lead)) / lead;
            if (t <= 0)
                return r;
            t = Math.Min(1, t);
            return r + t * t * (3 - 2 * t);
        }

        // ------------------------------------------------------------ bars
        /// <summary>The bar a position falls in (null before the song or after its end).</summary>
        public ChartBar BarOf(double quarter)
        {
            if (quarter < 0 || bars.Count == 0)
                return null;
            var (index, _) = Song.Tempo.BarAt(quarter);
            return index < bars.Count ? bars[index] : null;
        }

        IEnumerable<ChartBar> BarsOver(double from, double to)
        {
            var first = BarOf(from);
            if (first == null)
                yield break;
            for (int i = first.Index; i < bars.Count && bars[i].AtQuarter < to - 1e-6; ++i)
                yield return bars[i];
        }

        /// <summary>The section a bar belongs to, as an index into <see cref="Sections"/> (-1 if none).</summary>
        public int SectionIndexOf(int bar)
        {
            for (int i = 0; i < sections.Count; ++i)
                if (bar >= sections[i].FirstBar && bar < sections[i].FirstBar + sections[i].Bars)
                    return i;
            return -1;
        }

        // ------------------------------------------------------------ lyrics
        /// <summary>
        /// The lyric line being sung at a position: from its first word until the next line starts, but no longer
        /// than <see cref="LineTail"/> after its last word (so instrumental stretches show no line). -1 if none.
        /// </summary>
        public int LineAt(double quarter)
        {
            for (int i = lines.Count - 1; i >= 0; --i)
            {
                var line = lines[i];
                var start = line.Words[0].AtQuarter;
                if (start > quarter)
                    continue;
                var end = line.Words[line.Words.Count - 1].AtQuarter + LineTail;
                if (i + 1 < lines.Count)
                    end = Math.Min(end, lines[i + 1].Words[0].AtQuarter);
                return quarter < end ? i : -1;
            }
            return -1;
        }

        /// <summary>The next lyric line to start after a position (-1 if none).</summary>
        public int NextLineAfter(double quarter)
        {
            for (int i = 0; i < lines.Count; ++i)
                if (lines[i].Words[0].AtQuarter > quarter)
                    return i;
            return -1;
        }

        public LyricLine Line(int index) => index >= 0 && index < lines.Count ? lines[index] : null;
        public int LineCount => lines.Count;

        /// <summary>How many words of a line have started at a position (0 before the first word).</summary>
        public static int WordsSung(LyricLine line, double quarter) => line?.Words.Count(w => w.AtQuarter <= quarter) ?? 0;

        // ------------------------------------------------------------ chords
        /// <summary>The next chord change after a position: the first chord after it with a different name.</summary>
        public ChordEvent NextChange(double quarter)
        {
            var now = Song.ChordAt(quarter);
            return chords.FirstOrDefault(c => c.AtQuarter > quarter && (now == null || c.Name != now.Name));
        }

        // ------------------------------------------------------------ strumming
        /// <summary>
        /// The strumming patterns for a section: those made for it first, then those for the whole song (empty if
        /// the song has none).
        /// </summary>
        public IReadOnlyList<StrumPattern> PatternsFor(Section section)
        {
            var name = section?.Name ?? "";
            return Song.Strumming.Where(p => p.Sections.Contains(name))
                .Concat(Song.Strumming.Where(p => p.Sections.Count == 0))
                .ToList();
        }

        /// <summary>
        /// The stroke of a pattern being played at a position: its index in the pattern (patterns repeat each bar,
        /// or more often if shorter), and how far through that stroke's slot the position is (0..1). (-1, 0) before
        /// the song.
        /// </summary>
        public (int index, double fraction) StrokeAt(StrumPattern pattern, double quarter)
        {
            if (pattern == null || pattern.Strokes.Count == 0 || pattern.Subdivision <= 0 || quarter < 0)
                return (-1, 0);
            var (barIndex, inBar) = Song.Tempo.BarAt(quarter);
            var slot = 4.0 / pattern.Subdivision;
            var steps = inBar / slot;
            var step = (int)Math.Floor(steps + 1e-9);
            return (StrokeIndex(pattern, barIndex, step), steps - step);
        }

        /// <summary>
        /// Which stroke of a pattern a step of a bar is. A pattern repeats each bar, or more often if shorter; a pattern
        /// of whole bars (32 sixteenths in 4/4) takes one bar of its strokes after another, from the section's start.
        /// </summary>
        public int StrokeIndex(StrumPattern pattern, int bar, int stepInBar)
        {
            var count = pattern.Strokes.Count;
            var perBar = bar >= 0 && bar < bars.Count ? (int)Math.Round(bars[bar].Quarters / (4.0 / pattern.Subdivision)) : 0;
            if (perBar > 0 && count > perBar && count % perBar == 0)
            {
                var section = SectionIndexOf(bar);
                var first = section >= 0 ? sections[section].FirstBar : 0;
                return ((bar - first) % (count / perBar)) * perBar + stepInBar % perBar;
            }
            return stepInBar % count;
        }

        /// <summary>
        /// The counts to show under a pattern's strokes, as Ultimate Guitar shows them: "1 & 2 &" for eighths; for
        /// sixteenths the same, on every second slot (the slots between have none). The beats count on through the
        /// bars of a long pattern (1-4, then 5-8).
        /// </summary>
        public static IReadOnlyList<string> Counts(StrumPattern pattern)
        {
            var perBeat = Math.Max(1, pattern.Subdivision / 4);
            string[] names = perBeat == 2 ? new[] { "", "&" } : perBeat == 4 ? new[] { "", "", "&", "" } : perBeat == 3 ? new[] { "", "&", "a" } : new[] { "" };
            var counts = new List<string>();
            for (int i = 0; i < pattern.Strokes.Count; ++i)
            {
                var within = i % perBeat;
                counts.Add(within == 0 ? (i / perBeat + 1).ToString() : names[Math.Min(within, names.Length - 1)]);
            }
            return counts;
        }
    }
}
