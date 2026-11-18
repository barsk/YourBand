using System;
using System.Collections.Generic;
using YourBand.Core.Chart;
using YourBand.Core.Music;
using UnityEngine;
using UnityEngine.UIElements;

namespace YourBand.Runtime.UI
{
    /// <summary>
    /// The chart as rows of bars, two rows in view: the row being played on top with the play marker running
    /// through it, the next row under it. Just before the music reaches the next row, the rows slide up and a new
    /// row comes in at the bottom. Where the rows stand is worked out from the music's position alone
    /// (<see cref="SongChart.RowPosition"/>), so a jump simply shows the rows at the new place.
    /// </summary>
    /// <remarks>
    /// The lyrics have one size for the whole song: as large as lets the words of every bar sit at their beats,
    /// from the style's size down to <see cref="SmallestLyrics"/> of it. Bars that would need smaller text than
    /// that are left out of the choice: their words are pushed along, into the next bar's space if they must, and
    /// only a row that runs out of room even so has its words made smaller.
    /// </remarks>
    public sealed class ChartRows : VisualElement
    {
        public const int InView = 2;
        public const float SmallestLyrics = 0.75f;
        /// <summary>The space between two words, and a bar's padding left and right (as in Chart.uss).</summary>
        public const float WordGap = 10, CellPadding = 18;

        readonly RowView[] views = new RowView[InView + 1];
        readonly Label probe;  // measures words in the lyrics' style
        readonly Dictionary<string, float> widths = new Dictionary<string, float>();
        SongChart chart;
        float fittedWidth = -1;

        public ChartRows()
        {
            AddToClassList("gt-rows");
            for (int i = 0; i < views.Length; ++i)
                Add(views[i] = new RowView());
            probe = ChartLook.Label("", "gt-cell__word", this);
            probe.style.visibility = Visibility.Hidden;
            RegisterCallback<GeometryChangedEvent>(_ => FitLyrics());
        }

        public void SetSong(SongChart songChart)
        {
            chart = songChart;
            fittedWidth = -1;
            foreach (var v in views)
                v.Clear(chart);
            schedule.Execute(FitLyrics);
        }

        /// <summary>The chart shows other chords now: the rows draw their bars again.</summary>
        public void ChordsChanged()
        {
            foreach (var v in views)
                v.ForgetBars();
        }

        /// <summary>The lyrics' size for this song at this width: the largest that fits every bar, within limits.</summary>
        void FitLyrics()
        {
            var width = layout.width;
            if (chart == null || float.IsNaN(width) || width <= 0 || Math.Abs(width - fittedWidth) < 1)
                return;
            var full = probe.resolvedStyle.fontSize;
            if (float.IsNaN(full) || full <= 0)
                return;
            fittedWidth = width;
            widths.Clear();
            float Width(string word)
            {
                if (!widths.TryGetValue(word, out var w))
                    widths[word] = w = probe.MeasureTextSize(word, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x;
                return w;
            }
            // laid out at their beats, pushed along where they would touch: do they end inside the bar?
            bool Fits(ChartBar bar, float barWidth, float scale)
            {
                var right = float.MinValue;
                foreach (var w in bar.Words)
                    right = Math.Max((float)(w.Offset / bar.Quarters) * barWidth, right + WordGap) + Width(w.Word.Text) * scale;
                return right <= barWidth + 0.5f;
            }
            // the size each bar needs
            var needs = new List<float>();
            foreach (var bar in chart.Bars)
            {
                if (bar.Words.Count == 0)
                    continue;
                var barWidth = (float)(width * bar.Quarters / (SongChart.BarsPerRow * chart.TypicalBarQuarters)) - CellPadding;
                float low = 0.3f, high = 1f;
                if (Fits(bar, barWidth, high))
                    low = high;
                else
                    for (int i = 0; i < 12; ++i)
                    {
                        var mid = (low + high) / 2;
                        if (Fits(bar, barWidth, mid))
                            low = mid;
                        else
                            high = mid;
                    }
                needs.Add(low);
            }
            needs.Sort();
            // the bars that fit at a readable size choose it; the others push their words along
            var size = 1f;
            foreach (var need in needs)
                if (need >= SmallestLyrics)
                {
                    size = need;
                    break;
                }
            Debug.Log($"YourBand: lyrics at {full * size:0} px for {chart.Song.Info.Title}; "
                      + $"{needs.FindAll(n => n < size).Count} of {needs.Count} bars with words push them along");
            foreach (var v in views)
                v.SetLyricSize(full * size);
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
        float lyricSize;
        float rowScale = 1;  // below 1 only for a row whose words run out of room even pushed along

        public int RowIndex { get; private set; } = -1;

        public RowView()
        {
            AddToClassList("gt-row");
            section = ChartLook.Label("", "gt-row__section", this);
            cells = ChartLook.Box("gt-row__cells", this);
            RegisterCallback<GeometryChangedEvent>(_ => LayoutWords());
        }

        public void SetLyricSize(float size)
        {
            lyricSize = size;
            rowScale = 1;
            ApplyLyricSize();
        }

        void ApplyLyricSize()
        {
            for (int i = 0; i < inUse; ++i)
                pool[i].SetLyricSize(lyricSize * rowScale);
            LayoutWords();
        }

        /// <summary>
        /// The row's words on one line: each at its beat, or pushed along if the word before it is still there
        /// (into the next bar's space if it must). Words that would run past the row's end are moved back just
        /// enough to end there; only if the row's words do not fit side by side at all do they get smaller.
        /// </summary>
        void LayoutWords()
        {
            if (inUse == 0)
                return;
            var placed = new List<(Label label, float origin, float left, float width)>();
            var right = float.MinValue;
            for (int i = 0; i < inUse; ++i)
            {
                var cell = pool[i];
                var layer = cell.WordLayer;
                var width = layer.layout.width;
                if (float.IsNaN(width) || width <= 0)
                    return;
                var origin = cell.layout.x + layer.layout.x;
                foreach (var (label, at) in cell.Words)
                {
                    var w = label.layout.width;
                    w = float.IsNaN(w) ? 0 : w;
                    var left = Math.Max(origin + (float)at * width, right + ChartRows.WordGap);
                    placed.Add((label, origin, left, w));
                    right = left + w;
                }
            }
            if (placed.Count == 0)
                return;
            var first = pool[0].layout.x + pool[0].WordLayer.layout.x;
            var last = pool[inUse - 1];
            var end = last.layout.x + last.WordLayer.layout.x + last.WordLayer.layout.width;

            // the last words back from the row's end, as far as they must
            var limit = end;
            for (int k = placed.Count - 1; k >= 0 && placed[k].left + placed[k].width > limit + 0.5f; --k)
            {
                var p = placed[k];
                p.left = limit - p.width;
                placed[k] = p;
                limit = p.left - ChartRows.WordGap;
            }

            if (placed[0].left < first - 0.5f)
            {
                // more words than the row holds: smaller, decided on widths measured at the size they have now
                var size = lyricSize * rowScale;
                if (lyricSize <= 0 || rowScale <= 0.5f || placed.Exists(p => Math.Abs(p.label.resolvedStyle.fontSize - size) > 0.5f))
                    return;
                var needed = 0f;
                foreach (var p in placed)
                    needed += p.width + ChartRows.WordGap;
                rowScale = Math.Max(0.5f, rowScale * (end - first) / needed * 0.98f);
                for (int i = 0; i < inUse; ++i)
                    pool[i].SetLyricSize(lyricSize * rowScale);
                return;  // the new sizes lay the row out again
            }
            foreach (var (label, origin, left, _) in placed)
                if (Math.Abs(label.resolvedStyle.left - (left - origin)) > 0.5f)
                    label.style.left = left - origin;
        }

        public void Clear(SongChart songChart)
        {
            chart = songChart;
            RowIndex = -1;
            style.display = DisplayStyle.None;
        }

        public void ForgetBars()
        {
            RowIndex = -1;
            foreach (var cell in pool)
                cell.Forget();
        }

        public void Show(ChartRow row)
        {
            RowIndex = row.Index;
            inUse = row.Bars;
            rowScale = 1;
            section.text = row.Section?.Name.ToUpperInvariant() ?? "";
            section.EnableInClassList("gt-row__section--start", row.StartsSection);
            while (pool.Count < row.Bars)
            {
                var cell = new BarCell(LayoutWords);
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
            ApplyLyricSize();
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
        readonly SpreadRow chords;
        readonly VisualElement wordLayer;
        readonly List<(Label label, double at)> words = new List<(Label, double)>();
        readonly Action relayout;
        SongChart chart;
        ChartBar bar;
        bool current;
        float cursor = -1;  // 0..1 through the bar, -1: not here
        int beatNow = -1;

        /// <param name="wordsMoved">Called when the words change size: the row lays them out again.</param>
        public BarCell(Action wordsMoved)
        {
            relayout = wordsMoved;
            AddToClassList("gt-cell");
            // the beat numbers over the beat ticks, the bar's number at the right
            var head = ChartLook.Box("gt-cell__head", this);
            beats = ChartLook.Box("gt-cell__beats", head);
            number = ChartLook.Label("", "gt-cell__number", head);
            chords = new SpreadRow(ChartLook.Box("gt-cell__chords", this));
            track = ChartLook.Box("gt-cell__track", this);
            track.generateVisualContent += DrawTrack;
            wordLayer = ChartLook.Box("gt-cell__words", this);
            overlay = ChartLook.Box("gt-cell__overlay", this);
            overlay.pickingMode = PickingMode.Ignore;
            overlay.generateVisualContent += DrawOverlay;
            count = ChartLook.Label("", "gt-cell__count", this);
            count.pickingMode = PickingMode.Ignore;
        }

        public void Forget() => bar = null;

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
            wordLayer.Clear();
            words.Clear();
            foreach (var c in bar.ShownChords)
                if (!c.CarriedIn)
                    chords.Add(c.Chord.Name, c.Offset / bar.Quarters, "gt-cell__chord");
                else if (bar.ShownChords.Count == 1 || c.Quarters >= 1)
                    chords.Add(c.Chord.Name, 0, "gt-cell__chord").AddToClassList("gt-cell__chord--held");
            foreach (var w in bar.Words)
            {
                var label = ChartLook.Label(w.Word.Text, "gt-cell__word", wordLayer);
                label.EnableInClassList("gt-cell__word--backing", w.Backing);
                label.RegisterCallback<GeometryChangedEvent>(_ => relayout());
                words.Add((label, w.Offset / bar.Quarters));
            }
            current = false;
            cursor = -1;
            EnableInClassList("gt-cell--now", false);
            count.text = "";
            track.MarkDirtyRepaint();
            overlay.MarkDirtyRepaint();
        }

        /// <summary>The words and where in the bar they are sung (0..1); the row places them.</summary>
        public IReadOnlyList<(Label label, double at)> Words => words;
        public VisualElement WordLayer => wordLayer;

        public void SetLyricSize(float size)
        {
            if (size <= 0)
                return;
            foreach (var (label, _) in words)
                label.style.fontSize = size;
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
            foreach (var (label, at) in words)
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
            foreach (var c in bar.ShownChords)
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
