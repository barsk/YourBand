using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace GuitarTutor.Core.Songs
{
    /// <summary>
    /// Songs on disk: JSON with camelCase names and readable enums (strokes as "down", "up"...). The file carries
    /// a format version; a newer file than this code understands is refused rather than half read.
    /// </summary>
    public static class SongJson
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace,  // a file's lists replace the defaults, not add to them
        };

        public static string Write(Song song) => JsonConvert.SerializeObject(song, Settings);

        public static Song Read(string json)
        {
            var song = JsonConvert.DeserializeObject<Song>(json, Settings);
            if (song == null)
                throw new InvalidDataException("not a song file");
            if (song.Version > Song.CurrentVersion)
                throw new InvalidDataException($"song format {song.Version} is newer than this version understands ({Song.CurrentVersion})");
            return song;
        }

        public static void Save(Song song, string path) => File.WriteAllText(path, Write(song));

        public static Song Load(string path) => Read(File.ReadAllText(path));
    }
}
