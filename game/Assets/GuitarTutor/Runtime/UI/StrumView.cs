using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Songs;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuitarTutor.Runtime.UI
{
    /// <summary>
    /// A strumming pattern as Ultimate Guitar draws it: the strokes as arrows (down, up) on the pattern's grid, a bar of
    /// it to a row, with the counts under them (1 &amp; 2 &amp;, the beats going on through the rows) and under those a
    /// bracket for each eighth: one bar for a pattern of eighth notes, two for sixteenths. An accent is a &gt; and a
    /// muted stroke an x over its arrow; a rest is blank. The stroke being played lights up.
    /// </summary>
    public sealed class StrumView : VisualElement
    {
        const float MaxCell = 58, MaxWidth = 620;
        // one row: accent marks above the arrows, the arrows, the counts, the brackets
        const float ArrowTop = 22, ArrowBottom = 62, CountsTop = 66, CountsHeight = 24, BracketY = 96, RowHeight = 104;

        float Cell = MaxCell;  // narrower for a long row (16 slots), so the panels still fit on one row
        int perRow = 8, perBeat = 2;

        StrumPattern pattern;
        int now = -1;

        public StrumView()
        {
            generateVisualContent += Draw;
        }

        public void Show(StrumPattern shown)
        {
            if (shown == pattern)
                return;
            pattern = shown;
            now = -1;
            Clear();
            if (pattern == null)
                return;
            // a bar to a row (4/4): as many slots as four beats have
            perBeat = Mathf.Max(1, pattern.Subdivision / 4);
            perRow = Mathf.Min(pattern.Strokes.Count, 4 * perBeat);
            var rows = Mathf.CeilToInt(pattern.Strokes.Count / (float)perRow);
            Cell = Mathf.Min(MaxCell, MaxWidth / perRow);
            style.width = Cell * perRow;
            style.height = rows * RowHeight;
            var counts = SongChart.Counts(pattern);
            for (int i = 0; i < counts.Count; ++i)
            {
                if (counts[i].Length == 0)
                    continue;
                var label = ChartLook.Label(counts[i], "gt-strum__count", this);
                if (i % perBeat == 0)
                    label.AddToClassList("gt-strum__count--beat");
                label.style.left = i % perRow * Cell;
                label.style.top = i / perRow * RowHeight + CountsTop;
                label.style.width = Cell;
                label.style.height = CountsHeight;
            }
            MarkDirtyRepaint();
        }

        /// <summary>The stroke being played (-1: none, when paused before the song or in a count-in).</summary>
        public void SetNow(int index)
        {
            if (index == now)
                return;
            now = index;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            if (pattern == null)
                return;
            var p = ctx.painter2D;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            for (int i = 0; i < pattern.Strokes.Count; ++i)
            {
                var stroke = pattern.Strokes[i];
                var col = i % perRow;
                var top = i / perRow * RowHeight;
                var x = (col + 0.5f) * Cell;
                var colour = i == now ? ChartLook.Accent : ChartLook.Text;
                if (i == now)
                    ChartLook.FillRect(p, new Rect(col * Cell + 3, top, Cell - 6, RowHeight - 6), new Color(1, 1, 1, 0.07f));
                DrawBrackets(p, i, col, top);
                if (stroke == Stroke.Rest)
                    continue;  // blank: the stroke is skipped
                var down = stroke == Stroke.Down || stroke == Stroke.MutedDown || stroke == Stroke.AccentDown;
                var tail = new Vector2(x, top + (down ? ArrowTop + 4 : ArrowBottom - 4));
                var head = new Vector2(x, top + (down ? ArrowBottom - 4 : ArrowTop + 4));
                var back = down ? -1 : 1;
                ChartLook.Line(p, tail, head + new Vector2(0, back * 10), colour, 6);
                p.fillColor = colour;
                p.BeginPath();
                p.MoveTo(head);
                p.LineTo(head + new Vector2(-13, back * 20));
                p.LineTo(head + new Vector2(13, back * 20));
                p.ClosePath();
                p.Fill();
                var mark = new Vector2(x, top + ArrowTop - 12);
                if (stroke == Stroke.AccentDown || stroke == Stroke.AccentUp)
                {
                    // a > over the arrow
                    ChartLook.Line(p, mark + new Vector2(-8, -8), mark + new Vector2(8, 0), colour, 4);
                    ChartLook.Line(p, mark + new Vector2(-8, 8), mark + new Vector2(8, 0), colour, 4);
                }
                else if (stroke == Stroke.MutedDown || stroke == Stroke.MutedUp)
                {
                    // an x over the arrow
                    ChartLook.Line(p, mark + new Vector2(-7, -7), mark + new Vector2(7, 7), colour, 4);
                    ChartLook.Line(p, mark + new Vector2(-7, 7), mark + new Vector2(7, -7), colour, 4);
                }
            }
        }

        /// <summary>
        /// The bracket under the counts for the eighth that starts at slot <paramref name="i"/>: a single bar under
        /// eighth notes, a double under sixteenths (a bracket spans the two sixteenths of its eighth).
        /// </summary>
        void DrawBrackets(Painter2D p, int i, int col, float top)
        {
            var bars = perBeat >= 4 ? 2 : perBeat >= 2 ? 1 : 0;
            var span = perBeat >= 4 ? perBeat / 2 : 1;
            if (bars == 0 || i % span != 0)
                return;
            var left = col * Cell + Cell * 0.15f;
            var right = (col + span) * Cell - Cell * 0.15f;
            var y = top + BracketY;
            var colour = ChartLook.Dim;
            for (int b = 0; b < bars; ++b)
                ChartLook.Line(p, new Vector2(left, y - b * 6), new Vector2(right, y - b * 6), colour, 3);
            var height = bars * 6 + 2;
            ChartLook.Line(p, new Vector2(left, y), new Vector2(left, y - height), colour, 3);
            ChartLook.Line(p, new Vector2(right, y), new Vector2(right, y - height), colour, 3);
        }
    }
}
