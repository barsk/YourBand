using System.IO;
using System.Linq;
using YourBand.Runtime.Band;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace YourBand.EditorTools
{
    /// <summary>
    /// Makes the band's musicians from the assets tools/band_assets gathered into Assets/External/Band (not in
    /// git): each Rocketbox avatar as a humanoid with the mockup's skin and hair maps (as in E2), its Mixamo
    /// clip, its instrument with the playing geometry, as a prefab in Assets/External/Band/Resources/Band for
    /// BandStage. Without the assets it does nothing (the game then has no band view).
    /// </summary>
    public static class BandSetup
    {
        const string Root = "Assets/External/Band";
        const string Generated = Root + "/Generated/";
        const string Prefabs = Root + "/Resources/Band/";

        // role, avatar, clip, instrument (tools/band_assets/band_assets.py has the same casting)
        static readonly (BandRole role, string avatar, string clip, string instrument)[] Cast =
        {
            (BandRole.Drummer, "Male_Adult_09", "playing_drums_113820901", "DrumKit"),
            (BandRole.Bassist, "Female_Adult_08", "guitar_playing_111430901", "BandBass"),
            (BandRole.Guitarist, "Male_Adult_04", "guitar_playing_111490901", "BandGuitar"),
            (BandRole.Keyboardist, "Female_Adult_12", "piano_playing_112170902", "Keyboard"),
        };

        public static bool AssetsThere => Directory.Exists(Root + "/Avatars") && Directory.Exists(Root + "/Instruments");

        [MenuItem("YourBand/Set up band")]
        public static void Run()
        {
            if (!AssetsThere)
            {
                Debug.Log("YourBand band: no assets in " + Root + " (run tools/band_assets/band_assets.py): no band");
                return;
            }
            Directory.CreateDirectory(Generated);
            Directory.CreateDirectory(Prefabs);
            var stand = Material("Stand", new Color(0.05f, 0.05f, 0.055f), 0.4f);
            foreach (var (role, avatar, clip, instrument) in Cast)
            {
                var folder = Root + "/Avatars/" + role + "/";
                var fbx = folder + avatar + ".fbx";
                var prefix = Path.GetFileName(Directory.GetFiles(folder, "*_body_color.tga").First()).Split('_')[0];
                ConfigureTextures(folder);
                var body = Lit(role + " body", Tex(folder, prefix + "_body_color.tga"), Tex(folder, prefix + "_body_normal.tga"), Mask(folder, prefix, "body", role), false);
                var head = Lit(role + " head", Tex(folder, prefix + "_head_color.tga"), Tex(folder, prefix + "_head_normal.tga"), Mask(folder, prefix, "head", role), false);
                var cards = (TextureImporter)AssetImporter.GetAtPath(folder + prefix + "_opacity_cards4.png");
                var hair = Lit(role + " hair", cards != null && cards.DoesSourceTextureHaveAlpha()
                    ? Tex(folder, prefix + "_opacity_cards4.png") : Tex(folder, prefix + "_opacity_color.tga"), null, null, true);
                ConfigureAvatar(fbx, prefix, body, head, hair);
                var clipPath = Root + "/Mocap/" + clip + ".fbx";
                ConfigureClip(clipPath);
                var instrumentPath = Root + "/Instruments/" + instrument + ".fbx";
                ConfigureInstrument(instrumentPath, instrument);
                MakePrefab(role, fbx, clipPath, instrumentPath, Root + "/Instruments/" + instrument + ".json", stand);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("YourBand band: " + Cast.Length + " musicians in " + Prefabs);
        }

        // ------------------------------------------------------------------ avatars (as in E2)
        static void ConfigureTextures(string folder)
        {
            foreach (var path in Directory.GetFiles(folder).Where(p => p.EndsWith(".tga") || p.EndsWith(".png")))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                if (importer == null)
                    continue;
                var normal = path.Contains("_normal");
                var colour = path.Contains("_color") || path.Contains("_cards");
                var transparent = path.Contains("_opacity");
                if (importer.textureType == (normal ? TextureImporterType.NormalMap : TextureImporterType.Default)
                    && importer.sRGBTexture == colour && importer.alphaIsTransparency == transparent)
                    continue;
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = colour;
                importer.alphaIsTransparency = transparent;
                importer.SaveAndReimport();
            }
        }

        static Texture2D Tex(string folder, string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(folder + file);

        /// <summary>The mockup's look map (G roughness) as URP's mask: R metallic 0, G occlusion 1, A smoothness.</summary>
        static Texture2D Mask(string folder, string prefix, string part, BandRole role)
        {
            var source = folder + prefix + "_" + part + "_look4.png";
            if (!File.Exists(source))
                return null;
            var path = Generated + role + "_" + part + "_mask.png";
            if (!File.Exists(path))
            {
                var look = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                look.LoadImage(File.ReadAllBytes(source));
                var mask = look.GetPixels32().Select(c => new Color32(0, 255, 0, (byte)(255 - c.g))).ToArray();
                var t = new Texture2D(look.width, look.height, TextureFormat.RGBA32, true, true);
                t.SetPixels32(mask);
                File.WriteAllBytes(path, t.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Lit(string name, Texture2D color, Texture2D normal, Texture2D mask, bool cutout)
        {
            var path = Generated + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
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
            if (cutout)  // hair is either there or not: no half-transparent pixels (to blend with a key colour in VR)
            {
                m.SetFloat("_AlphaClip", 1);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Material(string name, Color color, float smoothness)
        {
            var path = Generated + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void ConfigureAvatar(string fbx, string prefix, Material body, Material head, Material hair)
        {
            var avatar = (ModelImporter)AssetImporter.GetAtPath(fbx);
            var map = avatar.GetExternalObjectMap();
            if (avatar.animationType == ModelImporterAnimationType.Human && !avatar.importAnimation
                && map.Values.Contains(body) && map.Values.Contains(head) && map.Values.Contains(hair))
                return;  // as set up before
            avatar.animationType = ModelImporterAnimationType.Human;
            avatar.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            avatar.importAnimation = false;
            avatar.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            avatar.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), prefix + "_body"), body);
            avatar.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), prefix + "_head"), head);
            avatar.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), prefix + "_opacity"), hair);
            avatar.SaveAndReimport();
        }

        static void ConfigureClip(string path)
        {
            var clip = (ModelImporter)AssetImporter.GetAtPath(path);
            if (clip.animationType == ModelImporterAnimationType.Human && clip.clipAnimations.Length > 0 && clip.clipAnimations.All(t => t.loopTime))
                return;  // as set up before
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

        // finishes painted with an image, which the game puts on the model's material itself: instrument, the
        // FBX's material, the image (next to the instruments), smoothness (lacquer)
        static readonly (string instrument, string material, string image, float smoothness)[] Finishes =
        {
            ("BandBass", "pbass_sunburst", "pbass_sunburst.png", 0.85f),
        };

        static void ConfigureInstrument(string path, string instrument)
        {
            var model = (ModelImporter)AssetImporter.GetAtPath(path);
            var map = model.GetExternalObjectMap();
            var finishes = Finishes.Where(f => f.instrument == instrument).Select(f =>
            {
                var m = Lit(instrument + " " + f.material, AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Instruments/" + f.image), null, null, false);
                m.SetFloat("_Smoothness", f.smoothness);
                return (f.material, m);
            }).ToList();
            if (model.animationType == ModelImporterAnimationType.None && model.materialImportMode == ModelImporterMaterialImportMode.ImportViaMaterialDescription
                && finishes.All(f => map.Values.Contains(f.m)))
                return;  // as set up before
            model.animationType = ModelImporterAnimationType.None;
            model.importAnimation = false;
            model.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var (material, m) in finishes)
                model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material), m);
            model.SaveAndReimport();
        }

        // ------------------------------------------------------------------ the musician
        static void MakePrefab(BandRole role, string fbx, string clipPath, string instrumentPath, string geometryPath, Material stand)
        {
            var root = new GameObject(role.ToString());
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            model.name = "Body";
            model.transform.SetParent(root.transform, false);
            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().First();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var clip = AssetDatabase.LoadAllAssetsAtPath(clipPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            animator.runtimeAnimatorController = AnimatorController.CreateAnimatorControllerAtPathWithClip(Generated + role + ".controller", clip);
            // the model faces the way its shoulders say: turn it to face the musician's forward
            var l = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
            var r = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
            var facing = Vector3.Cross(r - l, Vector3.up);
            facing.y = 0;
            model.transform.rotation = Quaternion.FromToRotation(facing.normalized, Vector3.forward) * model.transform.rotation;

            var instrument = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(instrumentPath));
            instrument.name = "Instrument";
            instrument.transform.SetParent(root.transform, false);
            var frame = instrument.AddComponent<InstrumentFrame>();
            frame.geometry = AssetDatabase.LoadAssetAtPath<TextAsset>(geometryPath);

            Musician m = role switch
            {
                BandRole.Drummer => root.AddComponent<Drummer>(),
                BandRole.Keyboardist => root.AddComponent<KeysPlayer>(),
                _ => root.AddComponent<GuitarPlayer>(),
            };
            m.role = role;
            m.body = animator;
            m.instrument = frame;
            if (role == BandRole.Drummer)
            {
                // the mockup's timing of the rock beat clip (drumming.py): beat 1 at frame 5.5, an eighth every
                // 8.75 frames, looping over 140 frames (two bars)
                m.clipBeat1 = 5.5f;
                m.clipFramesPerEighth = 8.75f;
                m.clipLoopFrames = 140f;
            }
            if (m is KeysPlayer keys)
                keys.standMaterial = stand;
            PrefabUtility.SaveAsPrefabAsset(root, Prefabs + role + ".prefab");
            Object.DestroyImmediate(root);
        }
    }
}
