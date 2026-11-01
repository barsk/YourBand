using System.Collections.Generic;
using GuitarTutor.Core.Chart;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuitarTutor.Runtime.UI
{
    /// <summary>Colours the chart draws with (the rest of the look is in Chart.uss), and small layout helpers.</summary>
    static class ChartLook
    {
        public static readonly Color Text = new Color32(0xe8, 0xea, 0xed, 0xff);
        public static readonly Color Dim = new Color32(0x8b, 0x93, 0x9d, 0xff);
        public static readonly Color Faint = new Color32(0x5a, 0x62, 0x6c, 0xff);
        public static readonly Color Accent = new Color32(0xf2, 0xa9, 0x3b, 0xff);
        public static readonly Color Dot = new Color32(0xe8, 0xea, 0xed, 0xff);

        // one colour per chord of the song, in the order they first appear: the bars' chord bands and the
        // diagrams' top edges match
        static readonly Color[] ChordColours =
        {
            new Color32(0x5b, 0x8f, 0xd6, 0xff), new Color32(0x62, 0xb5, 0x6a, 0xff), new Color32(0xe0, 0x6c, 0x6c, 0xff),
            new Color32(0xb8, 0x8a, 0xd9, 0xff), new Color32(0x4f, 0xbd, 0xba, 0xff), new Color32(0xe3, 0xc5, 0x4f, 0xff),
            new Color32(0xe8, 0x93, 0xb8, 0xff), new Color32(0xa8, 0x86, 0x6a, 0xff),
        };

        public static Color ForChord(SongChart chart, string name)
        {
            for (int i = 0; i < chart.ChordNames.Count; ++i)
                if (chart.ChordNames[i] == name)
                    return ChordColours[i % ChordColours.Length];
            return Faint;
        }

        /// <summary>A colour per track of the song, in its order (the chords' colours).</summary>
        public static Color ForTrack(int index) => ChordColours[index % ChordColours.Length];

        public static Label Label(string text, string className, VisualElement parent = null)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            parent?.Add(label);
            return label;
        }

        public static VisualElement Box(string className, VisualElement parent = null)
        {
            var box = new VisualElement();
            box.AddToClassList(className);
            parent?.Add(box);
            return box;
        }

        public static void FillRect(Painter2D p, Rect r, Color colour)
        {
            p.fillColor = colour;
            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMax));
            p.LineTo(new Vector2(r.xMin, r.yMax));
            p.ClosePath();
            p.Fill();
        }

        public static void Line(Painter2D p, Vector2 from, Vector2 to, Color colour, float width)
        {
            p.strokeColor = colour;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(from);
            p.LineTo(to);
            p.Stroke();
        }

        public static void Disc(Painter2D p, Vector2 centre, float radius, Color colour)
        {
            p.fillColor = colour;
            p.BeginPath();
            p.Arc(centre, radius, Angle.Degrees(0), Angle.Degrees(360), ArcDirection.Clockwise);
            p.Fill();
        }
    }

    /// <summary>
    /// Labels laid out along a line at musical positions (0..1 of the width), pushed right where they would
    /// overlap: chords and words in a bar. Positions are set once the labels know their size.
    /// </summary>
    sealed class SpreadRow
    {
        readonly VisualElement layer;
        readonly List<(Label label, double at)> items = new List<(Label, double)>();
        const float Gap = 8, SmallestScale = 0.55f;
        float scale = 1, baseSize;

        public SpreadRow(VisualElement layer)
        {
            this.layer = layer;
            layer.RegisterCallback<GeometryChangedEvent>(_ => Spread());
        }

        public void Clear()
        {
            items.Clear();
            layer.Clear();
            scale = 1;
        }

        public Label Add(string text, double at, string className)
        {
            var label = ChartLook.Label(text, className, layer);
            label.style.left = (float)(at * Mathf.Max(layer.layout.width, 0));
            label.RegisterCallback<GeometryChangedEvent>(_ => Spread());
            items.Add((label, at));
            return label;
        }

        public IReadOnlyList<(Label label, double at)> Items => items;

        void Spread()
        {
            var width = layer.layout.width;
            if (float.IsNaN(width) || width <= 0)
                return;
            var right = float.MinValue;
            foreach (var (label, at) in items)
            {
                var left = Mathf.Max((float)(at * width), right + Gap);
                if (Mathf.Abs(label.resolvedStyle.left - left) > 0.5f)
                    label.style.left = left;
                var w = label.layout.width;
                right = left + (float.IsNaN(w) ? 0 : w);
            }
            // too many to fit (a busy line in a small bar): smaller text, until they do
            if (items.Count > 0 && right > width + 1 && scale > SmallestScale)
            {
                if (scale == 1)
                    baseSize = items[0].label.resolvedStyle.fontSize;
                scale = Mathf.Max(SmallestScale, scale * width / right * 0.98f);
                foreach (var (label, _) in items)
                    label.style.fontSize = baseSize * scale;
            }
        }
    }
}
