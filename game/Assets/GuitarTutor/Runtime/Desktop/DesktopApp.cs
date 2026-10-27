using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuitarTutor.Core.Band;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Engine;
using GuitarTutor.Core.Playback;
using GuitarTutor.Core.Songs;
using GuitarTutor.Runtime.Band;
using GuitarTutor.Runtime.Engine;
using GuitarTutor.Runtime.Songs;
using GuitarTutor.Runtime.UI;
using GuitarTutor.Runtime.Vr;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GuitarTutor.Runtime.Desktop
{
    /// <summary>The game's controls; keys and the footswitch (through the engine) both become these.</summary>
    public enum Command
    {
        PlayPause, Restart, Home, BarBack, BarOn, SectionBack, SectionOn, Faster, Slower,
        NextPattern, NextSong, NextStyle, BandGuitar, BandView, Vr, ControlPanel, ToneWindow, Help, Quit,
    }

    /// <summary>
    /// The desktop game: loads a song, arranges the band's parts and hands them to the audio engine, runs the
    /// transport from the keyboard and the footswitch, and shows the chart at the music's position every frame.
    /// Without the engine it keeps time on its own, silently (as in M1).
    /// </summary>
    /// <remarks>
    /// Command line: <c>-song &lt;file&gt;</c> plays that song, <c>-demo</c> the built-in demo song (otherwise the
    /// first song in songs-local, or the demo if there is none). <c>-shot &lt;folder&gt; -at &lt;positions&gt;</c>
    /// renders a screenshot at each position (in quarter notes, comma separated; "countin" for the count-in) with
    /// every panel shown, then quits. <c>-noengine</c> does not look for the audio engine. <c>-parts drums,bass</c>
    /// gives the band only those parts (for checking them one at a time); <c>-style Rock</c> starts in that band style.
    /// <c>-view band</c> starts in the band view (the 3D band behind the chart; V switches it); <c>-nopanels</c>
    /// hides the panels in screenshots, <c>-nochart</c> the whole screen; <c>-cam guitarist</c> (or another
    /// role, optionally <c>:left</c>, <c>:right</c>, <c>:hands</c>) frames one musician. <c>-vr</c> starts in VR
    /// mode (X switches it; see VrMode for <c>-noxr</c> and <c>-xrruntime</c>).
    /// Without a running engine the game starts one (<c>-engine &lt;file&gt;</c>: which; <c>-engineargs "..."</c>: more
    /// for its command line) and closes it when it closes.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DesktopApp : MonoBehaviour
    {
        public StyleSheet style;

        static readonly DesktopScreen.Panel[] PanelOrder =
            { DesktopScreen.Panel.Chords, DesktopScreen.Panel.Strumming, DesktopScreen.Panel.Tab, DesktopScreen.Panel.Song };

        DesktopScreen screen;
        SongChart chart;
        Arrangement arrangement;
        BandStyle bandStyle;
        readonly List<string> songs = new List<string>();  // null: the demo song
        int songIndex, pattern;

        LocalTransport local;
        EngineLink link;
        EngineClock clock;
        EngineTransport engine;
        bool engineConnected;  // as last reported on the main thread
        System.Diagnostics.Process startedEngine;  // the engine this game started, if it did
        bool startingEngine;   // started, not connected yet
        bool engineReady;      // connected, and has this song
        ITransport transport;
        double? hold;      // screenshots: keep the music here
        double? resumeFrom;  // where playing again goes, when not where the music stopped
        bool countInNext = true;  // the next play starts somewhere new: count in
        bool bandGuitarPlays;     // the band guitarist plays the rhythm, as the engine last said
        BandStage band;           // the 3D band, once the band view has been shown
        bool bandView;            // the 3D band behind the chart (V)
        string closeUp;           // screenshots: one musician framed (-cam)
        bool guitarForShots;      // screenshots: the band guitarist plays (-guitarplays; no engine to say so)
        VrMode vr;
        float vrToggledAt = -10;  // when VR mode was last switched with a key
        bool bandViewBeforeVr;    // the band view as it was on the desktop

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Start()
        {
            // smooth enough for the scrolling chart, and leaves the processor to the audio engine
            Application.targetFrameRate = 120;
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (style != null)
                root.styleSheets.Add(style);
            screen = new DesktopScreen();
            root.Add(screen);
            screen.Strip.SeekToBar += bar => Seek(chart.Bars[Mathf.Clamp(bar, 0, chart.Bars.Count - 1)].AtQuarter);
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

            // screenshots are of the chart alone: the band stays out of it
            if (!args.Contains("-noengine") && Arg("-shot") == null)
            {
                link = new EngineLink();
                clock = new EngineClock();
                engine = new EngineTransport(link, clock);
                link.ConnectionChanged += OnEngineConnection;
                link.Message += OnEngineMessage;
                link.Start();
                StartCoroutine(StartEngineIfNeeded(Arg("-engine"), Arg("-engineargs") ?? ""));
            }
            vr = gameObject.AddComponent<VrMode>();
            vr.Commanded += Execute;
            Load(songIndex);
            if (Arg("-view") == "band")
                SetBandView(true);
            if (args.Contains("-vr"))
                SetVr(true);
            if (args.Contains("-nochart"))
                screen.style.display = DisplayStyle.None;
            closeUp = Arg("-cam");
            Musician.Logging = args.Contains("-bandlog");
            guitarForShots = args.Contains("-guitarplays");

            var shots = Arg("-shot");
            if (shots != null)
                StartCoroutine(Shots(shots, (Arg("-at") ?? "0").Split(',')));
        }

        void OnDestroy()
        {
            if (engineReady)
                engine.Pause();
            // an engine the game started goes with it (it also notices the game is gone, should this not arrive)
            if (startedEngine != null && engineConnected)
                link.Send(EngineProtocol.Quit());
            link?.Dispose();
            clock?.Dispose();
        }

        /// <summary>Starts the audio engine if there is none: give a running one a moment to answer first.</summary>
        IEnumerator StartEngineIfNeeded(string program, string extraArguments)
        {
            yield return new WaitForSecondsRealtime(1.0f);
            if (engineConnected || EngineProcess.IsRunning())
                yield break;
            startedEngine = EngineProcess.Start(EngineProcess.Find(program), extraArguments);
            startingEngine = startedEngine != null;
        }

        string songPath;  // the song file being played (null: the demo)

        void Load(int index)
        {
            songIndex = (index % songs.Count + songs.Count) % songs.Count;
            var path = songs[songIndex];
            songPath = path;  // before the band is arranged: the engine is told where the song is from
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
            if (transport != null && transport.Playing)
                transport.Pause();
            chart = new SongChart(song);
            local = new LocalTransport(song.Tempo, () => Now);
            pattern = 0;
            countInNext = true;
            resumeFrom = null;
            screen.SetSong(chart);
            var styleName = Environment.GetCommandLineArgs().SkipWhile(a => a != "-style").Skip(1).FirstOrDefault();
            Arrange(styleName != null ? BandStyle.Named(styleName) : Arranger.DefaultStyle(song));
            Debug.Log($"GuitarTutor: {song.Info.Artist} - {song.Info.Title}, {chart.Bars.Count} bars ({path ?? "demo"})");
            Debug.Log("GuitarTutor: song from " + SongOrigin.Describe(song, path));
            if (engineConnected)
                SendSongList();
        }

        /// <summary>A song file's name without its suffix: what the engine's song list shows ("heart-of-gold-gpx-ug").</summary>
        static string SongName(string path) =>
            path == null ? "Demo song" : Path.GetFileName(path).Replace(".song.json", "").Replace(".json", "");

        /// <summary>The engine's song list: the songs the game can open, and the one it has open.</summary>
        void SendSongList() => link.Send(EngineProtocol.Songs(songs.Select(SongName), songIndex));

        /// <summary>Writes the band's parts in a style and gives them to the engine (which stops playing).</summary>
        void Arrange(BandStyle style)
        {
            bandStyle = style;
            arrangement = Arranger.Arrange(chart, style);
            var parts = Environment.GetCommandLineArgs().SkipWhile(a => a != "-parts").Skip(1).FirstOrDefault();
            if (parts != null)
                arrangement.Notes.RemoveAll(n => !parts.Split(',').Contains(n.Part.ToString().ToLowerInvariant()));
            band?.SetSong(chart, arrangement);
            engineReady = false;
            transport = local;
            if (engineConnected)  // otherwise it goes when the engine connects
                SendSong();
        }

        void SendSong()
        {
            link.Send(EngineProtocol.Load(chart.Song.Info.Title, chart.Song.Tempo, arrangement, bandStyle.KeysSound, SongOrigin.Describe(chart.Song, songPath)));
            link.Send(EngineProtocol.Mix(BandPart.Keys, bandStyle.KeysGain));
        }

        void OnEngineConnection(bool connected)
        {
            Debug.Log("GuitarTutor: audio engine " + (connected ? "connected" : "gone"));
            engineConnected = connected;
            startingEngine = false;
            if (connected)
            {
                local.Pause();
                SendSongList();
                SendSong();
            }
            else
            {
                // carry on silently from where the music was
                var at = engineReady ? Math.Max(0, engine.Quarter) : local.Quarter;
                engineReady = false;
                transport = local;
                local.Seek(at);
            }
        }

        void OnEngineMessage(string type, JObject message)
        {
            if (type == "loaded")
            {
                // the band has the song: from here the engine's clock is the music
                var at = Math.Max(0, transport.Quarter);
                var wasPlaying = transport.Playing;
                local.Pause();
                engineReady = true;
                transport = engine;
                engine.Refresh();
                if (wasPlaying)
                    engine.Play(at, 0);
                else
                    engine.Seek(at);
                if (Math.Abs(local.Speed - 1) > 1e-6)
                    engine.SetSpeed(local.Speed);
                Debug.Log($"GuitarTutor: the band has {chart.Song.Info.Title} ({(int)message["notes"]} notes, keys {(string)message["keys"]})");
            }
            else if (type == "bandGuitar")
            {
                // from the control panel's checkbox, or the answer to G
                bandGuitarPlays = (bool?)message["plays"] ?? false;
                Debug.Log("GuitarTutor: the band guitarist " + (bandGuitarPlays ? "plays the rhythm" : "rests"));
            }
            else if (type == "openSong")
            {
                // chosen in the engine's song list
                var index = (int?)message["index"] ?? -1;
                if (index >= 0 && index < songs.Count && index != songIndex)
                    Load(index);
            }
            else if (type == "command" && Enum.TryParse((string)message["command"] switch
                     {
                         "playPause" => "PlayPause",
                         "restart" => "Restart",
                         "sectionPrevious" => "SectionBack",
                         "sectionNext" => "SectionOn",
                         _ => "",
                     }, out Command command))
                Execute(command);
        }

        void Update()
        {
            if (chart == null)
                return;
            link?.Poll();
            if (engineReady)
                engine.Refresh();
            HandleKeys();
            if (hold != null && !transport.Playing)
                transport.Play(hold.Value, 0);
            var quarter = hold ?? transport.Quarter;
            if (transport.Playing && quarter >= chart.EndQuarter && hold == null)
            {
                transport.Pause();
                transport.Seek(chart.EndQuarter - 1e-6);
            }
            var status = link == null ? "" : engineReady ? $"band: {bandStyle.Name}" + (bandGuitarPlays ? ", guitarist strums" : "") : engineConnected ? "band: loading"
                : startingEngine ? "band: starting the engine" : "no band (engine not running)";
            if (vr.Active)
                status += (status == "" ? "" : "   ") + vr.Status;
            screen.Show(quarter, CountInAt(quarter), new TransportState(transport.Playing, transport.Speed, chart.Song.Tempo.SecondsAt(Math.Max(0, quarter)), status), pattern);
            if (bandView && band != null)
            {
                band.GuitarPlays = bandGuitarPlays || guitarForShots;
                band.Show(quarter, chart.Song.Tempo.SecondsAt(Math.Max(0, quarter)));
                if (closeUp != null && Camera.main != null && !vr.Active)
                    band.Frame(Camera.main, closeUp);
            }
        }

        /// <summary>The 3D band behind the chart, or the chart alone (the default).</summary>
        void SetBandView(bool on)
        {
            if (on && band == null)
            {
                band = BandStage.Create(out var missing);
                if (band == null)
                {
                    Debug.LogWarning("GuitarTutor: no band view: " + missing);
                    return;
                }
                band.SetSong(chart, arrangement);
            }
            bandView = on;
            if (band != null)
                band.gameObject.SetActive(on);
            var eye = Camera.main;
            if (eye != null && !vr.Active)  // in VR the headset has the camera
            {
                eye.transform.position = on ? BandStage.ViewFrom : new Vector3(0, 1.6f, -4);
                eye.transform.rotation = on ? Quaternion.LookRotation(BandStage.ViewAt - BandStage.ViewFrom) : Quaternion.identity;
                eye.fieldOfView = 50;
            }
            Debug.Log("GuitarTutor: " + (on ? "band view" : "chart view"));
        }

        /// <summary>VR mode: the band in your room and the chart on a board, in the headset; or the desktop.</summary>
        void SetVr(bool on)
        {
            if (on == vr.Active)
                return;
            if (on)
            {
                bandViewBeforeVr = bandView;
                SetBandView(true);  // the band is what VR is for
                vr.Enter(band, GetComponent<UIDocument>());
            }
            else
            {
                vr.Leave();
                SetBandView(bandViewBeforeVr);
            }
            screen.SetVr(on);
        }

        CountIn? CountInAt(double quarter)
        {
            var until = hold == null ? transport.CountInUntil : countInShot;
            if (double.IsNaN(until) || quarter >= until)
                return null;
            var target = chart.BarOf(until) ?? chart.Bars[0];
            var signature = target.Signature;
            var into = Math.Max(0, quarter - (until - signature.QuartersPerBar));
            var beat = (int)Math.Min(signature.Numerator - 1, Math.Floor(into / (4.0 / signature.Denominator)));
            return new CountIn(beat, into / signature.QuartersPerBar, signature, target.Index);
        }

        // ------------------------------------------------------------ transport
        int CurrentBar()
        {
            var until = transport.CountInUntil;
            if (!double.IsNaN(until))
                return chart.BarOf(until)?.Index ?? 0;
            return chart.BarOf(Math.Max(0, transport.Quarter))?.Index ?? 0;
        }

        /// <summary>Plays from the start of a bar, after a bar of count-in.</summary>
        void PlayFrom(int bar)
        {
            var target = chart.Bars[Mathf.Clamp(bar, 0, chart.Bars.Count - 1)];
            Play(target.AtQuarter, target);
        }

        /// <summary>
        /// Plays again: after a pause it carries on from exactly where the music stopped; from a new place (moved
        /// there, or the start) it counts in first.
        /// </summary>
        void Resume()
        {
            var at = resumeFrom ?? transport.Quarter;
            var bar = at < 0 ? null : chart.BarOf(at);
            if (bar == null || at >= chart.EndQuarter - 1e-3)
                PlayFrom(0);
            else if (resumeFrom != null || countInNext)
                Play(at, bar);
            else
            {
                Debug.Log($"GuitarTutor: play on from {Where(at)}");
                transport.Play(at, 0);
            }
        }

        void Play(double from, ChartBar bar)
        {
            resumeFrom = null;
            countInNext = false;
            Debug.Log($"GuitarTutor: play from {Where(from)}, after a count-in of {bar.Signature.Numerator} beats");
            transport.Play(from, bar.Quarters);
        }

        void Pause()
        {
            var until = transport.CountInUntil;
            // paused during a count-in: playing again counts in to where it was going
            resumeFrom = double.IsNaN(until) ? (double?)null : until;
            transport.Pause();
            Debug.Log($"GuitarTutor: pause at {Where(transport.Quarter)}");
        }

        void Seek(double quarter)
        {
            resumeFrom = null;
            countInNext = true;
            quarter = Math.Max(0, Math.Min(quarter, chart.EndQuarter - 1e-6));
            Debug.Log($"GuitarTutor: go to {Where(quarter)}");
            transport.Seek(quarter);
        }

        /// <summary>A position as bar and beat, counted from 1 as the chart shows them.</summary>
        string Where(double quarter)
        {
            var bar = chart.BarOf(Math.Max(0, quarter));
            if (bar == null)
                return $"quarter {quarter:0.##}";
            var beat = (quarter - bar.AtQuarter) / (4.0 / bar.Signature.Denominator);
            return $"bar {bar.Index + 1} beat {Math.Floor(beat) + 1}" + (quarter < 0 ? " (count-in)" : "");
        }

        void SeekBar(int bar) => Seek(chart.Bars[Mathf.Clamp(bar, 0, chart.Bars.Count - 1)].AtQuarter);

        void StepSection(int direction)
        {
            var bar = CurrentBar();
            var index = chart.SectionIndexOf(bar);
            if (index < 0)
            {
                SeekBar(direction > 0 ? chart.Sections.FirstOrDefault().FirstBar : 0);
                return;
            }
            // back: to the start of this section, or the one before if already at its start
            if (direction < 0 && bar - chart.Sections[index].FirstBar > 0)
                SeekBar(chart.Sections[index].FirstBar);
            else
                SeekBar(chart.Sections[Mathf.Clamp(index + direction, 0, chart.Sections.Count - 1)].FirstBar);
        }

        void Execute(Command command)
        {
            switch (command)
            {
                case Command.PlayPause:
                    if (transport.Playing)
                        Pause();
                    else
                        Resume();
                    break;
                case Command.Restart: PlayFrom(0); break;
                case Command.Home: SeekBar(0); break;
                case Command.BarBack: SeekBar(CurrentBar() - 1); break;
                case Command.BarOn: SeekBar(CurrentBar() + 1); break;
                case Command.SectionBack: StepSection(-1); break;
                case Command.SectionOn: StepSection(1); break;
                case Command.Faster:
                case Command.Slower:
                    var speed = Math.Round(transport.Speed * 20 + (command == Command.Faster ? 1 : -1)) / 20;
                    transport.SetSpeed(Math.Max(0.5, Math.Min(1.5, speed)));
                    break;
                case Command.NextPattern: ++pattern; break;
                case Command.NextSong: Load(songIndex + 1); break;
                case Command.NextStyle:
                    var i = BandStyle.All.ToList().IndexOf(bandStyle);
                    var playing = transport.Playing;
                    var at = transport.Quarter;
                    Arrange(BandStyle.All[(i + 1) % BandStyle.All.Count]);
                    local.Seek(Math.Max(0, at));
                    if (playing)
                        local.Play(Math.Max(0, at), 0);
                    break;
                case Command.BandGuitar:
                    // the engine keeps the setting (the control panel has it too) and answers with it
                    if (engineConnected)
                        link.Send(EngineProtocol.BandGuitar(!bandGuitarPlays));
                    break;
                case Command.ControlPanel:
                case Command.ToneWindow:
                    if (engineConnected)
                    {
                        EngineProcess.AllowToFront();
                        link.Send(EngineProtocol.ShowWindow(command == Command.ControlPanel ? "controlPanel" : "guitar"));
                    }
                    break;
                case Command.BandView: SetBandView(!bandView); break;
                case Command.Vr:
                    // starting XR makes the input system see the keyboard again, and a key still down counts as
                    // pressed once more: a second X within a few seconds is that, not the user
                    if (Time.realtimeSinceStartup - vrToggledAt < 3f)
                    {
                        Debug.Log("GuitarTutor: VR switch ignored (too soon after the last)");
                        break;
                    }
                    vrToggledAt = Time.realtimeSinceStartup;
                    SetVr(!vr.Active);
                    break;
                case Command.Help: screen.ToggleHelp(); break;
                case Command.Quit: Application.Quit(); break;
            }
        }

        void HandleKeys()
        {
            var k = Keyboard.current;
            if (k == null)
                return;
            (Key key, Command command)[] keys =
            {
                (Key.Space, Command.PlayPause), (Key.R, Command.Restart), (Key.Home, Command.Home),
                (Key.LeftArrow, Command.BarBack), (Key.RightArrow, Command.BarOn),
                (Key.PageUp, Command.SectionBack), (Key.PageDown, Command.SectionOn),
                (Key.UpArrow, Command.Faster), (Key.DownArrow, Command.Slower),
                (Key.P, Command.NextPattern), (Key.L, Command.NextSong), (Key.B, Command.NextStyle), (Key.G, Command.BandGuitar), (Key.V, Command.BandView), (Key.X, Command.Vr),
                (Key.C, Command.ControlPanel), (Key.T, Command.ToneWindow),
                (Key.H, Command.Help), (Key.Escape, Command.Quit),
            };
            foreach (var (key, command) in keys)
                if (k[key].wasPressedThisFrame)
                    Execute(command);
            Key[] panelKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };
            for (int i = 0; i < panelKeys.Length; ++i)
                if (k[panelKeys[i]].wasPressedThisFrame || k[Key.Numpad1 + i].wasPressedThisFrame)
                {
                    var panel = PanelOrder[i];
                    screen.SetShown(panel, !screen.IsShown(panel));
                    PlayerPrefs.SetInt("panel." + panel, screen.IsShown(panel) ? 1 : 0);
                }
        }

        // ------------------------------------------------------------ screenshots
        double countInShot = double.NaN;

        IEnumerator Shots(string folder, string[] positions)
        {
            Directory.CreateDirectory(folder);
            var panels = !Environment.GetCommandLineArgs().Contains("-nopanels");
            foreach (var panel in PanelOrder)
                screen.SetShown(panel, panels);
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            for (int i = 0; i < 10; ++i)
                yield return null;
            foreach (var position in positions)
            {
                if (position == "countin")
                {
                    countInShot = 0;
                    hold = -2.5;
                }
                else
                {
                    countInShot = double.NaN;
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
