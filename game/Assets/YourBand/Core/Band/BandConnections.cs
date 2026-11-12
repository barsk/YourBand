using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YourBand.Core.Songs;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace YourBand.Core.Band
{
    /// <summary>The band's instruments (docs/instrument-tracks.md), each of which can play one of a song's tracks.</summary>
    public enum Instrument { RhythmGuitar, Bass, Keys, Drums, LeadGuitar, Instrument1 }

    /// <summary>A track of a song, as a choice stores it: its place in the file and its name (either finds it after a reimport).</summary>
    public sealed class TrackRef
    {
        /// <summary>
        /// The track's <see cref="SongTrack.Index"/>; -1: no track, the instrument plays its default (generated) part (a
        /// lead has none: it plays nothing); <see cref="NoneIndex"/>: the instrument is left out of the band.
        /// </summary>
        public int Index { get; set; } = -1;
        public string Name { get; set; } = "";
        // (until 2026-11-01 the rhythm guitar's choice also said how it played its track: "picked" or "stringKeys"; it plays
        // every track one way now, and an older file's flags are passed over)

        public const int NoneIndex = -2;

        public static TrackRef Default => new TrackRef();
        /// <summary>The instrument is out of the band in this song: it plays no part, made up or from a track.</summary>
        public static TrackRef None => new TrackRef { Index = NoneIndex };
        public static TrackRef Of(SongTrack track) => track == null ? Default : new TrackRef { Index = track.Index, Name = track.Name };
        [JsonIgnore] public bool IsDefault => Index == -1;
        [JsonIgnore] public bool IsNone => Index == NoneIndex;
    }

    /// <summary>
    /// Which track each of the band's instruments plays in a song. What you choose is kept in a file next to the song
    /// (<c>wonderwall.band.json</c> beside <c>wonderwall.song.json</c>), so a new import of the song keeps it; an
    /// instrument you have not chosen for is connected automatically (<see cref="AutoConnect"/>), each time the song is
    /// opened. A track that doubles another (<see cref="SongTrack.SameAs"/>) plays the other's notes.
    /// </summary>
    public sealed class BandConnections
    {
        public const int CurrentVersion = 1;

        /// <summary>The ids of the instruments in a file and in the messages to the engine.</summary>
        public static readonly IReadOnlyDictionary<Instrument, string> Ids = new Dictionary<Instrument, string>
        {
            [Instrument.RhythmGuitar] = "rhythm", [Instrument.Bass] = "bass", [Instrument.Keys] = "keys",
            [Instrument.Drums] = "drums", [Instrument.LeadGuitar] = "lead", [Instrument.Instrument1] = "instrument1",
        };

        public static readonly IReadOnlyDictionary<Instrument, string> Names = new Dictionary<Instrument, string>
        {
            [Instrument.RhythmGuitar] = "Rhythm guitar", [Instrument.Bass] = "Bass", [Instrument.Keys] = "Keyboards",
            [Instrument.Drums] = "Drums", [Instrument.LeadGuitar] = "Lead guitar", [Instrument.Instrument1] = "Instrument 1",
        };

        public int Version { get; set; } = CurrentVersion;
        /// <summary>The choices made, by instrument id ("lead"); an instrument not here is connected automatically.</summary>
        public Dictionary<string, TrackRef> Chosen { get; set; } = new Dictionary<string, TrackRef>();

        public static Instrument? InstrumentOf(string id) =>
            Ids.Where(kv => kv.Value == id).Select(kv => (Instrument?)kv.Key).FirstOrDefault();

        /// <summary>Whether the instrument's track was chosen (else it is connected automatically).</summary>
        public bool IsChosen(Instrument instrument) => Chosen.ContainsKey(Ids[instrument]);

        public void Choose(Instrument instrument, TrackRef track) => Chosen[Ids[instrument]] = track ?? TrackRef.Default;

        /// <summary>
        /// Whether the instrument is left out of the band in this song (chosen "None"): it plays nothing, not even a part
        /// the band makes up. A lead with no track plays nothing either, but that is its default.
        /// </summary>
        public bool IsOut(Instrument instrument) => Chosen.TryGetValue(Ids[instrument], out var chosen) && chosen != null && chosen.IsNone;

        /// <summary>
        /// The track an instrument plays in the song (one with notes: a doubling track's twin), or null for its default
        /// part (or none, for an instrument that is out: <see cref="IsOut"/>): your choice if you made one and the track
        /// is still there, else the automatic connection.
        /// </summary>
        public SongTrack TrackFor(Song song, Instrument instrument)
        {
            if (Chosen.TryGetValue(Ids[instrument], out var chosen) && chosen != null)
            {
                if (chosen.IsDefault || chosen.IsNone)
                    return null;
                var track = Find(song, chosen);
                if (track != null)
                    return Played(song, track);
            }
            return AutoConnect(song).TryGetValue(instrument, out var auto) ? auto : null;
        }

        /// <summary>The track a choice names: the one with its place and name, else the one with its name.</summary>
        public static SongTrack Find(Song song, TrackRef track) =>
            song.Tracks.FirstOrDefault(t => t.Index == track.Index && t.Name == track.Name)
            ?? song.Tracks.FirstOrDefault(t => t.Name == track.Name);

        /// <summary>The track whose notes a track plays: itself, or the one it doubles.</summary>
        public static SongTrack Played(Song song, SongTrack track) =>
            track.SameAs == null ? track : song.Tracks.FirstOrDefault(t => t.Name == track.SameAs && t.SameAs == null) ?? track;

        /// <summary>
        /// A drums, bass or keyboard track that plays in fewer of the song's bars than this is not connected automatically:
        /// a part the band makes up is better than one that is silent most of the song (a piano in a few bars). The leads
        /// take a short track: a solo is short.
        /// </summary>
        public const double MinBarShare = 1.0 / 3;

        /// <summary>
        /// The best guess at what each instrument plays, from the tracks' kinds: the rhythm guitar the song's own tab's
        /// track (the strummed guitar the chords come from), unless the song's strumming is an Ultimate Guitar page's; the drums, bass and keyboards the busiest track of their kind
        /// that plays through enough of the song (<see cref="MinBarShare"/>); the lead guitar the busiest other guitar;
        /// instrument 1 a lead instrument (a harmonica, strings), else a vocal line. A track goes to one instrument only;
        /// an instrument with nothing fitting plays its default part. A song with no tracks (from Ultimate Guitar pages)
        /// connects nothing.
        /// </summary>
        public static Dictionary<Instrument, SongTrack> AutoConnect(Song song)
        {
            var result = new Dictionary<Instrument, SongTrack>();
            var free = song.Tracks.Where(t => t.SameAs == null && t.Notes.Count > 0).ToList();
            var bars = Bars(song);
            bool Through(SongTrack t) => bars > 0 && t.Notes.Select(n => song.Tempo.BarAt(n.AtQuarter).bar).Distinct().Count() >= MinBarShare * bars;
            SongTrack Take(Instrument instrument, Func<SongTrack, bool> fits)
            {
                var pick = free.Where(fits).OrderByDescending(t => t.Notes.Count).FirstOrDefault();
                if (pick != null)
                {
                    result[instrument] = pick;
                    free.Remove(pick);
                }
                return pick;
            }
            if (Take(Instrument.RhythmGuitar, t => song.Riffs.Any(r => r.Name == t.Name)) == null)
                Take(Instrument.RhythmGuitar, t => t.Kind == "guitar");
            // a song strummed as an Ultimate Guitar page has it (chosen at its import): the guitarist plays that, its
            // default part, and the strummed track is no other instrument's either
            if (song.Sources.Any(s => s.Kind == "ultimate-guitar-strumming"))
                result.Remove(Instrument.RhythmGuitar);
            Take(Instrument.Drums, t => t.Kind == "drums" && Through(t));
            Take(Instrument.Bass, t => t.Kind == "bass" && Through(t));
            Take(Instrument.Keys, t => t.Kind == "keys" && Through(t));
            Take(Instrument.LeadGuitar, t => t.Kind == "guitar");
            if (Take(Instrument.Instrument1, t => t.Kind == "other") == null)
                Take(Instrument.Instrument1, t => t.Kind == "voice");
            return result;
        }

        /// <summary>The song's bars: up to the end of its last section or chord (a song with neither: its last track note).</summary>
        static int Bars(Song song)
        {
            var end = song.Sections.Select(s => s.AtQuarter + s.Quarters)
                .Concat(song.Chords.Select(c => c.AtQuarter + c.Quarters))
                .DefaultIfEmpty(0).Max();
            if (end <= 0)
                end = song.Tracks.SelectMany(t => t.Notes).Select(n => n.AtQuarter + n.Quarters).DefaultIfEmpty(0).Max();
            return end > 0 ? song.Tempo.BarAt(Math.Max(0, end - 1e-6)).bar + 1 : 0;
        }

        // ------------------------------------------------------------ the file
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        /// <summary>The connections file of a song file: <c>x.band.json</c> beside <c>x.song.json</c>; null for none (the demo).</summary>
        public static string PathFor(string songPath)
        {
            if (string.IsNullOrEmpty(songPath))
                return null;
            var name = Path.GetFileName(songPath);
            var stem = name.EndsWith(".song.json", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - ".song.json".Length)
                     : Path.GetFileNameWithoutExtension(name);
            return Path.Combine(Path.GetDirectoryName(songPath) ?? "", stem + ".band.json");
        }

        public static string Write(BandConnections connections) => JsonConvert.SerializeObject(connections, Settings);

        public static BandConnections Read(string json)
        {
            var connections = JsonConvert.DeserializeObject<BandConnections>(json, Settings) ?? new BandConnections();
            if (connections.Version > CurrentVersion)
                throw new InvalidDataException($"band file format {connections.Version} is newer than this version understands ({CurrentVersion})");
            connections.Chosen = connections.Chosen ?? new Dictionary<string, TrackRef>();
            return connections;
        }

        /// <summary>The song's connections: its file's, or none chosen if it has none.</summary>
        public static BandConnections Load(string songPath)
        {
            var path = PathFor(songPath);
            return path != null && File.Exists(path) ? Read(File.ReadAllText(path)) : new BandConnections();
        }

        public void Save(string songPath)
        {
            var path = PathFor(songPath);
            if (path != null)
                File.WriteAllText(path, Write(this));
        }
    }
}
