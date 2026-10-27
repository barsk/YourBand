using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Songs;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuitarTutor.Runtime.UI
{
    /// <summary>
    /// A strumming pattern as arrows on its grid (down, up, a dot for a rest, a cross for a muted stroke), with the
    /// counts under them; the stroke being played lights up.
    /// </summary>
    public sealed class StrumView : VisualElement
    {
        const float MaxCell = 58, MaxWidth = 620, ArrowTop = 24, ArrowBottom = 106, CountsTop = 112, Height = 142;  // room above the arrows for an accent's >

        float Cell = MaxCell;  // narrower for a long pattern (16 steps), so the panels still fit on one row

        StrumPattern pattern;
        int now = -1;

        public StrumView()
        {
            style.height = Height;
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
            Cell = Mathf.Min(MaxCell, MaxWidth / pattern.Strokes.Count);
            style.width = Cell * pattern.Strokes.Count;
            var counts = SongChart.Counts(pattern);
            var perBeat = Mathf.Max(1, pattern.Subdivision / 4);
            for (int i = 0; i < counts.Count; ++i)
            {
                var label = ChartLook.Label(counts[i], "gt-strum__count", this);
                if (i % perBeat == 0)
                    label.AddToClassList("gt-strum__count--beat");
                label.style.left = i * Cell;
                label.style.top = CountsTop;
                label.style.width = Cell;
                label.style.height = Height - CountsTop;
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
            for (int i = 0; i < pattern.Strokes.Count; ++i)
            {
                var stroke = pattern.Strokes[i];
                var x = (i + 0.5f) * Cell;
                var colour = i == now ? ChartLook.Accent : stroke == Stroke.Rest ? ChartLook.Faint : ChartLook.Text;
                if (i == now)
                    ChartLook.FillRect(p, new Rect(i * Cell + 3, 0, Cell - 6, Height), new Color(1, 1, 1, 0.07f));
                if (stroke == Stroke.Rest)
                {
                    ChartLook.Disc(p, new Vector2(x, (ArrowTop + ArrowBottom) / 2), 4, colour);
                    continue;
                }
                var down = stroke == Stroke.Down || stroke == Stroke.MutedDown || stroke == Stroke.AccentDown;
                var tail = new Vector2(x, down ? ArrowTop + 4 : ArrowBottom - 4);
                var head = new Vector2(x, down ? ArrowBottom - 4 : ArrowTop + 4);
                var back = down ? -1 : 1;
                p.lineCap = LineCap.Round;
                ChartLook.Line(p, tail, head + new Vector2(0, back * 10), colour, 6);
                p.fillColor = colour;
                p.BeginPath();
                p.MoveTo(head);
                p.LineTo(head + new Vector2(-13, back * 20));
                p.LineTo(head + new Vector2(13, back * 20));
                p.ClosePath();
                p.Fill();
                if (stroke == Stroke.AccentDown || stroke == Stroke.AccentUp)
                {
                    // a > over the arrow
                    var top = new Vector2(x, ArrowTop - 12);
                    ChartLook.Line(p, top + new Vector2(-8, -8), top + new Vector2(8, 0), colour, 4);
                    ChartLook.Line(p, top + new Vector2(-8, 8), top + new Vector2(8, 0), colour, 4);
                }
                if (stroke == Stroke.MutedDown || stroke == Stroke.MutedUp)
                {
                    var c = new Vector2(x, (ArrowTop + ArrowBottom) / 2);
                    ChartLook.Line(p, c + new Vector2(-12, -12), c + new Vector2(12, 12), ChartLook.Faint, 4);
                    ChartLook.Line(p, c + new Vector2(-12, 12), c + new Vector2(12, -12), ChartLook.Faint, 4);
                }
            }
        }
    }
}
