using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GuitarTutor.Runtime.Band
{
    /// <summary>
    /// The keyboardist, sat on a bench playing the piano clip: the keyboard goes under the clip's hands (key tops
    /// 5 mm under the fingertips, as in the mockup) on a stand.
    /// </summary>
    public sealed class KeysPlayer : Musician
    {
        public Material standMaterial;
        bool placed;

        void LateUpdate()
        {
            if (!posed || placed || instrument == null)
                return;
            // once, from the first pose: middle C under the middle of the two hands
            placed = true;
            instrument.Place(transform.position, transform.right, transform.up);
            var notes = (JArray)instrument.Data["notes"];
            var lowest = (int?)instrument.Data["lowest"] ?? 36;
            var middleC = instrument.ToWorld(notes[Mathf.Clamp(60 - lowest, 0, notes.Count - 1)]);
            var hands = (Bone(HumanBodyBones.LeftIndexDistal).position + Bone(HumanBodyBones.RightIndexDistal).position) / 2;
            instrument.transform.position += hands - transform.up * 0.005f - middleC;

            // a bench under the pelvis, a stand under the keyboard
            var hips = Bone(HumanBodyBones.Hips).position;
            Block("Bench", new Vector3(hips.x, 0, hips.z) - transform.forward * 0.05f, new Vector3(0.6f, hips.y - transform.position.y - 0.1f, 0.32f));
            var box = instrument.Data["box"];  // front and back (forward), underside (up), half width
            var front = (float)box[0];
            var back = (float)box[1];
            var under = instrument.ToWorld(new Vector3(0, (front + back) / 2, (float)box[2]));
            var height = under.y - transform.position.y;
            foreach (var side in new[] { -1f, 1f })
                Block("Stand", new Vector3(under.x, transform.position.y, under.z) + transform.right * side * ((float)box[3] - 0.08f),
                      new Vector3(0.04f, height, back - front - 0.04f));
        }

        void Block(string name, Vector3 footing, Vector3 size)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            Destroy(block.GetComponent<Collider>());
            block.transform.SetParent(transform, true);
            block.transform.rotation = transform.rotation;
            block.transform.position = footing + transform.up * size.y / 2;
            block.transform.localScale = size;
            if (standMaterial != null)
                block.GetComponent<Renderer>().sharedMaterial = standMaterial;
        }
    }
}
