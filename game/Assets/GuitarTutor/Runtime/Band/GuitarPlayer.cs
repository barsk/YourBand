using System.Collections.Generic;
using System.Linq;
using GuitarTutor.Core.Band;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Performance;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GuitarTutor.Runtime.Band
{
    /// <summary>
    /// The guitarist or the bassist. The instrument hangs in front of the hips, as in the mockup (the body's
    /// centre a little to the player's right of the middle, the neck to their left, up and a little forward, the
    /// top facing out and tilted up), and moves with the body. The fretting hand holds the chord (the guitar) or
    /// the note (the bass) from under the neck, palm up, the fingers arching over the treble edge onto their
    /// frets and the thumb behind the neck; the other hand strums over the bridge pickup like a pendulum, down
    /// on the beat and up between, touching the strings where the part has a stroke (the guitar), or plucks
    /// each note's string with the index and middle fingers in turn (the bass).
    /// </summary>
    public sealed class GuitarPlayer : Musician
    {
        /// <summary>The body's centre from the hips (the musician's frame: right, up, forward). Its depth is set
        /// from the player's own belly once the body is posed (<see cref="FrontToBody"/> ahead of it).</summary>
        public Vector3 bodyCentre = new Vector3(0.04f, 0.16f, 0.165f);
        /// <summary>Along the neck, and out of the top (the musician's frame).</summary>
        public Vector3 neck = new Vector3(-0.92f, 0.37f, 0.12f), top = new Vector3(0.05f, 0.25f, 1f);
        /// <summary>How far the instrument's centre is ahead of the front of the player's belly (m): close, and high on
        /// the body, so the picking arm bends and its forearm rests on the body's rounded upper corner.</summary>
        public float FrontToBody = 0.005f;
        /// <summary>The share of the capture's mean forward lean taken out of the spine: the bass capture hunches
        /// far over the instrument, which put it beside her (the mockup: 0.6).</summary>
        public float upright;
        /// <summary>The share of the chest's sway the instrument follows (a strapped instrument rests on the
        /// belly and hip and turns only partly with the chest; the mockup: 0.4).</summary>
        public float follow = 0.4f;

        /// <summary>Whether the guitarist plays (the band guitarist's switch); the bassist always does.</summary>
        public bool Playing { get; set; } = true;

        const float Change = 0.15f;  // quarter notes: the fretting hand moves to the next chord in this long before it

        HandRig fretting, picking;
        bool bass, measured;
        float lean;               // the capture's mean forward lean (degrees)
        Quaternion chestRest = Quaternion.identity, ride = Quaternion.identity;
        Transform[] upper;        // the spine, shoulders, neck and head, for straightening
        float[] share;            // how much of the straightening each takes (the spine bends gradually)
        int strings;
        JArray fretPoints, paths, neckSamples;
        float bridgeX, boardZ;
        List<Grip> grips = new List<Grip>();
        List<Strum> strums = new List<Strum>();
        List<BassNote> notes = new List<BassNote>();
        double quarter;

        protected override void Awake()
        {
            base.Awake();
            // before the clip first poses them: the hands' rest pose (straight fingers) is the model's
            fretting = new HandRig(body, true);
            picking = new HandRig(body, false);
            if (role == BandRole.Bassist)
            {
                // the mockup's P-Bass: a little lower and further forward, the neck steeper, the player straightened
                bodyCentre = new Vector3(0.05f, 0.12f, bodyCentre.z);
                neck = new Vector3(-0.9f, 0.4f, 0.15f);
                FrontToBody = 0.01f;
                upright = 0.6f;
            }
        }

        public override void SetSong(SongChart chart, Arrangement arrangement)
        {
            bass = role == BandRole.Bassist;
            grips = GuitarPerformance.Grips(chart);
            strums = GuitarPerformance.Strums(chart, arrangement.Notes);
            notes = BassPerformance.Plan(arrangement.Notes);
        }

        public override void Show(double q, double seconds)
        {
            quarter = q;
            base.Show(q, seconds);
        }

        void LateUpdate()
        {
            if (!posed || instrument == null)
                return;
            var hips = Bone(HumanBodyBones.Hips);
            if (!measured)
                MeasureBody();
            Straighten();
            instrument.Place(hips.position + Frame(bodyCentre), Frame(neck), Frame(top));
            if (fretPoints == null)
                Measure();
            if (bass)
            {
                FretNote();
                Pluck();
            }
            else
            {
                FretChord();
                StrumStrings();
            }
        }

        Vector3 Frame(Vector3 v) => ride * (transform.right * v.x + transform.up * v.y + transform.forward * v.z);

        // ------------------------------------------------------------------ the body under the instrument
        Transform Chest => Bone(HumanBodyBones.Chest) ?? Bone(HumanBodyBones.Spine);

        /// <summary>From the whole clip: how far it leans forward on average and its mean chest; then (with the
        /// body straightened) how far ahead the belly is, where the instrument hangs.</summary>
        void MeasureBody()
        {
            measured = true;
            var list = new List<(HumanBodyBones bone, float share)>
            {
                (HumanBodyBones.Spine, 1 / 3f), (HumanBodyBones.Chest, 2 / 3f), (HumanBodyBones.UpperChest, 1f),
                (HumanBodyBones.Neck, 1f), (HumanBodyBones.Head, 1f), (HumanBodyBones.LeftShoulder, 1f), (HumanBodyBones.RightShoulder, 1f),
            }.Where(x => Bone(x.bone) != null).ToList();
            upper = list.Select(x => Bone(x.bone)).ToArray();
            share = list.Select(x => x.share).ToArray();

            var hips = Bone(HumanBodyBones.Hips);
            var neckBone = Bone(HumanBodyBones.Neck) ?? Bone(HumanBodyBones.Head);
            float angles = 0;
            Vector3 forwards = Vector3.zero, ups = Vector3.zero;
            const int n = 24;
            SampleClip(n, () =>
            {
                var v = neckBone.position - hips.position;
                angles += Mathf.Atan2(Vector3.Dot(v, transform.forward), Vector3.Dot(v, transform.up)) * Mathf.Rad2Deg;
                forwards += Chest.rotation * Vector3.forward;
                ups += Chest.rotation * Vector3.up;
            });
            lean = angles / n;
            chestRest = Quaternion.LookRotation(forwards.normalized, ups.normalized);

            Straighten();
            var front = BellyFront();
            bodyCentre.z = front + FrontToBody;
            if (Logging)
                Debug.Log($"GuitarTutor {role}: mean lean {lean:0.0} degrees, taking out {upright * lean:0.0}; belly {front * 100:0} cm ahead of the hips");
        }

        /// <summary>The body's forward angle taken out of the spine (bending gradually), as the capture leans.</summary>
        void Straighten()
        {
            ride = Quaternion.identity;
            if (upper == null)
                return;
            var chest = Chest;
            // how the chest sways about its mean: the instrument follows part of it
            ride = Quaternion.Slerp(Quaternion.identity, chest.rotation * Quaternion.Inverse(chestRest), follow);
            var angle = upright * lean;
            if (Mathf.Abs(angle) < 1e-3f)
                return;
            var originals = upper.Select(b => b.rotation).ToArray();
            for (int i = 0; i < upper.Length; ++i)
                upper[i].rotation = Quaternion.AngleAxis(-angle * share[i], transform.right) * originals[i];
        }

        /// <summary>How far ahead of the hips the clothed body reaches, over the belly (m): from the skinned
        /// meshes as they are posed now, between the hips' height and a quarter metre above, near the middle.</summary>
        float BellyFront()
        {
            var hips = Bone(HumanBodyBones.Hips).position;
            var front = 0.1f;
            var mesh = new Mesh();
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.BakeMesh(mesh, false);
                foreach (var v in mesh.vertices)
                {
                    var d = smr.transform.TransformPoint(v) - hips;
                    var up = Vector3.Dot(d, transform.up);
                    if (up < -0.02f || up > 0.25f || Mathf.Abs(Vector3.Dot(d, transform.right)) > 0.12f)
                        continue;
                    front = Mathf.Max(front, Vector3.Dot(d, transform.forward));
                }
            }
            Destroy(mesh);
            return front;
        }

        void Measure()
        {
            var d = instrument.Data;
            strings = (int)d["strings"];
            fretPoints = (JArray)d["fret_points"];
            paths = (JArray)d["string_paths"];
            neckSamples = (JArray)d["neck"];
            bridgeX = (float)d["bridge_x"];
            boardZ = (float)d["board_z"];
        }

        // ------------------------------------------------------------------ the instrument's geometry (its frame)
        /// <summary>A string's fingertip target behind a fret, in the world: string 0 the lowest.</summary>
        Vector3 FretTarget(int fromLow, int fret) => instrument.ToWorld(Local(fromLow, fret));

        Vector3 Local(int fromLow, int fret)
        {
            var s = (JArray)fretPoints[strings - 1 - fromLow];  // the geometry counts from the thinnest string
            return InstrumentFrame.Point(s[Mathf.Clamp(fret, 0, s.Count - 1)]);
        }

        /// <summary>Where a string is (the instrument's frame) at a point along the neck, from its path.</summary>
        Vector3 StringAt(int fromLow, float x)
        {
            var path = (JArray)paths[strings - 1 - fromLow];
            for (int i = 1; i < path.Count; ++i)
            {
                var a = InstrumentFrame.Point(path[i - 1]);
                var b = InstrumentFrame.Point(path[i]);
                if ((x - a.x) * (x - b.x) <= 0 && Mathf.Abs(b.x - a.x) > 1e-6f)
                    return Vector3.Lerp(a, b, (x - a.x) / (b.x - a.x));
            }
            return InstrumentFrame.Point(path[0]);
        }

        /// <summary>The neck at a point along it: half its width, the height of its back, of its strings.</summary>
        (float halfWidth, float back, float strings) NeckAt(float x)
        {
            var first = neckSamples.First;
            for (int i = 1; i < neckSamples.Count; ++i)
            {
                var a = neckSamples[i - 1];
                var b = neckSamples[i];
                var xa = (float)a["x"];
                var xb = (float)b["x"];
                if ((x - xa) * (x - xb) <= 0)
                {
                    var t = Mathf.Abs(xb - xa) > 1e-6f ? (x - xa) / (xb - xa) : 0;
                    return (Mathf.Lerp((float)a["half_w"], (float)b["half_w"], t), Mathf.Lerp((float)a["back_z"], (float)b["back_z"], t),
                            Mathf.Lerp((float)a["string_z"], (float)b["string_z"], t));
                }
            }
            return ((float)first["half_w"], (float)first["back_z"], (float)first["string_z"]);
        }

        Vector3 Dir(float x, float y, float z) => instrument.DirectionToWorld(new Vector3(x, y, z));

        // ------------------------------------------------------------------ the fretting hand
        /// <summary>Fingertip targets for a chord shape (the instrument's frame), finger by finger (1 index .. 4 little).</summary>
        Dictionary<int, Vector3> ChordTargets(Grip g)
        {
            var targets = new Dictionary<int, Vector3>();
            if (g == null)
                return targets;
            for (int s = 0; s < Mathf.Min(6, strings); ++s)
                if (g.Frets[s] > 0 && g.Fingers[s] > 0 && !targets.ContainsKey(g.Fingers[s]))
                    targets[g.Fingers[s]] = Local(s, g.Frets[s]);
            if (g.Barre.HasValue)
            {
                var (fret, from, _) = g.Barre.Value;
                targets[1] = Local(from, fret) + new Vector3(0, 0.006f, 0);  // the index's tip past the lowest string it holds
            }
            return targets;
        }

        Grip GripAt(double q)
        {
            Grip found = null;
            foreach (var g in grips)
            {
                if (g.AtQuarter > q + 1e-9)
                    break;
                found = g;
            }
            return found ?? grips.FirstOrDefault();
        }

        /// <summary>The fretting hand on its targets: knuckles under the treble edge at their middle, the fingers
        /// on them (lifted by <paramref name="lift"/>), the thumb behind the neck.</summary>
        void Fret(Dictionary<int, Vector3> targets, float lift, float centreX)
        {
            var (halfWidth, back, _) = NeckAt(centreX);
            var knuckles = instrument.ToWorld(new Vector3(centreX + 0.005f, -halfWidth - 0.014f, boardZ - 0.004f));
            var rotation = fretting.Facing(Dir(0, 0.25f, 1f), Dir(0, -1f, 0.25f));
            var wrist = knuckles - rotation * fretting.Forward * fretting.Palm;
            var pole = fretting.Upper.position - transform.up * 0.4f - transform.forward * 0.15f - transform.right * 0.15f;
            fretting.Reach(wrist, rotation, pole);
            for (int k = 1; k <= 4; ++k)
            {
                if (targets.TryGetValue(k, out var t))
                    fretting.Fingers[k].Reach(instrument.ToWorld(t + new Vector3(0, 0, lift)));
                else
                    fretting.Fingers[k].Curl(0.45f);
            }
            fretting.Fingers[0].Reach(instrument.ToWorld(new Vector3(centreX - 0.02f, halfWidth * 0.2f, back - 0.004f)));
        }

        void FretChord()
        {
            var g = GripAt(quarter);
            if (g == null)
                return;
            var next = grips.FirstOrDefault(x => x.AtQuarter > g.AtQuarter + 1e-9);
            var targets = ChordTargets(g);
            var lift = 0f;
            var centre = Centre(targets, g);
            if (next != null && quarter > next.AtQuarter - Change)
            {
                // on the way to the next chord: fingers off the strings, the hand sliding there
                var u = (float)((quarter - (next.AtQuarter - Change)) / Change);
                lift = 0.012f * Mathf.Sin(Mathf.PI * u);
                var nextTargets = ChordTargets(next);
                centre = Mathf.Lerp(centre, Centre(nextTargets, next), Smooth(u));
                foreach (var k in nextTargets.Keys)
                    targets[k] = targets.TryGetValue(k, out var from) ? Vector3.Lerp(from, nextTargets[k], Smooth(u)) : nextTargets[k];
            }
            Fret(targets, lift, centre);
        }

        float Centre(Dictionary<int, Vector3> targets, Grip g)
        {
            if (targets.Count > 0)
                return targets.Values.Average(t => t.x);
            var fx = (JArray)instrument.Data["fret_x"];
            return (float)fx[Mathf.Clamp(g?.Position ?? 2, 1, fx.Count - 1)];
        }

        /// <summary>The bass's fretting hand: one finger per fret from the position the next notes need.</summary>
        void FretNote()
        {
            var (now, index) = NoteAt(quarter);
            if (now == null)
                return;
            var n = now.Value;
            // the position: the lowest fret among this note and the next few close by, so the hand stays
            var position = n.Fret > 0 ? n.Fret : 3;
            foreach (var m in notes.Skip(index).Take(4))
                if (m.Fret > 0 && m.Fret <= position + 3 && m.Fret >= position - 3)
                    position = Mathf.Min(position, m.Fret);
            position = Mathf.Max(1, position);
            var targets = new Dictionary<int, Vector3>();
            if (n.Fret > 0)
                targets[Mathf.Clamp(n.Fret - position + 1, 1, 4)] = Local(n.String, n.Fret);
            var fx = (JArray)instrument.Data["fret_x"];
            var centre = ((float)fx[Mathf.Clamp(position, 1, fx.Count - 1)] + (float)fx[Mathf.Clamp(position + 2, 1, fx.Count - 1)]) / 2;
            Fret(targets, 0, centre);
        }

        (BassNote? note, int index) NoteAt(double q)
        {
            var i = notes.FindLastIndex(x => x.AtQuarter <= q + 1e-9);
            if (i < 0)
                return (notes.Count > 0 ? notes[0] : (BassNote?)null, 0);
            return (notes[i], i);
        }

        // ------------------------------------------------------------------ the strumming and plucking hand
        void StrumStrings()
        {
            var x = bridgeX + 0.07f;  // over the bridge pickup
            var lowE = StringAt(0, x);
            var highE = StringAt(Mathf.Min(5, strings - 1), x);
            var middle = (lowE.y + highE.y) / 2;
            var swing = Mathf.Abs(lowE.y - highE.y) / 2 + 0.012f;
            // a pendulum: down through the strings on the beat, up through them on the "and"
            var y = middle;
            var contact = false;
            if (Playing)
            {
                y = middle - swing * Mathf.Sin(2 * Mathf.PI * (float)quarter);
                var crossing = System.Math.Round(quarter * 2) / 2;
                contact = strums.Any(s => System.Math.Abs(s.AtQuarter - crossing) < 0.12);
            }
            var z = (lowE.z + highE.z) / 2 + (contact ? 0.002f : 0.02f);
            Pick(instrument.ToWorld(new Vector3(x, y, z)));
        }

        /// <summary>Elbow flare of the picking arm, in the musician's frame (right, up, forward): where the pole sends
        /// the elbow, so the forearm rests on the body's rounded upper corner rather than hanging at the hip.</summary>
        static readonly Vector3 ElbowFlare = new Vector3(0.6f, -0.1f, -0.2f);

        Vector3 PickingElbow() => picking.Upper.position + Frame(ElbowFlare);

        /// <summary>The pick (between the thumb and the index, the captured grip) onto a point, the forearm over the body.</summary>
        void Pick(Vector3 point)
        {
            var rotation = picking.Facing(Dir(-0.25f, -0.75f, -0.6f).normalized, Dir(0.1f, 0.2f, 1f).normalized);
            picking.Wrist.rotation = rotation;
            // the fingers stay as captured: a relaxed pick grip, the other fingers loose (the mockup's)
            var pick = (picking.Fingers[0].Tip + picking.Fingers[1].Bones[1].position) / 2;
            var wrist = point - (pick - picking.Wrist.position);
            var pole = PickingElbow();
            picking.Reach(wrist, rotation, pole);
        }

        void Pluck()
        {
            var x = bridgeX + 0.2f;  // between the pickups
            var (now, index) = NoteAt(quarter);
            var rotation = picking.Facing(Dir(0.2f, -0.8f, -0.5f).normalized, Dir(0f, 0.3f, 1f).normalized);
            picking.Wrist.rotation = rotation;
            picking.Relax(0.35f);
            var finger = picking.Fingers[index % 2 == 0 ? 1 : 2];
            var stringNow = now?.String ?? 1;
            var s = StringAt(stringNow, x);
            // through the string at the note's time, from above it to resting against the next string down
            var since = now != null ? (float)(quarter - now.Value.AtQuarter) : 1f;
            var through = Mathf.Clamp01(since / 0.15f);
            var target = s + new Vector3(0, Mathf.Lerp(0.004f, -0.009f, Smooth(through)), -0.001f);
            // the hand sets the plucking finger's tip onto the target: wrist from the tip's place in the hand
            finger.Curl(0.5f);
            var tip = finger.Tip;
            var wrist = instrument.ToWorld(target) - (tip - picking.Wrist.position);
            var pole = PickingElbow();
            picking.Reach(wrist, rotation, pole);
            finger.Reach(instrument.ToWorld(target));
            // the thumb rests on the pickup, above the low string
            picking.Fingers[0].Reach(instrument.ToWorld(StringAt(0, x - 0.06f) + new Vector3(0, 0.02f, -0.01f)));
        }

        static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3 - 2 * u);
        }
    }
}
