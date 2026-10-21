using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GuitarTutor.Runtime.Songs
{
    /// <summary>
    /// Where songs are: your imported songs in the repository's <c>songs-local/</c> folder (not in git: see
    /// tools/songtool), found by looking upwards from the game, in the Editor and in a build next to it.
    /// </summary>
    public static class SongLibrary
    {
        public const string FolderName = "songs-local";

        public static string LocalFolder()
        {
            var dir = new DirectoryInfo(Application.dataPath);
            for (int up = 0; dir != null && up < 6; ++up, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, FolderName);
                if (Directory.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        /// <summary>The song files in songs-local, by name.</summary>
        public static List<string> Files()
        {
            var folder = LocalFolder();
            return folder == null
                ? new List<string>()
                : Directory.GetFiles(folder, "*.song.json").OrderBy(f => f, System.StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
