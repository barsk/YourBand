using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Playback;
using GuitarTutor.Core.Songs;
using GuitarTutor.Runtime.Songs;
using GuitarTutor.Runtime.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GuitarTutor.Runtime.Desktop
{
    /// <summary>
    /// The desktop game (M1: the chart and its panels, on an internal clock). Loads a song, runs the transport from
    /// the keyboard, and shows the chart at the clock's position every frame.
    /// </summary>
    /// <remarks>
    /// Command line: <c>-song &lt;file&gt;</c> plays that song, <c>-demo</c> the built-in demo song (otherwise the
    /// first song in songs-local, or the demo if there is none). <c>-shot &lt;folder&gt; -at &lt;positions&gt;</c>
    /// renders a screenshot at each position (in quarter notes, comma separated; "countin" for the count-in) with
    /// every panel shown, then quits.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DesktopApp : MonoBehaviour
    {
        public StyleSheet style;

        static readonly DesktopScreen.Panel[] PanelOrder =
            { DesktopScreen.Panel.Chords, DesktopScreen.Panel.Strumming, DesktopScreen.Panel.Tab, DesktopScreen.Panel.Song };

        DesktopScreen screen;
        SongChart chart;
        SongClock clock;
        readonly List<string> songs = new List<string>();  // null: the demo song
        int songIndex, pattern;
        double countInTarget = double.NaN;  // where the count-in ends (NaN: none)
        int countInBar;
        double? hold;  // screenshots: keep the clock here, playing

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Start()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (style != null)
                root.styleSheets.Add(style);
            screen = new DesktopScreen();
            root.Add(screen);
            screen.Strip.SeekToBar += SeekBar;
            foreach (var panel in PanelOrder)
                screen.SetShown(panel, PlayerPrefs.GetInt("panel." + panel, 1) == 1);

            var args = Environment.GetCommandLineArgs();
            string Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
            songs.AddRange(SongLibrary.Files());
            songs.Add(null);
            var requested = Arg("-song");
            if (requested != null)
            {
                songs.Insert(0, Path.GetFullPath(requested));
                songIndex = 0;
            }
            else if (args.Contains("-demo"))
                songIndex = songs.Count - 1;
            Load(songIndex);

            var shots = Arg("-shot");
            if (shots != null)
                StartCoroutine(Shots(shots, (Arg("-at") ?? "0").Split(',')));
        }

        void Load(int index)
        {
            songIndex = (index % songs.Count + songs.Count) % songs.Count;
            var path = songs[songIndex];
            Song song;
            try
            {
                song = path == null ? DemoSong.Create() : SongJson.Load(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"GuitarTutor: could not load {path}, playing the demo song instead: {e.Message}");
                song = DemoSong.Create();
            }
            chart = new SongChart(song);
            clock = new SongClock(song.Tempo);
            countInTarget = double.NaN;
            pattern = 0;
            screen.SetSong(chart);
            Debug.Log($"GuitarTutor: {song.Info.Artist} - {song.Info.Title}, {chart.Bars.Count} bars ({path ?? "demo"})");
        }

        void Update()
        {
            if (chart == null)
                return;
            HandleKeys();
            var now = Now;
            if (hold != null)
                clock.Seek(hold.Value, now);
            var quarter = clock.QuarterAt(now);
            if (clock.Playing && quarter >= chart.EndQuarter)
            {
                clock.Pause(now);
                clock.Seek(chart.EndQuarter - 1e-6, now);
                quarter = clock.QuarterAt(now);
            }
            screen.Show(quarter, CountInAt(quarter), new TransportState(clock.Playing, clock.Speed, clock.SongSecondsAt(now)), pattern);
        }

        CountIn? CountInAt(double quarter)
        {
            if (double.IsNaN(countInTarget))
                return null;
            if (quarter >= countInTarget)
            {
                countInTarget = double.NaN;
                return null;
            }
            var signature = chart.Song.Tempo.SignatureAtBar(countInBar);
            var into = Math.Max(0, quarter - (countInTarget - signature.QuartersPerBar));
            var beat = (int)Math.Min(signature.Numerator - 1, Math.Floor(into / (4.0 / signature.Denominator)));
            return new CountIn(beat, into / signature.QuartersPerBar, signature, countInBar);
        }

        // ------------------------------------------------------------ transport
        int CurrentBar()
        {
            if (!double.IsNaN(countInTarget))
                return countInBar;
            return chart.BarOf(Math.Max(0, clock.QuarterAt(Now)))?.Index ?? 0;
        }

        /// <summary>Plays from the start of a bar, after a bar of count-in.</summary>
        void PlayFrom(int bar)
        {
            bar = Mathf.Clamp(bar, 0, chart.Bars.Count - 1);
            var target = chart.Bars[bar];
            var now = Now;
            clock.Seek(target.AtQuarter - target.Quarters, now);
            countInTarget = target.AtQuarter;
            countInBar = bar;
            clock.Play(now);
        }

        void SeekBar(int bar)
        {
            bar = Mathf.Clamp(bar, 0, chart.Bars.Count - 1);
            countInTarget = double.NaN;
            clock.Seek(chart.Bars[bar].AtQuarter, Now);
        }

        void TogglePlay()
        {
            if (clock.Playing)
            {
                clock.Pause(Now);
                countInTarget = double.NaN;
                return;
            }
            var atEnd = clock.QuarterAt(Now) >= chart.EndQuarter - 1e-3;
            PlayFrom(atEnd ? 0 : CurrentBar());
        }

        void StepSection(int direction)
        {
            var bar = CurrentBar();
            var index = chart.SectionIndexOf(bar);
            if (index < 0)
            {
                SeekBar(direction > 0 ? chart.Sections.FirstOrDefault().FirstBar : 0);
                return;
            }
            // back: to the start of this section, or the one before if already near its start
            if (direction < 0 && bar - chart.Sections[index].FirstBar > 0)
                SeekBar(chart.Sections[index].FirstBar);
            else
            {
                var to = Mathf.Clamp(index + direction, 0, chart.Sections.Count - 1);
                SeekBar(chart.Sections[to].FirstBar);
            }
        }

        void HandleKeys()
        {
            var k = Keyboard.current;
            if (k == null)
                return;
            if (k.spaceKey.wasPressedThisFrame)
                TogglePlay();
            if (k.rKey.wasPressedThisFrame)
                PlayFrom(0);
            if (k.homeKey.wasPressedThisFrame)
                SeekBar(0);
            if (k.leftArrowKey.wasPressedThisFrame)
                SeekBar(CurrentBar() - 1);
            if (k.rightArrowKey.wasPressedThisFrame)
                SeekBar(CurrentBar() + 1);
            if (k.pageUpKey.wasPressedThisFrame)
                StepSection(-1);
            if (k.pageDownKey.wasPressedThisFrame)
                StepSection(1);
            if (k.upArrowKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame)
            {
                var speed = Math.Round(clock.Speed * 20 + (k.upArrowKey.wasPressedThisFrame ? 1 : -1)) / 20;
                clock.SetSpeed(Math.Max(0.5, Math.Min(1.5, speed)), Now);
            }
            Key[] panelKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };
            for (int i = 0; i < panelKeys.Length; ++i)
                if (k[panelKeys[i]].wasPressedThisFrame || k[Key.Numpad1 + i].wasPressedThisFrame)
                {
                    var panel = PanelOrder[i];
                    screen.SetShown(panel, !screen.IsShown(panel));
                    PlayerPrefs.SetInt("panel." + panel, screen.IsShown(panel) ? 1 : 0);
                }
            if (k.pKey.wasPressedThisFrame)
                ++pattern;
            if (k.lKey.wasPressedThisFrame)
                Load(songIndex + 1);
            if (k.hKey.wasPressedThisFrame)
                screen.ToggleHelp();
            if (k.escapeKey.wasPressedThisFrame)
                Application.Quit();
        }

        // ------------------------------------------------------------ screenshots
        IEnumerator Shots(string folder, string[] positions)
        {
            Directory.CreateDirectory(folder);
            foreach (var panel in PanelOrder)
                screen.SetShown(panel, true);
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            for (int i = 0; i < 10; ++i)
                yield return null;
            clock.Play(Now);
            foreach (var position in positions)
            {
                if (position == "countin")
                {
                    countInTarget = 0;
                    countInBar = 0;
                    hold = -2.5;
                }
                else
                {
                    countInTarget = double.NaN;
                    hold = double.Parse(position, System.Globalization.CultureInfo.InvariantCulture);
                }
                for (int i = 0; i < 6; ++i)
                    yield return null;  // layout settles (labels are placed once they know their size)
                yield return new WaitForEndOfFrame();
                var image = ScreenCapture.CaptureScreenshotAsTexture();
                var file = Path.Combine(folder, "desktop_" + position.Replace('.', '_') + ".png");
                File.WriteAllBytes(file, image.EncodeToPNG());
                Debug.Log($"GuitarTutor: screenshot {file} ({image.width} x {image.height})");
                Destroy(image);
            }
            Application.Quit();
        }
    }
}
