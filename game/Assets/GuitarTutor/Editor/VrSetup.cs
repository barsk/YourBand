using System.Collections.Generic;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace GuitarTutor.EditorTools
{
    /// <summary>
    /// VR mode's settings (docs/plan.md, "VR mode"): OpenXR for Windows, started by the game when VR mode is
    /// asked for (-vr, or X) rather than at launch, so the desktop game never starts a VR runtime; the Quest's
    /// controllers (Touch, Touch Plus) as Virtual Desktop's OpenXR runtime presents them.
    /// </summary>
    /// <remarks>
    /// The loader is OpenXR's without pre-initialisation: the usual one (what ticking OpenXR in Project Settings
    /// &gt; XR Plug-in Management assigns) opens the system's OpenXR runtime at every launch to choose the
    /// graphics adapter, which on this PC starts SteamVR for the desktop game too.
    /// </remarks>
    public static class VrSetup
    {
        const string Settings = "Assets/XR";
        const string Materials = "Assets/GuitarTutor/Resources/Vr";

        public static void Run()
        {
            // unlit: the chart board shows its texture as drawn, the beam its colour
            Unlit("Board");
            Unlit("Beam");

            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                Folder(Settings);
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, Settings + "/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
            }
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            general.InitManagerOnStart = false;
            EditorUtility.SetDirty(general);
            Folder(Settings + "/Loaders");
            var loaderPath = Settings + "/Loaders/OpenXRLoaderNoPreInit.asset";
            var loader = AssetDatabase.LoadAssetAtPath<OpenXRLoaderNoPreInit>(loaderPath);
            if (loader == null)
            {
                loader = ScriptableObject.CreateInstance<OpenXRLoaderNoPreInit>();
                AssetDatabase.CreateAsset(loader, loaderPath);
            }
            general.Manager.TrySetLoaders(new List<XRLoader> { loader });
            // (XR Plug-in Management keeps an instance of the usual loader in Loaders/ too, assigned to nothing)
            EditorUtility.SetDirty(general.Manager);

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (openXr == null)
            {
                Debug.LogWarning("GuitarTutor setup: no OpenXR settings for Windows");
                return;
            }
            foreach (var profile in new UnityEngine.XR.OpenXR.Features.OpenXRFeature[]
                     { openXr.GetFeature<OculusTouchControllerProfile>(), openXr.GetFeature<MetaQuestTouchPlusControllerProfile>() })
                if (profile != null && !profile.enabled)
                {
                    profile.enabled = true;
                    EditorUtility.SetDirty(profile);
                }
            EditorUtility.SetDirty(openXr);
            AssetDatabase.SaveAssets();
            Debug.Log($"GuitarTutor setup: VR with {general.Manager.activeLoaders.Count} XR loader(s), started by the game");
        }

        /// <summary>A folder made through the asset database (made on disk only, the XR packages would make a second).</summary>
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            Folder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
        }

        static void Unlit(string name)
        {
            Folder(Materials);
            var path = Materials + "/" + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                return;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name, color = Color.white };
            AssetDatabase.CreateAsset(material, path);
        }
    }
}
