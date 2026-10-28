using UnityEngine;
using UnityEngine.UIElements;

namespace GuitarTutor.Runtime.UI
{
    /// <summary>
    /// Covers the window with a dimmed panel, a circling ring and a line saying what is awaited. It takes the mouse
    /// (nothing below it is reached) while it is shown; the game itself ignores its keys meanwhile.
    /// </summary>
    public sealed class LoadingOverlay : VisualElement
    {
        readonly Spinner spinner = new Spinner();
        readonly Label message;

        public LoadingOverlay()
        {
            AddToClassList("gt-loading");
            pickingMode = PickingMode.Position;
            Add(spinner);
            message = ChartLook.Label("", "gt-loading__text", this);
            AddToClassList("gt-hidden");
        }

        /// <summary>Shows the overlay with this line, or hides it (null).</summary>
        public void Show(string text)
        {
            var shown = text != null;
            EnableInClassList("gt-hidden", !shown);
            spinner.Running = shown;
            if (shown)
                message.text = text;
        }

        /// <summary>A ring with a bright arc going around it.</summary>
        sealed class Spinner : VisualElement
        {
            const float Size = 84, Width = 8, TurnsPerSecond = 0.9f, ArcDegrees = 100;
            static readonly Color Track = new Color(1, 1, 1, 0.12f);
            static readonly Color Arc = new Color32(0xf2, 0xa9, 0x3b, 0xff);  // the accent colour of Chart.uss
            IVisualElementScheduledItem tick;

            public Spinner()
            {
                style.width = Size;
                style.height = Size;
                generateVisualContent += Draw;
            }

            public bool Running
            {
                set
                {
                    if (value && tick == null)
                        tick = schedule.Execute(MarkDirtyRepaint).Every(16);
                    else if (!value && tick != null)
                    {
                        tick.Pause();
                        tick = null;
                    }
                }
            }

            void Draw(MeshGenerationContext mgc)
            {
                var p = mgc.painter2D;
                var centre = new Vector2(Size / 2, Size / 2);
                var radius = Size / 2 - Width;
                p.lineWidth = Width;
                p.lineCap = LineCap.Round;
                p.strokeColor = Track;
                p.BeginPath();
                p.Arc(centre, radius, Angle.Degrees(0), Angle.Degrees(360), ArcDirection.Clockwise);
                p.Stroke();
                var from = Time.realtimeSinceStartup * TurnsPerSecond * 360f;
                p.strokeColor = Arc;
                p.BeginPath();
                p.Arc(centre, radius, Angle.Degrees(from), Angle.Degrees(from + ArcDegrees), ArcDirection.Clockwise);
                p.Stroke();
            }
        }
    }
}
