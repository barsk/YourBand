using GuitarTutor.Core.Band;
using GuitarTutor.Core.Chart;
using UnityEngine;

namespace GuitarTutor.Runtime.Band
{
    public enum BandRole
    {
        Drummer,
        Bassist,
        Guitarist,
        Keyboardist,
    }

    /// <summary>
    /// One of the band: a Rocketbox avatar playing its Mixamo clip, held to the song (the drummer's groove on the
    /// beat), with its instrument. The musician's frame: x to their right, y up, z forward (the way they face).
    /// </summary>
    public class Musician : MonoBehaviour
    {
        public BandRole role;
        public Animator body;
        public InstrumentFrame instrument;

        /// <summary>The clip as the mockup timed it: frame of the first beat, frames per eighth note, loop length
        /// (frames, 30 a second). 0 frames per eighth: not on the beat, played at its own pace.</summary>
        public float clipBeat1, clipFramesPerEighth, clipLoopFrames;

        /// <summary>Log what the musicians aim at (the game's -bandlog).</summary>
        public static bool Logging;

        const float ClipFps = 30f;
        int state;
        float clipLength;
        protected bool posed;
        float shown;  // the clip's normalized time now

        protected virtual void Awake()
        {
            body.speed = 0;
            body.applyRootMotion = false;
            var info = body.runtimeAnimatorController.animationClips;
            clipLength = info.Length > 0 ? info[0].length : 1f;
        }

        /// <summary>The song and the band's parts (a new song, or a new style).</summary>
        public virtual void SetSong(SongChart chart, Arrangement arrangement)
        {
        }

        /// <summary>Where the song is: quarter notes from its start, and the seconds that took.</summary>
        public virtual void Show(double quarter, double seconds)
        {
            if (state == 0)
            {
                // the controller's only state, by the animator's own hash: a name built by hand does not find it
                // (the clip is called "mixamo.com", and the dot is the path separator)
                body.Update(0);
                state = body.GetCurrentAnimatorStateInfo(0).fullPathHash;
            }
            shown = ClipTime(quarter, seconds) / clipLength;
            body.Play(state, 0, shown);
            body.Update(0);
            posed = true;
        }

        /// <summary>Poses the body at <paramref name="count"/> moments spread over the whole clip, calling
        /// <paramref name="each"/> at each (to measure the capture), then back as it was.</summary>
        protected void SampleClip(int count, System.Action each)
        {
            for (int i = 0; i < count; ++i)
            {
                body.Play(state, 0, (i + 0.5f) / count);
                body.Update(0);
                each();
            }
            body.Play(state, 0, shown);
            body.Update(0);
        }

        /// <summary>The clip's time (seconds) for a moment of the song.</summary>
        float ClipTime(double quarter, double seconds)
        {
            if (clipFramesPerEighth > 0)
            {
                // on the beat: the groove's eighths on ours, its loop over two bars
                var m = clipBeat1 + (float)(quarter * 2 * clipFramesPerEighth % clipLoopFrames);
                if (m < clipBeat1)
                    m += clipLoopFrames;
                if (m > clipBeat1 + clipLoopFrames - 4)
                    m -= clipLoopFrames;  // the loop's last frames are its first
                return Mathf.Clamp((m - 1) / ClipFps, 0, clipLength);
            }
            return (float)(seconds % clipLength);
        }

        protected Transform Bone(HumanBodyBones bone) => body.GetBoneTransform(bone);
    }
}
