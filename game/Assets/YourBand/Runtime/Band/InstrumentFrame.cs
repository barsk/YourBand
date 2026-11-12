using Newtonsoft.Json.Linq;
using UnityEngine;

namespace YourBand.Runtime.Band
{
    /// <summary>
    /// An instrument from tools/band_assets: its model and its playing geometry (frets and strings, drums,
    /// keys), given in the instrument's own frame (metres). Four empties in the model mark that frame (its
    /// origin and 0.1 m along each axis), so positions map onto the model however the exporter turned its axes.
    /// </summary>
    public sealed class InstrumentFrame : MonoBehaviour
    {
        public TextAsset geometry;

        JObject data;
        Vector3 origin, x, y, z;  // the frame in the model's root space: origin and one metre along each axis
        bool ready;

        public JObject Data
        {
            get
            {
                Prepare();
                return data;
            }
        }

        void Prepare()
        {
            if (ready)
                return;
            ready = true;
            data = geometry != null ? JObject.Parse(geometry.text) : new JObject();
            Vector3 Marker(string name)
            {
                var t = FindChild(transform, name);
                return t != null ? transform.InverseTransformPoint(t.position) : Vector3.zero;
            }
            origin = Marker("frame_origin");
            x = (Marker("frame_x") - origin) * 10;
            y = (Marker("frame_y") - origin) * 10;
            z = (Marker("frame_z") - origin) * 10;
        }

        /// <summary>A point given in the instrument's frame, in the world.</summary>
        public Vector3 ToWorld(Vector3 p)
        {
            Prepare();
            return transform.TransformPoint(origin + x * p.x + y * p.y + z * p.z);
        }

        public Vector3 ToWorld(JToken point) => ToWorld(Point(point));

        /// <summary>A direction given in the instrument's frame, in the world.</summary>
        public Vector3 DirectionToWorld(Vector3 d)
        {
            Prepare();
            return transform.TransformDirection(x * d.x + y * d.y + z * d.z).normalized;
        }

        /// <summary>
        /// Puts the instrument where its frame's origin is at <paramref name="at"/>, its x axis along
        /// <paramref name="xAxis"/> and its z axis as near <paramref name="zAxis"/> as square to x allows.
        /// </summary>
        public void Place(Vector3 at, Vector3 xAxis, Vector3 zAxis)
        {
            Prepare();
            var local = Quaternion.LookRotation(z, x);  // the frame's z and x in the model's root space
            var wanted = Quaternion.LookRotation(zAxis, xAxis);
            transform.rotation = wanted * Quaternion.Inverse(local);
            transform.position = at - transform.rotation * Vector3.Scale(origin, transform.lossyScale);
        }

        /// <summary>A part of the model by its name in the geometry (made safe for Unity by the exporter).</summary>
        public Transform Part(string name) => FindChild(transform, name);

        public static Vector3 Point(JToken t) => t is JArray a && a.Count >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : Vector3.zero;

        static Transform FindChild(Transform t, string name)
        {
            if (t.name == name)
                return t;
            foreach (Transform child in t)
            {
                var found = FindChild(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
