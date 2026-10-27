using System;
using System.Collections.Generic;
using System.Linq;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Music;
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
        /// <summary>What the band is doing, in a few words ("band: Ballad", "no band").</summary>
        public string Band { get; }

        public TransportState(bool playing, double speed, double songSeconds, string band)
        {
            Playing = playing;
            Speed = speed;
            SongSeconds = songSeconds;
            Band = band;
        }
    }

    /// <summary>
    /// The desktop screen (see docs/plan.md, "Desktop mode"): a header with the song and where you are, the panels
    /// (chords, strumming, song info), the tab across the screen, the chart's rows, and a help overlay. The panels
    /// and the tab can be toggled. The middle stays clear for the band once it is there (M6).
    /// </summary>
    public sealed class DesktopScreen : VisualElement
    {
        public enum Panel { Chords, Strumming, Tab, Song }

        public const int MaxChords = 6;

        public ChartStrip Strip { get; }

        readonly Label title, artist, status, hint, vrHint;
        readonly VisualElement help, vrHelp;
        bool vr;
        readonly Dictionary<Panel, VisualElement> panels = new Dictionary<Panel, VisualElement>();
        readonly Dictionary<Panel, Label> panelTitles = new Dictionary<Panel, Label>();
        const float PanelGap = 16, InfoWidth = 330;  // the panels' right margin; the standing song panel's width

        readonly VisualElement chordRow, info;
        readonly StrumView strum;
        readonly Label strumName, strumEmpty, strumOrigin;
        readonly TabTape tab;
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
            var chordsPanel = AddPanel(Panel.Chords, "CHORDS", middle);
            chordRow = ChartLook.Box("gt-panel__row", chordsPanel);
            var strumPanel = AddPanel(Panel.Strumming, "STRUMMING", middle);
            strumPanel.style.minWidth = 480;  // room for the pattern's name beside where it is from, whatever its length
            strum = new StrumView();
            strumPanel.Add(strum);
            strumName = ChartLook.Label("", "gt-strum__name", strumPanel);
            strumEmpty = ChartLook.Label("No strumming pattern in this song", "gt-panel__empty", strumPanel);
            strumOrigin = ChartLook.Label("", "gt-strum__origin", strumPanel);
            info = AddPanel(Panel.Song, "SONG", middle);

            // the song panel wraps under the others when they leave no room for it (six chords and a long pattern):
            // there it lies down, one row, rather than a tall panel that would push the tab strip over it
            void FitInfo(GeometryChangedEvent _)
            {
                float Taken(VisualElement p) => IsShown(p == chordsPanel ? Panel.Chords : Panel.Strumming) && !float.IsNaN(p.layout.width) ? p.layout.width + PanelGap : 0;
                var room = middle.contentRect.width;
                if (float.IsNaN(room) || room <= 0)
                    return;
                info.EnableInClassList("gt-panel--lying", Taken(chordsPanel) + Taken(strumPanel) + InfoWidth + PanelGap > room);
            }
            middle.RegisterCallback<GeometryChangedEvent>(FitInfo);
            chordsPanel.RegisterCallback<GeometryChangedEvent>(FitInfo);
            strumPanel.RegisterCallback<GeometryChangedEvent>(FitInfo);

            // the tab, across the screen above the chart
            var tabStrip = ChartLook.Box("gt-tabstrip", this);
            panels[Panel.Tab] = tabStrip;
            panelTitles[Panel.Tab] = ChartLook.Label("TAB", "gt-panel__title", tabStrip);
            tab = new TabTape();
            tabStrip.Add(tab);

            Strip = new ChartStrip();
            Add(Strip);
            hint = ChartLook.Label(string.Join("   ·   ", "Space play / pause", "R restart", "Left / Right bar", "PgUp / PgDn section", "Up / Down speed", "1-4 panels", "P pattern", "B band style", "G band guitarist", "V band view", "X VR", "C control panel", "T tone", "H help"),
                "gt-hint", this);
            vrHint = ChartLook.Label(string.Join("   ·   ", "A play / pause", "B restart", "right stick: bar, speed", "left stick: section", "trigger: move the chart or a musician, grip: the band", "right stick click: in front of you", "left stick click: band guitarist", "left stick up: help"),
                "gt-hint", this);
            vrHint.AddToClassList("gt-hidden");

            help = Help(new[]
                     {
                         ("Space", "play or pause; after a pause it plays on from where it stopped"),
                         ("R", "restart from the top, with a count-in"),
                         ("Home", "back to the top"),
                         ("Left / Right", "a bar back / on"),
                         ("PgUp / PgDn", "the section before / after"),
                         ("Up / Down", "speed up / slow down by 5%"),
                         ("1  2  3  4", "chords, strumming, tab, song panels"),
                         ("P", "next strumming pattern"),
                         ("B", "next band style"),
                         ("G", "band guitarist plays the rhythm, or rests (also in the control panel)"),
                         ("V", "band view: the band in 3D behind the chart, or the chart alone"),
                         ("X", "VR mode: the band in your room, in the headset (Virtual Desktop)"),
                         ("L", "next song"),
                         ("C", "control panel: audio device, band faders"),
                         ("T", "your guitar's tone (TONE3000)"),
                         ("H", "this help"),
                         ("Esc", "quit"),
                     });
            vrHelp = Help(new[]
                     {
                         ("A", "play or pause"),
                         ("B", "restart from the top, with a count-in"),
                         ("Right stick", "left / right: a bar back / on; up / down: speed"),
                         ("Left stick", "left / right: the section before / after; up: this help"),
                         ("Trigger", "hold, aimed at the chart or at a musician, to move it (or them)"),
                         ("Grip", "hold, aimed at a musician, to move the whole band"),
                         ("", "meanwhile the stick sizes the chart; or pushes, pulls and turns what you hold"),
                         ("Right stick click", "the band and the chart back in front of you"),
                         ("Left stick click", "the band guitarist plays the rhythm, or rests"),
                         ("X", "next key colour for the passthrough (magenta, green, blue, black)"),
                         ("Y", "with the left controller resting on the floor: the floor's height"),
                         ("Keyboard X", "back to the desktop"),
                     });
        }

        VisualElement Help((string keys, string what)[] rows)
        {
            var box = ChartLook.Box("gt-help", this);
            foreach (var (keys, what) in rows)
            {
                var row = ChartLook.Box("gt-help__row", box);
                ChartLook.Label(keys, "gt-help__keys", row);
                ChartLook.Label(what, "gt-help__what", row);
            }
            box.AddToClassList("gt-hidden");
            return box;
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

        public void ToggleHelp() => (vr ? vrHelp : help).ToggleInClassList("gt-hidden");

        /// <summary>The controllers' hints and help (VR mode), or the keyboard's.</summary>
        public void SetVr(bool on)
        {
            vr = on;
            hint.EnableInClassList("gt-hidden", on);
            vrHint.EnableInClassList("gt-hidden", !on);
            help.AddToClassList("gt-hidden");
            vrHelp.AddToClassList("gt-hidden");
        }

        public void SetSong(SongChart songChart)
        {
            chart = songChart;
            var song = chart.Song;
            title.text = song.Info.Title;
            artist.text = song.Info.Artist;
            Strip.SetSong(chart);
            tab.SetSong(chart, song.Info.Tuning);
            cards.Clear();
            chordRow.Clear();
            strum.Show(null, TimeSignature.FourFour);

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
                          + $"{Clock(transport.SongSeconds)} / {Clock(song.Tempo.SecondsAt(chart.EndQuarter))}   ·   {state}"
                          + (string.IsNullOrEmpty(transport.Band) ? "" : "   ·   " + transport.Band);
            status.EnableInClassList("gt-status--playing", transport.Playing);

            // the rows move on a beat before the next row starts, taking at most 0.35 s
            var beat = 4.0 / (bar?.Signature.Denominator ?? 4);
            var quartersPerSecond = song.Tempo.BpmAt(at) * transport.Speed / 60.0;
            Strip.Show(quarter, countIn, Math.Min(beat, 0.35 * quartersPerSecond));
            if (IsShown(Panel.Chords))
                ShowChords(at, countIn == null && quarter >= 0);
            if (IsShown(Panel.Strumming))
            {
                var patterns = chart.PatternsFor(section);
                var shown = patterns.Count > 0 ? patterns[((pattern % patterns.Count) + patterns.Count) % patterns.Count] : null;
                // the meter of the section's first bar: one odd bar in the middle of it does not change the panel
                var meter = (section != null ? chart.BarOf(section.AtQuarter) : null)?.Signature ?? bar?.Signature ?? TimeSignature.FourFour;
                strum.Show(shown, meter);
                strum.EnableInClassList("gt-hidden", shown == null);
                strumEmpty.EnableInClassList("gt-hidden", shown != null);
                strumOrigin.text = shown == null ? "" : "from " + SongOrigin.Name(SongOrigin.Of(song, shown));
                strumName.text = shown == null ? "" : patterns.Count > 1 ? $"{shown.Name}  ({pattern % patterns.Count + 1} of {patterns.Count}, P for the next)" : shown.Name;
                strum.SetNow(countIn == null && transport.Playing ? chart.StrokeAt(shown, quarter).index : -1);
            }
            if (IsShown(Panel.Tab))
                tab.Show(quarter);
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
