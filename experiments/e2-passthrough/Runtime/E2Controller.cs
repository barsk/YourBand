// Experiment E2 (see experiments/e2-passthrough/README.md): a musician in your room through Virtual Desktop's
// chroma-key passthrough.
//
// The camera clears to the key colour and nothing blends into it (no post-processing, no anti-aliasing, cutout
// hair), so Virtual Desktop can replace exactly those pixels with the camera view of your room.
//
// In the headset (right controller):
//   trigger  aim at the musician's feet and hold to move him along the floor
//   stick    left/right turns him
//   A        hold the controller on the floor and press: calibrates the floor height
//   B        next key colour (magenta, green, blue, black)
// Settings (key colour, floor offset, where the musician stands) are kept in e2-settings.json next to the log.
//
// Command line: -noxr (desktop only), -e2check <folder> (desktop check of the key colour, then quit).

using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Management;

public class E2Controller : MonoBehaviour
{
    public Camera eye;
    public Transform cameraOffset;   // the rig's floor offset (XR Origin's camera offset)
    public Transform rightHand;      // grip pose
    public Transform rightAim;       // pointer pose
    public Transform musician;
    public float controllerTipBelowGrip = 0.04f;

    static readonly Color[] Keys = { new Color(1, 0, 1), new Color(0, 1, 0), new Color(0, 0, 1), Color.black };

    [System.Serializable]
    class Settings
    {
        public int key;
        public float floorOffset;
        public float musicianX, musicianZ = 2f, musicianYaw = 180f;
    }

    Settings settings = new Settings();
    InputAction trigger, buttonA, buttonB, stick;
    bool grabbing;
    Vector3 grabOffset;

    static string SettingsPath => Path.Combine(Application.persistentDataPath, "e2-settings.json");

    void Awake()
    {
        try
        {
            if (File.Exists(SettingsPath))
                settings = JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("E2: settings unreadable, using defaults: " + e.Message);
        }
        Apply();

        trigger = new InputAction("trigger", binding: "<XRController>{RightHand}/triggerPressed");
        buttonA = new InputAction("A", binding: "<XRController>{RightHand}/primaryButton");
        buttonB = new InputAction("B", binding: "<XRController>{RightHand}/secondaryButton");
        stick = new InputAction("stick", binding: "<XRController>{RightHand}/thumbstick");
        foreach (var a in new[] { trigger, buttonA, buttonB, stick })
            a.Enable();
        buttonA.performed += _ => CalibrateFloor();
        buttonB.performed += _ => NextKey();
    }

    IEnumerator Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        var check = args.SkipWhile(a => a != "-e2check").Skip(1).FirstOrDefault();
        if (!args.Contains("-noxr") && check == null)
            yield return StartXR();
        else
            Debug.Log("E2: desktop only (no XR)");
        if (check != null)
            yield return KeyCheck(check);
    }

    IEnumerator StartXR()
    {
        var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (manager == null)
        {
            Debug.LogWarning("E2: no XR settings");
            yield break;
        }
        yield return manager.InitializeLoader();
        if (manager.activeLoader == null)
        {
            Debug.LogWarning("E2: no OpenXR runtime answered (is Virtual Desktop streaming?)");
            yield break;
        }
        manager.StartSubsystems();
        Debug.Log("E2: XR started with " + manager.activeLoader.name);
    }

    void OnDestroy()
    {
        var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (manager != null && manager.activeLoader != null)
        {
            manager.StopSubsystems();
            manager.DeinitializeLoader();
        }
    }

    void Update()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.kKey.wasPressedThisFrame)
                NextKey();
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
                Application.Quit();
        }
        if (rightAim == null || musician == null)
            return;

        // aim at the floor: where the pointer ray meets it
        var ray = new Ray(rightAim.position, rightAim.forward);
        var floor = new Plane(Vector3.up, Vector3.zero);
        var hit = floor.Raycast(ray, out var distance) ? ray.GetPoint(distance) : (Vector3?)null;
        if (trigger.WasPressedThisFrame() && hit != null && Flat(hit.Value - musician.position).magnitude < 0.7f)
        {
            grabbing = true;
            grabOffset = Flat(musician.position - hit.Value);
        }
        if (trigger.WasReleasedThisFrame() && grabbing)
        {
            grabbing = false;
            Save();
        }
        if (grabbing && hit != null)
            musician.position = Flat(hit.Value + grabOffset);

        var turn = stick.ReadValue<Vector2>().x;
        var turningNow = Mathf.Abs(turn) > 0.2f;
        if (turningNow)
            musician.Rotate(0, turn * 90f * Time.deltaTime, 0, Space.World);
        else if (turning)
            Save();  // once, when the stick is let go
        turning = turningNow;
    }

    bool turning;

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

    void CalibrateFloor()
    {
        if (rightHand == null)
            return;
        // the controller rests on the floor: its tip is at height 0
        var tip = rightHand.position.y - controllerTipBelowGrip;
        settings.floorOffset -= tip;
        Apply();
        Save();
        Debug.Log($"E2: floor calibrated, offset {settings.floorOffset:F3} m");
    }

    void NextKey()
    {
        settings.key = (settings.key + 1) % Keys.Length;
        Apply();
        Save();
        Debug.Log("E2: key colour " + ColorUtility.ToHtmlStringRGB(Keys[settings.key]));
    }

    void Apply()
    {
        if (eye != null)
        {
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = Keys[settings.key % Keys.Length];
        }
        if (cameraOffset != null)
            cameraOffset.localPosition = new Vector3(0, settings.floorOffset, 0);
        if (musician != null)
        {
            musician.position = new Vector3(settings.musicianX, 0, settings.musicianZ);
            musician.rotation = Quaternion.Euler(0, settings.musicianYaw, 0);
        }
    }

    void Save()
    {
        if (musician != null)
        {
            settings.musicianX = musician.position.x;
            settings.musicianZ = musician.position.z;
            settings.musicianYaw = musician.eulerAngles.y;
        }
        try
        {
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(settings, true));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("E2: could not save settings: " + e.Message);
        }
    }

    /// <summary>
    /// Desktop check of what Virtual Desktop will key: render a frame, count the pixels that are exactly the key,
    /// the musician's pixels, and the musician's edge pixels tinted towards the key (a pink fringe), then quit.
    /// </summary>
    IEnumerator KeyCheck(string folder)
    {
        Directory.CreateDirectory(folder);
        for (int i = 0; i < 90; ++i)
            yield return null;  // the animation is under way
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        eye.targetTexture = rt;
        yield return null;
        yield return new WaitForEndOfFrame();
        RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        eye.targetTexture = null;
        File.WriteAllBytes(Path.Combine(folder, "e2_desktop.png"), image.EncodeToPNG());

        var key = (Color32)Keys[settings.key % Keys.Length];
        var px = image.GetPixels32();
        int w = image.width, h = image.height, exact = 0, model = 0, edge = 0, tinted = 0;
        bool IsKey(Color32 c) => c.r == key.r && c.g == key.g && c.b == key.b;
        for (int y = 1; y < h - 1; ++y)
            for (int x = 1; x < w - 1; ++x)
            {
                var c = px[y * w + x];
                if (IsKey(c))
                {
                    ++exact;
                    continue;
                }
                ++model;
                if (IsKey(px[y * w + x - 1]) || IsKey(px[y * w + x + 1]) || IsKey(px[(y - 1) * w + x]) || IsKey(px[(y + 1) * w + x]))
                {
                    ++edge;
                    // tinted towards the key: its distance to the key much smaller than a typical skin/cloth colour
                    var d = Mathf.Abs(c.r - key.r) + Mathf.Abs(c.g - key.g) + Mathf.Abs(c.b - key.b);
                    if (d < 200)
                        ++tinted;
                }
            }
        var report = $"key #{ColorUtility.ToHtmlStringRGB(Keys[settings.key % Keys.Length])}: {exact} key pixels, {model} musician pixels, "
                     + $"{edge} edge pixels, {tinted} edge pixels near the key colour";
        File.WriteAllText(Path.Combine(folder, "e2_check.txt"), report + "\n");
        Debug.Log("E2 check: " + report);
        Application.Quit();
    }
}
