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
    /// The chart along the bottom of the desktop screen: the section map (click a section to go there) and the
    /// song's bars in rows, the row being played on top and the next one under it, moving on smoothly.
    /// </summary>
    public sealed class ChartStrip : VisualElement
    {
        readonly VisualElement sectionRow, sectionCursor;
        readonly ChartRows rows;
        readonly List<VisualElement> sectionBoxes = new List<VisualElement>();
        SongChart chart;
        int sectionNow = -2;

        /// <summary>Called with the bar to go to when a section of the map is clicked.</summary>
        public event Action<int> SeekToBar;

        public ChartStrip()
        {
            AddToClassList("gt-chart");
            sectionRow = ChartLook.Box("gt-sections", this);
            sectionCursor = ChartLook.Box("gt-sections__cursor");
            rows = new ChartRows();
            Add(rows);
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
            sectionNow = -2;
            rows.SetSong(chart);
        }

        /// <param name="quarter">Where the music is.</param>
        /// <param name="countIn">During a count-in: how far through it, and the bar it leads into; null otherwise.</param>
        /// <param name="lead">How long before a row's end the rows move on (quarter notes).</param>
        public void Show(double quarter, CountIn? countIn, double lead)
        {
            if (chart == null)
                return;
            rows.Show(quarter, countIn, lead);

            var bar = countIn is CountIn c ? (c.TargetBar < chart.Bars.Count ? chart.Bars[c.TargetBar] : null) : chart.BarOf(Math.Max(quarter, 0));
            var sectionIndex = chart.SectionIndexOf(bar?.Index ?? -1);
            if (sectionIndex != sectionNow)
            {
                for (int i = 0; i < sectionBoxes.Count; ++i)
                    sectionBoxes[i].EnableInClassList("gt-section--now", i == sectionIndex);
                sectionNow = sectionIndex;
            }
            var along = chart.EndQuarter > 0 ? Math.Max(0, Math.Min(1, quarter / chart.EndQuarter)) : 0;
            sectionCursor.style.left = Length.Percent((float)(along * 100));
        }
    }
}
