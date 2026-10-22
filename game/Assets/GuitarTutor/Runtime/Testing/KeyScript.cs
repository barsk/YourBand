using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace GuitarTutor.Runtime.Testing
{
    /// <summary>
    /// Key presses from the command line, fed to the game's own keyboard (never the desktop's), to test the
    /// controls end to end: <c>-keys "1:Space,4:DownArrow,6.5:shot,7:quit"</c>. Each step is seconds after start, then
    /// a key (a <see cref="Key"/> name), "shot" (a screenshot into the <c>-keyshots</c> folder) or "quit".
    /// </summary>
    public sealed class KeyScript : MonoBehaviour
    {
        List<(float at, string what)> steps;
        string folder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
            var script = Arg("-keys");
            if (script == null)
                return;
            // the window may be behind others while the script runs: keep the keyboard enabled without focus
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var runner = new GameObject("Key script").AddComponent<KeyScript>();
            runner.folder = Arg("-keyshots") ?? ".";
            try
            {
                runner.steps = script.Split(',').Select(s => s.Split(':'))
                    .Select(p => (float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), p[1].Trim()))
                    .OrderBy(s => s.Item1).ToList();
            }
            catch (Exception e) when (e is FormatException || e is IndexOutOfRangeException)
            {
                // a script that cannot be read would leave the game running with nobody at the keys
                Debug.LogError($"KeyScript: cannot read \"{script}\" ({e.Message}); quitting");
                Application.Quit();
            }
        }

        IEnumerator Start()
        {
            if (steps == null)
                yield break;
            var keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            Directory.CreateDirectory(folder);
            int shot = 0;
            foreach (var (at, what) in steps)
            {
                while (Time.realtimeSinceStartup < at)
                    yield return null;
                if (what == "quit")
                {
                    Application.Quit();
                    yield break;
                }
                if (what == "shot")
                {
                    yield return new WaitForEndOfFrame();
                    var image = ScreenCapture.CaptureScreenshotAsTexture();
                    var file = Path.Combine(folder, $"keys_{++shot}_{at:0.0}s.png".Replace(',', '.'));
                    File.WriteAllBytes(file, image.EncodeToPNG());
                    Destroy(image);
                    Debug.Log($"KeyScript: {at:0.0} s screenshot {file}");
                    continue;
                }
                if (!Enum.TryParse(what, true, out Key key))
                {
                    Debug.LogWarning("KeyScript: no key called " + what);
                    continue;
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                yield return null;
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                Debug.Log($"KeyScript: {at:0.0} s pressed {key}");
            }
        }
    }
}
