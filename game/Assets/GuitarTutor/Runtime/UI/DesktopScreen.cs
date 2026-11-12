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
    /// (song info, chords, strumming), the tab across the screen, the chart's rows, and overlays for help and the
    /// tracks (<see cref="TrackMatrix"/>). The panels
    /// and the tab can be toggled. The middle stays clear for the band once it is there (M6).
    /// </summary>
    public sealed class DesktopScreen : VisualElement
    {
        public enum Panel { Chords, Strumming, Tab, Song }

        public const int MaxChords = 6;

        public ChartStrip Strip { get; }
        /// <summary>The song's tracks bar by bar, over the chart (M).</summary>
        public TrackMatrix Tracks { get; }
        readonly LoadingOverlay loading = new LoadingOverlay();
        int tabIndex;  // which of the chart's tabs the TAB panel shows

        readonly Label title, artist, status, hint, vrHint;
        readonly VisualElement help, vrHelp;
        bool vr;
        readonly Dictionary<Panel, VisualElement> panels = new Dictionary<Panel, VisualElement>();
        readonly Dictionary<Panel, Label> panelTitles = new Dictionary<Panel, Label>();
        const float PanelGap = 16, InfoWidth = 330;  // the panels' right margin; the standing song panel's width
        const float StrumPanelFrame = 34;  // a panel's padding and border, left and right (Chart.uss)
        const float StrumPanelMinWidth = 480;  // room for the pattern's name beside where it is from, whatever its length

        readonly VisualElement chordRow, info;
        readonly StrumView strum;
        readonly Label strumName, strumOrigin;
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
            info = AddPanel(Panel.Song, "SONG", middle);  // first, at the top left
            var chordsPanel = AddPanel(Panel.Chords, "CHORDS", middle);
            chordRow = ChartLook.Box("gt-panel__row", chordsPanel);
            var strumPanel = AddPanel(Panel.Strumming, "STRUMMING", middle);
            strumPanel.style.minWidth = StrumPanelMinWidth;
            strum = new StrumView();
            strumPanel.Add(strum);
            strumName = ChartLook.Label("", "gt-strum__name", strumPanel);
            strumOrigin = ChartLook.Label("", "gt-strum__origin", strumPanel);

            // when the others leave no room beside the song panel (six chords and a long pattern) it lies down, one row
            // across the top, the others under it, rather than a tall panel that would push the tab strip over them; and
            // when the chords leave no room beside the strumming panel, it goes across the row under them, its pattern
            // as large as that allows (a narrow window). Both by the strumming panel's width beside the others.
            void Fit(GeometryChangedEvent _)
            {
                var room = middle.contentRect.width;
                if (float.IsNaN(room) || room <= 0 || float.IsNaN(chordsPanel.layout.width))
                    return;
                var chords = IsShown(Panel.Chords) ? chordsPanel.layout.width + PanelGap : 0;
                var strumming = IsShown(Panel.Strumming) ? Math.Max(StrumPanelMinWidth, strum.NaturalWidth + StrumPanelFrame) + PanelGap : 0;
                var lying = chords + strumming + InfoWidth + PanelGap > room;
                info.EnableInClassList("gt-panel--lying", lying);
                var row = room - (lying || !IsShown(Panel.Song) ? 0 : InfoWidth + PanelGap);
                var alone = IsShown(Panel.Strumming) && chords + strumming > row;
                strumPanel.EnableInClassList("gt-panel--across", alone);
                strum.SetRoom(alone ? room - StrumPanelFrame : StrumView.DefaultRoom);
            }
            middle.RegisterCallback<GeometryChangedEvent>(Fit);
            chordsPanel.RegisterCallback<GeometryChangedEvent>(Fit);
            strumPanel.RegisterCallback<GeometryChangedEvent>(Fit);
            strum.RegisterCallback<GeometryChangedEvent>(Fit);

            // the tab, across the screen above the chart
            var tabStrip = ChartLook.Box("gt-tabstrip", this);
            panels[Panel.Tab] = tabStrip;
            panelTitles[Panel.Tab] = ChartLook.Label("TAB", "gt-panel__title", tabStrip);
            tab = new TabTape();
            tabStrip.Add(tab);

            Strip = new ChartStrip();
            Add(Strip);
            hint = ChartLook.Label(string.Join("   ·   ", "Space play / pause", "R restart", "Left / Right bar", "PgUp / PgDn section", "Up / Down speed", "1-4 panels", "Tab track's tab", "M tracks", "P pattern", "B band style", "V band view", "X VR", "C control panel", "T tone", "H help"),
                "gt-hint", this);
            vrHint = ChartLook.Label(string.Join("   ·   ", "A play / pause", "B restart", "right stick: bar, speed", "left stick: section", "trigger: move the chart or a musician, grip: the band", "right stick click: in front of you", "left stick up: help"),
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
                         ("1  2  3  4", "song, chords, strumming, tab panels"),
                         ("Tab", "the TAB panel shows another track's tab (Guitar Pro songs); Shift+Tab the one before"),
                         ("P", "next strumming pattern"),
                         ("B", "next band style"),
                         ("V", "band view: the band in 3D behind the chart, or the chart alone"),
                         ("X", "VR mode: the band in your room, in the headset (Virtual Desktop)"),
                         ("L", "next song"),
                         ("C", "control panel: audio device, band faders"),
                         ("T", "your guitar's tone (TONE3000)"),
                         ("M", "the tracks bar by bar: click a bar to go there and see that track's tab"),
                         ("H", "this help"),
                         ("Esc", "close the help or the tracks; else quit"),
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
                         ("X", "next key colour for the passthrough (magenta, green, blue, black)"),
                         ("Y", "with the left controller resting on the floor: the floor's height"),
                         ("Keyboard X", "back to the desktop"),
                     });
            Tracks = new TrackMatrix();
            Add(Tracks);
            Add(loading);  // last: over everything
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

        /// <summary>Covers the window with a circling ring and this line (the mouse is blocked meanwhile), or uncovers it (null).</summary>
        public void SetLoading(string message) => loading.Show(message);

        /// <summary>
        /// The TAB panel to the next of the song's tabs (or the one before): the song's own, then the Guitar Pro file's
        /// other tracks written as tab. Shows the panel if it was hidden. Returns the tab's name, or null if the song has
        /// only its own.
        /// </summary>
        public string StepTab(int step)
        {
            if (chart == null || chart.Tabs.Count < 2)
                return null;
            tabIndex = ((tabIndex + step) % chart.Tabs.Count + chart.Tabs.Count) % chart.Tabs.Count;
            tab.SetTab(chart.Tabs[tabIndex]);
            ShowTabTitle();
            if (!IsShown(Panel.Tab))
                SetShown(Panel.Tab, true);
            var shown = chart.Tabs[tabIndex];
            return shown.Name != "" ? shown.Name : "the song's tab";
        }

        /// <summary>"TAB", the track's name, and which of the song's tabs it is when there are more.</summary>
        void ShowTabTitle()
        {
            var shown = chart.Tabs[tabIndex];
            panelTitles[Panel.Tab].text = "TAB" + (shown.Name != "" ? " · " + shown.Name : "")
                                        + (chart.Tabs.Count > 1 ? $"  ({tabIndex + 1} of {chart.Tabs.Count}, Tab for the next)" : "");
        }

        /// <summary>
        /// The TAB panel to a track's tab, if it has one (shown if it was hidden). Returns the tab's name, or null if the
        /// track has none.
        /// </summary>
        public string ShowTabOf(SongTrack track)
        {
            if (chart == null)
                return null;
            var index = chart.Tabs.ToList().FindIndex(t => t.Name == track.Name && t.Name != "");
            if (index < 0)
                return null;
            return StepTab(index - tabIndex) ?? track.Name;
        }

        public void ToggleHelp() => (vr ? vrHelp : help).ToggleInClassList("gt-hidden");

        /// <summary>Opens or closes the tracks overlay (closing the help, which sits in the same place).</summary>
        public void ToggleTracks()
        {
            Tracks.SetOpen(!Tracks.IsOpen);
            help.AddToClassList("gt-hidden");
        }

        /// <summary>Closes the help and the tracks overlay; false if neither was open.</summary>
        public bool CloseOverlays()
        {
            var open = Tracks.IsOpen || !help.ClassListContains("gt-hidden") || !vrHelp.ClassListContains("gt-hidden");
            Tracks.SetOpen(false);
            help.AddToClassList("gt-hidden");
            vrHelp.AddToClassList("gt-hidden");
            return open;
        }

        /// <summary>The controllers' hints and help (VR mode), or the keyboard's.</summary>
        public void SetVr(bool on)
        {
            vr = on;
            hint.EnableInClassList("gt-hidden", on);
            vrHint.EnableInClassList("gt-hidden", !on);
            help.AddToClassList("gt-hidden");
            vrHelp.AddToClassList("gt-hidden");
            Tracks.SetOpen(false);
        }

        public void SetSong(SongChart songChart)
        {
            chart = songChart;
            var song = chart.Song;
            title.text = song.Info.Title;
            artist.text = song.Info.Artist;
            Strip.SetSong(chart);
            Tracks.SetSong(chart);
            tab.SetSong(chart);
            tabIndex = 0;
            ShowTabTitle();
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
            Tracks.Show(at);
            if (IsShown(Panel.Chords))
                ShowChords(at, countIn == null && quarter >= 0);
            if (IsShown(Panel.Strumming))
            {
                var patterns = chart.PatternsFor(section);
                var shown = patterns.Count > 0 ? patterns[((pattern % patterns.Count) + patterns.Count) % patterns.Count] : null;
                // the section's meter, the one the pattern fits: one odd bar in it does not change the panel
                var meter = chart.PatternMeter(shown, section, bar?.Signature ?? TimeSignature.FourFour);
                strum.Show(shown, meter);
                strum.EnableInClassList("gt-hidden", shown == null);
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
