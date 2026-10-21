// Experiment E4, HDRP part: Lit materials with subsurface-scattering skin, lights in physical units, exposure.
//
// Lighting matches the URP scene: with a fixed exposure of EV100 9, HDRP shows a value L (lux, nits) about as
// bright as URP shows L / (1.2 * 2^9) = L / 614. URP's key 2.2 -> 1350 lux; fill 1.2 -> 9260 lumen;
// ambient ~0.3 -> a gradient sky at ~200 nits.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static partial class E4Setup
{
    const string PipelineName = "HDRP";
    const float Exposure = 9f;
    const string SkinProfile = "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipelineResources/SkinDiffusionProfile.asset";

    static Material Lit(string name, Texture2D color, Texture2D normal, Texture2D mask, Texture2D skin, bool cutout)
    {
        var m = new Material(Shader.Find("HDRP/Lit")) { name = name };
        AssetDatabase.CreateAsset(m, Generated + name + ".mat");  // first: the diffusion profile is stored as a sub-asset
        m.SetTexture("_BaseColorMap", color);
        m.SetColor("_BaseColor", Color.white);
        if (normal != null)
            m.SetTexture("_NormalMap", normal);
        if (mask != null)
        {
            m.SetTexture("_MaskMap", mask);  // R metallic 0, G occlusion, A smoothness
            m.SetFloat("_SmoothnessRemapMin", 0f);
            m.SetFloat("_SmoothnessRemapMax", 1f);
        }
        m.SetFloat("_Metallic", 0f);
        if (skin != null)
        {
            // skin scatters light under the surface; the mask keeps clothes and hair out of it
            HDMaterial.SetMaterialType(m, MaterialId.LitSSS);
            HDMaterial.SetDiffusionProfile(m, AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>(SkinProfile));
            m.SetTexture("_SubsurfaceMaskMap", skin);
            m.SetFloat("_SubsurfaceMask", 1f);
        }
        else
        {
            HDMaterial.SetMaterialType(m, MaterialId.LitStandard);
        }
        if (cutout)
        {
            HDMaterial.SetAlphaClipping(m, true);
            HDMaterial.SetAlphaCutoff(m, 0.5f);
        }
        HDMaterial.ValidateMaterial(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Dictionary<string, Material> MakeMaterials()
    {
        var body = LookMaps("body");
        var head = LookMaps("head");
        return new Dictionary<string, Material>
        {
            ["m006_body"] = Lit("Body", Tex("m006_body_color.tga"), Tex("m006_body_normal.tga"), body.mask, body.skin, false),
            ["m006_head"] = Lit("Head", Tex("m006_head_color.tga"), Tex("m006_head_normal.tga"), head.mask, head.skin, false),
            ["m006_opacity"] = Lit("Hair", HairTexture(), null, null, null, true),
        };
    }

    static Material Surface(string name, Color color, float smoothness)
    {
        var m = new Material(Shader.Find("HDRP/Lit")) { name = name };
        AssetDatabase.CreateAsset(m, Generated + name + ".mat");
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        HDMaterial.ValidateMaterial(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void SetupCamera(Camera cam)
    {
        var data = cam.gameObject.AddComponent<HDAdditionalCameraData>();
        data.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
    }

    static void MakeLights(Vector3 head)
    {
        var keyObject = new GameObject("Key");
        keyObject.AddHDLight(LightType.Directional);
        var key = keyObject.GetComponent<Light>();
        key.transform.rotation = Quaternion.Euler(40, -35, 0);
        key.color = new Color(1f, 0.95f, 0.88f);
        key.lightUnit = LightUnit.Lux;
        key.intensity = 1350f;
        key.shadows = LightShadows.Soft;

        var fillObject = new GameObject("Fill");
        fillObject.AddHDLight(LightType.Point);
        var fill = fillObject.GetComponent<Light>();
        fill.transform.position = head + new Vector3(-1.6f, 0.3f, -1.4f);
        fill.color = new Color(0.85f, 0.9f, 1f);
        fill.lightUnit = LightUnit.Lumen;
        fill.intensity = 9260f;
        fill.range = 6;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var exposure = profile.Add<UnityEngine.Rendering.HighDefinition.Exposure>(true);
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(Exposure);
        var environment = profile.Add<VisualEnvironment>(true);
        environment.skyType.Override((int)SkyType.Gradient);
        environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
        var sky = profile.Add<GradientSky>(true);
        sky.top.Override(new Color(0.32f, 0.33f, 0.36f));
        sky.middle.Override(new Color(0.25f, 0.24f, 0.23f));
        sky.bottom.Override(new Color(0.12f, 0.1f, 0.08f));
        sky.multiplier.Override(200f);
        profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
        profile.Add<Bloom>(true).intensity.Override(0.1f);
        AssetDatabase.CreateAsset(profile, Generated + "PostProfile.asset");
        var volume = new GameObject("Post").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
    }
}
