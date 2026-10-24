using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using GuitarTutor.Core.Music;
using GuitarTutor.Core.Songs;

namespace GuitarTutor.Core.Import.UltimateGuitar
{
    /// <summary>What an import did and what it could not settle, for the person checking the chart.</summary>
    public sealed class ImportReport
    {
        public List<string> Notes { get; } = new List<string>();
        public void Add(string note) => Notes.Add(note);
    }

    /// <summary>
    /// Builds a song from an Ultimate Guitar chords page, and optionally the song's tabs page (see docs/plan.md,
    /// "Combining sources"):
    /// <list type="bullet">
    /// <item>The chords page gives the skeleton (sections, chords, lyrics), the strumming, the chord shapes and the
    /// tempo; the tabs page gives riffs and fills. The pages are matched line by line on the lyrics, and where their
    /// chords differ the tabs page's are kept as an alternative.</item>
    /// <item>Neither page has chord lengths. The first guess: the chords of a line get a bar each, and the last chord
    /// stretches the line to a whole number of bars (1, 2, 4, 8: Bm A E becomes 1 + 1 + 2). Where the tabs page marks
    /// a section's time in the recording, the guess is checked against it and scaled if it is clearly off.</item>
    /// </list>
    /// </summary>
    public static class UgImporter
    {
        public static Song Import(UgPage chords, UgPage tabs, out ImportReport report)
        {
            report = new ImportReport();
            var song = new Song();
            song.Info.Title = chords.Title;
            song.Info.Artist = chords.Artist;
            song.Info.Key = chords.Key;
            song.Info.Capo = chords.Capo;
            song.Info.Tuning = Tuning(chords.Tuning, report);

            var strum = chords.Strummings.FirstOrDefault(s => s.Bpm > 0);
            var bpm = strum?.Bpm ?? 0;
            if (bpm <= 0)
            {
                bpm = 100;
                report.Add("No tempo on the page: guessed 100 BPM.");
            }
            var signature = strum != null ? Meter(strum.Measures.Count, strum.Denominator) : TimeSignature.FourFour;
            song.Tempo = new TempoMap(bpm, signature);
            var bar = signature.QuartersPerBar;

            var sheet = ChordSheet.Parse(chords.Content);
            var tabSheet = tabs != null ? ChordSheet.Parse(tabs.Content) : null;

            // timing: the first guess, then checked against the recording's time marks in the tabs page
            var scale = 1.0;
            var placed = Place(sheet, bar * scale);
            if (tabSheet != null)
                scale = Calibrate(placed, tabSheet, song.Tempo, report);
            if (scale != 1.0)
                placed = Place(sheet, bar * scale);

            foreach (var section in placed.Sections)
                song.Sections.Add(section);
            foreach (var line in placed.Lines)
            {
                song.Chords.AddRange(line.Chords);
                if (line.Lyric != null)
                    song.Lyrics.Add(line.Lyric);
                if (line.Backing != null)
                    song.Lyrics.Add(line.Backing);
            }
            report.Add($"{song.Sections.Count} sections, {song.Chords.Count} chords, {song.Lyrics.Count} lyric lines, "
                       + $"{song.Tempo.BarAt(placed.End).bar} bars at {bpm.ToString(CultureInfo.InvariantCulture)} BPM in {signature} "
                       + $"({Clock(song.Tempo.SecondsAt(placed.End))}).");

            Voicings(chords, song, report);
            Strumming(chords, song, report);
            song.Sources.Add(new Source
            {
                Kind = "ultimate-guitar-chords", Url = chords.Url, Revision = chords.Revision,
                Supplies = { "sections", "chords", "lyrics", "strumming", "voicings", "tempo" },
            });

            if (tabSheet != null)
            {
                Merge(tabSheet, placed, song, report);
                song.Sources.Add(new Source
                {
                    Kind = "ultimate-guitar-tabs", Url = tabs.Url, Revision = tabs.Revision, Supplies = { "riffs", "timing check" },
                });
            }
            return song;
        }

        // ------------------------------------------------------------ timing
        sealed class PlacedLine
        {
            public string Section = "";
            public ChordLine Source;
            public List<ChordEvent> Chords = new List<ChordEvent>();
            public LyricLine Lyric;
            public LyricLine Backing;  // the line's words in parentheses
            public double At, End;
            public string Key = "";  // the lyric, normalised for matching
        }

        sealed class Placement
        {
            public List<Section> Sections = new List<Section>();
            public List<PlacedLine> Lines = new List<PlacedLine>();
            public double End;
        }

        static int WholeBars(int chords)
        {
            int n = 1;
            while (n < chords)
                n *= 2;
            return n;
        }

        static Placement Place(ChordSheet sheet, double slot)
        {
            var p = new Placement();
            double at = 0;
            var lastWord = double.MinValue;  // lines are sung one after the other: none starts before the last ends
            foreach (var section in sheet.Sections)
            {
                var start = at;
                foreach (var line in section.Lines.Where(l => l.Chords != null))
                {
                    var c = line.Chords;
                    for (int r = 0; r < c.Repeat; ++r)
                    {
                        var placedLine = new PlacedLine { Section = section.Name, Source = c, At = at };
                        var slots = WholeBars(c.Chords.Count);
                        for (int i = 0; i < c.Chords.Count; ++i)
                        {
                            var length = (i < c.Chords.Count - 1 ? 1 : slots - (c.Chords.Count - 1)) * slot;
                            placedLine.Chords.Add(new ChordEvent { Name = c.Chords[i].Name, AtQuarter = at, Quarters = length });
                            at += length;
                        }
                        placedLine.End = at;
                        if (r == 0 && !string.IsNullOrWhiteSpace(c.Lyric))
                        {
                            (placedLine.Lyric, placedLine.Backing) = Lyric(c, placedLine, lastWord);
                            placedLine.Key = Normalise(c.Lyric);
                            foreach (var l in new[] { placedLine.Lyric, placedLine.Backing })
                                if (l != null && l.Words.Count > 0)
                                    lastWord = Math.Max(lastWord, l.Words[l.Words.Count - 1].AtQuarter);
                        }
                        p.Lines.Add(placedLine);
                    }
                }
                if (at > start)
                    p.Sections.Add(new Section { Name = section.Name, AtQuarter = start, Quarters = at - start });
            }
            p.End = at;
            return p;
        }

        /// <summary>
        /// The fastest the words before a line's first chord (a pickup) or after its last are sung: a quarter note
        /// for every five characters. Chords can be written close together over a bar; their spacing would put
        /// such words far ahead of the line or far after it.
        /// </summary>
        const double SpeechPace = 0.2;

        /// <summary>
        /// Words placed between the chords above them, in proportion to their columns, and never before
        /// <paramref name="after"/> (the last word of the line before). Words in parentheses are backing vocals:
        /// a line of their own, at the places they are written.
        /// </summary>
        static (LyricLine main, LyricLine backing) Lyric(ChordLine c, PlacedLine placed, double after)
        {
            var anchors = c.Chords.Select((ch, i) => (column: (double)ch.Column, at: placed.Chords[i].AtQuarter)).ToList();
            var main = new LyricLine();
            var backing = new LyricLine { Backing = true };
            var words = Regex.Matches(c.Lyric, @"\S+").Cast<Match>().ToList();
            double rateDefault = (placed.End - placed.At) / Math.Max(1, c.Lyric.Length);
            double previous = after + 0.25;
            var inParentheses = false;
            foreach (var w in words)
            {
                double q;
                var col = (double)w.Index;
                int k = anchors.FindLastIndex(a => a.column <= col);
                if (k < 0)  // before the first chord: a pickup, sung just ahead of the line (at most a bar ahead)
                {
                    var rate = Math.Min(anchors.Count > 1 ? Rate(anchors[0], anchors[1], rateDefault) : rateDefault, SpeechPace);
                    q = Math.Max(anchors[0].at - (anchors[0].column - col) * rate, anchors[0].at - 4);
                }
                else if (k < anchors.Count - 1)
                {
                    q = anchors[k].at + (col - anchors[k].column) * Rate(anchors[k], anchors[k + 1], rateDefault);
                }
                else  // after the last chord: at the pace of the line so far
                {
                    var rate = Math.Min(anchors.Count > 1 ? Rate(anchors[k - 1], anchors[k], rateDefault) : rateDefault, SpeechPace);
                    q = Math.Min(anchors[k].at + (col - anchors[k].column) * rate, placed.End - 0.25);
                }
                q = Math.Max(Math.Max(q, previous), 0);
                previous = q;
                var word = new LyricWord { Text = w.Value, AtQuarter = Math.Round(q * 4) / 4 };  // to sixteenth notes
                if (w.Value.StartsWith("("))
                    inParentheses = true;
                (inParentheses ? backing : main).Words.Add(word);
                if (w.Value.EndsWith(")") || w.Value.EndsWith("),") || w.Value.EndsWith(")."))
                    inParentheses = false;
            }
            main.AtQuarter = main.Words.Count > 0 ? main.Words[0].AtQuarter : placed.At;
            backing.AtQuarter = backing.Words.Count > 0 ? backing.Words[0].AtQuarter : placed.At;
            return (main.Words.Count > 0 ? main : null, backing.Words.Count > 0 ? backing : null);
        }

        static double Rate((double column, double at) a, (double column, double at) b, double fallback) =>
            b.column > a.column ? (b.at - a.at) / (b.column - a.column) : fallback;

        /// <summary>
        /// The time marks in the tabs page ("00:00-00:17") give some sections' length in the recording. Returns the
        /// factor the guessed chord lengths need (1 if they fit, or 0.25/0.5/2/4 if clearly off).
        /// </summary>
        static double Calibrate(Placement placed, ChordSheet tabs, TempoMap tempo, ImportReport report)
        {
            var factors = new List<double>();
            foreach (var section in tabs.Sections)
            {
                var times = section.Lines.Where(l => l.Tab?.Time != null).Select(l => l.Tab.Time.Value).ToList();
                if (times.Count == 0)
                    continue;
                var ours = placed.Sections.FirstOrDefault(s => SameSection(s.Name, section.Name));
                if (ours == null)
                {
                    report.Add($"Tabs page marks '{section.Name}' in the recording, but the chords page has no such section.");
                    continue;
                }
                var recorded = times.Max(t => t.end) - times.Min(t => t.start);
                var guessed = tempo.SecondsAt(ours.AtQuarter + ours.Quarters) - tempo.SecondsAt(ours.AtQuarter);
                var ratio = recorded / guessed;
                var snapped = new[] { 0.25, 0.5, 1, 2, 4 }.OrderBy(f => Math.Abs(Math.Log(ratio / f))).First();
                var fits = Math.Abs(ratio / snapped - 1) < 0.2;
                report.Add($"Timing check, {ours.Name}: guessed {Clock(guessed)}, the recording has {Clock(times.Min(t => t.start))}-"
                           + $"{Clock(times.Max(t => t.end))} ({Clock(recorded)})"
                           + (fits ? (snapped == 1 ? ": fits." : $": chord lengths x{snapped.ToString(CultureInfo.InvariantCulture)}.")
                                   : ": does not fit any simple factor; kept the guess."));
                if (fits)
                    factors.Add(snapped);
            }
            if (factors.Count == 0)
                return 1;
            var factor = factors.GroupBy(f => f).OrderByDescending(g => g.Count()).First().Key;
            return factor;
        }

        static bool SameSection(string a, string b) => Normalise(a) == Normalise(b);

        static string Normalise(string text) => Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", "");

        // ------------------------------------------------------------ the tabs page: alternatives and riffs
        static void Merge(ChordSheet tabs, Placement placed, Song song, ImportReport report)
        {
            int cursor = 0, matched = 0, unmatched = 0, differ = 0, fills = 0, timed = 0, spread = 0, unplaced = 0;
            var where = new List<string>();
            var riffCount = 0;
            var seen = new Dictionary<string, int>();  // how many sections of a name so far (Chorus 1, Chorus 2...)
            foreach (var section in tabs.Sections)
            {
                var name = Normalise(section.Name);
                seen[name] = seen.TryGetValue(name, out var n) ? n + 1 : 0;
                var ours = SectionLike(placed, section.Name, seen[name]);
                PlacedLine current = null;
                var fillsInLine = new List<TabBlock>();
                var loose = new List<TabBlock>();
                void Flush()
                {
                    if (current == null || fillsInLine.Count == 0)
                        return;
                    var last = current.Chords.Last();  // fills sit in the line's last chord, one after the other
                    var span = last.Quarters / fillsInLine.Count;
                    for (int i = 0; i < fillsInLine.Count; ++i)
                        song.Riffs.Add(Riff($"Fill {++riffCount} ({section.Name})", fillsInLine[i], last.AtQuarter + i * span, span));
                    fills += fillsInLine.Count;
                    fillsInLine.Clear();
                }

                foreach (var line in section.Lines)
                {
                    if (line.Chords != null)
                    {
                        Flush();
                        current = null;
                        if (string.IsNullOrWhiteSpace(line.Chords.Lyric))
                            continue;
                        var at = Find(placed, Normalise(line.Chords.Lyric), cursor);
                        if (at < 0)
                        {
                            ++unmatched;
                            where.Add($"lyric line {section.Lines.IndexOf(line) + 1} of '{section.Name}' not in the chords page");
                            continue;
                        }
                        ++matched;
                        cursor = at + 1;
                        current = placed.Lines[at];
                        var theirs = line.Chords.Chords.Select(c => c.Name).ToList();
                        var ourChords = current.Chords.Select(c => c.Name).ToList();
                        if (!theirs.SequenceEqual(ourChords))
                        {
                            ++differ;
                            song.Alternatives.Add(new ChordAlternative
                            {
                                AtQuarter = current.At, Quarters = current.End - current.At, Chords = theirs, Source = "ultimate-guitar-tabs",
                            });
                        }
                    }
                    else if (line.Tab != null)
                    {
                        if (line.Tab.Time != null)
                        {
                            var (start, end) = line.Tab.Time.Value;
                            var q0 = song.Tempo.QuarterAt(start);
                            var q1 = song.Tempo.QuarterAt(end);
                            song.Riffs.Add(Riff($"{section.Name} {Clock(start)}-{Clock(end)}", line.Tab, q0, q1 - q0));
                            ++timed;
                        }
                        else if (current != null)
                            fillsInLine.Add(line.Tab);
                        else
                            loose.Add(line.Tab);
                    }
                }
                Flush();

                // a section of tab only (a solo): its blocks one after the other over the section, by their widths
                if (loose.Count > 0)
                {
                    if (ours != null && section.Lines.All(l => l.Chords == null))
                    {
                        var total = (double)loose.Sum(t => Math.Max(1, t.Columns) * t.Repeat);
                        var at = ours.AtQuarter;
                        foreach (var t in loose)
                        {
                            var quarters = ours.Quarters * Math.Max(1, t.Columns) * t.Repeat / total;
                            song.Riffs.Add(Riff($"{section.Name} {++riffCount}", t, at, quarters));
                            at += quarters;
                        }
                        spread += loose.Count;
                    }
                    else
                    {
                        unplaced += loose.Count;
                        where.Add($"{loose.Count} tab block(s) in '{section.Name}' have no time mark and no line to sit in");
                    }
                }
            }
            report.Add($"Tabs page: {matched} lyric lines matched, {unmatched} not found in the chords page, {differ} with other chords "
                       + $"(kept as alternatives); riffs: {timed} placed by their time marks, {fills} fills placed in their lines, "
                       + $"{spread} spread over their section" + (unplaced > 0 ? $", {unplaced} could not be placed." : "."));
            foreach (var w in where)
                report.Add("  " + w);
        }

        /// <summary>The placed line with this lyric: a close match soon after the last one, else a near-exact one anywhere.</summary>
        static int Find(Placement placed, string key, int cursor)
        {
            int best = -1;
            double score = 0;
            for (int i = cursor; i < Math.Min(placed.Lines.Count, cursor + 8); ++i)
            {
                var s = Similarity(key, placed.Lines[i].Key);
                if (s > score)
                    (best, score) = (i, s);
            }
            if (score >= 0.7)
                return best;
            (best, score) = (-1, 0);
            for (int i = 0; i < placed.Lines.Count; ++i)
            {
                var s = Similarity(key, placed.Lines[i].Key);
                if (s > score)
                    (best, score) = (i, s);
            }
            return score >= 0.85 ? best : -1;
        }

        /// <summary>How alike two normalised texts are: shared letter pairs (Dice coefficient), 0..1.</summary>
        static double Similarity(string a, string b)
        {
            if (a.Length < 2 || b.Length < 2)
                return a == b ? 1 : 0;
            var pairs = new Dictionary<string, int>();
            for (int i = 0; i < a.Length - 1; ++i)
            {
                var p = a.Substring(i, 2);
                pairs[p] = pairs.TryGetValue(p, out var c) ? c + 1 : 1;
            }
            int common = 0;
            for (int i = 0; i < b.Length - 1; ++i)
            {
                var p = b.Substring(i, 2);
                if (pairs.TryGetValue(p, out var c) && c > 0)
                {
                    ++common;
                    pairs[p] = c - 1;
                }
            }
            return 2.0 * common / (a.Length - 1 + b.Length - 1);
        }

        /// <summary>The k-th section of this name, or one whose name contains it ("Chorus/Outro" for "Outro").</summary>
        static Section SectionLike(Placement placed, string name, int occurrence)
        {
            var key = Normalise(name);
            var same = placed.Sections.Where(s => Normalise(s.Name) == key).ToList();
            if (same.Count > occurrence)
                return same[occurrence];
            return placed.Sections.LastOrDefault(s => key.Length > 0 && Normalise(s.Name).Contains(key));
        }

        static readonly Regex Fret = new Regex(@"\d{1,2}");

        /// <summary>A tab block's notes spread over a span: columns in proportion, played `Repeat` times.</summary>
        static Riff Riff(string name, TabBlock block, double at, double quarters)
        {
            var riff = new Riff { Name = name };
            var columns = Math.Max(1, block.Columns);
            var strings = block.Strings.Count;
            var pass = quarters / Math.Max(1, block.Repeat);
            for (int r = 0; r < block.Repeat; ++r)
                for (int s = 0; s < strings; ++s)
                {
                    var text = block.Strings[s];
                    var notes = Fret.Matches(text).Cast<Match>().Where(m => int.Parse(m.Value) <= 24).ToList();
                    for (int n = 0; n < notes.Count; ++n)
                    {
                        var m = notes[n];
                        var next = n + 1 < notes.Count ? notes[n + 1].Index : columns;
                        var after = m.Index + m.Length < text.Length ? text[m.Index + m.Length] : '-';
                        riff.Notes.Add(new TabNote
                        {
                            StringIndex = strings - 1 - s,  // the top line is the highest string
                            Fret = int.Parse(m.Value),
                            AtQuarter = at + r * pass + m.Index * pass / columns,
                            Quarters = (next - m.Index) * pass / columns,
                            Technique = "bhprs~/\\".IndexOf(after) >= 0 ? after.ToString() : "",
                        });
                    }
                }
            riff.Notes.Sort((a, b) => a.AtQuarter.CompareTo(b.AtQuarter));
            return riff;
        }

        // ------------------------------------------------------------ shapes, strumming, tuning
        static void Voicings(UgPage page, Song song, ImportReport report)
        {
            var missing = new List<string>();
            foreach (var name in song.Chords.Select(c => c.Name).Distinct())
            {
                if (!page.Shapes.TryGetValue(name, out var shapes) || shapes.Count == 0)
                {
                    missing.Add(name);
                    continue;
                }
                var shape = shapes[0];
                var count = shape.Frets.Count;
                var voicing = new Voicing
                {
                    Id = $"{name}-{shape.Id}",
                    Chord = name,
                    Frets = Enumerable.Reverse(shape.Frets).ToList(),     // UG lists the high E first
                    Fingers = Enumerable.Reverse(shape.Fingers).ToList(),
                };
                if (shape.Barres.Count > 0)
                {
                    var b = shape.Barres[0];
                    var lo = count - 1 - Math.Max(b.StartString, b.LastString);
                    var hi = count - 1 - Math.Min(b.StartString, b.LastString);
                    voicing.Barre = new Barre { Fret = b.Fret, Finger = b.Finger, FirstString = lo, LastString = hi };
                }
                song.Voicings.Add(voicing);
                foreach (var c in song.Chords.Where(c => c.Name == name))
                    c.Voicing = voicing.Id;
            }
            if (missing.Count > 0)
                report.Add("No chord shapes on the page for: " + string.Join(", ", missing));
        }

        static void Strumming(UgPage page, Song song, ImportReport report)
        {
            var unknown = new SortedSet<int>();
            foreach (var s in page.Strummings)
            {
                var pattern = new StrumPattern { Name = s.Part, Subdivision = s.Denominator };
                foreach (var code in s.Measures)
                {
                    // UG's stroke codes: 1 down, 101 up, 202 no stroke; 3 and 203 come in down / up pairs among the
                    // others (Heart of Gold's intro: a muted chuck on each eighth), so: muted down, muted up
                    switch (code)
                    {
                        case 1: pattern.Strokes.Add(Stroke.Down); break;
                        case 101: pattern.Strokes.Add(Stroke.Up); break;
                        case 202: pattern.Strokes.Add(Stroke.Rest); break;
                        case 3: pattern.Strokes.Add(Stroke.MutedDown); break;
                        case 203: pattern.Strokes.Add(Stroke.MutedUp); break;
                        default: pattern.Strokes.Add(Stroke.Rest); unknown.Add(code); break;
                    }
                }
                var section = song.Sections.FirstOrDefault(x => SameSection(x.Name, s.Part));
                if (section != null)
                    pattern.Sections.Add(section.Name);
                song.Strumming.Add(pattern);
            }
            if (unknown.Count > 0)
                report.Add("Unknown strumming codes (taken as no stroke): " + string.Join(", ", unknown));
        }

        static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        static readonly Dictionary<string, int> Flats = new Dictionary<string, int> { ["Db"] = 1, ["Eb"] = 3, ["Gb"] = 6, ["Ab"] = 8, ["Bb"] = 10 };

        /// <summary>"E A D G B E" as pitches (E2 A2 D3 G3 B3 E4): each string at the octave nearest standard tuning.</summary>
        static List<string> Tuning(string text, ImportReport report)
        {
            int[] standard = { 40, 45, 50, 55, 59, 64 };
            var names = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (names.Length != 6)
            {
                report.Add($"Tuning '{text}' is not six strings: kept standard tuning.");
                return new List<string> { "E2", "A2", "D3", "G3", "B3", "E4" };
            }
            var result = new List<string>();
            for (int i = 0; i < 6; ++i)
            {
                var n = names[i];
                var pc = Array.IndexOf(Names, n);
                if (pc < 0 && !Flats.TryGetValue(n, out pc))
                {
                    report.Add($"Unknown note '{n}' in the tuning: kept standard tuning.");
                    return new List<string> { "E2", "A2", "D3", "G3", "B3", "E4" };
                }
                var midi = standard[i] - ((standard[i] - pc) % 12 + 12) % 12;  // at or below the standard string
                if (standard[i] - midi > 6)
                    midi += 12;  // nearer above
                result.Add(Names[midi % 12] + (midi / 12 - 1));
            }
            return result;
        }

        static TimeSignature Meter(int strokes, int denominator)
        {
            if (strokes <= 0 || denominator <= 0 || strokes == denominator)
                return TimeSignature.FourFour;
            // a pattern of whole 4/4 bars (32 sixteenths: two bars) is not one long bar; it is played over its bars
            var quarters = strokes * 4.0 / denominator;
            if (quarters >= 8 && Math.Abs(quarters % 4) < 1e-9)
                return TimeSignature.FourFour;
            return new TimeSignature(strokes, denominator);
        }

        static string Clock(double seconds)
        {
            var s = (int)Math.Round(seconds);
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
