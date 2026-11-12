using System.IO;
using System.Linq;

namespace YourBand.Core.Songs
{
    /// <summary>Where a song's data came from, in words for the log and the strumming panel.</summary>
    public static class SongOrigin
    {
        public const string GuitarPro = "guitar-pro", UltimateGuitar = "ultimate-guitar", Default = "default";

        /// <summary>
        /// The song as loaded: the file format and the pages it was made from, and where its strumming is from.
        /// "Guitar Pro file 'x.gp'; strumming from Ultimate Guitar page 'y.html' (overrides the file's)".
        /// </summary>
        public static string Describe(Song song, string songFile = null)
        {
            var parts = song.Sources.Select(Describe).Where(s => s.Length > 0).ToList();
            var from = parts.Count > 0 ? string.Join(" + ", parts) : "no source recorded";
            var file = string.IsNullOrEmpty(songFile) ? "" : $" ({Path.GetFileName(songFile)})";
            return $"{from}{file}; strumming: {Strumming(song, long_: true)}";
        }

        static string Describe(Source source)
        {
            var name = string.IsNullOrEmpty(source.Url) ? "" : $" '{Path.GetFileName(source.Url)}'";
            switch (source.Kind)
            {
                case "guitar-pro": return "Guitar Pro file" + name;
                case "ultimate-guitar-chords": return "Ultimate Guitar chords page" + name;
                case "ultimate-guitar-tabs": return "Ultimate Guitar tabs page" + name;
                case "ultimate-guitar-strumming": return "Ultimate Guitar strumming" + name;
                case "demo": return "built-in demo song";
                default: return source.Kind;
            }
        }

        /// <summary>
        /// Where the song's strumming comes from: its patterns' origin (the song's whole strumming when they agree,
        /// else a mix); for a song saved before patterns had one, its first source.
        /// </summary>
        public static string Strumming(Song song, bool long_ = false)
        {
            if (song.Strumming.Count == 0)
                return "none (the band plays a downstroke on every beat)";
            var origins = song.Strumming.Select(p => Of(song, p)).Distinct().ToList();
            return string.Join(" and ", origins.Select(o => Name(o, long_)));
        }

        /// <summary>A pattern's origin: its own, else (an older song file) the song's first source.</summary>
        public static string Of(Song song, StrumPattern pattern)
        {
            if (!string.IsNullOrEmpty(pattern.Origin))
                return pattern.Origin;
            var kind = song.Sources.FirstOrDefault()?.Kind ?? "";
            return kind.StartsWith("ultimate-guitar") ? UltimateGuitar : kind == "guitar-pro" ? GuitarPro : kind == "demo" ? Default : "";
        }

        public static string Name(string origin, bool long_ = false)
        {
            switch (origin)
            {
                case GuitarPro: return long_ ? "Guitar Pro file (the recorded strokes)" : "Guitar Pro file";
                case UltimateGuitar: return long_ ? "Ultimate Guitar page" : "Ultimate Guitar page";
                case Default: return "built-in";
                default: return "unknown";
            }
        }
    }
}
