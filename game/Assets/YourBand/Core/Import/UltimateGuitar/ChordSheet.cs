using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using YourBand.Core.Music;

namespace YourBand.Core.Import.UltimateGuitar
{
    /// <summary>
    /// A chord sheet as text, split into sections and lines: chord lines (chords at columns, the lyric line under
    /// them, a repeat count such as "x4"), tab blocks (six string lines, maybe a time span from the recording),
    /// and other text. Works on both page kinds: the chords page tags chords ([ch]Bm[/ch]), the tabs page writes
    /// them as plain text.
    /// </summary>
    public sealed class ChordSheet
    {
        public List<SheetSection> Sections { get; } = new List<SheetSection>();
        /// <summary>The shapes a list of the song's chords gives ("C  x32010"): per chord, the frets from the lowest string, -1 not played.</summary>
        public Dictionary<string, int[]> Shapes { get; } = new Dictionary<string, int[]>();

        static readonly Regex ListedShape = new Regex(@"^\s*(\S+)\s+([xX\d]{6})\s*$");
        static readonly Regex Header = new Regex(@"^\s*\[([A-Za-z][^\]\[]*)\]\s*$");
        static readonly Regex ChordTag = new Regex(@"\[ch\](.*?)\[/ch\]");
        static readonly Regex TabString = new Regex(@"^\s*([eBGDAEbgda])\s*\|(.*)$");
        static readonly Regex Repeat = new Regex(@"(?:^|\s|\()[xX×]\s*(\d{1,2})\)?(?:\s|$)|(?:^|\s)(\d{1,2})\s*[xX×](?:\s|$)");
        /// <summary>A chord's frets as a list of chords used writes them: C x32010, Em 022000.</summary>
        static readonly Regex FretList = new Regex(@"^[xX\d]{4,}$");
        static readonly Regex TimeSpan = new Regex(@"(\d{1,2}):(\d{2})\s*-\s*(\d{1,2}):(\d{2})");

        public static ChordSheet Parse(string content)
        {
            var sheet = new ChordSheet();
            var section = new SheetSection { Name = "" };
            sheet.Sections.Add(section);
            var lines = content.Replace("\r", "").Split('\n').Select(l => l.Replace("[tab]", "").Replace("[/tab]", "")).ToList();
            (double start, double end)? pendingTime = null;

            for (int i = 0; i < lines.Count; ++i)
            {
                var raw = lines[i];
                if (raw.Trim().Length == 0)
                    continue;

                var header = Header.Match(raw);
                if (header.Success && !raw.Contains("[ch]"))
                {
                    section = new SheetSection { Name = header.Groups[1].Value.Trim() };
                    sheet.Sections.Add(section);
                    continue;
                }

                var listed = ListedShape.Match(ChordTag.Replace(raw, "$1"));
                if (listed.Success && ChordName.IsChord(listed.Groups[1].Value))
                {
                    if (!sheet.Shapes.ContainsKey(listed.Groups[1].Value))
                        sheet.Shapes.Add(listed.Groups[1].Value, listed.Groups[2].Value.Select(c => char.IsDigit(c) ? c - '0' : -1).ToArray());
                    continue;
                }

                if (TabString.IsMatch(raw))
                {
                    var block = new TabBlock { Time = pendingTime };
                    pendingTime = null;
                    var firstColumn = TabString.Match(raw).Groups[2].Index;
                    while (i < lines.Count && TabString.IsMatch(lines[i]))
                        block.Strings.Add(TabString.Match(lines[i++]).Groups[2].Value);
                    --i;
                    var tail = block.Strings.Select(s => s.Substring(s.LastIndexOf('|') + 1)).FirstOrDefault(t => Repeat.IsMatch(t));
                    if (tail != null)
                        block.Repeat = RepeatCount(tail);
                    for (int s = 0; s < block.Strings.Count; ++s)  // the playing part: up to the last bar line
                    {
                        var last = block.Strings[s].LastIndexOf('|');
                        if (last >= 0)
                            block.Strings[s] = block.Strings[s].Substring(0, last);
                    }
                    block.FirstColumn = firstColumn;
                    for (int c = 0; c < block.Strings[0].Length; ++c)
                        if (block.Strings[0][c] == '|')
                            block.BarColumns.Add(firstColumn + c);
                    section.Lines.Add(new SheetLine { Tab = block });
                    continue;
                }

                var chords = ChordLine(raw);
                if (chords != null)
                {
                    // the next line is its lyric, unless it is another chord line, a header or a tab
                    if (i + 1 < lines.Count && lines[i + 1].Trim().Length > 0 && ChordLine(lines[i + 1]) == null
                        && !Header.IsMatch(lines[i + 1]) && !TabString.IsMatch(lines[i + 1]) && !TimeSpan.IsMatch(lines[i + 1]))
                        chords.Lyric = lines[++i].TrimEnd();
                    section.Lines.Add(new SheetLine { Chords = chords });
                    continue;
                }

                var time = TimeSpan.Match(raw);
                if (time.Success)
                {
                    pendingTime = (Seconds(time.Groups[1].Value, time.Groups[2].Value), Seconds(time.Groups[3].Value, time.Groups[4].Value));
                    section.Lines.Add(new SheetLine { Text = raw.Trim(), Time = pendingTime });
                    continue;
                }
                section.Lines.Add(new SheetLine { Text = raw.TrimEnd() });
            }
            sheet.Sections.RemoveAll(s => s.Name == "" && s.Lines.Count == 0);
            return sheet;
        }

        /// <summary>The chords of a line and their columns, or null if the line is not (only) chords.</summary>
        public static ChordLine ChordLine(string raw)
        {
            var line = new ChordLine();
            string rest;
            if (raw.Contains("[ch]"))
            {
                // the columns are those of the text as shown, without the tags
                var plain = new StringBuilder();
                int at = 0;
                foreach (Match m in ChordTag.Matches(raw))
                {
                    plain.Append(raw, at, m.Index - at);
                    line.Chords.Add(new SheetChord { Name = m.Groups[1].Value.Trim(), Column = plain.Length, Bar = Bars(plain.ToString()) });
                    plain.Append(m.Groups[1].Value);
                    at = m.Index + m.Length;
                }
                plain.Append(raw.Substring(at));
                rest = ChordTag.Replace(raw, " ");
            }
            else
            {
                foreach (Match m in Regex.Matches(raw, @"\S+"))
                    if (ChordName.IsChord(m.Value))
                        line.Chords.Add(new SheetChord { Name = m.Value, Column = m.Index, Bar = Bars(raw.Substring(0, m.Index)) });
                rest = Regex.Replace(raw, @"\S+", m => ChordName.IsChord(m.Value) ? " " : m.Value);
            }
            if (line.Chords.Count == 0)
                return null;
            line.Barred = raw.Contains('|');
            var leftover = rest.Replace('|', ' ').Trim();
            if (leftover.Length > 0)
            {
                // "C  x32010": a chord's shape in a list of the song's chords, not a chord played 32010 times
                if (FretList.IsMatch(leftover))
                    return null;
                if (!Repeat.IsMatch(" " + leftover + " ") || Regex.Replace(leftover, @"[xX×\d()\s]", "").Length > 0)
                    return raw.Contains("[ch]") ? WithLyricInline(line, leftover) : null;  // words: a lyric line, not chords
                line.Repeat = RepeatCount(leftover);
            }
            return line;
        }

        static int Bars(string before) => before.Count(c => c == '|');

        // a tagged chord line with text beside the chords (rare): keep the chords, the text is a note
        static ChordLine WithLyricInline(ChordLine line, string text)
        {
            line.Note = text;
            return line;
        }

        static int RepeatCount(string text)
        {
            var m = Repeat.Match(" " + text.Trim() + " ");
            if (!m.Success)
                return 1;
            var n = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return int.TryParse(n, out var count) && count > 0 ? count : 1;
        }

        static double Seconds(string minutes, string seconds) => int.Parse(minutes) * 60 + int.Parse(seconds);
    }

    public sealed class SheetSection
    {
        public string Name = "";
        public List<SheetLine> Lines = new List<SheetLine>();
    }

    /// <summary>One element of a section: chords (with their lyric), a tab block, or other text.</summary>
    public sealed class SheetLine
    {
        public ChordLine Chords;
        public TabBlock Tab;
        public string Text;
        public (double start, double end)? Time;
    }

    public sealed class ChordLine
    {
        public List<SheetChord> Chords = new List<SheetChord>();
        public string Lyric;
        public int Repeat = 1;
        public string Note;
        /// <summary>The line has bar lines (| Em C | D G |): the chords between two of them share a bar.</summary>
        public bool Barred;
    }

    public sealed class SheetChord
    {
        public string Name = "";
        public int Column;
        public int Bar;  // how many bar lines come before it in its line
    }

    /// <summary>Six string lines of ASCII tab, the high E first; optionally a time span in the recording.</summary>
    public sealed class TabBlock
    {
        public List<string> Strings = new List<string>();
        public int Repeat = 1;
        public (double start, double end)? Time;
        /// <summary>The columns of the page where a bar line divides the tab (none: it is one stretch, of any length).</summary>
        public List<int> BarColumns = new List<int>();
        /// <summary>The column of the page where the strings' text starts.</summary>
        public int FirstColumn;

        public int Columns => Strings.Count == 0 ? 0 : Strings.Max(s => s.Length);
    }
}
