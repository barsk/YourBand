using System.IO;
using System.Linq;
using GuitarTutor.Core.Band;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Engine;
using GuitarTutor.Core.Songs;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GuitarTutor.Core.Tests
{
    public class ConnectionTests
    {
        static SongTrack Track(int index, string name, string kind, int notes, string sameAs = null)
        {
            var track = new SongTrack { Index = index, Name = name, Kind = kind, SameAs = sameAs };
            if (sameAs == null)
                for (int i = 0; i < notes; ++i)
                    track.Notes.Add(new TrackNote { AtQuarter = i, Quarters = 1, Pitch = 60 + i % 12 });
            return track;
        }

        /// <summary>A Guitar Pro song as Heart of Gold's file has it: the strummed guitar is the song's own tab.</summary>
        static Song GuitarProSong()
        {
            var song = DemoSong.Create();
            song.Riffs.Clear();
            song.Riffs.Add(new Riff { Name = "Guitar 2" });
            song.Tracks.Add(Track(0, "Vocal", "voice", 30));
            song.Tracks.Add(Track(1, "Harmonica", "other", 20));
            song.Tracks.Add(Track(2, "Guitar 1", "guitar", 25));
            song.Tracks.Add(Track(3, "Guitar 2", "guitar", 300));
            song.Tracks.Add(Track(4, "Guitar 3", "guitar", 0, sameAs: "Guitar 2"));
            song.Tracks.Add(Track(5, "Bass", "bass", 40));
            song.Tracks.Add(Track(6, "Drumkit", "drums", 90));
            return song;
        }

        [Test]
        public void AutoConnect_GivesEachInstrumentTheTrackOfItsKind()
        {
            var auto = BandConnections.AutoConnect(GuitarProSong());
            Assert.AreEqual("Guitar 2", auto[Instrument.RhythmGuitar].Name);  // the song's own tab, though another guitar is busier elsewhere
            Assert.AreEqual("Bass", auto[Instrument.Bass].Name);
            Assert.AreEqual("Drumkit", auto[Instrument.Drums].Name);
            Assert.AreEqual("Guitar 1", auto[Instrument.LeadGuitar].Name);
            Assert.AreEqual("Harmonica", auto[Instrument.Instrument1].Name);  // a lead instrument before the vocal
            Assert.IsFalse(auto.ContainsKey(Instrument.Keys));  // no keyboard track: the keys play their part
            Assert.AreEqual(0, BandConnections.AutoConnect(DemoSong.Create()).Count);  // no tracks (an Ultimate Guitar song)
        }

        [Test]
        public void TrackFor_TakesAChoiceOverTheAutomaticOne()
        {
            var song = GuitarProSong();
            var connections = new BandConnections();
            Assert.AreEqual("Harmonica", connections.TrackFor(song, Instrument.Instrument1).Name);
            connections.Choose(Instrument.Instrument1, TrackRef.Of(song.Tracks[0]));  // the vocal
            Assert.AreEqual("Vocal", connections.TrackFor(song, Instrument.Instrument1).Name);
            connections.Choose(Instrument.Drums, TrackRef.Default);
            Assert.IsNull(connections.TrackFor(song, Instrument.Drums));  // its default (generated) part
            // a doubling track plays its twin's notes
            connections.Choose(Instrument.RhythmGuitar, TrackRef.Of(song.Tracks[4]));
            Assert.AreEqual("Guitar 2", connections.TrackFor(song, Instrument.RhythmGuitar).Name);
            // a new import moved the harmonica: it is found by its name
            connections.Choose(Instrument.LeadGuitar, new TrackRef { Index = 9, Name = "Harmonica" });
            Assert.AreEqual("Harmonica", connections.TrackFor(song, Instrument.LeadGuitar).Name);
            // a track no longer there: connected automatically again
            connections.Choose(Instrument.Bass, new TrackRef { Index = 5, Name = "Fretless" });
            Assert.AreEqual("Bass", connections.TrackFor(song, Instrument.Bass).Name);
        }

        [Test]
        public void TheBandFile_IsNextToTheSongAndKeepsTheChoices()
        {
            Assert.AreEqual(Path.Combine("songs", "wonderwall.band.json"), BandConnections.PathFor(Path.Combine("songs", "wonderwall.song.json")));
            Assert.IsNull(BandConnections.PathFor(null));
            var connections = new BandConnections();
            connections.Choose(Instrument.Instrument1, new TrackRef { Index = 0, Name = "Vocal" });
            connections.Choose(Instrument.Drums, TrackRef.Default);
            var json = BandConnections.Write(connections);
            StringAssert.Contains("\"instrument1\"", json);
            var read = BandConnections.Read(json);
            Assert.AreEqual("Vocal", read.Chosen["instrument1"].Name);
            Assert.IsTrue(read.Chosen["drums"].IsDefault);
            Assert.IsTrue(read.IsChosen(Instrument.Drums));
            Assert.IsFalse(read.IsChosen(Instrument.Bass));
        }

        [Test]
        public void Leads_PlayTheirConnectedTracks()
        {
            var song = GuitarProSong();
            song.Tracks[1].Notes[0].Technique = "x";  // a dead note on the harmonica: not played
            var chart = new SongChart(song);
            var connections = new BandConnections();
            var a = Arranger.Arrange(chart, BandStyle.Named("Ballad"), connections);
            Assert.AreEqual(25, a.Part(BandPart.Lead1).Count());  // Guitar 1
            Assert.AreEqual(19, a.Part(BandPart.Lead2).Count());  // the harmonica, but its dead note
            CollectionAssert.AreEqual(new[] { "Guitar 1", "Harmonica" }, a.LeadNames);
            connections.Choose(Instrument.LeadGuitar, TrackRef.Default);
            var none = Arranger.Arrange(chart, BandStyle.Named("Ballad"), connections);
            Assert.AreEqual(0, none.Part(BandPart.Lead1).Count());  // the band makes up no lead part
            Assert.AreEqual("", none.LeadNames[0]);
        }

        [Test]
        public void TheEngine_IsToldTheTracksAndWhatEachInstrumentPlays()
        {
            var song = GuitarProSong();
            var connections = new BandConnections();
            connections.Choose(Instrument.Drums, TrackRef.Default);
            var (type, message) = EngineProtocol.Read(EngineProtocol.Tracks(song, connections));
            Assert.AreEqual("tracks", type);
            var tracks = (JArray)message["tracks"];
            Assert.AreEqual(6, tracks.Count);  // the doubling guitar is one choice with its twin
            Assert.AreEqual("Guitar 2 = Guitar 3 (guitar, 300 notes)", (string)tracks[3]["name"]);
            var instruments = ((JArray)message["instruments"]).ToDictionary(i => (string)i["id"]);
            Assert.AreEqual(6, instruments.Count);
            Assert.AreEqual(3, (int)instruments["rhythm"]["track"]);
            Assert.AreEqual(-1, (int)instruments["drums"]["track"]);
            Assert.IsTrue((bool)instruments["drums"]["chosen"]);
            Assert.IsFalse((bool)instruments["bass"]["chosen"]);
        }
    }
}
