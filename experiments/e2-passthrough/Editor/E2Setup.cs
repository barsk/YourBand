// Experiment E2 editor setup (batch mode: -executeMethod E2Setup.Run): the Rocketbox guitarist on a key-colour
// background, a minimal XR rig (XR Origin, head and right controller from the Input System), URP without
// anti-aliasing, XR started by the app (so desktop runs do not start a VR runtime), and a Windows build.

using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class E2Setup
{
    const string Root = "Assets/E2";
    const string AvatarFbx = Root + "/Avatar/Export/Male_Adult_04.fbx";
    const string Textures = Root + "/Avatar/Textures/";
    const string ClipFbx = Root + "/Mocap/guitar_playing_111490901.fbx";
    const string Generated = Root + "/Generated/";
    const string ScenePath = Root + "/E2.unity";

    public static void Run()
    {
        Directory.CreateDirectory(Generated);
        ConfigureTextures();
        var body = Lit("Body", Tex("m006_body_color.tga"), Tex("m006_body_normal.tga"), Mask("body"), false);
        var head = Lit("Head", Tex("m006_head_color.tga"), Tex("m006_head_normal.tga"), Mask("head"), false);
        var cards = (TextureImporter)AssetImporter.GetAtPath(Textures + "m006_opacity_cards4.png");  // the mockup's recoloured hair
        var hairTexture = cards != null && cards.DoesSourceTextureHaveAlpha() ? Tex("m006_opacity_cards4.png") : Tex("m006_opacity_color.tga");
        var hair = Lit("Hair", hairTexture, null, null, true);
        AssetDatabase.SaveAssets();
        ConfigureModels(body, head, hair);
        ConfigureRendering();
        BuildScene();
        Build();
    }

    // ------------------------------------------------------------------ avatar (as in E4, URP)
    static void ConfigureTextures()
    {
        foreach (var path in Directory.GetFiles(Textures).Where(p => !p.EndsWith(".meta")))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
            if (importer == null)
                continue;
            importer.textureType = path.Contains("_normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = path.Contains("_color") || path.Contains("_cards");
            importer.alphaIsTransparency = path.Contains("_opacity");
            importer.SaveAndReimport();
        }
    }

    static Texture2D Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + file);

    /// The mockup's look map (G roughness) as a smoothness mask: R metallic 0, G occlusion 1, A smoothness.
    static Texture2D Mask(string part)
    {
        var look = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        look.LoadImage(File.ReadAllBytes(Textures + "m006_" + part + "_look4.png"));
        var src = look.GetPixels32();
        var mask = src.Select(c => new Color32(0, 255, 0, (byte)(255 - c.g))).ToArray();
        var t = new Texture2D(look.width, look.height, TextureFormat.RGBA32, true, true);
        t.SetPixels32(mask);
        var path = Generated + part + "_mask.png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Lit(string name, Texture2D color, Texture2D normal, Texture2D mask, bool cutout)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetTexture("_BaseMap", color);
        if (normal != null)
        {
            m.SetTexture("_BumpMap", normal);
            m.EnableKeyword("_NORMALMAP");
        }
        if (mask != null)
        {
            m.SetTexture("_MetallicGlossMap", mask);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", 1f);
        }
        if (cutout)  // hair is either there or not: no half-transparent pixels to blend with the key colour
        {
            m.SetFloat("_AlphaClip", 1);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }
        AssetDatabase.CreateAsset(m, Generated + name + ".mat");
        return m;
    }

    static void ConfigureModels(Material body, Material head, Material hair)
    {
        var avatar = (ModelImporter)AssetImporter.GetAtPath(AvatarFbx);
        avatar.animationType = ModelImporterAnimationType.Human;
        avatar.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        avatar.importAnimation = false;
        avatar.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        avatar.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "m006_body"), body);
        avatar.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "m006_head"), head);
        avatar.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "m006_opacity"), hair);
        avatar.SaveAndReimport();

        var clip = (ModelImporter)AssetImporter.GetAtPath(ClipFbx);
        clip.animationType = ModelImporterAnimationType.Human;
        clip.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        clip.materialImportMode = ModelImporterMaterialImportMode.None;
        var takes = clip.defaultClipAnimations;
        foreach (var t in takes)
        {
            t.loopTime = true;
            t.lockRootRotation = t.lockRootHeightY = t.lockRootPositionXZ = true;
            t.keepOriginalOrientation = t.keepOriginalPositionY = t.keepOriginalPositionXZ = true;
        }
        clip.clipAnimations = takes;
        clip.SaveAndReimport();
    }

    // ------------------------------------------------------------------ rendering and XR
    static void ConfigureRendering()
    {
        // no anti-aliasing anywhere: an edge pixel is the musician or the key, never a blend of both
        for (int level = 0; level < QualitySettings.names.Length; ++level)
            if (QualitySettings.GetRenderPipelineAssetAt(level) is UniversalRenderPipelineAsset urp)
            {
                urp.msaaSampleCount = 1;
                urp.renderScale = 1f;
                EditorUtility.SetDirty(urp);
            }
        if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset main)
        {
            main.msaaSampleCount = 1;
            EditorUtility.SetDirty(main);
        }

        // the app starts XR itself (E2Controller), so a desktop run does not start a VR runtime
        var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (xr != null)
        {
            xr.InitManagerOnStart = false;
            EditorUtility.SetDirty(xr);
        }
        // the VR template builds Windows players with IL2CPP; Mono needs no extra module
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.productName = "E2 Passthrough";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.runInBackground = true;
        AssetDatabase.SaveAssets();
    }

    static InputActionProperty Action(string binding) => new InputActionProperty(new InputAction(binding: binding));

    static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // rig: XR Origin (floor tracking) > camera offset > head camera, right controller grip and aim
        var rig = new GameObject("XR Origin");
        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(rig.transform, false);
        var eyeObject = new GameObject("Main Camera") { tag = "MainCamera" };
        eyeObject.transform.SetParent(offset.transform, false);
        eyeObject.transform.localPosition = new Vector3(0, 1.6f, 0);  // the desktop view; the headset moves it
        var eye = eyeObject.AddComponent<Camera>();
        eye.nearClipPlane = 0.05f;
        var cameraData = eye.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = false;  // tone mapping would change the key colour
        cameraData.antialiasing = AntialiasingMode.None;
        var head = eyeObject.AddComponent<TrackedPoseDriver>();
        head.positionInput = Action("<XRHMD>/centerEyePosition");
        head.rotationInput = Action("<XRHMD>/centerEyeRotation");
        var origin = rig.AddComponent<XROrigin>();
        origin.Camera = eye;
        origin.CameraFloorOffsetObject = offset;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        Transform Controller(string name, string pose)
        {
            var go = new GameObject(name);
            go.transform.SetParent(offset.transform, false);
            var driver = go.AddComponent<TrackedPoseDriver>();
            driver.positionInput = Action($"<XRController>{{RightHand}}/{pose}Position");
            driver.rotationInput = Action($"<XRController>{{RightHand}}/{pose}Rotation");
            return go.transform;
        }
        var grip = Controller("Right Grip", "device");
        var aim = Controller("Right Aim", "pointer");
        var pointer = GameObject.CreatePrimitive(PrimitiveType.Cube);  // a thin aiming beam
        Object.DestroyImmediate(pointer.GetComponent<Collider>());
        pointer.name = "Beam";
        pointer.transform.SetParent(aim, false);
        pointer.transform.localScale = new Vector3(0.004f, 0.004f, 2f);
        pointer.transform.localPosition = new Vector3(0, 0, 1f);
        pointer.GetComponent<Renderer>().sharedMaterial = Unlit("Beam", new Color(0.95f, 0.95f, 0.95f));

        // the musician on his station, facing the player; a ring marks his footprint on the real floor
        var station = new GameObject("Musician");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(AvatarFbx));
        model.transform.SetParent(station.transform, false);
        var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        animator.avatar = AssetDatabase.LoadAllAssetsAtPath(AvatarFbx).OfType<Avatar>().First();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var clip = AssetDatabase.LoadAllAssetsAtPath(ClipFbx).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        animator.runtimeAnimatorController = AnimatorController.CreateAnimatorControllerAtPathWithClip(Root + "/Guitarist.controller", clip);
        var l = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
        var r = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
        var facing = Vector3.Cross(r - l, Vector3.up);
        facing.y = 0;
        model.transform.rotation = Quaternion.FromToRotation(facing.normalized, Vector3.forward) * model.transform.rotation;
        Ring(station.transform, 0.45f);
        station.transform.position = new Vector3(0, 0, 2);
        station.transform.rotation = Quaternion.Euler(0, 180, 0);

        var key = new GameObject("Key").AddComponent<Light>();
        key.type = LightType.Directional;
        key.transform.rotation = Quaternion.Euler(40, 20, 0);  // from above and behind the player, onto the musician's front
        key.intensity = 1.6f;
        key.shadows = LightShadows.Soft;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.37f);

        var controller = new GameObject("E2").AddComponent<E2Controller>();
        controller.eye = eye;
        controller.cameraOffset = offset.transform;
        controller.rightHand = grip;
        controller.rightAim = aim;
        controller.musician = station.transform;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    static Material Unlit(string name, Color color)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
        m.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(m, Generated + name + ".mat");
        return m;
    }

    static void Ring(Transform parent, float radius)
    {
        var go = new GameObject("Footprint");
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.widthMultiplier = 0.012f;
        line.positionCount = 64;
        for (int i = 0; i < 64; ++i)
        {
            var a = i * Mathf.PI * 2 / 64;
            line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0.003f, Mathf.Sin(a) * radius));
        }
        line.sharedMaterial = Unlit("Footprint", new Color(0.9f, 0.9f, 0.9f));
    }

    static void Build()
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Builds/E2/E2 Passthrough.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });
        Debug.Log($"E2: build {report.summary.result}, {report.summary.totalSize / 1e6:F0} MB");
        if (report.summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
