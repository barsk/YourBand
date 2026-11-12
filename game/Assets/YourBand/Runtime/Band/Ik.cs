using UnityEngine;

namespace YourBand.Runtime.Band
{
    /// <summary>Limb IK for the musicians, applied after the animator has posed them (LateUpdate).</summary>
    public static class Ik
    {
        /// <summary>
        /// Two-bone IK (an arm or a leg): turns <paramref name="upper"/> and <paramref name="lower"/> so that
        /// <paramref name="end"/> reaches <paramref name="target"/> (as near as the limb's length allows), the middle
        /// joint bending towards <paramref name="pole"/>. Each bone turns as little as it can, so the clip's twist
        /// stays. Returns how far the end stays from the target (0: reached).
        /// </summary>
        public static float TwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
        {
            var a = upper.position;
            var l1 = Vector3.Distance(a, lower.position);
            var l2 = Vector3.Distance(lower.position, end.position);
            var toTarget = target - a;
            var reach = toTarget.magnitude;
            var d = Mathf.Clamp(reach, Mathf.Abs(l1 - l2) + 1e-4f, l1 + l2 - 1e-4f);
            var along = toTarget / Mathf.Max(reach, 1e-6f);
            // the elbow (knee) on the circle the two lengths allow, on the pole's side
            var cos = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1f, 1f);
            var side = Vector3.ProjectOnPlane(pole - a, along);
            if (side.sqrMagnitude < 1e-8f)
                side = Vector3.ProjectOnPlane(lower.position - a, along);
            side.Normalize();
            var middle = a + along * (l1 * cos) + side * (l1 * Mathf.Sqrt(1 - cos * cos));
            upper.rotation = Quaternion.FromToRotation(lower.position - a, middle - a) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, a + along * d - lower.position) * lower.rotation;
            return Mathf.Max(0, reach - d);
        }

        /// <summary>
        /// A rotation for a bone whose local <paramref name="localForward"/> should point along
        /// <paramref name="forward"/> and whose local <paramref name="localUp"/> as near <paramref name="up"/> as
        /// square to it allows (for hands: the fingers' direction and the back of the hand).
        /// </summary>
        public static Quaternion Aim(Vector3 localForward, Vector3 localUp, Vector3 forward, Vector3 up) =>
            Quaternion.LookRotation(forward, up) * Quaternion.Inverse(Quaternion.LookRotation(localForward, localUp));
    }
}
