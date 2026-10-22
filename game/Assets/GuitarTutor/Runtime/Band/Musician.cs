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

        protected virtual void Awake()
        {
            body.speed = 0;
            body.applyRootMotion = false;
            var info = body.runtimeAnimatorController.animationClips;
            clipLength = info.Length > 0 ? info[0].length : 1f;
            state = Animator.StringToHash("Base Layer." + (info.Length > 0 ? info[0].name : ""));
        }

        /// <summary>The song and the band's parts (a new song, or a new style).</summary>
        public virtual void SetSong(SongChart chart, Arrangement arrangement)
        {
        }

        /// <summary>Where the song is: quarter notes from its start, and the seconds that took.</summary>
        public virtual void Show(double quarter, double seconds)
        {
            body.Play(state, 0, ClipTime(quarter, seconds) / clipLength);
            posed = true;
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
