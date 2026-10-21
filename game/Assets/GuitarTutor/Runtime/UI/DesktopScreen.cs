using System;
using System.Collections.Generic;
using System.Linq;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Songs;
using UnityEngine.UIElements;

namespace GuitarTutor.Runtime.UI
{
    /// <summary>What the screen shows besides the song position: the transport's state.</summary>
    public readonly struct TransportState
    {
        public bool Playing { get; }
        public double Speed { get; }
        public double SongSeconds { get; }

        public TransportState(bool playing, double speed, double songSeconds)
        {
            Playing = playing;
            Speed = speed;
            SongSeconds = songSeconds;
        }
    }

    /// <summary>
    /// The desktop screen (see docs/plan.md, "Desktop mode"): a header with the song and where you are, the panels
    /// (chords, strumming, tab, song info: each can be toggled), the chart strip, and a help overlay. The middle
    /// stays clear for the band once it is there (M6).
    /// </summary>
    public sealed class DesktopScreen : VisualElement
    {
        public enum Panel { Chords, Strumming, Tab, Song }

        public const int MaxChords = 6;

        public ChartStrip Strip { get; }

        readonly Label title, artist, status;
        readonly VisualElement help;
        readonly Dictionary<Panel, VisualElement> panels = new Dictionary<Panel, VisualElement>();
        readonly Dictionary<Panel, Label> panelTitles = new Dictionary<Panel, Label>();
        readonly VisualElement chordRow, info;
        readonly StrumView strum;
        readonly Label strumName, strumEmpty, tabEmpty;
        readonly TabView tab;
        readonly List<(string name, VisualElement card, Label tag)> cards = new List<(string, VisualElement, Label)>();
        SongChart chart;

        public DesktopScreen()
        {
            AddToClassList("gt-root");
            var header = ChartLook.Box("gt-header", this);
            title = ChartLook.Label("", "gt-title", header);
            artist = ChartLook.Label("", "gt-artist", header);
            status = ChartLook.Label("", "gt-status", header);

            var middle = ChartLook.Box("gt-middle", this);
            chordRow = ChartLook.Box("gt-panel__row", AddPanel(Panel.Chords, "CHORDS", middle));
            var strumPanel = AddPanel(Panel.Strumming, "STRUMMING", middle);
            strum = new StrumView();
            strumPanel.Add(strum);
            strumName = ChartLook.Label("", "gt-strum__name", strumPanel);
            strumEmpty = ChartLook.Label("No strumming pattern in this song", "gt-panel__empty", strumPanel);
            info = AddPanel(Panel.Song, "SONG", middle);
            var tabPanel = AddPanel(Panel.Tab, "TAB", middle);
            tab = new TabView();
            tabPanel.Add(tab);
            tabEmpty = ChartLook.Label("No tab in these bars", "gt-panel__empty", tabPanel);

            Strip = new ChartStrip();
            Add(Strip);
            ChartLook.Label(string.Join("   ·   ", "Space play / pause", "R restart", "Left / Right bar", "PgUp / PgDn section", "Up / Down speed", "1-4 panels", "P pattern", "H help"),
                "gt-hint", this);

            help = ChartLook.Box("gt-help", this);
            foreach (var (keys, what) in new[]
                     {
                         ("Space", "play, or pause (playing again counts you in)"),
                         ("R", "restart from the top, with a count-in"),
                         ("Home", "back to the top"),
                         ("Left / Right", "a bar back / on"),
                         ("PgUp / PgDn", "the section before / after"),
                         ("Up / Down", "speed up / slow down by 5%"),
                         ("1  2  3  4", "chords, strumming, tab, song panels"),
                         ("P", "next strumming pattern"),
                         ("L", "next song"),
                         ("H", "this help"),
                         ("Esc", "quit"),
                     })
            {
                var row = ChartLook.Box("gt-help__row", help);
                ChartLook.Label(keys, "gt-help__keys", row);
                ChartLook.Label(what, "gt-help__what", row);
            }
            help.AddToClassList("gt-hidden");
        }

        VisualElement AddPanel(Panel which, string heading, VisualElement parent)
        {
            var panel = ChartLook.Box("gt-panel", parent);
            panelTitles[which] = ChartLook.Label(heading, "gt-panel__title", panel);
            panels[which] = panel;
            return panel;
        }

        public bool IsShown(Panel which) => !panels[which].ClassListContains("gt-hidden");

        public void SetShown(Panel which, bool shown) => panels[which].EnableInClassList("gt-hidden", !shown);

        public void ToggleHelp() => help.ToggleInClassList("gt-hidden");

        public void SetSong(SongChart songChart)
        {
            chart = songChart;
            var song = chart.Song;
            title.text = song.Info.Title;
            artist.text = song.Info.Artist;
            Strip.SetSong(chart);
            cards.Clear();
            chordRow.Clear();
            strum.Show(null);

            info.Clear();
            info.Add(panelTitles[Panel.Song]);
            void Row(string key, string value)
            {
                var row = ChartLook.Box("gt-info__row", info);
                ChartLook.Label(key, "gt-info__key", row);
                ChartLook.Label(value, "gt-info__value", row);
            }
            Row("Key", string.IsNullOrEmpty(song.Info.Key) ? "not given" : song.Info.Key);
            Row("Capo", song.Info.Capo > 0 ? "fret " + song.Info.Capo : "none");
            Row("Tuning", Tuning(song.Info.Tuning));
            var bpm = song.Tempo.BpmAt(0);
            var changes = song.Tempo.Tempos.Count > 1 ? " (changes)" : "";
            Row("Tempo", $"{bpm:0.#} BPM{changes}");
            Row("Time", string.Join(", ", song.Tempo.Meters.Select(m => m.Signature.ToString()).Distinct()));
            Row("Length", $"{chart.Bars.Count} bars, {Clock(song.Tempo.SecondsAt(chart.EndQuarter))}");
        }

        static string Tuning(IReadOnlyList<string> strings)
        {
            var notes = string.Join(" ", strings.Select(s => s.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-')));
            return string.Join(" ", strings) == "E2 A2 D3 G3 B3 E4" ? "standard (" + notes + ")" : notes;
        }

        static string Clock(double seconds)
        {
            var s = (int)Math.Floor(Math.Max(0, seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        /// <param name="pattern">Which of the section's strumming patterns to show.</param>
        public void Show(double quarter, CountIn? countIn, TransportState transport, int pattern)
        {
            if (chart == null)
                return;
            var song = chart.Song;
            var at = countIn is CountIn c && c.TargetBar < chart.Bars.Count ? chart.Bars[c.TargetBar].AtQuarter : Math.Max(quarter, 0);
            var bar = chart.BarOf(at) ?? (chart.Bars.Count > 0 ? chart.Bars[chart.Bars.Count - 1] : null);
            var section = song.SectionAt(at);

            var state = countIn != null ? "count-in" : transport.Playing ? "playing" : "paused";
            var speed = Math.Abs(transport.Speed - 1) > 1e-6 ? $" at {transport.Speed * 100:0}%" : "";
            status.text = $"{section?.Name ?? "-"}   ·   bar {(bar?.Index ?? 0) + 1} of {chart.Bars.Count}   ·   "
                          + $"{song.Tempo.BpmAt(at) * transport.Speed:0} BPM{speed}   ·   "
                          + $"{Clock(transport.SongSeconds)} / {Clock(song.Tempo.SecondsAt(chart.EndQuarter))}   ·   {state}";
            status.EnableInClassList("gt-status--playing", transport.Playing);

            Strip.Show(quarter, countIn);
            if (IsShown(Panel.Chords))
                ShowChords(at, countIn == null && quarter >= 0);
            if (IsShown(Panel.Strumming))
            {
                var patterns = chart.PatternsFor(section);
                var shown = patterns.Count > 0 ? patterns[((pattern % patterns.Count) + patterns.Count) % patterns.Count] : null;
                strum.Show(shown);
                strum.EnableInClassList("gt-hidden", shown == null);
                strumEmpty.EnableInClassList("gt-hidden", shown != null);
                strumName.text = shown == null ? "" : patterns.Count > 1 ? $"{shown.Name}  ({pattern % patterns.Count + 1} of {patterns.Count}, P for the next)" : shown.Name;
                strum.SetNow(countIn == null && transport.Playing ? chart.StrokeAt(shown, quarter).index : -1);
            }
            if (IsShown(Panel.Tab))
            {
                tab.Show(chart, bar, song.Info.Tuning);
                tab.SetCursor(countIn == null ? quarter : -1);
                var riff = bar?.Notes.FirstOrDefault().Riff ?? (bar != null && bar.Index + 1 < chart.Bars.Count ? chart.Bars[bar.Index + 1].Notes.FirstOrDefault().Riff : null);
                panelTitles[Panel.Tab].text = riff != null ? "TAB  ·  " + riff.Name.ToUpperInvariant() : "TAB";
                tabEmpty.EnableInClassList("gt-hidden", tab.HasNotes);
                tab.EnableInClassList("gt-hidden", !tab.HasNotes);
            }
        }

        void ShowChords(double at, bool started)
        {
            var now = chart.Song.ChordAt(at);
            // before the music starts, the chord coming is the one it starts on
            var next = started ? chart.NextChange(at) : now ?? chart.NextChange(at);
            // the song's chords if they fit, otherwise the one sounding and the ones coming up
            var names = chart.ChordNames.Count <= MaxChords
                ? chart.ChordNames.ToList()
                : new[] { now?.Name }.Concat(chart.Song.Chords.Where(c => c.AtQuarter > at).Select(c => c.Name))
                    .Where(n => n != null).Distinct().Take(MaxChords).ToList();
            if (!names.SequenceEqual(cards.Select(c => c.name)))
            {
                chordRow.Clear();
                cards.Clear();
                foreach (var name in names)
                {
                    var card = ChartLook.Box("gt-chord-card", chordRow);
                    card.style.borderTopColor = ChartLook.ForChord(chart, name);
                    var diagram = new ChordDiagram();
                    var chord = chart.Song.Chords.First(c => c.Name == name);
                    diagram.Show(name, chart.Song.VoicingFor(chord));
                    card.Add(diagram);
                    cards.Add((name, card, ChartLook.Label("", "gt-chord-card__tag", card)));
                }
            }
            foreach (var (name, card, tag) in cards)
            {
                var isNow = now != null && name == now.Name && started;
                var isNext = next != null && name == next.Name;
                card.EnableInClassList("gt-chord-card--now", isNow);
                card.EnableInClassList("gt-chord-card--next", isNext && !isNow);
                tag.text = isNow ? "now" : isNext ? "next" : "";
            }
        }
    }
}
