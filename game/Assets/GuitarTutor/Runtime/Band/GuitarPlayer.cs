using UnityEngine;

namespace GuitarTutor.Runtime.Band
{
    /// <summary>
    /// The guitarist or the bassist: the instrument hangs in front of the hips, as in the mockup (the body's
    /// centre a little to the player's right of the middle, the neck to their left, up and a little forward, the
    /// top facing out and tilted up), and moves with the body.
    /// </summary>
    public sealed class GuitarPlayer : Musician
    {
        /// <summary>The body's centre from the hips (the musician's frame: right, up, forward; the front of the
        /// belly is about 0.12 m ahead of the hips).</summary>
        public Vector3 bodyCentre = new Vector3(0.04f, 0.1f, 0.165f);
        /// <summary>Along the neck, and out of the top (the musician's frame).</summary>
        public Vector3 neck = new Vector3(-0.92f, 0.37f, 0.12f), top = new Vector3(0.05f, 0.25f, 1f);

        void LateUpdate()
        {
            if (!posed || instrument == null)
                return;
            var hips = Bone(HumanBodyBones.Hips);
            Vector3 Frame(Vector3 v) => transform.right * v.x + transform.up * v.y + transform.forward * v.z;
            instrument.Place(hips.position + Frame(bodyCentre), Frame(neck), Frame(top));
        }
    }
}
