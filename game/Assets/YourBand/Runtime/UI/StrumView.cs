using YourBand.Core.Chart;
using YourBand.Core.Music;
using YourBand.Core.Songs;
using UnityEngine;
using UnityEngine.UIElements;

namespace YourBand.Runtime.UI
{
    /// <summary>
    /// A strumming pattern as Ultimate Guitar draws it: the strokes as arrows (down, up) on the pattern's grid, as many
    /// whole bars of it to a row as its room (<see cref="SetRoom"/>) holds with cells of <see cref="MinCell"/> or more
    /// (a faint line between bars), with the counts under them (1 &amp; 2 &amp;, the beats going on through the rows)
    /// and under those a bracket for each eighth: one bar for a pattern of eighth notes, two for sixteenths. An accent is
    /// a &gt; and a muted stroke an x over its arrow; a rest is blank. The stroke being played lights up. A long row in
    /// a small room is drawn smaller; a wide room (the panel alone across the screen) lets it grow, up to full size.
    /// </summary>
    public sealed class StrumView : VisualElement
    {
        /// <summary>The room it has beside the other panels: two bars of sixteenths still fit on a row.</summary>
        public const float DefaultRoom = 620;
        // a cell's width: at most MaxCell, at least MinCell; from FullHeightCell on a row is as tall as at full size (an
        // arrow needs little width, so a narrower cell keeps it tall)
        const float MaxCell = 58, MinCell = 18, FullHeightCell = 40, SmallestScale = 0.6f, SmallestCount = 12;
        // one row at full size: accent marks above the arrows, the arrows, the counts, the brackets
        const float ArrowTop = 20, ArrowBottom = 74, CountsTop = 78, CountsHeight = 24, BracketY = 108, RowHeight = 116, CountSize = 20;

        float Cell = MaxCell;
        float scale = 1;  // the row's height against a full-size one: its arrows, counts and brackets
        float head = 10;  // an arrowhead's half width: as at full size, but no wider than a narrow cell leaves room for
        int perRow = 8, perBeat = 2, perBar = 8;
        float room = DefaultRoom;

        StrumPattern pattern;
        TimeSignature meter = TimeSignature.FourFour;
        int now = -1;

        /// <summary>How wide the pattern is in <see cref="DefaultRoom"/>: its width beside the other panels.</summary>
        public float NaturalWidth { get; private set; }

        public StrumView()
        {
            generateVisualContent += Draw;
        }

        /// <param name="shown">The pattern: any number of bars of the meter.</param>
        /// <param name="signature">The meter its bars are in: a row is a bar, a beat is what the meter counts.</param>
        public void Show(StrumPattern shown, TimeSignature signature)
        {
            if (shown == pattern && signature.Equals(meter))
                return;
            pattern = shown;
            meter = signature;
            now = -1;
            Layout();
        }

        /// <summary>The width it may take (see <see cref="DefaultRoom"/>).</summary>
        public void SetRoom(float width)
        {
            if (float.IsNaN(width) || Mathf.Abs(width - room) < 1)
                return;
            room = width;
            Layout();
        }

        /// <summary>Whole bars to a row, as many as the room holds with cells of at least MinCell (one bar at the least; a pattern shorter than a bar is one short row).</summary>
        int PerRow(float width)
        {
            var bars = Mathf.Max(1, Mathf.CeilToInt(pattern.Strokes.Count / (float)perBar));
            var fit = 1;
            while (fit < bars && width / (perBar * (fit + 1)) >= MinCell)
                ++fit;
            return Mathf.Min(pattern.Strokes.Count, perBar * fit);
        }

        void Layout()
        {
            Clear();
            if (pattern == null)
            {
                NaturalWidth = 0;
                return;
            }
            perBeat = SongChart.SlotsPerBeat(pattern, meter);
            perBar = SongChart.SlotsPerBar(pattern, meter);
            var naturalRow = PerRow(DefaultRoom);
            NaturalWidth = Mathf.Min(MaxCell, DefaultRoom / naturalRow) * naturalRow;
            perRow = PerRow(room);
            var rows = Mathf.CeilToInt(pattern.Strokes.Count / (float)perRow);
            Cell = Mathf.Min(MaxCell, room / perRow);
            scale = Mathf.Clamp(Cell / FullHeightCell, SmallestScale, 1);
            head = Mathf.Min(10 * scale, Cell * 0.35f);
            style.width = Cell * perRow;
            style.height = rows * RowHeight * scale;
            var counts = SongChart.Counts(pattern, meter);
            for (int i = 0; i < counts.Count; ++i)
            {
                if (counts[i].Length == 0)
                    continue;
                var label = ChartLook.Label(counts[i], "gt-strum__count", this);
                if (i % perBeat == 0)
                    label.AddToClassList("gt-strum__count--beat");
                label.style.left = i % perRow * Cell;
                // a count no smaller than SmallestCount, centred where the row's own size would have it, clear of the brackets
                var size = Mathf.Max(SmallestCount, CountSize * scale);
                label.style.top = (i / perRow * RowHeight + CountsTop + CountsHeight / 2) * scale - size / 2 - 2;
                label.style.width = Cell;
                label.style.height = size + 2;
                label.style.fontSize = size;
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
            var s = scale;
            for (int i = 0; i < pattern.Strokes.Count; ++i)
            {
                var stroke = pattern.Strokes[i];
                var col = i % perRow;
                var top = i / perRow * RowHeight * s;
                var x = (col + 0.5f) * Cell;
                var colour = i == now ? ChartLook.Accent : ChartLook.Text;
                if (i == now)
                    ChartLook.FillRect(p, new Rect(col * Cell + 3 * s, top, Cell - 6 * s, (RowHeight - 6) * s), new Color(1, 1, 1, 0.07f));
                // a bar starting inside the row: a faint line before it
                if (col > 0 && col % perBar == 0)
                    ChartLook.Line(p, new Vector2(col * Cell, top + ArrowTop * s), new Vector2(col * Cell, top + BracketY * s), ChartLook.Faint, 2);
                DrawBrackets(p, i, col, top);
                if (stroke == Stroke.Rest)
                    continue;  // blank: the stroke is skipped
                var down = stroke == Stroke.Down || stroke == Stroke.MutedDown || stroke == Stroke.AccentDown;
                var tail = new Vector2(x, top + (down ? ArrowTop + 4 : ArrowBottom - 4) * s);
                var head = new Vector2(x, top + (down ? ArrowBottom - 4 : ArrowTop + 4) * s);
                var back = down ? -1 : 1;
                ChartLook.Line(p, tail, head + new Vector2(0, back * 9 * s), colour, 4.5f * s);
                p.fillColor = colour;
                p.BeginPath();
                p.MoveTo(head);
                p.LineTo(head + new Vector2(-this.head, back * 17 * s));
                p.LineTo(head + new Vector2(this.head, back * 17 * s));
                p.ClosePath();
                p.Fill();
                var mark = new Vector2(x, top + (ArrowTop - 9) * s);
                if (stroke == Stroke.AccentDown || stroke == Stroke.AccentUp)
                {
                    // a > over the arrow
                    ChartLook.Line(p, mark + new Vector2(-5, -6) * s, mark + new Vector2(5, 0) * s, colour, 3 * s);
                    ChartLook.Line(p, mark + new Vector2(-5, 6) * s, mark + new Vector2(5, 0) * s, colour, 3 * s);
                }
                else if (stroke == Stroke.MutedDown || stroke == Stroke.MutedUp)
                {
                    // an x over the arrow
                    ChartLook.Line(p, mark + new Vector2(-5, -5) * s, mark + new Vector2(5, 5) * s, colour, 3 * s);
                    ChartLook.Line(p, mark + new Vector2(-5, 5) * s, mark + new Vector2(5, -5) * s, colour, 3 * s);
                }
            }
        }

        /// <summary>
        /// The bracket under the counts for the eighth that starts at slot <paramref name="i"/>: a single bar under
        /// eighth notes, a double under sixteenths (a bracket spans the two sixteenths of its eighth).
        /// </summary>
        void DrawBrackets(Painter2D p, int i, int col, float top)
        {
            // by the note length of the slots: sixteenths two bars and a bracket over two of them, eighths one bar and one
            var sub = pattern.Subdivision;
            var bars = sub >= 16 ? 2 : sub >= 8 ? 1 : 0;
            var span = sub >= 16 ? sub / 8 : 1;
            if (bars == 0 || i % span != 0)
                return;
            // each bracket stops short of its slots' edges, leaving a gap to the next (the panel stays as wide)
            var s = scale;
            var inset = Mathf.Min(11f * s, Cell * span * 0.3f);
            var left = col * Cell + inset;
            var right = (col + span) * Cell - inset;
            var y = top + BracketY * s;
            var colour = ChartLook.Dim;
            for (int b = 0; b < bars; ++b)
                ChartLook.Line(p, new Vector2(left, y - b * 6 * s), new Vector2(right, y - b * 6 * s), colour, 3 * s);
            var height = (bars * 6 + 2) * s;
            ChartLook.Line(p, new Vector2(left, y), new Vector2(left, y - height), colour, 3 * s);
            ChartLook.Line(p, new Vector2(right, y), new Vector2(right, y - height), colour, 3 * s);
        }
    }
}
