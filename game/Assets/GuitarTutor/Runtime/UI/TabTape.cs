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
    /// right to left with the music, at a fixed distance per beat.
    /// </summary>
    public sealed class TabTape : VisualElement
    {
        public const float PixelsPerQuarter = 110, MarkerAt = 0.25f;
        const float Gutter = 34, Top = 26, LineGap = 21;
        const int Strings = 6;

        readonly VisualElement tape, names, marker;
        readonly Label empty;
        readonly List<double> noteTimes = new List<double>();  // the notes on the tape, in time order
        SongChart chart;
        int builtFrom = -1, builtTo = -1;
        double tapeStart;  // the position at the tape's left edge

        public TabTape()
        {
            AddToClassList("gt-tape");
            style.height = Top + LineGap * (Strings - 1) + 12;
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

        static float LineY(int stringIndex) => Top + (Strings - 1 - stringIndex) * LineGap;

        public void SetSong(SongChart songChart, IReadOnlyList<string> tuning)
        {
            chart = songChart;
            builtFrom = builtTo = -1;
            tape.Clear();
            noteTimes.Clear();
            names.Clear();
            for (int s = 0; s < Strings; ++s)
            {
                var name = s < tuning.Count ? tuning[s].TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-') : "";
                var label = ChartLook.Label(s == Strings - 1 ? name.ToLowerInvariant() : name, "gt-tape__string", names);
                label.style.top = LineY(s) - 10;
            }
        }

        /// <summary>Moves the tape to a position (quarter notes; negative during a count-in from the start).</summary>
        public void Show(double quarter)
        {
            var width = layout.width;
            if (chart == null || chart.Bars.Count == 0 || float.IsNaN(width) || width <= 0)
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
            Riff lastRiff = null;
            for (int b = from; b <= to; ++b)
            {
                var bar = chart.Bars[b];
                foreach (var n in bar.Notes)
                {
                    var at = bar.AtQuarter + n.Offset;
                    var x = (float)((at - tapeStart) * PixelsPerQuarter);
                    noteTimes.Add(at);
                    var label = ChartLook.Label(n.Note.Fret + n.Note.Technique, "gt-tape__fret", tape);
                    label.style.left = x;
                    label.style.top = LineY(n.Note.StringIndex) - 11;
                    label.style.translate = new Translate(Length.Percent(-50), 0);
                    if (n.Riff != lastRiff)
                    {
                        lastRiff = n.Riff;
                        ChartLook.Label(n.Riff.Name, "gt-tape__riff", tape).style.left = x;
                    }
                }
            }
            noteTimes.Sort();
            tape.MarkDirtyRepaint();
        }

        void DrawStrings(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var width = contentRect.width;
            for (int s = 0; s < Strings; ++s)
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
                ChartLook.Line(p, new Vector2(x, LineY(Strings - 1)), new Vector2(x, LineY(0)), ChartLook.Dim, 2);
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
