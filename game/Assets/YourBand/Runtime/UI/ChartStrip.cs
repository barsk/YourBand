using System;
using System.Collections.Generic;
using YourBand.Core.Chart;
using YourBand.Core.Music;
using YourBand.Core.Songs;
using UnityEngine.UIElements;

namespace YourBand.Runtime.UI
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
        double[] shares;  // each section's width in the map (its bars, or what its name needs), and their sum
        double sharesTotal;

        // a section's name needs about this much of the map's width (px): chars at the label's size, and its padding
        const double CharWidth = 10, LabelPadding = 22, MapWidth = 1800;

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
            // widths in proportion to the bars, but no section narrower than its name: the others give way
            var bars = 0.0;
            foreach (var span in chart.Sections)
                bars += span.Bars;
            shares = new double[chart.Sections.Count];
            sharesTotal = 0;
            for (int i = 0; i < shares.Length; ++i)
            {
                var need = (chart.Sections[i].Section.Name.Length * CharWidth + LabelPadding) / MapWidth * bars;
                shares[i] = Math.Max(chart.Sections[i].Bars, need);
                sharesTotal += shares[i];
            }
            var index = 0;
            foreach (var span in chart.Sections)
            {
                var box = ChartLook.Box("gt-section", sectionRow);
                box.style.flexGrow = (float)shares[index++];
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

        /// <summary>How far along the map the music is (0..1): through the section it is in, by that section's width.</summary>
        double CursorAlong(double quarter)
        {
            if (chart.Sections.Count == 0 || sharesTotal <= 0)
                return chart.EndQuarter > 0 ? Math.Max(0, Math.Min(1, quarter / chart.EndQuarter)) : 0;
            double before = 0;
            for (int i = 0; i < chart.Sections.Count; ++i)
            {
                var section = chart.Sections[i].Section;
                var within = section.Quarters > 0 ? (quarter - section.AtQuarter) / section.Quarters : 0;
                if (i == chart.Sections.Count - 1 || quarter < section.AtQuarter + section.Quarters)
                    return Math.Max(0, Math.Min(1, (before + shares[i] * Math.Max(0, Math.Min(1, within))) / sharesTotal));
                before += shares[i];
            }
            return 1;
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
            sectionCursor.style.left = Length.Percent((float)(CursorAlong(quarter) * 100));
        }
    }
}
