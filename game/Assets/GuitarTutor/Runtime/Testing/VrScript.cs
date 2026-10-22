using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;

namespace GuitarTutor.Runtime.Testing
{
    /// <summary>
    /// A headset and two Touch controllers played from the command line, on the game's own input system, to test
    /// VR mode's controls without a headset (with <c>-vr -noxr</c>):
    /// <c>-vrscript "1:head 0 1.6 0; 2:aim 0.3 1.2 0.2 &gt; -1 0 2; 2.5:press trigger; 3:aim 0.3 1.2 0.2 &gt; -0.5 0 2.5; 3.5:release trigger; 4:shot; 5:quit"</c>.
    /// Steps (separated by ";") are seconds after start, then: <c>head x y z [yaw [pitch]]</c>; <c>aim x y z &gt; tx ty tz</c>
    /// (the right controller's pointer at a point); <c>left x y z</c> (the left controller); <c>press</c> /
    /// <c>release</c> a button (trigger, grip, a, b, click on the right; x, y on the left); <c>stick x y</c>,
    /// <c>lstick x y</c>; <c>shot</c> (a screenshot into the <c>-keyshots</c> folder); <c>quit</c>. Positions are
    /// metres from the room's floor.
    /// </summary>
    public sealed class VrScript : MonoBehaviour
    {
        const string ControllerLayout = @"{
            ""name"": ""ScriptedTouchController"",
            ""extend"": ""XRController"",
            ""controls"": [
                { ""name"": ""triggerPressed"", ""layout"": ""Button"" },
                { ""name"": ""gripPressed"", ""layout"": ""Button"" },
                { ""name"": ""primaryButton"", ""layout"": ""Button"" },
                { ""name"": ""secondaryButton"", ""layout"": ""Button"" },
                { ""name"": ""thumbstickClicked"", ""layout"": ""Button"" },
                { ""name"": ""thumbstick"", ""layout"": ""Stick"" },
                { ""name"": ""pointerPosition"", ""layout"": ""Vector3"" },
                { ""name"": ""pointerRotation"", ""layout"": ""Quaternion"" }
            ]
        }";

        List<(float at, string what)> steps;
        string folder;
        XRHMD hmd;
        InputDevice right, left;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
            var script = Arg("-vrscript");
            if (script == null)
                return;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var runner = new GameObject("VR script").AddComponent<VrScript>();
            runner.folder = Arg("-keyshots") ?? ".";
            try
            {
                runner.steps = script.Split(';').Where(s => s.Trim() != "").Select(s => s.Split(new[] { ':' }, 2))
                    .Select(p => (float.Parse(p[0], CultureInfo.InvariantCulture), p[1].Trim()))
                    .OrderBy(s => s.Item1).ToList();
            }
            catch (Exception e) when (e is FormatException || e is IndexOutOfRangeException)
            {
                Debug.LogError($"VrScript: cannot read \"{script}\" ({e.Message}); quitting");
                Application.Quit();
            }
        }

        IEnumerator Start()
        {
            if (steps == null)
                yield break;
            InputSystem.RegisterLayout(ControllerLayout);
            hmd = InputSystem.AddDevice<XRHMD>("Scripted HMD");
            right = InputSystem.AddDevice("ScriptedTouchController", "Scripted right");
            left = InputSystem.AddDevice("ScriptedTouchController", "Scripted left");
            InputSystem.SetDeviceUsage(right, UnityEngine.InputSystem.CommonUsages.RightHand);
            InputSystem.SetDeviceUsage(left, UnityEngine.InputSystem.CommonUsages.LeftHand);
            // all three tracked, the controllers held in front at waist height until told otherwise
            Set(hmd, d => Write(d, "isTracked", 1f));
            Set(right, d => { Write(d, "isTracked", 1f); WritePose(d, "pointer", new Vector3(0.2f, 1.1f, 0.3f), Quaternion.identity); });
            Set(left, d => { Write(d, "isTracked", 1f); WritePose(d, "device", new Vector3(-0.2f, 1.1f, 0.3f), Quaternion.identity); });
            Directory.CreateDirectory(folder);
            int shot = 0;
            foreach (var (at, what) in steps)
            {
                while (Time.realtimeSinceStartup < at)
                    yield return null;
                var words = what.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                float F(int i) => float.Parse(words[i], CultureInfo.InvariantCulture);
                switch (words[0])
                {
                    case "quit":
                        Application.Quit();
                        yield break;
                    case "shot":
                        yield return new WaitForEndOfFrame();
                        var image = ScreenCapture.CaptureScreenshotAsTexture();
                        var file = Path.Combine(folder, $"vr_{++shot}_{at:0.0}s.png".Replace(',', '.'));
                        File.WriteAllBytes(file, image.EncodeToPNG());
                        Destroy(image);
                        Debug.Log($"VrScript: {at:0.0} s screenshot {file}");
                        continue;
                    case "head":
                        var turn = Quaternion.Euler(words.Length > 5 ? F(5) : 0, words.Length > 4 ? F(4) : 0, 0);
                        Set(hmd, d => WritePose(d, "centerEye", new Vector3(F(1), F(2), F(3)), turn));
                        break;
                    case "aim":
                        var from = new Vector3(F(1), F(2), F(3));
                        var to = new Vector3(F(5), F(6), F(7));  // words[4] is ">"
                        Set(right, d => WritePose(d, "pointer", from, Quaternion.LookRotation(to - from)));
                        break;
                    case "left":
                        Set(left, d => WritePose(d, "device", new Vector3(F(1), F(2), F(3)), Quaternion.identity));
                        break;
                    case "press":
                    case "release":
                        var (device, control) = Button(words[1]);
                        var value = words[0] == "press" ? 1f : 0f;
                        Set(device, d => Write(d, control, value));
                        break;
                    case "stick":
                    case "lstick":
                        var v = new Vector2(F(1), F(2));
                        Set(words[0] == "stick" ? right : left, d => ((StickControl)d["thumbstick"]).WriteValueIntoEvent(v, current));
                        break;
                    default:
                        Debug.LogWarning("VrScript: cannot do " + what);
                        continue;
                }
                Debug.Log($"VrScript: {at:0.0} s {what}");
            }
        }

        (InputDevice, string) Button(string name) => name switch
        {
            "trigger" => (right, "triggerPressed"),
            "grip" => (right, "gripPressed"),
            "a" => (right, "primaryButton"),
            "b" => (right, "secondaryButton"),
            "click" => (right, "thumbstickClicked"),
            "x" => (left, "primaryButton"),
            "y" => (left, "secondaryButton"),
            _ => throw new ArgumentException("no button " + name),
        };

        InputEventPtr current;

        /// <summary>A state event for the device as it is now, changed by <paramref name="change"/>, queued.</summary>
        void Set(InputDevice device, Action<InputDevice> change)
        {
            using (StateEvent.From(device, out var e))
            {
                current = e;
                change(device);
                InputSystem.QueueEvent(e);
            }
        }

        void Write(InputDevice d, string control, float value) => ((InputControl<float>)d[control]).WriteValueIntoEvent(value, current);

        void WritePose(InputDevice d, string pose, Vector3 at, Quaternion rotation)
        {
            ((InputControl<Vector3>)d[pose + "Position"]).WriteValueIntoEvent(at, current);
            ((InputControl<Quaternion>)d[pose + "Rotation"]).WriteValueIntoEvent(rotation, current);
        }
    }
}
