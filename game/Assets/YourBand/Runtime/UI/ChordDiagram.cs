using System.Linq;
using YourBand.Core.Songs;
using UnityEngine;
using UnityEngine.UIElements;

namespace YourBand.Runtime.UI
{
    /// <summary>
    /// A chord box drawn from a voicing: strings upright with the lowest on the left, the nut (or the first fret
    /// shown, numbered) at the top, a dot per fretted string with its finger, the barre, and x / o above muted and
    /// open strings.
    /// </summary>
    public sealed class ChordDiagram : VisualElement
    {
        const float Width = 130, Height = 194;
        const float X0 = 26, StringGap = 19, Y0 = 62, FretGap = 25, Radius = 9;
        const int FretsShown = 5;
        const float TitleSize = 26;  // (as in Chart.uss)

        Voicing voicing;
        int firstFret = 1;
        readonly Label title;

        public ChordDiagram()
        {
            style.width = Width;
            style.height = Height;
            title = ChartLook.Label("", "gt-diagram__name", this);
            generateVisualContent += Draw;
        }

        int Strings => voicing?.Frets.Count ?? 6;

        public void Show(string chord, Voicing shape)
        {
            voicing = shape;
            title.text = chord;
            // (a long name, Dsus4add9/F#, made smaller to stay within its box: a character is about 0.58 of the font's size)
            title.style.fontSize = Mathf.Min(TitleSize, (Width - 6) / (0.58f * Mathf.Max(1, chord?.Length ?? 1)));
            foreach (var child in Children().Where(c => c != title).ToList())
                Remove(child);

            if (voicing != null)
            {
                var fretted = voicing.Frets.Where(f => f > 0).ToList();
                var top = fretted.Count > 0 ? fretted.Max() : 0;
                firstFret = top <= FretsShown ? 1 : fretted.Min();
                if (firstFret > 1)
                    Place(ChartLook.Label(firstFret.ToString(), "gt-diagram__fret", this), 0, Y0 + FretGap * 0.5f - 10, X0 - 8, 20);
                for (int s = 0; s < Strings; ++s)
                {
                    var fret = voicing.Frets[s];
                    if (fret <= 0)
                        Place(ChartLook.Label(fret < 0 ? "x" : "o", "gt-diagram__mark", this), StringX(s) - 9, Y0 - 21, 18, 18);
                    else if (!UnderBarre(s, fret, out _))
                    {
                        var finger = s < voicing.Fingers.Count ? voicing.Fingers[s] : 0;
                        if (finger > 0 && FretRow(fret) >= 0)
                            Place(ChartLook.Label(finger.ToString(), "gt-diagram__finger", this), StringX(s) - Radius, FretY(fret) - Radius, Radius * 2, Radius * 2);
                    }
                }
                var b = voicing.Barre;
                if (b != null && FretRow(b.Fret) >= 0)
                {
                    var middle = (StringX(b.FirstString) + StringX(b.LastString)) / 2;
                    Place(ChartLook.Label(b.Finger.ToString(), "gt-diagram__finger", this), middle - Radius, FretY(b.Fret) - Radius, Radius * 2, Radius * 2);
                }
            }
            MarkDirtyRepaint();
        }

        static void Place(VisualElement e, float left, float top, float width, float height)
        {
            e.style.left = left;
            e.style.top = top;
            e.style.width = width;
            e.style.height = height;
        }

        float StringX(int s) => X0 + s * StringGap * 5f / Mathf.Max(1, Strings - 1);

        int FretRow(int fret) => fret - firstFret < FretsShown ? fret - firstFret : -1;

        float FretY(int fret) => Y0 + (FretRow(fret) + 0.5f) * FretGap;

        bool UnderBarre(int s, int fret, out Barre barre)
        {
            barre = voicing?.Barre;
            return barre != null && barre.Fret == fret && s >= barre.FirstString && s <= barre.LastString
                   && (s >= voicing.Fingers.Count || voicing.Fingers[s] == 0 || voicing.Fingers[s] == barre.Finger);
        }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.lineCap = LineCap.Butt;
            var right = StringX(Strings - 1);
            var bottom = Y0 + FretsShown * FretGap;
            // frets, the nut thick when it is shown
            for (int f = 0; f <= FretsShown; ++f)
            {
                var y = Y0 + f * FretGap;
                var nut = f == 0 && firstFret == 1;
                ChartLook.Line(p, new Vector2(X0 - (nut ? 1.5f : 0), y), new Vector2(right + (nut ? 1.5f : 0), y), nut ? ChartLook.Text : ChartLook.Faint, nut ? 5 : 1.5f);
            }
            for (int s = 0; s < Strings; ++s)
                ChartLook.Line(p, new Vector2(StringX(s), Y0), new Vector2(StringX(s), bottom), ChartLook.Dim, 1 + (Strings - 1 - s) * 0.25f);
            if (voicing == null)
                return;

            var barre = voicing.Barre;
            if (barre != null && FretRow(barre.Fret) >= 0)
            {
                p.lineCap = LineCap.Round;
                ChartLook.Line(p, new Vector2(StringX(barre.FirstString), FretY(barre.Fret)), new Vector2(StringX(barre.LastString), FretY(barre.Fret)), ChartLook.Dot, Radius * 2);
                p.lineCap = LineCap.Butt;
            }
            for (int s = 0; s < Strings; ++s)
            {
                var fret = voicing.Frets[s];
                if (fret > 0 && FretRow(fret) >= 0 && !UnderBarre(s, fret, out _))
                    ChartLook.Disc(p, new Vector2(StringX(s), FretY(fret)), Radius, ChartLook.Dot);
            }
        }
    }
}
