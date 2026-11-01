using System;
using System.Collections.Generic;
using System.Linq;
using GuitarTutor.Core.Band;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Songs;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuitarTutor.Runtime.UI
{
    /// <summary>
    /// The song's tracks bar by bar, over the chart (M), as TuxGuitar shows them: a row per track of the Guitar Pro file,
    /// a cell per bar, brighter the more the track plays in it, with the sections' names above and where the music is.
    /// Each track says which of the band plays it. Clicking a cell goes to that bar (<see cref="BarChosen"/>).
    /// </summary>
    public sealed class TrackMatrix : VisualElement
    {
        const float NameWidth = 280, RowHeight = 24, CellGap = 1;
        const float CharWidth = 7.5f;  // about a section name's character at the size it is drawn, for leaving out ones that would overlap

        readonly VisualElement names, area, sectionLayer, grid, cursor;
        readonly Label empty;
        readonly List<(SongTrack track, int[] counts, Color colour, Label role)> rows = new List<(SongTrack, int[], Color, Label)>();
        SongChart chart;

        /// <summary>Called with the bar clicked and the track whose row it is in.</summary>
        public event Action<int, SongTrack> BarChosen;

        public TrackMatrix()
        {
            AddToClassList("gt-matrix");
            AddToClassList("gt-hidden");
            var head = ChartLook.Box("gt-matrix__head", this);
            ChartLook.Label("TRACKS", "gt-panel__title", head);
            ChartLook.Label("click a bar to go there and see that track's tab   ·   M to close", "gt-matrix__hint", head);
            empty = ChartLook.Label("This song has no tracks (only a Guitar Pro file has them)", "gt-panel__empty", this);
            var body = ChartLook.Box("gt-matrix__body", this);
            names = ChartLook.Box("gt-matrix__names", body);
            names.style.width = NameWidth;
            area = ChartLook.Box("gt-matrix__area", body);
            sectionLayer = ChartLook.Box("gt-matrix__sections", area);
            grid = ChartLook.Box("gt-matrix__grid", area);
            grid.generateVisualContent += Draw;
            grid.RegisterCallback<PointerDownEvent>(Click);
            cursor = ChartLook.Box("gt-matrix__cursor", area);
            area.RegisterCallback<GeometryChangedEvent>(_ => PlaceSections());
        }

        public bool IsOpen => !ClassListContains("gt-hidden");

        public void SetOpen(bool open) => EnableInClassList("gt-hidden", !open);

        public void SetSong(SongChart songChart)
        {
            chart = songChart;
            rows.Clear();
            names.Clear();
            ChartLook.Box("gt-matrix__corner", names);  // beside the section names
            var song = chart.Song;
            for (int i = 0; i < song.Tracks.Count; ++i)
            {
                var track = song.Tracks[i];
                var row = ChartLook.Box("gt-matrix__row", names);
                ChartLook.Label(track.Name, "gt-matrix__name", row).style.color = ChartLook.ForTrack(i);
                var role = ChartLook.Label("", "gt-matrix__role", row);
                // a doubling track plays as its twin does
                rows.Add((track, chart.NotesPerBar(BandConnections.Played(song, track)), ChartLook.ForTrack(i), role));
            }
            grid.style.height = rows.Count * RowHeight;
            empty.EnableInClassList("gt-hidden", rows.Count > 0);
            area.EnableInClassList("gt-hidden", rows.Count == 0);
            PlaceSections();
            grid.MarkDirtyRepaint();
        }

        /// <summary>Which of the band plays each track ("rhythm guitar", "bass"...); a track missing plays for nobody.</summary>
        public void SetPlayers(IReadOnlyDictionary<SongTrack, string> players)
        {
            foreach (var (track, _, _, role) in rows)
                role.text = players != null && players.TryGetValue(track, out var who) ? who : "";
        }

        /// <summary>Where the music is (quarter notes).</summary>
        public void Show(double quarter)
        {
            if (chart == null || chart.Bars.Count == 0 || !IsOpen)
                return;
            var bar = chart.BarOf(Math.Max(0, Math.Min(quarter, chart.EndQuarter - 1e-6)));
            var along = bar == null ? 0 : bar.Index + (Math.Max(0, quarter) - bar.AtQuarter) / Math.Max(1e-6, bar.Quarters);
            cursor.style.left = Length.Percent((float)(Math.Max(0, Math.Min(1, along / chart.Bars.Count)) * 100));
        }

        /// <summary>
        /// The sections' names over the bars they start at, each left out where the one before, or the end of the
        /// matrix, leaves no room.
        /// </summary>
        void PlaceSections()
        {
            sectionLayer.Clear();
            var width = area.contentRect.width;
            if (chart == null || chart.Bars.Count == 0 || float.IsNaN(width) || width <= 0)
                return;
            var right = float.MinValue;
            foreach (var span in chart.Sections)
            {
                var x = width * span.FirstBar / chart.Bars.Count;
                var end = x + span.Section.Name.Length * CharWidth;
                if (x < right || end > width)
                    continue;
                ChartLook.Label(span.Section.Name, "gt-matrix__section", sectionLayer).style.left = x;
                right = end + 10;
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            if (chart == null || chart.Bars.Count == 0)
                return;
            var p = ctx.painter2D;
            var width = grid.contentRect.width;
            var cell = width / chart.Bars.Count;
            for (int r = 0; r < rows.Count; ++r)
            {
                var (_, counts, colour, _) = rows[r];
                var top = r * RowHeight;
                for (int b = 0; b < counts.Length; ++b)
                {
                    var rect = new Rect(b * cell, top + CellGap, Mathf.Max(1, cell - CellGap), RowHeight - 2 * CellGap);
                    if (counts[b] == 0)
                        ChartLook.FillRect(p, rect, new Color(1, 1, 1, 0.05f));
                    else
                        ChartLook.FillRect(p, rect, new Color(colour.r, colour.g, colour.b, 0.35f + 0.65f * Mathf.Min(1, counts[b] / 8f)));
                }
            }
            // the sections' starts, across the rows
            foreach (var span in chart.Sections.Skip(1))
            {
                var x = width * span.FirstBar / chart.Bars.Count - CellGap / 2;
                ChartLook.Line(p, new Vector2(x, 0), new Vector2(x, rows.Count * RowHeight), ChartLook.Dim, 1);
            }
        }

        void Click(PointerDownEvent e)
        {
            var width = grid.contentRect.width;
            if (chart == null || chart.Bars.Count == 0 || width <= 0 || rows.Count == 0)
                return;
            var bar = Mathf.Clamp((int)(e.localPosition.x / width * chart.Bars.Count), 0, chart.Bars.Count - 1);
            var row = Mathf.Clamp((int)(e.localPosition.y / RowHeight), 0, rows.Count - 1);
            BarChosen?.Invoke(bar, rows[row].track);
            e.StopPropagation();
        }
    }
}
