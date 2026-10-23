using System;
using System.Collections.Generic;
using System.Linq;
using GuitarTutor.Core.Band;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Music;
using GuitarTutor.Core.Performance;
using UnityEngine;

namespace GuitarTutor.Runtime.Band
{
    /// <summary>
    /// The drummer: sat on the kit's throne, playing the band's drum part (DrumPerformance: who plays what) with
    /// the kit's own sticks. Each hand's stick pivots at the grip: it rises after a hit and comes down onto the
    /// next drum at the note's time, higher for a louder hit, less when the next comes soon; the arm reaches the
    /// grip by IK. The feet press the kick and hi-hat pedals, which move with them; the crash swings when hit.
    /// The clip (a rock beat) moves the body and the head.
    /// </summary>
    public sealed class Drummer : Musician
    {
        static readonly Vector3 PelvisOnSeat = new Vector3(0f, 0.09f, -0.03f);  // right, up, forward
        const float ThroneBack = -0.04f;           // m: the throne further from the drums than the kit has it (nearer: where the right foot just reaches the kick pedal)
        const float StickTip = 0.27f;              // m from the grip (thumb and index) to the tip
        const float StickDown = 25f;               // degrees below level when the tip strikes
        const float ElbowsOut = 0.15f;             // m: the sticks aim from beside the shoulders, so they point in
        const float KickDown = 0.125f, KickUp = 0.35f;  // quarter notes: the footboard goes down before a hit, back after
        static readonly (float amplitude, float frequency, float decay) CrashSwing = (7f, 1.4f, 1.2f);  // degrees, Hz, s

        readonly Dictionary<Limb, List<DrumStroke>> strokes = new Dictionary<Limb, List<DrumStroke>>();
        TempoMap tempo = new TempoMap(120, TimeSignature.FourFour);
        bool seated, gripped;
        readonly Hand[] hands = new Hand[2];
        Pedal kick, hihat;
        Transform crash, hihatTop;
        // the moving parts' geometry (pivots, axes, rest poses) is kept in the drummer's own frame, not the world's:
        // in VR the band is moved about, and a pivot left behind would swing the cymbals and pedals about thin air
        Vector3 crashPivot, crashAxis, hatRestPosition, crashRestPosition;
        Quaternion crashRest;
        float hatDrop;
        double quarter;

        sealed class Hand
        {
            public bool left;
            public Transform upper, lower, hand, stick;
            public Vector3 grip, stickDirection, back;  // in the hand's frame (rotation only): the grip, the stick's way, the back of the hand
        }

        Vector3 Wp(Vector3 local) => transform.TransformPoint(local);
        Vector3 Wd(Vector3 local) => transform.TransformDirection(local);
        Quaternion Wq(Quaternion local) => transform.rotation * local;
        Vector3 Lp(Vector3 world) => transform.InverseTransformPoint(world);
        Vector3 Ld(Vector3 world) => transform.InverseTransformDirection(world);
        Quaternion Lq(Quaternion world) => Quaternion.Inverse(transform.rotation) * world;

        /// <summary>A pedal: its numbers are in the drummer's frame (see the fields above).</summary>
        sealed class Pedal
        {
            public Transform board, beater;
            public Quaternion boardRest, beaterRest;
            public Vector3 boardRestPosition, beaterRestPosition;
            public Vector3 hinge, axis, axle, heel, ball;
            public float travel, hit;
        }

        protected override void Awake()
        {
            base.Awake();
            if (instrument != null)
                instrument.Place(transform.position, transform.right, transform.up);
        }

        public override void SetSong(SongChart chart, Arrangement arrangement)
        {
            tempo = chart.Song.Tempo;
            strokes.Clear();
            foreach (Limb limb in Enum.GetValues(typeof(Limb)))
                strokes[limb] = new List<DrumStroke>();
            foreach (var s in DrumPerformance.Plan(arrangement.Notes))
                strokes[s.Limb].Add(s);
            if (Logging)
                Debug.Log($"GuitarTutor drummer plays: " + string.Join(", ", strokes.Select(kv => $"{kv.Key} {kv.Value.Count}")) + $" ({strokes.Values.SelectMany(x => x).Count(x => x.Open)} open hat)");
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
            if (!seated)
                Seat();
            if (!gripped)
                Grip();
            Pedals();
            Cymbals();
            foreach (var h in hands)
                Play(h);
        }

        // ------------------------------------------------------------------ set-up, from the first pose
        void Seat()
        {
            seated = true;
            // the throne a little back from the kit, for room between the body and the snare
            foreach (var part in instrument.Data["throne"])
                instrument.Part((string)part)?.Translate(-transform.forward * ThroneBack, Space.World);
            var seat = instrument.ToWorld(instrument.Data["seat"]) - transform.forward * ThroneBack;
            var wanted = seat + transform.right * PelvisOnSeat.x + transform.up * PelvisOnSeat.y + transform.forward * PelvisOnSeat.z;
            body.transform.position += wanted - Bone(HumanBodyBones.Hips).position;
        }

        void Grip()
        {
            gripped = true;
            var sticks = instrument.Data["sticks"];
            for (int i = 0; i < 2; ++i)
            {
                var left = i == 0;  // the kit's first stick is the left hand's (as in the mockup)
                var h = hands[i] = new Hand
                {
                    left = left,
                    upper = Bone(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm),
                    lower = Bone(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm),
                    hand = Bone(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand),
                };
                var thumb = Bone(left ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal).position;
                var index = Bone(left ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate).position;
                var little = Bone(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal).position;
                var indexBase = Bone(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal).position;
                var middleBase = Bone(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal).position;
                var wrist = h.hand.position;
                var fulcrum = (thumb + index) / 2;
                var heel = wrist + (little - wrist) * 0.35f;
                var toHand = Quaternion.Inverse(h.hand.rotation);
                h.grip = toHand * (fulcrum - wrist);
                h.stickDirection = toHand * (fulcrum - heel).normalized;
                var fingers = middleBase - wrist;
                var across = indexBase - little;  // from the little finger to the index
                h.back = toHand * (Vector3.Cross(fingers, across) * (left ? 1 : -1)).normalized;

                // the kit's stick into the hand: its tip ahead of the grip along the hand's stick direction
                var stick = instrument.Part((string)sticks[i]["object"]);
                if (stick == null)
                    continue;
                var butt = instrument.ToWorld(sticks[i]["butt"]);
                var tip = instrument.ToWorld(sticks[i]["tip"]);
                var direction = h.hand.rotation * h.stickDirection;
                var turn = Quaternion.FromToRotation(tip - butt, direction);
                stick.rotation = turn * stick.rotation;
                stick.position = fulcrum + direction * StickTip - turn * (tip - stick.position);
                stick.SetParent(h.hand, true);
                h.stick = stick;
            }

            Pedal MakePedal(string name, string beaterKey)
            {
                var d = instrument.Data[name];
                var p = new Pedal
                {
                    board = instrument.Part((string)d["board"]),
                    hinge = Lp(instrument.ToWorld(d["hinge"])),
                    axis = Ld(instrument.DirectionToWorld(InstrumentFrame.Point(d["axis"]))),
                    travel = (float)d["travel"] * Mathf.Rad2Deg,
                    heel = Lp(instrument.ToWorld(instrument.Data[name == "kick" ? "kick_heel" : "hihat_heel"])),
                    ball = Lp(instrument.ToWorld(instrument.Data[name == "kick" ? "kick_ball" : "hihat_ball"])),
                };
                if (p.board != null)
                    (p.boardRestPosition, p.boardRest) = (Lp(p.board.position), Lq(p.board.rotation));
                if (beaterKey != null)
                {
                    p.beater = instrument.Part((string)d[beaterKey]);
                    p.axle = Lp(instrument.ToWorld(d["axle"]));
                    p.hit = (float)d["hit"] * Mathf.Rad2Deg;
                    if (p.beater != null)
                        (p.beaterRestPosition, p.beaterRest) = (Lp(p.beater.position), Lq(p.beater.rotation));
                }
                return p;
            }
            kick = MakePedal("kick", "beater");
            hihat = MakePedal("hihat", null);

            var swing = instrument.Data["swing"]["crash"];
            crash = instrument.Part((string)swing["object"]);
            crashPivot = Lp(instrument.ToWorld(swing["pivot"]));
            crashAxis = Ld(instrument.DirectionToWorld(InstrumentFrame.Point(swing["axis"])));
            if (crash != null)
                (crashRestPosition, crashRest) = (Lp(crash.position), Lq(crash.rotation));
            var tops = instrument.Data["hihat"]["top"];
            hihatTop = instrument.Part((string)tops[0]);
            hatDrop = (float)instrument.Data["hihat"]["drop"];
            if (hihatTop != null)
            {
                // the top and its clutch move as one: the clutch under the top
                var clutch = instrument.Part((string)tops[1]);
                if (clutch != null)
                    clutch.SetParent(hihatTop, true);
                hatRestPosition = Lp(hihatTop.position);
            }
        }

        // ------------------------------------------------------------------ the hands
        /// <summary>The previous and next stroke of a limb around now (either may be missing).</summary>
        (DrumStroke? before, DrumStroke? after) Around(Limb limb)
        {
            var list = strokes.TryGetValue(limb, out var l) ? l : null;
            if (list == null || list.Count == 0)
                return (null, null);
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (list[mid].AtQuarter <= quarter)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return (lo > 0 ? list[lo - 1] : (DrumStroke?)null, lo < list.Count ? list[lo] : (DrumStroke?)null);
        }

        Vector3 Strike(KitPiece piece)
        {
            var name = piece switch
            {
                KitPiece.HiHat => "hihat",
                KitPiece.Snare => "snare",
                KitPiece.Crash => "crash",
                KitPiece.Ride => "ride",
                KitPiece.Tom1 => "tom1",
                KitPiece.Tom2 => "tom2",
                _ => "floor_tom",
            };
            return instrument.ToWorld(instrument.Data["strike"][name]);
        }

        /// <summary>The stick's way when its tip strikes there: from the shoulder's side, a little down.</summary>
        Vector3 StrikeDirection(Hand h, Vector3 strike)
        {
            var from = h.upper.position + transform.right * (h.left ? -ElbowsOut : ElbowsOut);
            var level = Vector3.ProjectOnPlane(strike - from, transform.up).normalized;
            return (level * Mathf.Cos(StickDown * Mathf.Deg2Rad) - transform.up * Mathf.Sin(StickDown * Mathf.Deg2Rad)).normalized;
        }

        void Play(Hand h)
        {
            if (h == null)
                return;
            var (before, after) = Around(h.left ? Limb.LeftHand : Limb.RightHand);
            var rest = h.left ? KitPiece.Snare : KitPiece.HiHat;
            var from = Strike(before?.Piece ?? after?.Piece ?? rest);
            var to = Strike(after?.Piece ?? before?.Piece ?? rest);
            float u = 0, gap = 2, velocity = 80;
            if (before != null && after != null)
            {
                var t0 = before.Value.AtQuarter;
                var t1 = after.Value.AtQuarter;
                u = (float)((quarter - t0) / (t1 - t0));
                gap = (float)(tempo.SecondsAt(t1) - tempo.SecondsAt(t0));
                velocity = after.Value.Velocity;
            }
            else if (after != null)  // waiting for the first hit: up, over it
                (u, velocity) = (0.5f, after.Value.Velocity);
            else if (before != null)  // after the last: back up
                (u, gap) = (Mathf.Min(1, (float)(quarter - before.Value.AtQuarter)), 2);

            // the grip moves on to the next drum while the stick is up
            var move = Smooth(Mathf.InverseLerp(0.1f, 0.75f, u));
            var fromDirection = StrikeDirection(h, from);
            var toDirection = StrikeDirection(h, to);
            var direction = Vector3.Slerp(fromDirection, toDirection, move).normalized;
            var grip = Vector3.Lerp(from - fromDirection * StickTip, to - toDirection * StickTip, move);

            // the lift: up quickly after the hit, held, down faster and faster onto the next
            var most = Mathf.Lerp(16f, 48f, Mathf.InverseLerp(40, 127, velocity));
            most = gap > 1.2f ? 25f : Mathf.Min(most, gap * 220f);  // a long rest: just hold the stick up
            var lift = u < 0.3f ? most * Smooth(u / 0.3f) : u < 0.65f ? most : most * (1 - Square((u - 0.65f) / 0.35f));
            var across = Vector3.Cross(transform.up, direction).normalized;
            direction = Quaternion.AngleAxis(-lift, across) * direction;

            // the hand: holding the stick that way, the back of the hand up and outwards
            var outwards = transform.right * (h.left ? -1 : 1);
            var back = (transform.up + outwards * 0.5f).normalized;
            var rotation = Ik.Aim(h.stickDirection, h.back, direction, back);
            var wrist = grip - rotation * h.grip;
            // the elbow stays where the capture has it (the mockup keeps the captured elbow direction), pushed out and
            // forward so it clears the body
            var pole = h.lower.position + outwards * 0.2f + transform.forward * 0.1f;
            var miss = Ik.TwoBone(h.upper, h.lower, h.hand, wrist, pole);
            h.hand.rotation = rotation;
            if (Logging)
            {
                var tip = h.stick != null ? h.stick.position : Vector3.zero;
                Debug.Log($"GuitarTutor drummer {(h.left ? "L" : "R")} q {quarter:0.00}: {before?.Piece}->{after?.Piece} u {u:0.00} lift {lift:0} "
                          + $"grip {transform.InverseTransformPoint(grip)} wrist {transform.InverseTransformPoint(wrist)} reached {transform.InverseTransformPoint(h.hand.position)} miss {miss * 1000:0} mm "
                          + $"shoulder {transform.InverseTransformPoint(h.upper.position)}");
            }
        }

        // ------------------------------------------------------------------ the feet and the kit
        void Pedals()
        {
            // kick: the footboard goes down before each hit and up after it; the beater swings to the head
            var (before, after) = Around(Limb.RightFoot);
            var press = 0f;
            if (after != null && after.Value.AtQuarter - quarter < KickDown)
                press = Smooth(1 - (float)((after.Value.AtQuarter - quarter) / KickDown));
            if (before != null && quarter - before.Value.AtQuarter < KickUp)
                press = Mathf.Max(press, 1 - Smooth((float)((quarter - before.Value.AtQuarter) / KickUp)));
            Press(kick, press, Bone(HumanBodyBones.RightUpperLeg), Bone(HumanBodyBones.RightLowerLeg), Bone(HumanBodyBones.RightFoot), Bone(HumanBodyBones.RightToes));
            if (kick.beater != null)
            {
                kick.beater.SetPositionAndRotation(Wp(kick.beaterRestPosition), Wq(kick.beaterRest));
                kick.beater.RotateAround(Wp(kick.axle), Wd(kick.axis), kick.hit * press);
            }
            // hi-hat: closed, but for the open hits: the foot lifts just before one and lets the hat shut again by the
            // next beat; the top cymbal rises with it (it opens about half its gap)
            var open = HatOpen();
            Press(hihat, 1 - open, Bone(HumanBodyBones.LeftUpperLeg), Bone(HumanBodyBones.LeftLowerLeg), Bone(HumanBodyBones.LeftFoot), Bone(HumanBodyBones.LeftToes));
            if (hihatTop != null)
                hihatTop.position = Wp(hatRestPosition) - transform.up * (hatDrop * (1 - HatOpenShare * open));
            if (Logging && open > 0)
                Debug.Log($"GuitarTutor hi-hat q {quarter:0.00}: open {open:0.00}, top {(hihatTop != null ? (hihatTop.position - hatRestPosition).magnitude * 1000 : 0):0} mm below rest");
        }

        const float HatOpenShare = 0.55f;  // how much of its gap the hi-hat opens

        /// <summary>How open the hi-hat is now (0 closed, 1 open): up quickly before an open hit, held a beat's
        /// worth, then shut.</summary>
        float HatOpen()
        {
            var (before, after) = Around(Limb.RightHand);
            var open = 0f;
            if (before != null && before.Value.Open && before.Value.Piece == KitPiece.HiHat)
            {
                var t = (float)(quarter - before.Value.AtQuarter);
                open = t < 0.4f ? 1f : 1f - Smooth(Mathf.InverseLerp(0.4f, 0.55f, t));
            }
            if (after != null && after.Value.Open && after.Value.Piece == KitPiece.HiHat)
                open = Mathf.Max(open, Smooth(Mathf.InverseLerp(0.3f, 0.05f, (float)(after.Value.AtQuarter - quarter))));
            return open;
        }

        /// <summary>A pedal pressed (0 up, 1 down), and the foot on its footboard: heel on the heel plate, toes
        /// towards the ball point.</summary>
        void Press(Pedal p, float press, Transform thigh, Transform calf, Transform foot, Transform toes)
        {
            var hinge = Wp(p.hinge);
            var axis = Wd(p.axis);
            var turn = Quaternion.AngleAxis(p.travel * press, axis);
            if (p.board != null)
            {
                p.board.SetPositionAndRotation(Wp(p.boardRestPosition), Wq(p.boardRest));
                p.board.RotateAround(hinge, axis, p.travel * press);
            }
            var ball = hinge + turn * (Wp(p.ball) - hinge);
            var heel = hinge + turn * (Wp(p.heel) - hinge);
            var ankle = heel + transform.up * 0.075f + (ball - heel) * 0.1f;
            var pole = thigh.position + transform.forward * 0.6f + (thigh.position - calf.position).normalized * -0.1f;
            var miss = Ik.TwoBone(thigh, calf, foot, ankle, pole);
            if (Logging)
                Debug.Log($"FOOT {(thigh == Bone(HumanBodyBones.RightUpperLeg) ? "kick" : "hat")} q={quarter:0.00} press={press:0.00} miss={miss:0.000} hips={Bone(HumanBodyBones.Hips).position.y - transform.position.y:0.000} ankleTarget={ankle.y - transform.position.y:0.000} footY={foot.position.y - transform.position.y:0.000}");
            // the foot along the footboard: its toes (the ball of the foot) towards the ball point
            if (toes != null)
                foot.rotation = Quaternion.FromToRotation(toes.position - foot.position, ball + transform.up * 0.02f - foot.position) * foot.rotation;
        }

        void Cymbals()
        {
            if (crash == null)
                return;
            var last = strokes.Values.SelectMany(s => s).Where(s => s.Piece == KitPiece.Crash && s.AtQuarter <= quarter)
                .Select(s => (double?)s.AtQuarter).DefaultIfEmpty(null).Max();
            crash.SetPositionAndRotation(Wp(crashRestPosition), Wq(crashRest));
            if (last == null)
                return;
            var since = (float)(tempo.SecondsAt(quarter) - tempo.SecondsAt(last.Value));
            var angle = CrashSwing.amplitude * Mathf.Exp(-since / CrashSwing.decay) * Mathf.Sin(2 * Mathf.PI * CrashSwing.frequency * since);
            var tilt = Vector3.Cross(Wd(crashAxis), transform.forward).normalized;  // swings about a level axis across the cymbal
            crash.RotateAround(Wp(crashPivot), tilt, angle);
        }

        static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3 - 2 * u);
        }

        static float Square(float u) => u * u;
    }
}
