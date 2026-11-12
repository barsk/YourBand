using System.Collections.Generic;
using System.Linq;
using YourBand.Core.Band;
using YourBand.Core.Chart;
using YourBand.Core.Performance;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace YourBand.Runtime.Band
{
    /// <summary>
    /// The keyboardist, sat on a bench playing the piano clip: the keyboard goes under the clip's hands (key tops
    /// 5 mm under the fingertips, as in the mockup) on a stand. The right hand plays the keys part's chords
    /// (KeysPerformance: which finger on which key), each finger pressing its key down while the note sounds;
    /// the left hand rests lower on the keys between its notes.
    /// </summary>
    public sealed class KeysPlayer : Musician
    {
        public Material standMaterial;
        const float KeyDip = 0.008f;  // m a key goes down

        HandRig left, right;
        bool placed;
        List<KeyPress> presses = new List<KeyPress>();
        JArray keyPoints, keyObjects;
        int lowest;
        readonly Dictionary<int, (Transform key, Vector3 rest)> keys = new Dictionary<int, (Transform, Vector3)>();
        double quarter;

        protected override void Awake()
        {
            base.Awake();
            left = new HandRig(body, true);
            right = new HandRig(body, false);
        }

        public override void SetSong(SongChart chart, Arrangement arrangement) => presses = KeysPerformance.Plan(arrangement.Notes);

        public override void Show(double q, double seconds)
        {
            quarter = q;
            base.Show(q, seconds);
        }

        void LateUpdate()
        {
            if (!posed || instrument == null)
                return;
            if (!placed)
                Place();
            Play();
        }

        void Place()
        {
            // once, from the first pose: middle C under the middle of the two hands
            placed = true;
            instrument.Place(transform.position, transform.right, transform.up);
            keyPoints = (JArray)instrument.Data["notes"];
            keyObjects = (JArray)instrument.Data["key_objects"];
            lowest = (int?)instrument.Data["lowest"] ?? 36;
            var middleC = instrument.ToWorld(keyPoints[Mathf.Clamp(60 - lowest, 0, keyPoints.Count - 1)]);
            var hands = (Bone(HumanBodyBones.LeftIndexDistal).position + Bone(HumanBodyBones.RightIndexDistal).position) / 2;
            instrument.transform.position += hands - transform.up * 0.005f - middleC;
            for (int i = 0; i < keyObjects.Count; ++i)
            {
                var key = instrument.Part((string)keyObjects[i]);
                if (key != null)
                    keys[lowest + i] = (key, key.localPosition);
            }

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

        Vector3 KeyTop(int pitch) => instrument.ToWorld(keyPoints[Mathf.Clamp(pitch - lowest, 0, keyPoints.Count - 1)]);

        void Play()
        {
            var sounding = presses.Where(p => p.AtQuarter <= quarter + 1e-9 && quarter < p.AtQuarter + p.Quarters * 0.97).ToList();
            foreach (var (key, rest) in keys.Values)
                key.localPosition = rest;
            foreach (var p in sounding)
                if (keys.TryGetValue(p.Pitch, out var k))
                    k.key.position -= transform.up * KeyDip;

            // each hand over the chord it plays now, or the last (the right) / a C3 to G3 (the left) while it waits
            Hand(right, sounding.Where(p => p.Right).ToList(), presses.LastOrDefault(p => p.Right && p.AtQuarter <= quarter), 64);
            Hand(left, sounding.Where(p => !p.Right).ToList(), presses.LastOrDefault(p => !p.Right && p.AtQuarter <= quarter), 50);
        }

        void Hand(HandRig hand, List<KeyPress> now, KeyPress last, int restPitch)
        {
            var playing = now.Count > 0;
            var over = playing ? now : last.Quarters > 0 && quarter - last.AtQuarter < 4 ? new List<KeyPress> { last } : null;
            var centre = over != null ? over.Select(p => KeyTop(p.Pitch)).Aggregate(Vector3.zero, (a, b) => a + b) / over.Count : KeyTop(restPitch);
            var rotation = hand.Facing((transform.forward - transform.up * 0.35f).normalized, transform.up);
            var knuckles = centre - transform.forward * 0.075f + transform.up * (playing ? 0.04f : 0.05f);
            var wrist = knuckles - rotation * hand.Forward * hand.Palm;
            var outwards = transform.right * (hand.Left ? -1 : 1);
            var pole = hand.Upper.position + outwards * 0.25f - transform.up * 0.35f - transform.forward * 0.05f;
            hand.Reach(wrist, rotation, pole);
            var used = new HashSet<int>();
            foreach (var p in now)
            {
                var f = Mathf.Clamp(p.Finger - 1, 0, 4);
                hand.Fingers[f].Reach(KeyTop(p.Pitch) - transform.up * KeyDip);
                used.Add(f);
            }
            for (int f = 0; f < 5; ++f)
                if (!used.Contains(f))
                    hand.Fingers[f].Curl(0.3f);
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
