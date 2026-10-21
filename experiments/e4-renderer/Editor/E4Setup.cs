// Experiment E4 (see experiments/e4-renderer/README.md): the same Rocketbox guitarist in URP and in HDRP.
// Shared part of the editor setup; the renderer-specific materials and lights are in E4Pipeline.*.cs.
//
// Batch mode entry point:  Unity.exe -batchmode -quit -projectPath <project> -executeMethod E4Setup.Run
// It imports the avatar and the Mixamo clip as humanoids, makes materials from the Rocketbox textures
// (plus the roughness/skin maps made for the Blender mockup), builds the scene and a benchmark player.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class E4Setup
{
    const string Root = "Assets/E4";
    const string AvatarFbx = Root + "/Avatar/Export/Male_Adult_04.fbx";
    const string Textures = Root + "/Avatar/Textures/";
    const string ClipFbx = Root + "/Mocap/guitar_playing_111490901.fbx";
    const string Generated = Root + "/Generated/";
    const string ScenePath = Root + "/E4.unity";

    public static void Run()
    {
        Directory.CreateDirectory(Generated);
        ConfigureTextures();
        var materials = MakeMaterials();
        AssetDatabase.SaveAssets();
        ConfigureModels(materials);
        BuildScene();
        ConfigurePlayer();
        Build();
    }

    // ------------------------------------------------------------------ textures
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
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }
    }

    /// The Blender mockup's look map (R skin weight, G roughness, B sheen) as a mask map for both pipelines:
    /// R metallic 0, G occlusion 1, B detail 0, A smoothness; and the skin weight alone as a subsurface mask.
    static (Texture2D mask, Texture2D skin) LookMaps(string part)
    {
        var look = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        look.LoadImage(File.ReadAllBytes(Textures + "m006_" + part + "_look4.png"));
        var src = look.GetPixels32();
        var mask = new Color32[src.Length];
        var skin = new Color32[src.Length];
        for (int i = 0; i < src.Length; ++i)
        {
            mask[i] = new Color32(0, 255, 0, (byte)(255 - src[i].g));
            skin[i] = new Color32(src[i].r, src[i].r, src[i].r, 255);
        }
        return (Save(part + "_mask", mask, look.width, look.height), Save(part + "_skin", skin, look.width, look.height));
    }

    static Texture2D Save(string name, Color32[] pixels, int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
        t.SetPixels32(pixels);
        var path = Generated + name + ".png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + file);

    static Texture2D HairTexture()
    {
        // the recoloured hair cards from the mockup, if they carry the alpha; else Rocketbox's own
        var cards = (TextureImporter)AssetImporter.GetAtPath(Textures + "m006_opacity_cards4.png");
        return cards != null && cards.DoesSourceTextureHaveAlpha() ? Tex("m006_opacity_cards4.png") : Tex("m006_opacity_color.tga");
    }

    // ------------------------------------------------------------------ models
    static void ConfigureModels(Dictionary<string, Material> materials)
    {
        var avatar = (ModelImporter)AssetImporter.GetAtPath(AvatarFbx);
        avatar.animationType = ModelImporterAnimationType.Human;
        avatar.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        avatar.importAnimation = false;
        avatar.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        foreach (var kv in materials)  // the FBX's materials (m006_body, m006_head, m006_opacity) -> ours
            avatar.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
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

        var a = AssetDatabase.LoadAllAssetsAtPath(AvatarFbx).OfType<Avatar>().FirstOrDefault();
        Debug.Log($"E4: avatar humanoid {(a != null && a.isHuman)}, valid {(a != null && a.isValid)}");
    }

    // ------------------------------------------------------------------ scene
    static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(1.2f, 1, 1.2f);
        floor.GetComponent<Renderer>().sharedMaterial = Surface("Floor", new Color(0.33f, 0.24f, 0.17f), 0.35f);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Backdrop";
        wall.transform.position = new Vector3(0, 1.5f, 2.2f);
        wall.transform.localScale = new Vector3(8, 3, 0.1f);
        wall.GetComponent<Renderer>().sharedMaterial = Surface("Backdrop", new Color(0.55f, 0.55f, 0.58f), 0.2f);

        // the guitarist, playing the Mixamo pick-guitar clip, facing the cameras (-z)
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarFbx);
        var player = (GameObject)PrefabUtility.InstantiatePrefab(model);
        player.name = "Guitarist";
        var animator = player.GetComponent<Animator>() ?? player.AddComponent<Animator>();
        animator.avatar = AssetDatabase.LoadAllAssetsAtPath(AvatarFbx).OfType<Avatar>().First();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var clip = AssetDatabase.LoadAllAssetsAtPath(ClipFbx).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        var controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(Root + "/Guitarist.controller", clip);
        animator.runtimeAnimatorController = controller;

        var l = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
        var r = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
        var facing = Vector3.Cross(r - l, Vector3.up);
        facing.y = 0;
        player.transform.rotation = Quaternion.FromToRotation(facing.normalized, Vector3.back) * player.transform.rotation;
        var head = animator.GetBoneTransform(HumanBodyBones.Head).position;
        Debug.Log($"E4: head at {head}, height {head.y:F2} m");

        // cameras: head and hands close, and the whole player
        var neck = head - new Vector3(0, 0.12f, 0);
        var closeup = Camera("Closeup", neck + new Vector3(0.3f, 0.05f, -1.3f), neck - new Vector3(0, 0.08f, 0), 40);
        var full = Camera("Full", new Vector3(0.6f, 1.1f, -3.4f), new Vector3(0, 0.9f, 0), 35);
        full.enabled = false;

        MakeLights(head);

        var bench = new GameObject("Bench").AddComponent<E4Bench>();
        bench.shots = new[] { closeup, full };
        bench.subject = animator;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    static Camera Camera(string name, Vector3 at, Vector3 target, float fov)
    {
        var go = new GameObject(name);
        go.transform.position = at;
        go.transform.LookAt(target);
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        SetupCamera(cam);
        return cam;
    }

    // ------------------------------------------------------------------ player build
    static void ConfigurePlayer()
    {
        PlayerSettings.enableFrameTimingStats = true;  // GPU frame times for the benchmark
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.runInBackground = true;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.productName = "E4 " + PipelineName;
    }

    static void Build()
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Builds/E4/E4.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });
        Debug.Log($"E4: build {report.summary.result}, {report.summary.totalSize / 1e6:F0} MB, {report.summary.totalTime.TotalSeconds:F0} s");
        if (report.summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
