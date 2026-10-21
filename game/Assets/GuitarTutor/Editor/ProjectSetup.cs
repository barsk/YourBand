using System.IO;
using GuitarTutor.Runtime.Desktop;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GuitarTutor.EditorTools
{
    /// <summary>
    /// Sets the project up from code, so it can be rebuilt from the command line (build_desktop.cmd): the UI
    /// Toolkit theme and panel settings, the Desktop scene, the player settings; and builds the desktop player.
    /// </summary>
    public static class ProjectSetup
    {
        const string SettingsFolder = "Assets/GuitarTutor/Settings";
        const string ThemePath = SettingsFolder + "/GuitarTutor Theme.tss";
        const string PanelPath = SettingsFolder + "/Desktop Panel.asset";
        const string StylePath = "Assets/GuitarTutor/Runtime/UI/Chart.uss";
        const string ScenePath = "Assets/GuitarTutor/Scenes/Desktop.unity";
        public const string BuildPath = "Builds/Desktop/GuitarTutor.exe";

        static readonly Color Background = new Color32(0x14, 0x16, 0x1a, 0xff);

        /// <summary>Menu: sets everything up, the Desktop scene made again.</summary>
        [MenuItem("GuitarTutor/Set up project")]
        public static void Run() => Setup(true);

        /// <param name="rebuildScene">Make the Desktop scene again even if it exists (it is otherwise left alone,
        /// so a build does not rewrite it).</param>
        static void Setup(bool rebuildScene)
        {
            Directory.CreateDirectory(SettingsFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
            }
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();

            if (rebuildScene || !File.Exists(ScenePath))
                BuildScene();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
            Debug.Log("GuitarTutor setup: done");
        }

        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // loaded after the new scene: opening it unloads assets nothing references
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.transform.position = new Vector3(0, 1.6f, -4);

            var app = new GameObject("Desktop", typeof(UIDocument), typeof(DesktopApp));
            app.GetComponent<UIDocument>().panelSettings = panel;
            app.GetComponent<DesktopApp>().style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
        }

        /// <summary>Batch mode: sets up the project and builds the desktop player; exits with 1 if the build fails.</summary>
        public static void BuildDesktop()
        {
            Setup(false);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            var summary = report.summary;
            Debug.Log($"GuitarTutor build: {summary.result}, {summary.totalErrors} errors, {summary.totalSize / 1e6:F0} MB, {summary.outputPath}");
            if (Application.isBatchMode && summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
