using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YourBand.Runtime.Band;
using YourBand.Runtime.Desktop;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace YourBand.Runtime.Vr
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
    /// vr-settings.json in the game's data folder (<c>-vrsettings &lt;file&gt;</c>: another file, for tests).
    /// Controllers: A play / pause, B restart, right stick bars and speed, left stick sections (up: help); the
    /// trigger moves the board, or the musician it is aimed at (to fit the band into the room; a ring shows their
    /// station, red where it crowds another's), the grip the whole band; the stick meanwhile resizes the board,
    /// or pushes, pulls and turns what is held; right stick click brings the band and the board back in front of you; X
    /// the next key colour; Y, with the left controller resting on the floor, sets the floor's height.
    /// </remarks>
    public sealed class VrMode : MonoBehaviour
    {
        /// <summary>The key colours Virtual Desktop can key out: magenta (the default), green, blue, black.</summary>
        static readonly Color[] Keys = { new Color(1, 0, 1), new Color(0, 1, 0), new Color(0, 0, 1), Color.black };
        static readonly Color BoardColor = new Color32(0x14, 0x16, 0x1a, 0xff);
        static readonly Color Aiming = new Color(0.95f, 0.66f, 0.23f), Idle = new Color(0.8f, 0.8f, 0.8f), Crowded = new Color(0.95f, 0.25f, 0.2f);
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
            public List<Station> stations = new List<Station>();  // musicians moved from the stage layout
        }

        /// <summary>Where a musician stands in the band (relative to it).</summary>
        [Serializable]
        class Station
        {
            public string role;
            public float x, z, yaw;
        }

        /// <summary>A command from the controllers.</summary>
        public event Action<Command> Commanded;

        public bool Active { get; private set; }
        /// <summary>For the status line: whether the headset is there.</summary>
        public string Status { get; private set; } = "";

        Settings settings = new Settings();
        /// <summary>vr-settings.json in the game's data folder, or the file given by <c>-vrsettings</c> (tests).</summary>
        static string SettingsPath => Environment.GetCommandLineArgs().SkipWhile(a => a != "-vrsettings").Skip(1).FirstOrDefault()
                                      ?? Path.Combine(Application.persistentDataPath, "vr-settings.json");

        Camera eye;
        Transform eyeParent;
        Vector3 eyeAt;
        Quaternion eyeRotation;
        float eyeFov;
        Color eyeBackground;
        CameraClearFlags eyeClear;
        bool eyeHdr, eyeMsaa;

        Transform rig, offset, rightAim, leftGrip, beam, dot;
        Material beamMaterial, ringMaterial;
        LineRenderer ring;
        TrackedPoseDriver head;
        BandStage band;
        UIDocument document;
        PanelSettings desktopPanel, boardPanel;
        RenderTexture boardTexture;
        Transform board;
        bool xrRunning, recentreWhenTracked;
        float trackedFor;  // how long the headset has been tracked, for the first placing

        InputAction trigger, grip, buttonA, buttonB, buttonX, buttonY, stickClick, rightStick, leftStick, rightTracked, leftTracked, headTracked;
        enum Held { Nothing, Board, Band, Musician }
        Held held;
        Musician heldMusician;
        Vector3 boardInAimAt, bandOffset;
        float grabDistance;  // how far along the beam a held musician or band is
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
                Debug.LogWarning("YourBand VR: settings unreadable, using defaults: " + e.Message);
            }
            InputAction Button(string binding) => new InputAction(type: InputActionType.Button, binding: binding);
            trigger = Button("<XRController>{RightHand}/triggerPressed");
            grip = Button("<XRController>{RightHand}/gripPressed");
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
            OpenXRRuntime.wantsToQuit += HeadsetGone;
        }

        /// <summary>
        /// The OpenXR runtime ended the session (Virtual Desktop disconnected, say) and has shut XR down: OpenXR
        /// would quit the game; instead the room stays on the desktop, and X twice looks for the headset again.
        /// </summary>
        bool HeadsetGone()
        {
            if (!Active)
                return true;
            xrRunning = false;
            Status = "VR: the headset went (X twice to look for it again)";
            Debug.LogWarning("YourBand VR: the OpenXR session ended; the room is shown on the desktop");
            return false;
        }

        IEnumerable<InputAction> Actions => new[] { trigger, grip, buttonA, buttonB, buttonX, buttonY, stickClick, rightStick, leftStick, rightTracked, leftTracked, headTracked };

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
            Debug.Log("YourBand: VR mode");
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
                foreach (var m in band.Musicians)
                    band.ResetStation(m);
            }
            RenderPipelineManager.beginContextRendering -= MakeMips;
            Destroy(rig.gameObject);
            Destroy(board.gameObject);
            Destroy(boardPanel);
            boardTexture.Release();
            Destroy(boardTexture);
            Status = "";
            Debug.Log("YourBand: desktop mode");
        }

        void OnDestroy()
        {
            OpenXRRuntime.wantsToQuit -= HeadsetGone;
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
                Debug.LogWarning("YourBand VR: no XR settings");
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
                Debug.LogWarning("YourBand VR: no OpenXR runtime answered (is Virtual Desktop streaming?); the room is shown on the desktop");
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
            Debug.Log("YourBand VR: XR started with " + manager.activeLoader.name);
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
                    Debug.LogWarning("YourBand VR: no OpenXR runtime at " + json + ", using the system's");
                return;
            }
            Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", json);
            Debug.Log("YourBand VR: OpenXR runtime " + json);
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
            // a musician's station on the floor, while it is aimed at or moved
            ringMaterial = new Material(beamMaterial);
            ringMaterial.SetFloat("_Cull", (float)CullMode.Off);  // seen from above whichever way the line faces
            ring = new GameObject("Station").AddComponent<LineRenderer>();
            ring.transform.SetParent(rig, false);
            ring.transform.localRotation = Quaternion.Euler(-90, 0, 0);  // flat on the floor
            ring.alignment = LineAlignment.TransformZ;
            ring.sharedMaterial = ringMaterial;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.loop = true;
            ring.widthMultiplier = 0.015f;
            ring.positionCount = 48;
            ring.enabled = false;
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
            {
                band.transform.SetPositionAndRotation(new Vector3(settings.bandX, 0, settings.bandZ), Quaternion.Euler(0, settings.bandYaw, 0));
                foreach (var m in band.Musicians)
                {
                    var station = settings.stations.Find(x => x.role == m.role.ToString());
                    if (station != null)
                        m.transform.SetLocalPositionAndRotation(new Vector3(station.x, 0, station.z), Quaternion.Euler(0, station.yaw, 0));
                    else
                        band.ResetStation(m);
                }
            }
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
                settings.stations = band.Musicians.Select(m => new Station
                {
                    role = m.role.ToString(), x = m.transform.localPosition.x, z = m.transform.localPosition.z, yaw = m.transform.localEulerAngles.y,
                }).ToList();
            }
            settings.boardAt = board.position;
            var e = board.eulerAngles;
            settings.boardPitch = e.x > 180 ? e.x - 360 : e.x;
            settings.boardYaw = e.y;
            settings.boardWidth = board.localScale.x;
            try
            {
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(settings, true));
                Debug.Log("YourBand VR: saved " + JsonUtility.ToJson(settings));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("YourBand VR: could not save the settings: " + ex.Message);
            }
        }

        // ------------------------------------------------------------ controllers
        void Update()
        {
            if (!Active)
                return;
            if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
                NextKey();
            trackedFor = xrRunning && headTracked.IsPressed() ? trackedFor + Time.unscaledDeltaTime : 0;
            if (recentreWhenTracked && trackedFor > 0.5f)
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
                Debug.Log("YourBand VR: the band and the board in front of you");
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
                ring.enabled = false;
                held = Held.Nothing;
                return;
            }
            // what the beam points at: the board or a musician, whichever is nearer; else the floor
            var ray = new Ray(rightAim.position, rightAim.forward);
            var onBoard = BoardHit(ray);
            var (aimedAt, onMusician) = MusicianHit(ray);
            if (onBoard != null && onMusician != null)
            {
                if (onBoard < onMusician)
                    (aimedAt, onMusician) = (null, null);
                else
                    onBoard = null;
            }
            var floor = new Plane(Vector3.up, Vector3.zero);
            float? onFloor = floor.Raycast(ray, out var d) && d < 8 ? d : (float?)null;
            var target = onBoard != null ? Held.Board : aimedAt != null ? Held.Musician : Held.Nothing;

            // the trigger takes the board or one musician, the grip the whole band
            if (held == Held.Nothing && (trigger.WasPressedThisFrame() && target != Held.Nothing || grip.WasPressedThisFrame() && aimedAt != null))
            {
                held = trigger.WasPressedThisFrame() ? target : Held.Band;
                heldMusician = aimedAt;
                var inverse = Quaternion.Inverse(rightAim.rotation);
                boardInAimAt = inverse * (board.position - rightAim.position);
                boardInAimRotation = inverse * board.rotation;
                if (aimedAt != null)
                {
                    grabDistance = onMusician.Value;
                    bandOffset = Flat((held == Held.Musician ? aimedAt.transform.position : band.transform.position) - ray.GetPoint(grabDistance));
                }
            }
            if ((held == Held.Band ? grip : trigger).WasReleasedThisFrame() && held != Held.Nothing)
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
                var width = Mathf.Clamp(board.localScale.x * (1 + stick.y * 0.5f * Time.deltaTime), 0.5f, 3f);
                board.localScale = new Vector3(width, width / BoardAspect, 1);
            }
            else if (held == Held.Band || held == Held.Musician)
            {
                // the band, or one musician, stays where the beam took hold of it, along the floor; the stick up /
                // down pushes it away or pulls it in, left / right turns it (a musician on the spot, the band
                // about where the beam holds it)
                grabDistance = Mathf.Clamp(grabDistance * (1 + stick.y * 0.8f * Time.deltaTime), 0.3f, 10f);
                var at = Flat(ray.GetPoint(grabDistance));
                var moving = held == Held.Band ? band.transform : heldMusician.transform;
                moving.position = at + bandOffset;
                if (Mathf.Abs(stick.x) > 0.2f)
                {
                    moving.RotateAround(held == Held.Band ? at : moving.position, Vector3.up, stick.x * 60f * Time.deltaTime);
                    bandOffset = moving.position - at;
                }
            }
            else
            {
                switch (Flick(stick, ref rightX, ref rightY))
                {
                    case 1: Commanded?.Invoke(Command.BarOn); break;
                    case -1: Commanded?.Invoke(Command.BarBack); break;
                    case 2: Commanded?.Invoke(Command.Faster); break;
                    case -2: Commanded?.Invoke(Command.Slower); break;
                }
            }

            // the beam to what it points at, a dot where it lands
            var hit = held == Held.Board ? boardInAimAt.magnitude : held != Held.Nothing ? grabDistance : onBoard ?? onMusician ?? onFloor;
            var length = hit ?? 3f;
            beam.localScale = new Vector3(0.004f, 0.004f, length);
            beam.localPosition = new Vector3(0, 0, length / 2);
            beamMaterial.color = target != Held.Nothing || held != Held.Nothing ? Aiming : Idle;
            dot.gameObject.SetActive(held == Held.Nothing && hit != null);
            if (hit != null)
                dot.position = ray.GetPoint(hit.Value);
            ShowStation(held == Held.Musician ? heldMusician : held == Held.Nothing ? aimedAt : null);
        }

        /// <summary>
        /// The musician the ray meets first, and how far along it: each stands in an upright cylinder round their
        /// station (their footprint, as tall as a standing player).
        /// </summary>
        (Musician, float?) MusicianHit(Ray ray)
        {
            if (band == null)
                return (null, null);
            Musician found = null;
            float? nearest = null;
            foreach (var m in band.Musicians)
            {
                var t = CylinderHit(ray, m.transform.position, BandStage.Footprint(m.role), 1.9f);
                if (t != null && (nearest == null || t < nearest))
                {
                    found = m;
                    nearest = t;
                }
            }
            return (found, nearest);
        }

        /// <summary>Where a ray first meets an upright cylinder standing on the floor at <paramref name="foot"/>, or null.</summary>
        static float? CylinderHit(Ray ray, Vector3 foot, float radius, float height)
        {
            // the stretch of the ray inside the circle (seen from above), then inside the height
            var o = new Vector2(ray.origin.x - foot.x, ray.origin.z - foot.z);
            var dir = new Vector2(ray.direction.x, ray.direction.z);
            float from, to;
            var a = dir.sqrMagnitude;
            if (a < 1e-8f)
            {
                if (o.sqrMagnitude > radius * radius)
                    return null;
                (from, to) = (0, float.MaxValue);
            }
            else
            {
                var b = 2 * Vector2.Dot(dir, o);
                var c = o.sqrMagnitude - radius * radius;
                var disc = b * b - 4 * a * c;
                if (disc < 0)
                    return null;
                var root = Mathf.Sqrt(disc);
                (from, to) = ((-b - root) / (2 * a), (-b + root) / (2 * a));
            }
            var y = ray.origin.y - foot.y;
            if (Mathf.Abs(ray.direction.y) > 1e-6f)
            {
                var t0 = (0 - y) / ray.direction.y;
                var t1 = (height - y) / ray.direction.y;
                from = Mathf.Max(from, Mathf.Min(t0, t1));
                to = Mathf.Min(to, Mathf.Max(t0, t1));
            }
            else if (y < 0 || y > height)
                return null;
            from = Mathf.Max(from, 0);
            return from <= to ? from : (float?)null;
        }

        /// <summary>The ring round a musician's station on the floor: red where it crowds another's.</summary>
        void ShowStation(Musician m)
        {
            ring.enabled = m != null;
            if (m == null)
                return;
            var r = BandStage.Footprint(m.role);
            var centre = Flat(m.transform.position) + Vector3.up * 0.005f;
            for (int i = 0; i < ring.positionCount; ++i)
            {
                var a = i * Mathf.PI * 2 / ring.positionCount;
                ring.SetPosition(i, centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r);
            }
            ringMaterial.color = band.Crowds(m) ? Crowded : Aiming;
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
            Debug.Log($"YourBand VR: floor calibrated, offset {settings.floorOffset:F3} m");
        }

        void NextKey()
        {
            settings.key = (Mathf.Abs(settings.key) + 1) % Keys.Length;
            Apply();
            Save();
            Debug.Log("YourBand VR: key colour #" + ColorUtility.ToHtmlStringRGB(Keys[settings.key]));
        }
    }
}
