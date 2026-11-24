using System;
using System.Collections.Generic;
using System.Linq;
using YourBand.Core.Music;
using YourBand.Core.Songs;

namespace YourBand.Core.Chart
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
        /// <summary>The song's chords in this bar: the ones the band plays from.</summary>
        public List<BarChord> Chords { get; } = new List<BarChord>();
        /// <summary>The chords the chart shows in this bar (<see cref="SongChart.ShowChordsOf"/>).</summary>
        public List<BarChord> ShownChords { get; } = new List<BarChord>();
        public List<BarWord> Words { get; } = new List<BarWord>();
        public List<BarNote> Notes { get; } = new List<BarNote>();
    }

    /// <summary>
    /// A tab the TAB panel can show: the song's own (its riffs; a Guitar Pro song's from its main guitar track), or
    /// another of a Guitar Pro file's tracks written as tab. Its notes are bar by bar, as <see cref="ChartBar.Notes"/>
    /// holds the song's own.
    /// </summary>
    public sealed class ChartTab
    {
        /// <summary>The track's name; the song's own tab has its main track's name (a Guitar Pro song), else none.</summary>
        public string Name { get; }
        /// <summary>The open strings, lowest first, as note names.</summary>
        public IReadOnlyList<string> Tuning { get; }
        /// <summary>The song's own tab (the one the band's guitarist follows), not another track's.</summary>
        public bool IsSongTab { get; }
        /// <summary>
        /// A drum tab: a line per drum (<see cref="Tuning"/> holds their short names, the bass drum lowest), each hit
        /// a mark in its note's <see cref="TabNote.Technique"/> (x, o, X or O accented, (o) a ghost note), no frets.
        /// </summary>
        public bool IsDrums { get; }
        readonly IReadOnlyList<IReadOnlyList<BarNote>> notes;

        internal ChartTab(string name, IReadOnlyList<string> tuning, bool isSongTab, IReadOnlyList<IReadOnlyList<BarNote>> barNotes, bool isDrums = false)
        {
            Name = name;
            Tuning = tuning;
            IsSongTab = isSongTab;
            notes = barNotes;
            IsDrums = isDrums;
        }

        /// <summary>The tab notes played in a bar, in time order.</summary>
        public IReadOnlyList<BarNote> NotesIn(int bar) => bar >= 0 && bar < notes.Count ? notes[bar] : Array.Empty<BarNote>();
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
    /// views only ask it questions, and tell it whose chords to show (<see cref="ShowChordsOf"/>).
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
        /// <summary>The chords the chart shows, by name, in the order they first appear.</summary>
        public IReadOnlyList<string> ChordNames { get; private set; }
        /// <summary>The chords the chart shows, in time order (<see cref="ShowChordsOf"/>).</summary>
        public IReadOnlyList<ChordEvent> ShownChords => shown;
        public double EndQuarter { get; }
        /// <summary>The tabs the TAB panel can show: the song's own first, then the Guitar Pro file's other tracks written as tab.</summary>
        public IReadOnlyList<ChartTab> Tabs => tabs;

        readonly List<ChartBar> bars = new List<ChartBar>();
        readonly List<SectionSpan> sections = new List<SectionSpan>();
        readonly List<ChartRow> rows = new List<ChartRow>();
        readonly List<ChartTab> tabs = new List<ChartTab>();
        readonly List<int> rowOfBar = new List<int>();
        readonly List<LyricLine> lines;
        readonly List<ChordEvent> chords;
        List<ChordEvent> shown;
        Dictionary<SongTrack, ILookup<int, ChordMarker>> markersByBar;
        Dictionary<SongTrack, bool[]> soundingBars;  // per track and bar: whether it plays there

        public SongChart(Song song)
        {
            Song = song ?? throw new ArgumentNullException(nameof(song));
            chords = song.Chords.OrderBy(c => c.AtQuarter).ToList();
            // the lead lines are the song's lines; backing vocals only add their words to the bars
            lines = song.Lyrics.Where(l => l.Words.Count > 0 && !l.Backing).OrderBy(l => l.AtQuarter).ToList();

            var ends = new List<double> { 0 };
            ends.AddRange(chords.Select(c => c.AtQuarter + c.Quarters));
            ends.AddRange(song.Sections.Select(s => s.AtQuarter + s.Quarters));
            ends.AddRange(song.Lyrics.SelectMany(l => l.Words).Select(w => w.AtQuarter + 1));
            ends.AddRange(song.Riffs.SelectMany(r => r.Notes).Select(n => n.AtQuarter + Math.Max(n.Quarters, 0.25)));
            // and every track of the file, played or not: a song whose guitar stops before the end (Dreams: bass, drums
            // and vocals play bars 113-144 alone) goes on to the last note of any instrument, as the file does
            ends.AddRange(song.Tracks.SelectMany(t => t.Notes).Select(n => n.AtQuarter + Math.Max(n.Quarters, 0.25)));
            ends.AddRange(song.Parts.SelectMany(p => p.Notes).Select(n => n.AtQuarter + Math.Max(n.Quarters, 0.25)));
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
            BuildTabs(song);

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
            ShowChordsOf(tabs[0].Name);
        }

        // ------------------------------------------------------------ the chords shown
        /// <summary>
        /// The chart shows the chords as a track has them, as a Guitar Pro file writes a chord on a beat of each track (the
        /// band goes on playing from the song's chords, <see cref="ChartBar.Chords"/>). A bar takes the chords written in it
        /// on this track, else on the first other track of the file that has any there. In a bar with none, a written chord
        /// goes on while the track that wrote it plays; otherwise the bar takes the song's chords. Each chord lasts until the
        /// next, or until a bar nothing plays in. A track that writes no chords at all (a bass, the drums) shows those of the
        /// song's own track. A song with no written chords shows the song's chords.
        /// </summary>
        public void ShowChordsOf(string trackName)
        {
            shown = ChordsAsWrittenFor(trackName) ?? chords;
            ChordNames = shown.Select(c => c.Name).Distinct().ToList();
            foreach (var bar in bars)
                bar.ShownChords.Clear();
            foreach (var c in shown)
                foreach (var bar in BarsOver(c.AtQuarter, c.AtQuarter + c.Quarters))
                {
                    var from = Math.Max(c.AtQuarter, bar.AtQuarter);
                    var to = Math.Min(c.AtQuarter + c.Quarters, bar.AtQuarter + bar.Quarters);
                    bar.ShownChords.Add(new BarChord(c, from - bar.AtQuarter, to - from, c.AtQuarter < bar.AtQuarter - 1e-6));
                }
        }

        /// <summary>The chord the chart shows at a position (null before the first or in a gap).</summary>
        public ChordEvent ChordAt(double quarter) =>
            shown.LastOrDefault(c => c.AtQuarter <= quarter && quarter < c.AtQuarter + c.Quarters);

        List<ChordEvent> ChordsAsWrittenFor(string trackName)
        {
            var marked = Song.Tracks.Where(t => t.Chords.Count > 0).ToList();
            if (marked.Count == 0)
                return null;
            if (markersByBar == null)
            {
                markersByBar = marked.ToDictionary(t => t, t => t.Chords.Where(m => BarOf(m.AtQuarter) != null).ToLookup(m => BarOf(m.AtQuarter).Index));
                soundingBars = new Dictionary<SongTrack, bool[]>();
                foreach (var t in Song.Tracks)
                {
                    var plays = new bool[bars.Count];
                    var played = t.SameAs != null ? Song.Tracks.FirstOrDefault(o => o.Name == t.SameAs) ?? t : t;  // (a doubling track's notes are its twin's)
                    foreach (var n in played.Notes)
                        foreach (var bar in BarsOver(n.AtQuarter, n.AtQuarter + Math.Max(n.Quarters, 1e-3)))
                            plays[bar.Index] = true;
                    soundingBars[t] = plays;
                }
            }
            var first = marked.FirstOrDefault(t => t.Name == trackName) ?? marked.FirstOrDefault(t => t.Name == tabs[0].Name);
            var order = marked.Where(t => t == first).Concat(marked.Where(t => t != first)).ToList();

            var result = new List<ChordEvent>();
            ChordEvent open = null, openFrom = null;  // the chord going on, and the song's chord it is, if it is one
            SongTrack writer = null;  // the track that wrote the chord going on
            void Close(double at)
            {
                if (open != null)
                    open.Quarters = at - open.AtQuarter;
                open = openFrom = null;
                writer = null;
            }
            void Start(double at, string name, string voicing, ChordEvent from, SongTrack track)
            {
                Close(at);
                open = new ChordEvent { Name = name, AtQuarter = at, Voicing = voicing };
                openFrom = from;
                writer = track;
                result.Add(open);
            }
            foreach (var bar in bars)
            {
                var source = order.FirstOrDefault(t => markersByBar[t][bar.Index].Any());
                if (source != null)
                    foreach (var m in markersByBar[source][bar.Index].OrderBy(m => m.AtQuarter))
                        Start(m.AtQuarter, m.Name, m.Voicing, null, source);
                else if (writer != null && soundingBars[writer][bar.Index])
                    continue;
                else if (bar.Chords.Count > 0)
                {
                    foreach (var c in bar.Chords)
                        if (c.Chord != openFrom)
                            Start(bar.AtQuarter + c.Offset, c.Chord.Name, c.Chord.Voicing, c.Chord, null);
                }
                else if (!soundingBars.Values.Any(plays => plays[bar.Index]))
                    Close(bar.AtQuarter);
            }
            Close(EndQuarter);
            return result.Where(c => c.Quarters > 1e-6).ToList();
        }

        // ------------------------------------------------------------ tabs
        /// <summary>
        /// The song's own tab, then each track of the file that is written as tab, has notes of its own (not the same as
        /// another's) and is not the song's own tab's track, a vocal too (a file writes the melody on a guitar's staff),
        /// and each drum track as drum tab (<see cref="DrumTab"/>).
        /// </summary>
        void BuildTabs(Song song)
        {
            var own = song.Riffs.Count > 0 ? song.Tracks.FirstOrDefault(t => song.Riffs.Any(r => r.Name == t.Name)) : null;
            tabs.Add(new ChartTab(own?.Name ?? "", song.Info.Tuning, true, bars.Select(b => (IReadOnlyList<BarNote>)b.Notes).ToList()));
            foreach (var track in song.Tracks)
            {
                if (track.SameAs == null && track.Kind == "drums" && track.Notes.Count > 0)
                {
                    tabs.Add(DrumTab(track));
                    continue;
                }
                if (track == own || track.SameAs != null || track.Tuning.Count == 0 || !track.Notes.Any(n => n.StringIndex >= 0))
                    continue;
                tabs.Add(new ChartTab(track.Name, track.Tuning, false, PerBar(track, track.Notes.Where(n => n.StringIndex >= 0 && n.StringIndex < track.Tuning.Count)
                                                                                             .Select(n => (n, TabNoteOf(n))))));
            }
        }

        /// <summary>
        /// The indexes (into <see cref="Tabs"/>) of the tabs the TAB panel cycles through: the song's own, and the tab of
        /// each of the named tracks (the ones the band's instruments play), in the order of <see cref="Tabs"/>.
        /// </summary>
        public IReadOnlyList<int> TabsOf(IEnumerable<string> trackNames)
        {
            var names = new HashSet<string>(trackNames);
            var result = new List<int>();
            for (int i = 0; i < tabs.Count; ++i)
                if (i == 0 || (tabs[i].Name != "" && names.Contains(tabs[i].Name)))
                    result.Add(i);
            return result;
        }

        /// <summary>
        /// How much a track plays in each bar, for the track overview: the notes starting in it, or 1 for a bar a note
        /// started before still sounds through; 0 where the track is silent.
        /// </summary>
        public int[] NotesPerBar(SongTrack track)
        {
            var counts = new int[bars.Count];
            foreach (var n in track.Notes)
            {
                var first = BarOf(n.AtQuarter);
                if (first == null)
                    continue;
                ++counts[first.Index];
                for (var b = first.Index + 1; b < bars.Count && bars[b].AtQuarter < n.AtQuarter + n.Quarters - 1e-6; ++b)
                    counts[b] = Math.Max(counts[b], 1);
            }
            return counts;
        }

        /// <summary>A track's tab notes bar by bar, in time order.</summary>
        IReadOnlyList<IReadOnlyList<BarNote>> PerBar(SongTrack track, IEnumerable<(TrackNote track, TabNote tab)> notes)
        {
            var riff = new Riff { Name = track.Name };
            var perBar = bars.Select(_ => new List<BarNote>()).ToList();
            foreach (var (n, note) in notes)
            {
                riff.Notes.Add(note);
                var bar = BarOf(n.AtQuarter);
                if (bar != null)
                    perBar[bar.Index].Add(new BarNote(note, n.AtQuarter - bar.AtQuarter, riff));
            }
            foreach (var list in perBar)
                list.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            return perBar.Select(l => (IReadOnlyList<BarNote>)l).ToList();
        }

        /// <summary>
        /// The drum kit's lines as drum tab has them, top to bottom, with the General MIDI drums on each and their
        /// marks (x a cymbal or a stick on the rim, o a drum or an open hi-hat).
        /// </summary>
        static readonly (string line, (int drum, string mark)[] drums)[] DrumLines =
        {
            ("CC", new[] { (49, "x"), (57, "x"), (52, "x"), (55, "x") }),  // crash, crash 2, china, splash
            ("RC", new[] { (51, "x"), (59, "x"), (53, "b") }),              // ride, ride 2, its bell
            ("HH", new[] { (42, "x"), (46, "o") }),                        // closed, open
            ("HT", new[] { (50, "o"), (48, "o") }),
            ("SD", new[] { (38, "o"), (40, "o"), (37, "x"), (39, "x") }),  // snare, electric snare, side stick, clap
            ("MT", new[] { (47, "o"), (45, "o") }),
            ("FT", new[] { (43, "o"), (41, "o") }),
            ("BD", new[] { (36, "o"), (35, "o") }),
            ("HF", new[] { (44, "x") }),                                   // the hi-hat's pedal
        };

        /// <summary>Percussion of one sound, named on its line above the kit's (any other by its General MIDI number).</summary>
        static readonly Dictionary<int, string> PercussionNames = new Dictionary<int, string>
        {
            { 54, "Tb" }, { 56, "Cb" }, { 69, "Ca" }, { 70, "Ma" }, { 75, "Cl" }, { 82, "Sh" },  // tambourine, cowbell, cabasa, maracas, claves, shaker
        };

        /// <summary>
        /// A drum track as drum tab: a line for each drum the track plays, the kit's in drum tab's order and any
        /// other percussion above them (<see cref="PercussionNames"/>, else its General MIDI number); an accented hit in
        /// capitals, a ghost note in brackets.
        /// </summary>
        ChartTab DrumTab(SongTrack track)
        {
            var kit = DrumLines.SelectMany(l => l.drums.Select(d => (d.drum, l.line, d.mark))).ToDictionary(d => d.drum, d => (d.line, d.mark));
            (string line, string mark) LineOf(int drum) =>
                kit.TryGetValue(drum, out var l) ? l : (PercussionNames.TryGetValue(drum, out var name) ? name : drum.ToString(), "x");
            var order = DrumLines.Select(l => l.line).ToList();
            var lines = track.Notes.Select(n => LineOf(n.Pitch).line).Distinct()
                .OrderByDescending(l => order.IndexOf(l)).ThenByDescending(l => l, StringComparer.Ordinal).ToList();  // lowest first
            var notes = track.Notes.Select(n =>
            {
                var (line, mark) = LineOf(n.Pitch);
                var t = n.Technique ?? "";
                if (t.Contains('a'))
                    mark = mark.ToUpperInvariant();
                if (t.Contains('g'))
                    mark = "(" + mark + ")";
                return (n, new TabNote { StringIndex = lines.IndexOf(line), Fret = 0, AtQuarter = n.AtQuarter, Quarters = n.Quarters, Technique = mark });
            });
            return new ChartTab(track.Name, lines, false, PerBar(track, notes), isDrums: true);
        }

        /// <summary>
        /// A track's note as the tab shows it: "x" for a dead note, a "g" before the symbol for a ghost note (the tab puts
        /// its fret in brackets), and the one technique symbol the tab shows (bend, slide, hammer-on, vibrato).
        /// </summary>
        static TabNote TabNoteOf(TrackNote n)
        {
            var t = n.Technique ?? "";
            var symbol = t.Contains('x') ? "x"
                : (t.Contains('g') ? "g" : "") + (t.Contains('b') ? "b" : t.Contains('s') ? "s" : t.Contains('h') ? "h" : t.Contains('~') ? "~" : "");
            return new TabNote { StringIndex = n.StringIndex, Fret = n.Fret, AtQuarter = n.AtQuarter, Quarters = n.Quarters, Technique = symbol };
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
        /// <summary>The next chord change the chart shows after a position: the first chord after it with a different name.</summary>
        public ChordEvent NextChange(double quarter)
        {
            var now = ChordAt(quarter);
            return shown.FirstOrDefault(c => c.AtQuarter > quarter && (now == null || c.Name != now.Name));
        }

        // ------------------------------------------------------------ strumming
        /// <summary>The strum for a song that has no pattern of its own: 1, 2 &amp;, (3) &amp;, 4 &amp; (D - D U - U D U), the most common one.</summary>
        public static readonly StrumPattern DefaultStrum = new StrumPattern
        {
            Name = "Common strum", Subdivision = 8, Origin = SongOrigin.Default,
            Strokes = { Stroke.Down, Stroke.Rest, Stroke.Down, Stroke.Up, Stroke.Rest, Stroke.Up, Stroke.Down, Stroke.Up },
        };

        /// <summary>
        /// The strumming patterns for a section: those made for it first, then those for the whole song. A song with none has
        /// <see cref="DefaultStrum"/> where the section is in 4/4 (it is a bar of 4/4), and otherwise none.
        /// </summary>
        public IReadOnlyList<StrumPattern> PatternsFor(Section section)
        {
            if (Song.Strumming.Count == 0)
            {
                var meter = (bars.FirstOrDefault(b => b.Section == section) ?? bars.FirstOrDefault())?.Signature;
                return meter != null && meter.Value.Numerator == 4 && meter.Value.Denominator == 4 ? new[] { DefaultStrum } : new StrumPattern[0];
            }
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
        public static IReadOnlyList<string> Counts(StrumPattern pattern) => Counts(pattern, TimeSignature.FourFour);

        /// <summary>The slots of a pattern in a beat of a meter (a quarter in 4/4 and 3/4, an eighth in 7/8, a dotted quarter in 6/8).</summary>
        public static int SlotsPerBeat(StrumPattern pattern, TimeSignature meter)
        {
            var compound = meter.Denominator == 8 && meter.Numerator % 3 == 0 && meter.Numerator > 3;
            var beatQuarters = compound ? 1.5 : 4.0 / meter.Denominator;
            return Math.Max(1, (int)Math.Round(beatQuarters * pattern.Subdivision / 4.0));
        }

        /// <summary>
        /// The meter a section's pattern is shown in: of the meters of the section's bars, the most common one the pattern
        /// fills a whole number of bars of (Redbud Tree's verse starts on a 3/4 bar, but is mostly 4/4, and its pattern is
        /// a 4/4 bar), else the most common; <paramref name="fallback"/> without a section.
        /// </summary>
        public TimeSignature PatternMeter(StrumPattern pattern, Section section, TimeSignature fallback)
        {
            if (section == null || pattern == null || pattern.Subdivision <= 0)
                return fallback;
            var meters = bars.Where(b => b.AtQuarter >= section.AtQuarter - 1e-6 && b.AtQuarter < section.AtQuarter + section.Quarters - 1e-6)
                .GroupBy(b => b.Signature).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
            if (meters.Count == 0)
                return fallback;
            var quarters = pattern.Strokes.Count * 4.0 / pattern.Subdivision;
            foreach (var meter in meters)
            {
                var filled = quarters / meter.QuartersPerBar;
                if (filled >= 1 - 1e-6 && Math.Abs(filled - Math.Round(filled)) < 1e-6)
                    return meter;
            }
            return meters[0];
        }

        /// <summary>The slots of a pattern in a bar of a meter.</summary>
        public static int SlotsPerBar(StrumPattern pattern, TimeSignature meter) =>
            Math.Max(1, (int)Math.Round(meter.QuartersPerBar * pattern.Subdivision / 4.0));

        /// <summary>The counts of a pattern in a meter (beats as the meter counts them; see <see cref="SlotsPerBeat"/>).</summary>
        public static IReadOnlyList<string> Counts(StrumPattern pattern, TimeSignature meter)
        {
            var perBeat = SlotsPerBeat(pattern, meter);
            string[] names = perBeat == 2 ? new[] { "", "&" } : perBeat == 4 ? new[] { "", "", "&", "" } : perBeat == 3 ? new[] { "", "&", "a" }
                : perBeat == 6 ? new[] { "", "", "&", "", "a", "" } : new[] { "" };
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
