using System.Collections.Generic;
using GuitarTutor.Core.Band;
using GuitarTutor.Core.Chart;
using UnityEngine;
using UnityEngine.Rendering;

namespace GuitarTutor.Runtime.Band
{
    /// <summary>
    /// The band as 3D musicians (docs/plan.md, "Band animation"): the four musicians from
    /// Resources/Band/&lt;role&gt; (made by GuitarTutor &gt; Set up band from tools/band_assets), on a stage facing
    /// the audience at -z, with a floor and lights. Shown at the song's position every frame.
    /// </summary>
    public sealed class BandStage : MonoBehaviour
    {
        /// <summary>Where each musician stands (metres; the audience is towards -z) and which way they face
        /// (degrees about up; 180 faces the audience).</summary>
        static readonly (BandRole role, Vector3 at, float facing)[] Layout =
        {
            (BandRole.Drummer, new Vector3(0f, 0f, 2.6f), 180f),
            (BandRole.Guitarist, new Vector3(-1.5f, 0f, 1.1f), 160f),
            (BandRole.Bassist, new Vector3(1.4f, 0f, 1.3f), 200f),
            (BandRole.Keyboardist, new Vector3(2.7f, 0f, 2.5f), 235f),
        };

        /// <summary>The desktop's view of the band: from the audience, at standing eye height.</summary>
        public static readonly Vector3 ViewFrom = new Vector3(0f, 1.55f, -3.2f), ViewAt = new Vector3(0.3f, 1.05f, 1.6f);

        readonly List<Musician> musicians = new List<Musician>();
        GameObject floor;

        public IReadOnlyList<Musician> Musicians => musicians;

        /// <summary>The band in your room (VR mode): no stage floor, the room's own floor shows through.</summary>
        public bool InRoom
        {
            set => floor.SetActive(!value);
        }

        /// <summary>The middle of the band, on the floor.</summary>
        public Vector3 Centre
        {
            get
            {
                var sum = Vector3.zero;
                foreach (var m in musicians)
                    sum += m.transform.position;
                return musicians.Count > 0 ? sum / musicians.Count : transform.position;
            }
        }

        /// <summary>Whether a point on the floor is where one of the musicians is (within <paramref name="reach"/> m).</summary>
        public bool Covers(Vector3 point, float reach = 0.8f)
        {
            foreach (var m in musicians)
            {
                var d = m.transform.position - point;
                if (new Vector2(d.x, d.z).magnitude < reach)
                    return true;
            }
            return false;
        }

        /// <summary>The band, or null (and why) if its assets have not been made (tools/band_assets).</summary>
        public static BandStage Create(out string missing)
        {
            missing = null;
            var prefabs = new Dictionary<BandRole, GameObject>();
            foreach (var (role, _, _) in Layout)
            {
                var prefab = Resources.Load<GameObject>("Band/" + role);
                if (prefab == null)
                {
                    missing = $"no {role} (make the band with tools/band_assets, then GuitarTutor > Set up band)";
                    return null;
                }
                prefabs[role] = prefab;
            }
            var stage = new GameObject("Band").AddComponent<BandStage>();
            foreach (var (role, at, facing) in Layout)
            {
                var m = Instantiate(prefabs[role], at, Quaternion.Euler(0, facing, 0), stage.transform).GetComponent<Musician>();
                m.name = role.ToString();
                stage.musicians.Add(m);
            }
            stage.Dress();
            return stage;
        }

        /// <summary>The song and the band's parts, for each musician to plan their playing.</summary>
        public void SetSong(SongChart chart, Arrangement arrangement)
        {
            foreach (var m in musicians)
                m.SetSong(chart, arrangement);
        }

        /// <summary>Whether the band guitarist plays (strums the rhythm) or rests.</summary>
        public bool GuitarPlays
        {
            set
            {
                foreach (var m in musicians)
                    if (m is GuitarPlayer g && m.role == BandRole.Guitarist)
                        g.Playing = value;
            }
        }

        public void Show(double quarter, double seconds)
        {
            foreach (var m in musicians)
                m.Show(quarter, seconds);
        }

        /// <summary>
        /// Points a camera at one musician (for checking them in screenshots): "guitarist" from the front,
        /// "guitarist:left" / ":right" from their side, "guitarist:hands" close on the hands.
        /// </summary>
        public void Frame(Camera eye, string what)
        {
            var parts = what.ToLowerInvariant().Split(':');
            var m = musicians.Find(x => x.role.ToString().ToLowerInvariant() == parts[0]);
            if (m == null)
                return;
            var t = m.transform;
            var view = parts.Length > 1 ? parts[1] : "front";
            var l = m.body.GetBoneTransform(HumanBodyBones.LeftHand).position;
            var r = m.body.GetBoneTransform(HumanBodyBones.RightHand).position;
            var hands = (l + r) / 2;
            var (from, at) = view switch
            {
                "left" => (t.position - t.right * 1.5f + Vector3.up * 1.2f + t.forward * 0.3f, t.position + Vector3.up * 1.0f),
                "right" => (t.position + t.right * 1.5f + Vector3.up * 1.2f + t.forward * 0.3f, t.position + Vector3.up * 1.0f),
                "hands" => (hands + t.forward * 0.75f + Vector3.up * 0.2f, hands),
                "above" => (t.position + t.forward * 1.3f + Vector3.up * 2.3f, t.position + t.forward * 0.35f + Vector3.up * 0.7f),
                _ => (t.position + t.forward * 2.4f + Vector3.up * 1.35f, t.position + Vector3.up * 1.05f),
            };
            eye.transform.position = from;
            eye.transform.rotation = Quaternion.LookRotation(at - from);
            eye.fieldOfView = view == "hands" ? 45 : 40;
        }

        void Dress()
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            Destroy(floor.GetComponent<Collider>());
            floor.transform.SetParent(transform, false);
            floor.transform.localScale = new Vector3(1.6f, 1f, 1.2f);
            floor.transform.localPosition = new Vector3(0.5f, 0f, 1.5f);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var boards = new Material(lit) { color = new Color(0.16f, 0.14f, 0.13f) };
            boards.SetFloat("_Smoothness", 0.35f);
            floor.GetComponent<Renderer>().sharedMaterial = boards;

            Light Lamp(string name, Vector3 euler, float intensity, Color color, bool shadows)
            {
                var light = new GameObject(name).AddComponent<Light>();
                light.transform.SetParent(transform, false);
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(euler);
                light.intensity = intensity;
                light.color = color;
                light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                return light;
            }
            Lamp("Key", new Vector3(38, 25, 0), 1.5f, new Color(1f, 0.95f, 0.88f), true);  // from the front left, above
            Lamp("Fill", new Vector3(20, -60, 0), 0.45f, new Color(0.8f, 0.86f, 1f), false);
            Lamp("Rim", new Vector3(30, 170, 0), 0.9f, new Color(1f, 0.85f, 0.7f), false);  // from behind the band
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.24f, 0.24f, 0.26f);
        }
    }
}
