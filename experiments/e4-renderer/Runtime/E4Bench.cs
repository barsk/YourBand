// Experiment E4 benchmark: renders each shot at about Quest 3 stereo resolution (4128 x 2208) for every
// quality level of the project, measures GPU and CPU frame times, saves a screenshot per shot and level,
// writes results.json and quits.
//
//   E4.exe -e4out <folder>      (default: the folder next to the executable)

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class E4Bench : MonoBehaviour
{
    public Camera[] shots;
    public Animator subject;  // the guitarist: checked to be playing during each measurement
    public int width = 4128, height = 2208;
    public int warmupFrames = 150, measuredFrames = 600;

    [System.Serializable]
    class Result
    {
        public string pipeline, quality, shot;
        public float gpuMs, gpuP95Ms, cpuMs, renderThreadMs, frameMs, frameP95Ms;
        public int frames;
    }

    [System.Serializable]
    class Results
    {
        public string device, api;
        public int width, height;
        public List<Result> results = new List<Result>();
    }

    IEnumerator Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        var outDir = OutputFolder();
        Directory.CreateDirectory(outDir);
        var all = new Results { device = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(), width = width, height = height };
        var timings = new FrameTiming[1];

        for (int level = 0; level < QualitySettings.names.Length; ++level)
        {
            QualitySettings.SetQualityLevel(level, true);
            QualitySettings.vSyncCount = 0;  // each quality level brings its own vsync setting back
            yield return null;
            var pipeline = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline.name
                         : UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline.name : "built-in";

            foreach (var shot in shots)
            {
                foreach (var s in shots)
                    s.enabled = s == shot;
                var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                shot.targetTexture = target;

                for (int i = 0; i < warmupFrames; ++i)
                    yield return null;
                var handBefore = Hand();
                var stateBefore = subject != null ? subject.GetCurrentAnimatorStateInfo(0).normalizedTime : -1;

                var gpu = new List<float>();
                var frames = new List<float>();
                float cpu = 0, render = 0;
                for (int i = 0; i < measuredFrames; ++i)
                {
                    yield return null;
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                    {
                        gpu.Add((float)timings[0].gpuFrameTime);
                        cpu += (float)timings[0].cpuMainThreadFrameTime;
                        render += (float)timings[0].cpuRenderThreadFrameTime;
                    }
                    frames.Add(Time.unscaledDeltaTime * 1000f);
                }
                gpu.Sort();
                frames.Sort();
                Debug.Log($"E4 check: animator {(subject != null && subject.isActiveAndEnabled)}, state time {stateBefore:F2} -> "
                          + $"{(subject != null ? subject.GetCurrentAnimatorStateInfo(0).normalizedTime : -1):F2}, right hand moved "
                          + $"{(Hand() - handBefore).magnitude * 100:F1} cm; frame timing enabled {FrameTimingManager.IsFeatureEnabled()}, "
                          + $"GPU samples {gpu.Count}, first {(gpu.Count > 0 ? gpu[0] : -1):F3} ms");
                var r = new Result
                {
                    pipeline = pipeline, quality = QualitySettings.names[level], shot = shot.name, frames = measuredFrames,
                    gpuMs = gpu.Count > 0 ? gpu.Average() : -1, gpuP95Ms = gpu.Count > 0 ? gpu[(int)(gpu.Count * 0.95f)] : -1,
                    cpuMs = gpu.Count > 0 ? cpu / gpu.Count : -1, renderThreadMs = gpu.Count > 0 ? render / gpu.Count : -1,
                    frameMs = frames.Average(), frameP95Ms = frames[(int)(frames.Count * 0.95f)],
                };
                all.results.Add(r);
                Debug.Log($"E4 {r.pipeline} {r.quality} {r.shot}: frame {r.frameMs:F2} ms (p95 {r.frameP95Ms:F2}), GPU {r.gpuMs:F2} ms, "
                          + $"CPU main {r.cpuMs:F2} ms, render thread {r.renderThreadMs:F2} ms");

                yield return Screenshot(shot, Path.Combine(outDir, $"{r.pipeline}_{r.quality}_{r.shot}.png"));
                shot.targetTexture = null;
                target.Release();
            }
        }

        File.WriteAllText(Path.Combine(outDir, "results.json"), JsonUtility.ToJson(all, true));
        Application.Quit();
    }

    IEnumerator Screenshot(Camera shot, string path)
    {
        // a 1920 x 1080 picture of the same shot, a few frames in so temporal anti-aliasing has settled
        var picture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        shot.targetTexture = picture;
        for (int i = 0; i < 30; ++i)
            yield return null;
        yield return new WaitForEndOfFrame();
        var previous = RenderTexture.active;
        RenderTexture.active = picture;
        var image = new Texture2D(picture.width, picture.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, picture.width, picture.height), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(path, image.EncodeToPNG());
        picture.Release();
    }

    Vector3 Hand() => subject != null ? subject.GetBoneTransform(HumanBodyBones.RightHand).position : Vector3.zero;

    static string OutputFolder()
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; ++i)
            if (args[i] == "-e4out")
                return args[i + 1];
        return Path.GetDirectoryName(Application.dataPath);
    }
}
