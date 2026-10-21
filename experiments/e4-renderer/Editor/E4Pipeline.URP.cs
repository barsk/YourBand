// Experiment E4, URP part: Lit materials (no subsurface scattering in URP), lights and post-processing.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static partial class E4Setup
{
    const string PipelineName = "URP";

    static Material Lit(string name, Texture2D color, Texture2D normal, Texture2D mask, bool cutout)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetTexture("_BaseMap", color);
        m.SetColor("_BaseColor", Color.white);
        if (normal != null)
        {
            m.SetTexture("_BumpMap", normal);
            m.EnableKeyword("_NORMALMAP");
        }
        if (mask != null)
        {
            // smoothness from the mask's alpha (metallic stays 0)
            m.SetTexture("_MetallicGlossMap", mask);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_SmoothnessTextureChannel", 0);
            m.SetFloat("_Smoothness", 1f);
        }
        m.SetFloat("_Metallic", 0f);
        if (cutout)
        {
            m.SetFloat("_AlphaClip", 1);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }
        AssetDatabase.CreateAsset(m, Generated + name + ".mat");
        return m;
    }

    static Dictionary<string, Material> MakeMaterials()
    {
        var body = LookMaps("body");
        var head = LookMaps("head");
        return new Dictionary<string, Material>
        {
            ["m006_body"] = Lit("Body", Tex("m006_body_color.tga"), Tex("m006_body_normal.tga"), body.mask, false),
            ["m006_head"] = Lit("Head", Tex("m006_head_color.tga"), Tex("m006_head_normal.tga"), head.mask, false),
            ["m006_opacity"] = Lit("Hair", HairTexture(), null, null, true),
        };
    }

    static Material Surface(string name, Color color, float smoothness)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(m, Generated + name + ".mat");
        return m;
    }

    static void SetupCamera(Camera cam)
    {
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.TemporalAntiAliasing;
    }

    static void MakeLights(Vector3 head)
    {
        var key = new GameObject("Key").AddComponent<Light>();
        key.type = LightType.Directional;
        key.transform.rotation = Quaternion.Euler(40, -35, 0);
        key.color = new Color(1f, 0.95f, 0.88f);
        key.intensity = 2.2f;
        key.shadows = LightShadows.Soft;

        var fill = new GameObject("Fill").AddComponent<Light>();
        fill.type = LightType.Point;
        fill.transform.position = head + new Vector3(-1.6f, 0.3f, -1.4f);
        fill.color = new Color(0.85f, 0.9f, 1f);
        fill.intensity = 1.2f;
        fill.range = 6;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.32f, 0.33f, 0.36f);
        RenderSettings.ambientEquatorColor = new Color(0.25f, 0.24f, 0.23f);
        RenderSettings.ambientGroundColor = new Color(0.12f, 0.1f, 0.08f);

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var tonemap = profile.Add<Tonemapping>(true);
        tonemap.mode.Override(TonemappingMode.ACES);
        profile.Add<Bloom>(true).intensity.Override(0.1f);
        AssetDatabase.CreateAsset(profile, Generated + "PostProfile.asset");
        var volume = new GameObject("Post").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
    }
}
