using System;
using System.Collections.Generic;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Songs;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuitarTutor.Runtime.UI
{
    /// <summary>
    /// The tab as a tape running under a fixed play marker, a quarter of the way in: the strings as written (the
    /// highest on top), the fret numbers at their positions, bar lines and riff names, all moving smoothly from
    /// right to left with the music, at a fixed distance per beat. It shows one of the chart's tabs: the song's own,
    /// or another track's (<see cref="SetTab"/>), with that track's strings.
    /// </summary>
    public sealed class TabTape : VisualElement
    {
        public const float PixelsPerQuarter = 110, MarkerAt = 0.25f;
        const float Gutter = 34, Top = 26, LineGap = 21;
        const int MinLines = 6;  // the tape is as tall as six strings at least; fewer (a bass's four) sit in the middle

        readonly VisualElement tape, names, marker;
        readonly Label empty;
        readonly List<double> noteTimes = new List<double>();  // the notes on the tape, in time order
        SongChart chart;
        ChartTab shown;
        int strings = MinLines;
        int builtFrom = -1, builtTo = -1;
        double tapeStart;  // the position at the tape's left edge

        public TabTape()
        {
            AddToClassList("gt-tape");
            style.height = Top + LineGap * (MinLines - 1) + 12;
            generateVisualContent += DrawStrings;
            tape = ChartLook.Box("gt-tape__tape", this);
            tape.generateVisualContent += DrawBars;
            marker = ChartLook.Box("gt-tape__marker", this);
            marker.style.left = Length.Percent(MarkerAt * 100);
            names = ChartLook.Box("gt-tape__names", this);
            names.style.width = Gutter;
            empty = ChartLook.Label("No tab here", "gt-tape__empty", this);
            pickingMode = PickingMode.Ignore;
        }

        float LineY(int stringIndex) => Top + (Math.Max(MinLines, strings) - strings) * LineGap / 2f + (strings - 1 - stringIndex) * LineGap;

        /// <summary>A new song: its own tab shown.</summary>
        public void SetSong(SongChart songChart)
        {
            chart = songChart;
            SetTab(chart.Tabs[0]);
        }

        /// <summary>Shows one of the chart's tabs (<see cref="SongChart.Tabs"/>), on its own strings.</summary>
        public void SetTab(ChartTab tab)
        {
            shown = tab;
            strings = Math.Max(1, tab.Tuning.Count);
            style.height = Top + LineGap * (Math.Max(MinLines, strings) - 1) + 12;
            builtFrom = builtTo = -1;
            tape.Clear();
            noteTimes.Clear();
            names.Clear();
            for (int s = 0; s < strings; ++s)
            {
                var name = tab.Tuning[s].TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-');
                var label = ChartLook.Label(s == strings - 1 && strings == 6 ? name.ToLowerInvariant() : name, "gt-tape__string", names);
                label.style.top = LineY(s) - 10;
            }
            MarkDirtyRepaint();
        }

        /// <summary>Moves the tape to a position (quarter notes; negative during a count-in from the start).</summary>
        public void Show(double quarter)
        {
            var width = layout.width;
            if (chart == null || shown == null || chart.Bars.Count == 0 || float.IsNaN(width) || width <= 0)
                return;
            var markerX = width * MarkerAt;
            var from = quarter - markerX / PixelsPerQuarter;
            var to = quarter + (width - markerX) / PixelsPerQuarter;
            var firstBar = chart.BarOf(Math.Max(0, Math.Min(from, chart.EndQuarter - 1e-6)))?.Index ?? 0;
            var lastBar = chart.BarOf(Math.Max(0, Math.Min(to, chart.EndQuarter - 1e-6)))?.Index ?? chart.Bars.Count - 1;
            if (firstBar < builtFrom || lastBar > builtTo)
                Build(Math.Max(0, firstBar - 2), Math.Min(chart.Bars.Count - 1, lastBar + 6));
            tape.style.translate = new Translate((float)(markerX - (quarter - tapeStart) * PixelsPerQuarter), 0);

            // anything to read in view?
            var i = noteTimes.BinarySearch(from);
            if (i < 0)
                i = ~i;
            empty.EnableInClassList("gt-hidden", i < noteTimes.Count && noteTimes[i] <= to);
        }

        void Build(int from, int to)
        {
            builtFrom = from;
            builtTo = to;
            tapeStart = chart.Bars[from].AtQuarter;
            var end = chart.Bars[to].AtQuarter + chart.Bars[to].Quarters;
            tape.Clear();
            noteTimes.Clear();
            tape.style.width = (float)((end - tapeStart) * PixelsPerQuarter);
            // riffs that overlap (the same fill placed twice) show their notes once, and a name only where
            // there is room for it
            var drawn = new HashSet<(int, int, long)>();
            var named = new HashSet<Riff>();
            var nameRight = float.MinValue;
            for (int b = from; b <= to; ++b)
            {
                var bar = chart.Bars[b];
                foreach (var n in shown.NotesIn(b))
                {
                    var at = bar.AtQuarter + n.Offset;
                    var x = (float)((at - tapeStart) * PixelsPerQuarter);
                    if (n.Note.StringIndex < 0 || n.Note.StringIndex >= strings
                        || !drawn.Add((n.Note.StringIndex, n.Note.Fret, (long)Math.Round(at * 960))))
                        continue;
                    noteTimes.Add(at);
                    var label = ChartLook.Label(FretText(n.Note), "gt-tape__fret", tape);
                    label.style.left = x;
                    label.style.top = LineY(n.Note.StringIndex) - 11;
                    label.style.translate = new Translate(Length.Percent(-50), 0);
                    if (named.Add(n.Riff) && x > nameRight)
                    {
                        ChartLook.Label(n.Riff.Name, "gt-tape__riff", tape).style.left = x;
                        nameRight = x + n.Riff.Name.Length * 8 + 16;  // about the name's width at 14 px
                    }
                }
            }
            noteTimes.Sort();
            tape.MarkDirtyRepaint();
        }

        /// <summary>A note as the tab writes it: its fret and technique symbol, "x" for a dead note, a ghost note's fret in brackets.</summary>
        static string FretText(TabNote note)
        {
            var technique = note.Technique ?? "";
            if (technique == "x")
                return "x";
            if (technique.StartsWith("g"))
                return "(" + note.Fret + ")" + technique.Substring(1);
            return note.Fret + technique;
        }

        void DrawStrings(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var width = contentRect.width;
            for (int s = 0; s < strings; ++s)
                ChartLook.Line(p, new Vector2(Gutter, LineY(s)), new Vector2(width - 2, LineY(s)), ChartLook.Faint, 1.5f);
        }

        void DrawBars(MeshGenerationContext ctx)
        {
            if (chart == null || builtFrom < 0)
                return;
            var p = ctx.painter2D;
            for (int b = builtFrom; b <= builtTo + 1 && b <= chart.Bars.Count; ++b)
            {
                var at = b < chart.Bars.Count ? chart.Bars[b].AtQuarter : chart.EndQuarter;
                var x = (float)((at - tapeStart) * PixelsPerQuarter);
                ChartLook.Line(p, new Vector2(x, LineY(strings - 1)), new Vector2(x, LineY(0)), ChartLook.Dim, 2);
                if (b >= chart.Bars.Count)
                    continue;
                // beats as small ticks under the lowest string
                var bar = chart.Bars[b];
                var beat = 4.0 / bar.Signature.Denominator;
                for (int k = 1; k < bar.Signature.Numerator; ++k)
                {
                    var bx = (float)((at + k * beat - tapeStart) * PixelsPerQuarter);
                    ChartLook.Line(p, new Vector2(bx, LineY(0) + 3), new Vector2(bx, LineY(0) + 8), ChartLook.Faint, 1.5f);
                }
            }
        }
    }
}
