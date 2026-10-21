using System.Collections.Generic;
using GuitarTutor.Core.Chart;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuitarTutor.Runtime.UI
{
    /// <summary>
    /// Tab for the current bar and the next, as written (the highest string on top), with the fret numbers at
    /// their positions and a cursor.
    /// </summary>
    public sealed class TabView : VisualElement
    {
        const float Width = 640, Left = 26, LineGap = 22, Top = 14, Height = Top * 2 + LineGap * 5;
        const int Strings = 6;

        ChartBar first;
        double firstAt, quarters;
        float cursor = -1;   // 0..1 over the bars shown, -1 hidden
        readonly List<string> stringNames = new List<string>();

        public TabView()
        {
            style.width = Width;
            style.height = Height;
            generateVisualContent += Draw;
        }

        /// <summary>True if the bars shown have any notes.</summary>
        public bool HasNotes { get; private set; }

        float LineY(int stringIndex) => Top + (Strings - 1 - stringIndex) * LineGap;

        // where a position falls, as a fraction of the bars shown
        static float X(double fraction) => Left + 12 + (float)fraction * (Width - Left - 28);

        public void Show(SongChart chart, ChartBar bar, IReadOnlyList<string> tuning)
        {
            if (bar == first)
                return;
            first = bar;
            Clear();
            HasNotes = false;
            stringNames.Clear();
            if (bar == null)
            {
                MarkDirtyRepaint();
                return;
            }
            var next = bar.Index + 1 < chart.Bars.Count ? chart.Bars[bar.Index + 1] : null;
            firstAt = bar.AtQuarter;
            quarters = bar.Quarters + (next?.Quarters ?? 0);
            for (int s = 0; s < Strings; ++s)
            {
                var name = s < tuning.Count ? tuning[s].TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-') : "";
                var label = ChartLook.Label(s == Strings - 1 ? name.ToLowerInvariant() : name, "gt-tab__string", this);
                label.style.left = 0;
                label.style.width = Left - 8;
                label.style.top = LineY(s) - 10;
                label.style.height = 20;
            }
            foreach (var b in next == null ? new[] { bar } : new[] { bar, next })
                foreach (var n in b.Notes)
                {
                    HasNotes = true;
                    var label = ChartLook.Label(n.Note.Fret + n.Note.Technique, "gt-tab__fret", this);
                    label.style.left = X((b.AtQuarter + n.Offset - firstAt) / quarters);
                    label.style.translate = new Translate(Length.Percent(-50), 0);
                    label.style.top = LineY(n.Note.StringIndex) - 11;
                    label.style.height = 22;
                }
            MarkDirtyRepaint();
        }

        /// <summary>The position to mark (in quarter notes from the song's start; outside the bars shown: none).</summary>
        public void SetCursor(double quarter)
        {
            var c = first == null || quarter < firstAt || quarter >= firstAt + quarters ? -1f : (float)((quarter - firstAt) / quarters);
            if (Mathf.Abs(c - cursor) < 0.0005f)
                return;
            cursor = c;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var right = Width - 4;
            for (int s = 0; s < Strings; ++s)
                ChartLook.Line(p, new Vector2(Left, LineY(s)), new Vector2(right, LineY(s)), ChartLook.Faint, 1.5f);
            if (first == null)
                return;
            var barLine = X(first.Quarters / quarters) - 8;
            foreach (var x in new[] { Left, barLine, right })
                ChartLook.Line(p, new Vector2(x, LineY(Strings - 1)), new Vector2(x, LineY(0)), ChartLook.Dim, 2);
            if (cursor >= 0)
            {
                var x = X(cursor);
                ChartLook.Line(p, new Vector2(x, Top - 8), new Vector2(x, Height - Top + 8), ChartLook.Accent, 3);
            }
        }
    }
}
