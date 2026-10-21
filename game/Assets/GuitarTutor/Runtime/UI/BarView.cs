using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Music;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuitarTutor.Runtime.UI
{
    /// <summary>
    /// One bar of the chart strip: its beats, the chords at their positions over a band per chord, and the words
    /// sung in it. The current bar is large and has the beat cursor; the bars after it are smaller.
    /// </summary>
    public sealed class BarView : VisualElement
    {
        readonly Label number, section;
        readonly VisualElement beats, track;
        readonly SpreadRow chords, words;
        SongChart chart;
        ChartBar bar;
        TimeSignature signature = TimeSignature.FourFour;
        bool countIn;
        float cursor = -1;   // 0..1 through the bar, -1 hidden
        int beatNow = -1;

        public BarView(bool current)
        {
            AddToClassList("gt-bar");
            AddToClassList(current ? "gt-bar--current" : "gt-bar--next");
            var head = ChartLook.Box("gt-bar__head", this);
            number = ChartLook.Label("", "gt-bar__number", head);
            section = ChartLook.Label("", "gt-bar__section", head);
            beats = ChartLook.Box("gt-bar__beats", this);
            chords = new SpreadRow(ChartLook.Box("gt-bar__chords", this));
            track = ChartLook.Box("gt-bar__track", this);
            track.generateVisualContent += DrawTrack;
            words = new SpreadRow(ChartLook.Box("gt-bar__words", this));
        }

        public void Show(SongChart songChart, ChartBar shown)
        {
            if (shown == bar && !countIn && songChart == chart)
                return;
            chart = songChart;
            bar = shown;
            countIn = false;
            chords.Clear();
            words.Clear();
            if (bar == null)
            {
                number.text = section.text = "";
                SetBeats(TimeSignature.FourFour);
                track.MarkDirtyRepaint();
                return;
            }
            number.text = (bar.Index + 1).ToString();
            section.text = bar.StartsSection ? bar.Section.Name : "";
            SetBeats(bar.Signature);
            foreach (var c in bar.Chords)
                if (!c.CarriedIn)
                    chords.Add(c.Chord.Name, c.Offset / bar.Quarters, "gt-bar__chord");
                else if (bar.Chords.Count == 1 || c.Quarters >= 1)
                    chords.Add(c.Chord.Name, 0, "gt-bar__chord").AddToClassList("gt-bar__chord--held");
            foreach (var w in bar.Words)
                words.Add(w.Word.Text, w.Offset / bar.Quarters, "gt-bar__word");
            track.MarkDirtyRepaint();
        }

        /// <summary>A count-in bar: only the beats, counted out loud in the chord row.</summary>
        public void ShowCountIn(TimeSignature counted, int beat)
        {
            if (!countIn || bar != null)
            {
                bar = null;
                countIn = true;
                number.text = "";
                section.text = "Count-in";
                SetBeats(counted);
                chords.Clear();
                words.Clear();
                track.MarkDirtyRepaint();
            }
            var text = (beat + 1).ToString();
            if (chords.Items.Count == 0 || chords.Items[0].label.text != text)
            {
                chords.Clear();
                chords.Add(text, (double)beat / counted.Numerator, "gt-bar__chord");
            }
        }

        void SetBeats(TimeSignature shown)
        {
            if (shown.Equals(signature) && beats.childCount == shown.Numerator)
                return;
            signature = shown;
            beats.Clear();
            for (int b = 0; b < shown.Numerator; ++b)
            {
                var label = ChartLook.Label((b + 1).ToString(), "gt-bar__beat", beats);
                label.style.left = Length.Percent(100f * b / shown.Numerator);
            }
            beatNow = -1;
        }

        /// <summary>How far through the bar the music is (0..1), or -1 for no cursor.</summary>
        public void SetCursor(float fraction)
        {
            var beat = fraction < 0 ? -1 : Mathf.Min((int)(fraction * signature.Numerator), signature.Numerator - 1);
            if (beat != beatNow)
            {
                for (int b = 0; b < beats.childCount; ++b)
                    beats[b].EnableInClassList("gt-bar__beat--now", b == beat);
                beatNow = beat;
            }
            foreach (var (label, at) in words.Items)
                label.EnableInClassList("gt-bar__word--sung", fraction >= 0 && at <= fraction + 1e-4);
            if (Mathf.Abs(fraction - cursor) > 0.0005f)
            {
                cursor = fraction;
                track.MarkDirtyRepaint();
            }
        }

        void DrawTrack(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = track.contentRect;
            if (r.width <= 0)
                return;
            if (bar != null)
                foreach (var c in bar.Chords)
                {
                    var x0 = (float)(c.Offset / bar.Quarters) * r.width;
                    var x1 = (float)((c.Offset + c.Quarters) / bar.Quarters) * r.width;
                    var colour = ChartLook.ForChord(chart, c.Chord.Name);
                    ChartLook.FillRect(p, new Rect(x0 + 1, 6, x1 - x0 - 2, r.height - 12), new Color(colour.r, colour.g, colour.b, 0.38f));
                    ChartLook.FillRect(p, new Rect(x0 + 1, 6, x1 - x0 - 2, 3), colour);
                }
            // beats, and the eighths between them
            var steps = signature.Numerator * 2;
            for (int i = 0; i <= steps; ++i)
            {
                var x = Mathf.Clamp(r.width * i / steps, 1, r.width - 1);
                var beat = i % 2 == 0;
                ChartLook.Line(p, new Vector2(x, beat ? 0 : r.height * 0.35f), new Vector2(x, beat ? r.height : r.height * 0.65f),
                    i == 0 ? ChartLook.Dim : ChartLook.Faint, i == 0 ? 3 : beat ? 2 : 1);
            }
            if (cursor >= 0)
            {
                var x = cursor * r.width;
                ChartLook.FillRect(p, new Rect(0, 0, x, r.height), new Color(1, 1, 1, 0.08f));
                ChartLook.Line(p, new Vector2(x, -4), new Vector2(x, r.height + 4), ChartLook.Accent, 4);
            }
        }
    }
}
