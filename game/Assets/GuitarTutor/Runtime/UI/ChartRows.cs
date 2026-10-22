using System;
using System.Collections.Generic;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Music;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuitarTutor.Runtime.UI
{
    /// <summary>
    /// The chart as rows of bars, two rows in view: the row being played on top with the play marker running
    /// through it, the next row under it. Just before the music reaches the next row, the rows slide up and a new
    /// row comes in at the bottom. Where the rows stand is worked out from the music's position alone
    /// (<see cref="SongChart.RowPosition"/>), so a jump simply shows the rows at the new place.
    /// </summary>
    public sealed class ChartRows : VisualElement
    {
        public const int InView = 2;

        readonly RowView[] views = new RowView[InView + 1];
        SongChart chart;

        public ChartRows()
        {
            AddToClassList("gt-rows");
            for (int i = 0; i < views.Length; ++i)
                Add(views[i] = new RowView());
        }

        public void SetSong(SongChart songChart)
        {
            chart = songChart;
            foreach (var v in views)
                v.Clear(chart);
        }

        /// <param name="lead">How long before a row's end the rows start moving on (quarter notes).</param>
        public void Show(double quarter, CountIn? countIn, double lead)
        {
            if (chart == null || chart.Rows.Count == 0)
                return;
            // during a count-in the rows wait at the bar it leads into
            var at = countIn is CountIn c && c.TargetBar < chart.Bars.Count ? chart.Bars[c.TargetBar].AtQuarter : quarter;
            var position = chart.RowPosition(at, countIn == null ? lead : 0);
            var first = (int)Math.Floor(position);
            for (int k = 0; k <= InView; ++k)
            {
                var index = first + k;
                var view = views[index % views.Length];
                if (index >= chart.Rows.Count)
                {
                    view.style.display = DisplayStyle.None;
                    continue;
                }
                if (view.RowIndex != index)
                    view.Show(chart.Rows[index]);
                view.style.display = DisplayStyle.Flex;
                view.style.top = Length.Percent((float)((index - position) * 100.0 / InView));
                view.SetState(quarter, countIn);
            }
        }
    }

    /// <summary>One row: the section's name as a small headline, then its bars side by side, sized by their length.</summary>
    sealed class RowView : VisualElement
    {
        readonly Label section;
        readonly VisualElement cells;
        readonly List<BarCell> pool = new List<BarCell>();
        SongChart chart;
        int inUse;  // cells showing a bar

        public int RowIndex { get; private set; } = -1;

        public RowView()
        {
            AddToClassList("gt-row");
            section = ChartLook.Label("", "gt-row__section", this);
            cells = ChartLook.Box("gt-row__cells", this);
        }

        public void Clear(SongChart songChart)
        {
            chart = songChart;
            RowIndex = -1;
            style.display = DisplayStyle.None;
        }

        public void Show(ChartRow row)
        {
            RowIndex = row.Index;
            inUse = row.Bars;
            section.text = row.Section?.Name.ToUpperInvariant() ?? "";
            section.EnableInClassList("gt-row__section--start", row.StartsSection);
            while (pool.Count < row.Bars)
            {
                var cell = new BarCell();
                pool.Add(cell);
                cells.Add(cell);
            }
            for (int i = 0; i < pool.Count; ++i)
            {
                var cell = pool[i];
                if (i >= row.Bars)
                {
                    cell.style.display = DisplayStyle.None;
                    continue;
                }
                var bar = chart.Bars[row.FirstBar + i];
                cell.style.display = DisplayStyle.Flex;
                // bars as wide as they are long: four of the song's usual bars fill the row
                cell.style.width = Length.Percent((float)(100.0 * bar.Quarters / (SongChart.BarsPerRow * chart.TypicalBarQuarters)));
                cell.Show(chart, bar);
            }
        }

        public void SetState(double quarter, CountIn? countIn)
        {
            for (int i = 0; i < inUse; ++i)
                pool[i].SetState(quarter, countIn);
        }
    }

    /// <summary>
    /// One bar of a row: its number and beats, the chords at their positions over a band per chord, and the words
    /// sung in it. The bar being played is lit, with the play marker running across it.
    /// </summary>
    sealed class BarCell : VisualElement
    {
        readonly Label number, count;
        readonly VisualElement beats, track, overlay;
        readonly SpreadRow chords, words;
        SongChart chart;
        ChartBar bar;
        bool current;
        float cursor = -1;  // 0..1 through the bar, -1: not here
        int beatNow = -1;

        public BarCell()
        {
            AddToClassList("gt-cell");
            // the beat numbers over the beat ticks, the bar's number at the right
            var head = ChartLook.Box("gt-cell__head", this);
            beats = ChartLook.Box("gt-cell__beats", head);
            number = ChartLook.Label("", "gt-cell__number", head);
            chords = new SpreadRow(ChartLook.Box("gt-cell__chords", this));
            track = ChartLook.Box("gt-cell__track", this);
            track.generateVisualContent += DrawTrack;
            words = new SpreadRow(ChartLook.Box("gt-cell__words", this));
            overlay = ChartLook.Box("gt-cell__overlay", this);
            overlay.pickingMode = PickingMode.Ignore;
            overlay.generateVisualContent += DrawOverlay;
            count = ChartLook.Label("", "gt-cell__count", this);
            count.pickingMode = PickingMode.Ignore;
        }

        public void Show(SongChart songChart, ChartBar shown)
        {
            if (shown == bar && songChart == chart)
                return;
            chart = songChart;
            bar = shown;
            number.text = (bar.Index + 1).ToString();
            beats.Clear();
            for (int b = 0; b < bar.Signature.Numerator; ++b)
                ChartLook.Label((b + 1).ToString(), "gt-cell__beat", beats).style.left = Length.Percent(100f * b / bar.Signature.Numerator);
            beatNow = -1;
            chords.Clear();
            words.Clear();
            foreach (var c in bar.Chords)
                if (!c.CarriedIn)
                    chords.Add(c.Chord.Name, c.Offset / bar.Quarters, "gt-cell__chord");
                else if (bar.Chords.Count == 1 || c.Quarters >= 1)
                    chords.Add(c.Chord.Name, 0, "gt-cell__chord").AddToClassList("gt-cell__chord--held");
            foreach (var w in bar.Words)
                words.Add(w.Word.Text, w.Offset / bar.Quarters, "gt-cell__word");
            current = false;
            cursor = -1;
            EnableInClassList("gt-cell--now", false);
            count.text = "";
            track.MarkDirtyRepaint();
            overlay.MarkDirtyRepaint();
        }

        public void SetState(double quarter, CountIn? countIn)
        {
            if (bar == null)
                return;
            var inBar = countIn is CountIn c
                ? c.TargetBar == bar.Index
                : quarter >= bar.AtQuarter && quarter < bar.AtQuarter + bar.Quarters;
            var fraction = !inBar ? -1f : countIn != null ? 0f : (float)((quarter - bar.AtQuarter) / bar.Quarters);
            if (inBar != current)
            {
                current = inBar;
                EnableInClassList("gt-cell--now", inBar);
            }
            if (Mathf.Abs(fraction - cursor) > 0.0005f)
            {
                cursor = fraction;
                overlay.MarkDirtyRepaint();
            }
            var beat = fraction < 0 ? -1 : Mathf.Min((int)(fraction * bar.Signature.Numerator), bar.Signature.Numerator - 1);
            if (countIn != null && inBar)
                beat = -1;
            if (beat != beatNow)
            {
                for (int b = 0; b < beats.childCount; ++b)
                    beats[b].EnableInClassList("gt-cell__beat--now", b == beat);
                beatNow = beat;
            }
            // the count-in, counted out large in the bar it leads into
            var counting = countIn is CountIn k && inBar ? (k.Beat + 1).ToString() : "";
            if (count.text != counting)
                count.text = counting;
            var sungUpTo = countIn != null ? double.MinValue : quarter;
            foreach (var (label, at) in words.Items)
                label.EnableInClassList("gt-cell__word--sung", bar.AtQuarter + at * bar.Quarters <= sungUpTo + 1e-4);
        }

        void DrawTrack(MeshGenerationContext ctx)
        {
            if (bar == null)
                return;
            var p = ctx.painter2D;
            var r = track.contentRect;
            if (r.width <= 0)
                return;
            foreach (var c in bar.Chords)
            {
                var x0 = (float)(c.Offset / bar.Quarters) * r.width;
                var x1 = (float)((c.Offset + c.Quarters) / bar.Quarters) * r.width;
                var colour = ChartLook.ForChord(chart, c.Chord.Name);
                ChartLook.FillRect(p, new Rect(x0 + 1, 4, x1 - x0 - 2, r.height - 8), new Color(colour.r, colour.g, colour.b, 0.38f));
                ChartLook.FillRect(p, new Rect(x0 + 1, 4, x1 - x0 - 2, 3), colour);
            }
            // beats, and the eighths between them
            var steps = bar.Signature.Numerator * 2;
            for (int i = 0; i <= steps; ++i)
            {
                var x = Mathf.Clamp(r.width * i / steps, 1, r.width - 1);
                var onBeat = i % 2 == 0;
                ChartLook.Line(p, new Vector2(x, onBeat ? 0 : r.height * 0.35f), new Vector2(x, onBeat ? r.height : r.height * 0.65f),
                    i == 0 ? ChartLook.Dim : ChartLook.Faint, i == 0 ? 3 : onBeat ? 2 : 1);
            }
        }

        void DrawOverlay(MeshGenerationContext ctx)
        {
            if (cursor < 0)
                return;
            var p = ctx.painter2D;
            var r = overlay.contentRect;
            var x = cursor * r.width;
            ChartLook.FillRect(p, new Rect(0, 0, x, r.height), new Color(1, 1, 1, 0.035f));
            ChartLook.Line(p, new Vector2(x, 2), new Vector2(x, r.height - 2), ChartLook.Accent, 4);
        }
    }
}
