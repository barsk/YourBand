using UnityEngine;

namespace GuitarTutor.Runtime.Band
{
    /// <summary>
    /// A humanoid arm and hand for playing: the arm's bones for IK, the hand's own axes (measured from its bones,
    /// so any avatar works), and the fingers, posed from their straight rest pose onto fingertip targets.
    /// </summary>
    public sealed class HandRig
    {
        public readonly bool Left;
        public readonly Transform Upper, Lower, Wrist;
        /// <summary>0 the thumb, 1 the index ... 4 the little finger.</summary>
        public readonly Finger[] Fingers = new Finger[5];
        /// <summary>In the hand's frame (its rotation, world lengths): wrist to the middle finger's knuckle, the
        /// back of the hand, from the little finger's knuckle towards the index's.</summary>
        public readonly Vector3 Forward, Back, Across;
        /// <summary>Wrist to the middle knuckle (m).</summary>
        public readonly float Palm;

        public HandRig(Animator a, bool left)
        {
            Left = left;
            HumanBodyBones B(HumanBodyBones l, HumanBodyBones r) => left ? l : r;
            Upper = a.GetBoneTransform(B(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm));
            Lower = a.GetBoneTransform(B(HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm));
            Wrist = a.GetBoneTransform(B(HumanBodyBones.LeftHand, HumanBodyBones.RightHand));
            HumanBodyBones[][] chains =
            {
                new[] { B(HumanBodyBones.LeftThumbProximal, HumanBodyBones.RightThumbProximal), B(HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.RightThumbIntermediate), B(HumanBodyBones.LeftThumbDistal, HumanBodyBones.RightThumbDistal) },
                new[] { B(HumanBodyBones.LeftIndexProximal, HumanBodyBones.RightIndexProximal), B(HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.RightIndexIntermediate), B(HumanBodyBones.LeftIndexDistal, HumanBodyBones.RightIndexDistal) },
                new[] { B(HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightMiddleProximal), B(HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.RightMiddleIntermediate), B(HumanBodyBones.LeftMiddleDistal, HumanBodyBones.RightMiddleDistal) },
                new[] { B(HumanBodyBones.LeftRingProximal, HumanBodyBones.RightRingProximal), B(HumanBodyBones.LeftRingIntermediate, HumanBodyBones.RightRingIntermediate), B(HumanBodyBones.LeftRingDistal, HumanBodyBones.RightRingDistal) },
                new[] { B(HumanBodyBones.LeftLittleProximal, HumanBodyBones.RightLittleProximal), B(HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.RightLittleIntermediate), B(HumanBodyBones.LeftLittleDistal, HumanBodyBones.RightLittleDistal) },
            };
            for (int i = 0; i < 5; ++i)
                Fingers[i] = new Finger(this, i, a.GetBoneTransform(chains[i][0]), a.GetBoneTransform(chains[i][1]), a.GetBoneTransform(chains[i][2]));
            var toHand = Quaternion.Inverse(Wrist.rotation);
            var forward = Fingers[2].Bones[0].position - Wrist.position;
            Palm = forward.magnitude;
            Forward = toHand * forward.normalized;
            var across = Fingers[1].Bones[0].position - Fingers[4].Bones[0].position;
            Across = toHand * Vector3.ProjectOnPlane(across, forward).normalized;
            // the back of the hand: for a left hand, fingers forward and the index to the right, it faces up
            Back = Vector3.Cross(Forward, Across) * (left ? 1 : -1);
            Back.Normalize();
        }

        /// <summary>Turns the hand so its fingers point along <paramref name="forward"/> and its back faces as
        /// near <paramref name="back"/> as square to it allows.</summary>
        public Quaternion Facing(Vector3 forward, Vector3 back) => Ik.Aim(Forward, Back, forward, back);

        /// <summary>The arm onto a wrist position, the elbow towards <paramref name="pole"/>, then the hand turned.</summary>
        public void Reach(Vector3 wrist, Quaternion rotation, Vector3 pole)
        {
            Ik.TwoBone(Upper, Lower, Wrist, wrist, pole);
            Wrist.rotation = rotation;
        }

        /// <summary>Every finger back to its straight rest pose, then curled by <paramref name="curl"/> (0..1).</summary>
        public void Relax(float curl)
        {
            foreach (var f in Fingers)
                f.Curl(curl);
        }

        public Vector3 BackWorld => Wrist.rotation * Back;
        public Vector3 AcrossWorld => Wrist.rotation * Across;
        public Vector3 ForwardWorld => Wrist.rotation * Forward;
    }

    /// <summary>A finger's three bones, and posing it onto a fingertip target (cyclic coordinate descent on its
    /// joints: they bend towards the palm, within a hand's limits; the knuckle also turns a little sideways).</summary>
    public sealed class Finger
    {
        public readonly Transform[] Bones;
        readonly HandRig hand;
        readonly bool thumb;
        readonly Quaternion[] rest = new Quaternion[3];
        readonly float tip;  // past the last bone: the fingertip

        public Finger(HandRig hand, int index, Transform proximal, Transform intermediate, Transform distal)
        {
            this.hand = hand;
            thumb = index == 0;
            Bones = new[] { proximal, intermediate, distal };
            for (int i = 0; i < 3; ++i)
                rest[i] = Bones[i].localRotation;
            tip = Vector3.Distance(intermediate.position, distal.position) * 0.85f;
        }

        public Vector3 Tip
        {
            get
            {
                var d = Bones[2].position - Bones[1].position;
                return Bones[2].position + d.normalized * tip;
            }
        }

        /// <summary>Straight (as the model rests), then each joint bent by up to 80 degrees times <paramref name="amount"/>.</summary>
        public void Curl(float amount)
        {
            for (int i = 0; i < 3; ++i)
                Bones[i].localRotation = rest[i];
            var bend = (thumb ? 35f : 80f) * amount;
            for (int i = 0; i < 3; ++i)
                Bones[i].RotateAround(Bones[i].position, Hinge, bend * (i == 0 ? 0.8f : i == 1 ? 1.1f : 0.8f));
        }

        /// <summary>
        /// The axis a joint bends about, positive turns bending towards the palm: for a finger the hand's across
        /// axis (a left hand's palm down, index to the right: turning about it takes the fingers down); for the
        /// thumb the hand's forward axis, the other way round.
        /// </summary>
        Vector3 Hinge => thumb ? hand.ForwardWorld * (hand.Left ? -1 : 1) : hand.AcrossWorld * (hand.Left ? 1 : -1);

        /// <summary>
        /// The fingertip onto <paramref name="target"/> from a light curl: each joint in turn, from the last to the
        /// knuckle, bends to bring the tip nearer (no joint straighter than the rest pose or past 100 degrees; the
        /// knuckle also turns up to 20 degrees sideways). Returns how far the tip stays (m).
        /// </summary>
        public float Reach(Vector3 target, int rounds = 8)
        {
            Curl(0.2f);
            var bent = new float[3] { 0.2f * 80f * 0.8f, 0.2f * 80f * 1.1f, 0.2f * 80f * 0.8f };
            var side = 0f;
            for (int round = 0; round < rounds; ++round)
            {
                for (int i = 2; i >= 0; --i)
                {
                    var joint = Bones[i].position;
                    var axis = Hinge.normalized;
                    var a = Vector3.ProjectOnPlane(Tip - joint, axis);
                    var b = Vector3.ProjectOnPlane(target - joint, axis);
                    if (a.sqrMagnitude < 1e-10f || b.sqrMagnitude < 1e-10f)
                        continue;
                    var angle = Vector3.SignedAngle(a, b, axis);
                    var wanted = Mathf.Clamp(bent[i] + angle, 0f, thumb ? 60f : 100f);
                    Bones[i].RotateAround(joint, axis, wanted - bent[i]);
                    bent[i] = wanted;
                    if (i == 0)
                    {
                        // the knuckle's sideways turn, about the back of the hand
                        var up = hand.BackWorld;
                        var sa = Vector3.ProjectOnPlane(Tip - joint, up);
                        var sb = Vector3.ProjectOnPlane(target - joint, up);
                        if (sa.sqrMagnitude > 1e-10f && sb.sqrMagnitude > 1e-10f)
                        {
                            var turn = Mathf.Clamp(side + Vector3.SignedAngle(sa, sb, up), -20f, 20f);
                            Bones[0].RotateAround(joint, up, turn - side);
                            side = turn;
                        }
                    }
                }
            }
            return Vector3.Distance(Tip, target);
        }
    }
}
