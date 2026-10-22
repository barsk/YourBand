using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GuitarTutor.Runtime.Engine
{
    /// <summary>
    /// Starts the audio engine for the game when none is running: hidden, and tied to the game's process, so it
    /// closes when the game does (even if the game crashes). An engine you started yourself is left alone.
    /// </summary>
    public static class EngineProcess
    {
        public const string ProcessName = "GuitarTutor Engine";
        const string BuildPath = "engine/build/Release/gt_engine_artefacts/Release/GuitarTutor Engine.exe";

        public static bool IsRunning() => Process.GetProcessesByName(ProcessName).Length > 0;

        /// <summary>
        /// The engine program: given on the command line (-engine), next to the game (Engine/), or the
        /// repository's build, found by looking upwards from the game. Null if there is none.
        /// </summary>
        public static string Find(string given)
        {
            if (!string.IsNullOrEmpty(given))
                return File.Exists(given) ? Path.GetFullPath(given) : null;
            var dir = new DirectoryInfo(Application.dataPath);
            for (int up = 0; dir != null && up < 6; ++up, dir = dir.Parent)
                foreach (var candidate in new[] { Path.Combine(dir.FullName, "Engine", ProcessName + ".exe"), Path.Combine(dir.FullName, BuildPath) })
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
            return null;
        }

        /// <summary>Starts the engine for this game; null (and a log line) if it cannot.</summary>
        /// <param name="extraArguments">More for the engine's command line (tests: "--silent --record x.wav").</param>
        public static Process Start(string program, string extraArguments)
        {
            if (program == null)
            {
                Debug.LogWarning("GuitarTutor: no audio engine found to start (build engine/, or give -engine <file>)");
                return null;
            }
            try
            {
                var info = new ProcessStartInfo(program, $"--parent {Process.GetCurrentProcess().Id} {extraArguments}".Trim())
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(program),
                };
                var process = Process.Start(info);
                Debug.Log($"GuitarTutor: started the audio engine ({program}, process {process?.Id})");
                return process;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"GuitarTutor: could not start the audio engine {program}: {e.Message}");
                return null;
            }
        }
    }
}
