using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuitarTutor.Runtime.Band;
using GuitarTutor.Runtime.Desktop;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace GuitarTutor.Runtime.Vr
{
    /// <summary>
    /// VR mode (docs/plan.md, "VR mode"): the band in your room and the chart on a board in front of you, in the
    /// headset through Virtual Desktop. The camera clears to a key colour that Virtual Desktop's passthrough
    /// replaces with the camera view of the room, and nothing blends into it (no post-processing, no
    /// anti-aliasing, cutout hair: experiment E2). The chart is the desktop screen drawn onto the board.
    /// </summary>
    /// <remarks>
    /// The game starts XR itself (so the desktop game never starts a VR runtime), with Virtual Desktop's OpenXR
    /// runtime (VDXR) if it is installed: <c>-xrruntime &lt;json&gt;</c> names another, <c>-xrruntime system</c>
    /// uses the system's. Without a headset (or with <c>-noxr</c>) the room is shown on the desktop instead, from
    /// standing eye height. Where the band and the board stand, the floor's height and the key colour are kept in
    /// vr-settings.json in the game's data folder.
    /// Controllers: A play / pause, B restart, right stick bars and speed, left stick sections (up: help); the
    /// trigger moves the board or the band (aim at a musician's feet), the stick meanwhile resizes the board or
    /// turns the band; right stick click brings both back in front of you; X the next key colour; Y, with the left
    /// controller resting on the floor, sets the floor's height.
    /// </remarks>
    public sealed class VrMode : MonoBehaviour
    {
        /// <summary>The key colours Virtual Desktop can key out: magenta (the default), green, blue, black.</summary>
        static readonly Color[] Keys = { new Color(1, 0, 1), new Color(0, 1, 0), new Color(0, 0, 1), Color.black };
        static readonly Color BoardColor = new Color32(0x14, 0x16, 0x1a, 0xff);
        static readonly Color Aiming = new Color(0.95f, 0.66f, 0.23f), Idle = new Color(0.8f, 0.8f, 0.8f);
        const float BoardAspect = 16f / 9;  // the board's width to height: the desktop screen's layout as it is
        const float ControllerTipBelowGrip = 0.04f;

        [Serializable]
        class Settings
        {
            public int key;
            public float floorOffset;
            public bool placed;  // the band and the board have been put somewhere
            public float bandX, bandZ, bandYaw;
            public Vector3 boardAt;
            public float boardPitch, boardYaw;
            public float boardWidth = 1.25f;
        }

        /// <summary>A command from the controllers.</summary>
        public event Action<Command> Commanded;

        public bool Active { get; private set; }
        /// <summary>For the status line: whether the headset is there.</summary>
        public string Status { get; private set; } = "";

        Settings settings = new Settings();
        static string SettingsPath => Path.Combine(Application.persistentDataPath, "vr-settings.json");

        Camera eye;
        Transform eyeParent;
        Vector3 eyeAt;
        Quaternion eyeRotation;
        float eyeFov;
        Color eyeBackground;
        CameraClearFlags eyeClear;
        bool eyeHdr, eyeMsaa;

        Transform rig, offset, rightAim, leftGrip, beam, dot;
        Material beamMaterial;
        TrackedPoseDriver head;
        BandStage band;
        UIDocument document;
        PanelSettings desktopPanel, boardPanel;
        RenderTexture boardTexture;
        Transform board;
        bool xrRunning, recentreWhenTracked;

        InputAction trigger, buttonA, buttonB, buttonX, buttonY, stickClick, rightStick, leftStick, rightTracked, leftTracked, headTracked;
        enum Held { Nothing, Board, Band }
        Held held;
        Vector3 boardInAimAt, bandOffset;
        Quaternion boardInAimRotation;
        int rightX, rightY, leftX, leftY;  // the sticks' flick state

        void Awake()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    settings = JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings();
            }
            catch (Exception e)
            {
                Debug.LogWarning("GuitarTutor VR: settings unreadable, using defaults: " + e.Message);
            }
            InputAction Button(string binding) => new InputAction(type: InputActionType.Button, binding: binding);
            trigger = Button("<XRController>{RightHand}/triggerPressed");
            buttonA = Button("<XRController>{RightHand}/primaryButton");
            buttonB = Button("<XRController>{RightHand}/secondaryButton");
            buttonX = Button("<XRController>{LeftHand}/primaryButton");
            buttonY = Button("<XRController>{LeftHand}/secondaryButton");
            stickClick = Button("<XRController>{RightHand}/thumbstickClicked");
            rightTracked = Button("<XRController>{RightHand}/isTracked");
            leftTracked = Button("<XRController>{LeftHand}/isTracked");
            headTracked = Button("<XRHMD>/isTracked");
            rightStick = new InputAction(type: InputActionType.Value, binding: "<XRController>{RightHand}/thumbstick");
            leftStick = new InputAction(type: InputActionType.Value, binding: "<XRController>{LeftHand}/thumbstick");
        }

        IEnumerable<InputAction> Actions => new[] { trigger, buttonA, buttonB, buttonX, buttonY, stickClick, rightStick, leftStick, rightTracked, leftTracked, headTracked };

        // ------------------------------------------------------------ in and out
        /// <summary>Into VR: the band (if there is one) in the room, the chart onto the board, XR started.</summary>
        public void Enter(BandStage stage, UIDocument chart)
        {
            if (Active)
                return;
            Active = true;
            band = stage;
            document = chart;
            eye = Camera.main;
            BuildRig();
            BuildBoard();
            if (band != null)
                band.InRoom = true;
            recentreWhenTracked = !settings.placed;
            Apply();
            foreach (var a in Actions)
                a.Enable();
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("-noxr"))
                Status = "VR: on the desktop (-noxr)";
            else
                StartCoroutine(StartXR(args.SkipWhile(a => a != "-xrruntime").Skip(1).FirstOrDefault()));
            Debug.Log("GuitarTutor: VR mode");
        }

        /// <summary>Back to the desktop: XR stopped, the camera and the chart as they were.</summary>
        public void Leave()
        {
            if (!Active)
                return;
            Active = false;
            StopAllCoroutines();
            StopXR();
            foreach (var a in Actions)
                a.Disable();
            held = Held.Nothing;
            if (eye != null)
            {
                Destroy(head);
                eye.transform.SetParent(eyeParent, false);
                eye.transform.SetLocalPositionAndRotation(eyeAt, eyeRotation);
                eye.fieldOfView = eyeFov;
                eye.clearFlags = eyeClear;
                eye.backgroundColor = eyeBackground;
                eye.allowHDR = eyeHdr;
                eye.allowMSAA = eyeMsaa;
            }
            if (document != null)
                document.panelSettings = desktopPanel;
            if (band != null)
            {
                band.InRoom = false;
                band.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            }
            RenderPipelineManager.beginContextRendering -= MakeMips;
            Destroy(rig.gameObject);
            Destroy(board.gameObject);
            Destroy(boardPanel);
            boardTexture.Release();
            Destroy(boardTexture);
            Status = "";
            Debug.Log("GuitarTutor: desktop mode");
        }

        void OnDestroy()
        {
            StopXR();
            foreach (var a in Actions)
                a.Dispose();
        }

        IEnumerator StartXR(string runtime)
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null)
            {
                Status = "VR: not set up in this build";
                Debug.LogWarning("GuitarTutor VR: no XR settings");
                yield break;
            }
            Status = "VR: looking for the headset";
            if (manager.activeLoader == null)
            {
                UseRuntime(runtime);
                yield return manager.InitializeLoader();
            }
            if (manager.activeLoader == null)
            {
                Status = "VR: no headset (is Virtual Desktop streaming?)";
                Debug.LogWarning("GuitarTutor VR: no OpenXR runtime answered (is Virtual Desktop streaming?); the room is shown on the desktop");
                yield break;
            }
            manager.StartSubsystems();
            xrRunning = true;
            // positions from the room's floor
            var inputs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var input in inputs)
                input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            Status = "VR: headset";
            Debug.Log("GuitarTutor VR: XR started with " + manager.activeLoader.name);
        }

        /// <summary>Virtual Desktop's OpenXR runtime unless told otherwise (an already set XR_RUNTIME_JSON stands).</summary>
        static void UseRuntime(string runtime)
        {
            if (runtime == "system" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XR_RUNTIME_JSON")))
                return;
            var json = runtime ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                               "Virtual Desktop Streamer", "OpenXR", "virtualdesktop-openxr.json");
            if (!File.Exists(json))
            {
                if (runtime != null)
                    Debug.LogWarning("GuitarTutor VR: no OpenXR runtime at " + json + ", using the system's");
                return;
            }
            Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", json);
            Debug.Log("GuitarTutor VR: OpenXR runtime " + json);
        }

        void StopXR()
        {
            if (!xrRunning)
                return;
            xrRunning = false;
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null && manager.activeLoader != null)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
        }

        // ------------------------------------------------------------ the rig and the board
        void BuildRig()
        {
            // rig (the room's floor) > floor offset > the head's camera, the right controller's pointer, the left grip
            rig = new GameObject("VR Rig").transform;
            offset = new GameObject("Floor Offset").transform;
            offset.SetParent(rig, false);
            eyeParent = eye.transform.parent;
            eyeAt = eye.transform.localPosition;
            eyeRotation = eye.transform.localRotation;
            eyeFov = eye.fieldOfView;
            eyeClear = eye.clearFlags;
            eyeBackground = eye.backgroundColor;
            eyeHdr = eye.allowHDR;
            eyeMsaa = eye.allowMSAA;
            eye.transform.SetParent(offset, false);
            // the desktop's view until the headset moves it: standing, looking a little down
            eye.transform.SetLocalPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.Euler(14, 0, 0));
            eye.fieldOfView = 70;
            eye.nearClipPlane = 0.05f;
            // nothing may blend into the key colour: no HDR tone, no anti-aliased edges
            eye.allowHDR = false;
            eye.allowMSAA = false;
            head = eye.gameObject.AddComponent<TrackedPoseDriver>();
            head.positionInput = new InputActionProperty(new InputAction(binding: "<XRHMD>/centerEyePosition"));
            head.rotationInput = new InputActionProperty(new InputAction(binding: "<XRHMD>/centerEyeRotation"));

            Transform Tracked(string name, string hand, string pose)
            {
                var t = new GameObject(name).transform;
                t.SetParent(offset, false);
                var driver = t.gameObject.AddComponent<TrackedPoseDriver>();
                driver.positionInput = new InputActionProperty(new InputAction(binding: $"<XRController>{{{hand}}}/{pose}Position"));
                driver.rotationInput = new InputActionProperty(new InputAction(binding: $"<XRController>{{{hand}}}/{pose}Rotation"));
                return t;
            }
            rightAim = Tracked("Right Aim", "RightHand", "pointer");
            leftGrip = Tracked("Left Grip", "LeftHand", "device");
            // the aiming beam, and a dot where it lands
            beamMaterial = new Material(Resources.Load<Material>("Vr/Beam"));
            beam = Part(PrimitiveType.Cube, "Beam", rightAim, beamMaterial);
            dot = Part(PrimitiveType.Sphere, "Dot", rig, beamMaterial);
            dot.localScale = Vector3.one * 0.02f;
            beam.gameObject.SetActive(false);
            dot.gameObject.SetActive(false);
        }

        static Transform Part(PrimitiveType shape, string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        void BuildBoard()
        {
            // the desktop screen drawn into a texture, on an opaque board: nothing on it is the key colour
            boardTexture = new RenderTexture(2048, Mathf.RoundToInt(2048 / BoardAspect), 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Chart board",
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            boardTexture.Create();
            desktopPanel = document.panelSettings;
            boardPanel = Instantiate(desktopPanel);
            boardPanel.name = "Chart board";
            boardPanel.targetTexture = boardTexture;
            boardPanel.clearColor = true;
            boardPanel.colorClearValue = BoardColor;
            boardPanel.SetScreenToPanelSpaceFunction(_ => new Vector2(float.NaN, float.NaN));  // no mouse on the board
            document.panelSettings = boardPanel;
            var material = new Material(Resources.Load<Material>("Vr/Board"));
            material.SetTexture("_BaseMap", boardTexture);
            board = Part(PrimitiveType.Quad, "Chart Board", null, material);
            RenderPipelineManager.beginContextRendering += MakeMips;
        }

        // the chart is drawn into the texture before the cameras render: its smaller sizes for a far board
        void MakeMips(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (boardTexture != null)
                boardTexture.GenerateMips();
        }

        /// <summary>Settings onto the scene: key colour, floor height, where the band and the board are.</summary>
        void Apply()
        {
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = Keys[Mathf.Abs(settings.key) % Keys.Length];
            offset.localPosition = new Vector3(0, settings.floorOffset, 0);
            if (!settings.placed)
                Recentre(new Vector3(0, 1.6f, 0), Vector3.forward);
            if (band != null)
                band.transform.SetPositionAndRotation(new Vector3(settings.bandX, 0, settings.bandZ), Quaternion.Euler(0, settings.bandYaw, 0));
            board.SetPositionAndRotation(settings.boardAt, Quaternion.Euler(settings.boardPitch, settings.boardYaw, 0));
            board.localScale = new Vector3(settings.boardWidth, settings.boardWidth / BoardAspect, 1);
        }

        /// <summary>The band and the board in front of someone at <paramref name="at"/> looking along <paramref name="forward"/>.</summary>
        void Recentre(Vector3 at, Vector3 forward)
        {
            forward.y = 0;
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.forward;
            var turn = Quaternion.LookRotation(forward.normalized);
            var yaw = turn.eulerAngles.y;
            var floor = new Vector3(at.x, 0, at.z);
            // the band ahead as on the desktop's stage, you at its front edge
            var bandAt = floor + turn * new Vector3(0, 0, 0.5f);
            settings.bandX = bandAt.x;
            settings.bandZ = bandAt.z;
            settings.bandYaw = yaw;
            // the board like a music stand: a metre ahead, well below the eyes (the band shows over it), leaning back
            var boardAt = floor + turn * new Vector3(0, 0, 1.1f);
            boardAt.y = Mathf.Clamp(at.y - 0.75f, 0.55f, 1.3f);
            settings.boardAt = boardAt;
            settings.boardPitch = 35;
            settings.boardYaw = yaw;
            settings.placed = true;
        }

        void Save()
        {
            if (band != null)
            {
                settings.bandX = band.transform.position.x;
                settings.bandZ = band.transform.position.z;
                settings.bandYaw = band.transform.eulerAngles.y;
            }
            settings.boardAt = board.position;
            var e = board.eulerAngles;
            settings.boardPitch = e.x > 180 ? e.x - 360 : e.x;
            settings.boardYaw = e.y;
            settings.boardWidth = board.localScale.x;
            try
            {
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(settings, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("GuitarTutor VR: could not save the settings: " + ex.Message);
            }
        }

        // ------------------------------------------------------------ controllers
        void Update()
        {
            if (!Active)
                return;
            if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
                NextKey();
            if (recentreWhenTracked && xrRunning && headTracked.IsPressed())
            {
                // the first time in VR: everything in front of wherever you are
                recentreWhenTracked = false;
                Recentre(eye.transform.position, eye.transform.forward);
                Apply();
                Save();
            }

            if (buttonA.WasPressedThisFrame())
                Commanded?.Invoke(Command.PlayPause);
            if (buttonB.WasPressedThisFrame())
                Commanded?.Invoke(Command.Restart);
            if (buttonX.WasPressedThisFrame())
                NextKey();
            if (buttonY.WasPressedThisFrame())
                CalibrateFloor();
            if (stickClick.WasPressedThisFrame())
            {
                Recentre(eye.transform.position, eye.transform.forward);
                Apply();
                Save();
                Debug.Log("GuitarTutor VR: the band and the board in front of you");
            }
            var left = leftStick.ReadValue<Vector2>();
            switch (Flick(left, ref leftX, ref leftY))
            {
                case 1: Commanded?.Invoke(Command.SectionOn); break;
                case -1: Commanded?.Invoke(Command.SectionBack); break;
                case 2: Commanded?.Invoke(Command.Help); break;
            }
            Aim(rightStick.ReadValue<Vector2>());
        }

        /// <summary>A stick pushed most of the way from the middle: 1 right, -1 left, 2 up, -2 down, 0 nothing new.</summary>
        static int Flick(Vector2 v, ref int x, ref int y)
        {
            int Axis(float value, float other, ref int state)
            {
                var now = Mathf.Abs(value) > 0.7f && Mathf.Abs(value) > Mathf.Abs(other) ? Math.Sign(value) : Mathf.Abs(value) < 0.3f ? 0 : state;
                var step = now != state ? now : 0;
                state = now;
                return step;
            }
            var sx = Axis(v.x, v.y, ref x);
            var sy = Axis(v.y, v.x, ref y);
            return sx != 0 ? sx : sy * 2;
        }

        void Aim(Vector2 stick)
        {
            var tracked = rightTracked.IsPressed();
            beam.gameObject.SetActive(tracked);
            if (!tracked)
            {
                dot.gameObject.SetActive(false);
                held = Held.Nothing;
                return;
            }
            var ray = new Ray(rightAim.position, rightAim.forward);
            var onBoard = BoardHit(ray);
            var floor = new Plane(Vector3.up, Vector3.zero);
            Vector3? onFloor = floor.Raycast(ray, out var d) && d < 8 ? ray.GetPoint(d) : (Vector3?)null;
            var target = onBoard != null ? Held.Board : onFloor != null && band != null && band.Covers(onFloor.Value) ? Held.Band : Held.Nothing;

            if (trigger.WasPressedThisFrame() && target != Held.Nothing)
            {
                held = target;
                var inverse = Quaternion.Inverse(rightAim.rotation);
                boardInAimAt = inverse * (board.position - rightAim.position);
                boardInAimRotation = inverse * board.rotation;
                if (onFloor != null && band != null)
                    bandOffset = band.transform.position - onFloor.Value;
            }
            if (trigger.WasReleasedThisFrame() && held != Held.Nothing)
            {
                if (held == Held.Board)  // stood upright again: no roll
                    board.rotation = Quaternion.LookRotation(board.forward, Vector3.up);
                held = Held.Nothing;
                Save();
            }

            if (held == Held.Board)
            {
                // the board goes with the controller; the stick up / down makes it bigger / smaller
                board.SetPositionAndRotation(rightAim.position + rightAim.rotation * boardInAimAt, rightAim.rotation * boardInAimRotation);
                var width = Mathf.Clamp(board.localScale.x * (1 + stick.y * Time.deltaTime), 0.5f, 3f);
                board.localScale = new Vector3(width, width / BoardAspect, 1);
            }
            else if (held == Held.Band && onFloor != null)
            {
                // the band slides along the floor under the beam; the stick left / right turns it about that point
                var at = onFloor.Value;
                band.transform.position = Flat(at + bandOffset);
                if (Mathf.Abs(stick.x) > 0.2f)
                {
                    band.transform.RotateAround(at, Vector3.up, stick.x * 60f * Time.deltaTime);
                    bandOffset = band.transform.position - at;
                }
            }
            else if (held == Held.Nothing)
            {
                switch (Flick(stick, ref rightX, ref rightY))
                {
                    case 1: Commanded?.Invoke(Command.BarOn); break;
                    case -1: Commanded?.Invoke(Command.BarBack); break;
                    case 2: Commanded?.Invoke(Command.Faster); break;
                    case -2: Commanded?.Invoke(Command.Slower); break;
                }
            }

            // the beam to what it points at
            var length = held == Held.Board ? boardInAimAt.magnitude : onBoard ?? (onFloor != null ? d : 3f);
            beam.localScale = new Vector3(0.004f, 0.004f, length);
            beam.localPosition = new Vector3(0, 0, length / 2);
            beamMaterial.color = target != Held.Nothing || held != Held.Nothing ? Aiming : Idle;
            var lands = held == Held.Nothing && (onBoard != null || onFloor != null);
            dot.gameObject.SetActive(lands);
            if (lands)
                dot.position = ray.GetPoint(onBoard ?? d);
        }

        /// <summary>How far along the ray it meets the board, or null if it misses it.</summary>
        float? BoardHit(Ray ray)
        {
            if (!new Plane(board.forward, board.position).Raycast(ray, out var d) || d <= 0)
                return null;
            var local = board.InverseTransformPoint(ray.GetPoint(d));
            return Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f ? d : (float?)null;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        void CalibrateFloor()
        {
            if (!leftTracked.IsPressed())
                return;
            // the left controller rests on the floor: its tip is at height 0
            settings.floorOffset -= leftGrip.position.y - ControllerTipBelowGrip;
            Apply();
            Save();
            Debug.Log($"GuitarTutor VR: floor calibrated, offset {settings.floorOffset:F3} m");
        }

        void NextKey()
        {
            settings.key = (Mathf.Abs(settings.key) + 1) % Keys.Length;
            Apply();
            Save();
            Debug.Log("GuitarTutor VR: key colour #" + ColorUtility.ToHtmlStringRGB(Keys[settings.key]));
        }
    }
}
