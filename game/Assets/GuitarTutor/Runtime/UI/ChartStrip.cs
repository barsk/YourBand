using System;
using System.Collections.Generic;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Music;
using GuitarTutor.Core.Songs;
using UnityEngine.UIElements;

namespace GuitarTutor.Runtime.UI
{
    /// <summary>A count-in before the music: which beat of it, how far through it (0..1), and the bar it leads into.</summary>
    public readonly struct CountIn
    {
        public int Beat { get; }
        public double Fraction { get; }
        public TimeSignature Signature { get; }
        public int TargetBar { get; }

        public CountIn(int beat, double fraction, TimeSignature signature, int targetBar)
        {
            Beat = beat;
            Fraction = fraction;
            Signature = signature;
            TargetBar = targetBar;
        }
    }

    /// <summary>
    /// The chart strip along the bottom of the desktop screen: the section map (click a section to go there), the
    /// current bar large with the next bars after it, and the lyric line being sung with the next one under it.
    /// </summary>
    public sealed class ChartStrip : VisualElement
    {
        public const int BarsAhead = 3;

        readonly VisualElement sectionRow, sectionCursor, lineNow, lineNext;
        readonly BarView current;
        readonly BarView[] ahead = new BarView[BarsAhead];
        readonly List<VisualElement> sectionBoxes = new List<VisualElement>();
        SongChart chart;
        int sectionNow = -2, lineShown = -2, nextShown = -2, wordsLit = -1;

        /// <summary>Called with the bar to go to when a section of the map is clicked.</summary>
        public event Action<int> SeekToBar;

        public ChartStrip()
        {
            AddToClassList("gt-chart");
            sectionRow = ChartLook.Box("gt-sections", this);
            sectionCursor = ChartLook.Box("gt-sections__cursor");
            var bars = ChartLook.Box("gt-bars", this);
            current = new BarView(true);
            bars.Add(current);
            for (int i = 0; i < BarsAhead; ++i)
                bars.Add(ahead[i] = new BarView(false));
            var lyrics = ChartLook.Box("gt-lyrics", this);
            lineNow = ChartLook.Box("gt-lyrics__line", lyrics);
            lineNext = ChartLook.Box("gt-lyrics__line", lyrics);
            lineNext.AddToClassList("gt-lyrics__line--next");
        }

        public void SetSong(SongChart songChart)
        {
            chart = songChart;
            sectionRow.Clear();
            sectionBoxes.Clear();
            foreach (var span in chart.Sections)
            {
                var box = ChartLook.Box("gt-section", sectionRow);
                box.style.flexGrow = span.Bars;
                box.style.flexBasis = 0;
                ChartLook.Label(span.Section.Name, "gt-section__name", box);
                var first = span.FirstBar;
                box.RegisterCallback<ClickEvent>(_ => SeekToBar?.Invoke(first));
                sectionBoxes.Add(box);
            }
            sectionRow.Add(sectionCursor);
            sectionNow = lineShown = nextShown = -2;
            wordsLit = -1;
        }

        /// <param name="quarter">Where the music is.</param>
        /// <param name="countIn">During a count-in: how far through it, and the bar it leads into; null otherwise.</param>
        public void Show(double quarter, CountIn? countIn)
        {
            if (chart == null)
                return;
            ChartBar bar = null;
            int firstAhead;
            if (countIn is CountIn c)
            {
                current.ShowCountIn(c.Signature, c.Beat);
                current.SetCursor((float)c.Fraction);
                firstAhead = c.TargetBar;
                quarter = c.TargetBar < chart.Bars.Count ? chart.Bars[c.TargetBar].AtQuarter - 1e-6 : quarter;
            }
            else
            {
                bar = chart.BarOf(Math.Max(quarter, 0));
                current.Show(chart, bar);
                current.SetCursor(bar == null ? -1 : (float)((quarter - bar.AtQuarter) / bar.Quarters));
                firstAhead = bar == null ? chart.Bars.Count : bar.Index + 1;
            }
            for (int i = 0; i < BarsAhead; ++i)
            {
                var index = firstAhead + i;
                ahead[i].Show(chart, index < chart.Bars.Count ? chart.Bars[index] : null);
                ahead[i].SetCursor(-1);
            }

            var sectionIndex = chart.SectionIndexOf(bar?.Index ?? countIn?.TargetBar ?? -1);
            if (sectionIndex != sectionNow)
            {
                for (int i = 0; i < sectionBoxes.Count; ++i)
                    sectionBoxes[i].EnableInClassList("gt-section--now", i == sectionIndex);
                sectionNow = sectionIndex;
            }
            var along = chart.EndQuarter > 0 ? Math.Max(0, Math.Min(1, quarter / chart.EndQuarter)) : 0;
            sectionCursor.style.left = Length.Percent((float)(along * 100));

            ShowLyrics(quarter);
        }

        void ShowLyrics(double quarter)
        {
            var now = chart.LineAt(quarter);
            var next = chart.NextLineAfter(quarter);
            if (now != lineShown)
            {
                lineNow.Clear();
                var line = chart.Line(now);
                if (line != null)
                    foreach (var w in line.Words)
                        ChartLook.Label(w.Text, "gt-lyrics__word", lineNow);
                else if (chart.LineCount > 0)
                    ChartLook.Label(chart.Song.SectionAt(Math.Max(quarter, 0))?.Name ?? "", "gt-lyrics__rest", lineNow);
                lineShown = now;
                wordsLit = -1;
            }
            if (next != nextShown)
            {
                lineNext.Clear();
                var line = chart.Line(next);
                if (line != null)
                    foreach (var w in line.Words)
                        ChartLook.Label(w.Text, "gt-lyrics__word", lineNext);
                nextShown = next;
            }
            var sung = SongChart.WordsSung(chart.Line(now), quarter);
            if (sung != wordsLit)
            {
                for (int i = 0; i < lineNow.childCount; ++i)
                    lineNow[i].EnableInClassList("gt-lyrics__word--sung", i < sung && chart.Line(now) != null);
                wordsLit = sung;
            }
        }
    }
}
