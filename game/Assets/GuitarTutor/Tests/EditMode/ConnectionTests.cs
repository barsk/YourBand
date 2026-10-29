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
            song.Tracks.Add(Track(5, "Bass", "bass", 60));  // a note a quarter: through 15 of the song's 36 bars
            song.Tracks.Add(Track(6, "Drumkit", "drums", 90));
            song.Tracks.Add(Track(7, "Piano", "keys", 8));  // two bars only
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
            Assert.IsFalse(auto.ContainsKey(Instrument.Keys));  // the piano plays in two bars: the keys play their part
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
        public void DrumsAndBass_PlayTheirTracksInsteadOfMadeUpParts()
        {
            var song = GuitarProSong();
            song.Tracks[5].Notes[1].Technique = "x";  // a dead bass note: not played
            var chart = new SongChart(song);
            var madeUp = Arranger.Arrange(chart, BandStyle.Named("Ballad"));
            var connections = new BandConnections();
            var a = Arranger.Arrange(chart, BandStyle.Named("Ballad"), connections);
            CollectionAssert.AreEqual(song.Tracks[6].Notes.Select(n => (n.Pitch, n.AtQuarter, n.Velocity)),
                                      a.Part(BandPart.Drums).Select(n => (n.Pitch, n.AtQuarter, n.Velocity)));
            Assert.AreEqual(59, a.Part(BandPart.Bass).Count());
            // the piano is too short to be connected automatically: the keys make up their part as before (the same notes; their velocities vary a little differently)
            // (a chord's notes in pitch order: the arrangement's sort by time leaves their order open)
            CollectionAssert.AreEqual(madeUp.Part(BandPart.Keys).Select(n => (n.AtQuarter, n.Pitch, n.Quarters)).OrderBy(k => k),
                                      a.Part(BandPart.Keys).Select(n => (n.AtQuarter, n.Pitch, n.Quarters)).OrderBy(k => k));
        }

        [Test]
        public void TheRhythmGuitar_StrumsWhereItsTrackStrumsAndPicksWhereItPicks()
        {
            var song = GuitarProSong();
            var rhythm = song.Tracks[3];  // Guitar 2, the song's own tab's track
            rhythm.Notes.Clear();
            void Strike(double at, int[] strings, string technique = "")
            {
                foreach (var s in strings)
                    rhythm.Notes.Add(new TrackNote { AtQuarter = at, Quarters = 0.5, Pitch = 40 + 5 * s, StringIndex = s, Fret = 0, Velocity = 95, Technique = technique });
            }
            var top = new[] { 2, 3, 4, 5 };
            Strike(0, top);
            Strike(0.5, top);
            Strike(1, top);
            Strike(1.5, top, "x");  // dead strings: a muted stroke
            foreach (var at in new[] { 2.0, 2.5, 3.0 })  // picked, one string at a time
                rhythm.Notes.Add(new TrackNote { AtQuarter = at, Quarters = 0.5, Pitch = 62, StringIndex = 3, Fret = 7, Velocity = 79 });
            Strike(3.5, new[] { 0, 1 });  // two strings: a stroke too (a bass note and a pinch)
            Strike(4, top, "U");  // the file records an upstroke
            var connections = new BandConnections();  // (strummed as chosen: four strings at most would read as fingerpicked)
            connections.Choose(Instrument.RhythmGuitar, TrackRef.Of(rhythm, picked: false));
            var a = Arranger.Arrange(new SongChart(song), BandStyle.Named("Ballad"), connections);

            CollectionAssert.AreEqual(new[] { (0.0, false, false), (0.5, true, false), (1.0, false, false), (1.5, true, true), (3.5, true, false), (4.0, true, false) },
                                      a.Strokes.Select(s => (s.AtQuarter, s.Up, s.Muted)));  // down on the beat, up off it
            CollectionAssert.AreEqual(new[] { (2.0, 3.5) }, a.SingleSpans);  // the picked stretch: no strumming over it
            var guitar = a.Part(BandPart.Guitar).ToList();
            Assert.AreEqual(3, guitar.Count(n => n.Single));
            Assert.AreEqual(18, guitar.Count(n => !n.Single));  // the struck strings (the dead strum sounds none)
            // one held chord per chord of the chart, on the strings the track strums in it
            var strums = new[] { 0.0, 0.5, 1.0, 3.5, 4.0 };
            var chords = song.Chords.Where(c => strums.Any(s => s >= c.AtQuarter && s < c.AtQuarter + c.Quarters)).ToList();
            CollectionAssert.AreEqual(chords.Select(c => (c.AtQuarter, c.Quarters)), a.Voicings.Select(v => (v.AtQuarter, v.Quarters)).Distinct());
            foreach (var c in chords)
            {
                var strings = rhythm.Notes.Where(n => !n.Technique.Contains("x") && n.AtQuarter >= c.AtQuarter && n.AtQuarter < c.AtQuarter + c.Quarters
                                                      && strums.Contains(n.AtQuarter)).Select(n => n.StringIndex).Distinct().Count();
                Assert.AreEqual(strings, a.Voicings.Count(v => v.AtQuarter == c.AtQuarter));
            }
        }

        [Test]
        public void AFingerpickedTrack_IsPlayedNoteByNote()
        {
            var song = GuitarProSong();
            var rhythm = song.Tracks[3];
            rhythm.Notes.Clear();
            // Redbud Tree's way: a bass note, then a pinch on the top strings; never five strings at once
            for (int bar = 0; bar < 8; ++bar)
            {
                var at = bar * 4.0;
                rhythm.Notes.Add(new TrackNote { AtQuarter = at, Quarters = 0.5, Pitch = 45, StringIndex = 1, Fret = 0, Velocity = 90, Technique = "r" });
                foreach (var s in new[] { 3, 4, 5 })
                    rhythm.Notes.Add(new TrackNote { AtQuarter = at + 0.5, Quarters = 0.5, Pitch = 50 + 5 * s, StringIndex = s, Fret = 0, Velocity = 80 });
                rhythm.Notes.Add(new TrackNote { AtQuarter = at + 1, Quarters = 0.5, Pitch = 40, StringIndex = 0, Fret = 0, Velocity = 80, Technique = "x" });  // dead
            }
            Assert.IsTrue(BandConnections.FingerPicked(rhythm));
            var connections = new BandConnections();
            Assert.IsTrue(connections.PlaysPicked(song, Instrument.RhythmGuitar));
            var a = Arranger.Arrange(new SongChart(song), BandStyle.Named("Ballad"), connections);
            Assert.AreEqual(0, a.Strokes.Count);  // no strumming: the guitar stays out of its strumming mode
            Assert.AreEqual(0, a.Voicings.Count);
            Assert.AreEqual(0, a.SingleSpans.Count);
            var guitar = a.Part(BandPart.Guitar).ToList();
            Assert.AreEqual(32, guitar.Count);  // every note but the dead ones
            Assert.IsTrue(guitar.All(n => !n.Single));
            var bass = guitar.Where(n => n.Pitch == 45).OrderBy(n => n.AtQuarter).ToList();
            Assert.AreEqual(3.99, bass[0].Quarters, 1e-6);  // let ring: until the next note on its string, a bar on

            // chosen strummed after all: the strokes are back
            connections.Choose(Instrument.RhythmGuitar, TrackRef.Of(rhythm, picked: false));
            Assert.IsFalse(connections.PlaysPicked(song, Instrument.RhythmGuitar));
            Assert.AreEqual(8, Arranger.Arrange(new SongChart(song), BandStyle.Named("Ballad"), connections).Strokes.Count);  // a pinch a bar
            Assert.IsFalse(BandConnections.FingerPicked(song.Tracks[2]));  // (no tab: not a fingerpicked guitar)
        }

        [Test]
        public void ASongStrummedFromUltimateGuitar_KeepsItsGuitaristOnThatStrumming()
        {
            var song = GuitarProSong();
            song.Sources.Add(new Source { Kind = "ultimate-guitar-strumming", Url = "heart-of-gold.html" });
            var auto = BandConnections.AutoConnect(song);
            Assert.IsFalse(auto.ContainsKey(Instrument.RhythmGuitar));
            Assert.AreEqual("Guitar 1", auto[Instrument.LeadGuitar].Name);  // the strummed track is no lead's either
        }

        [Test]
        public void EveryInstrumentOnDefault_PlaysAsBeforeThereWereTracks()
        {
            var song = GuitarProSong();
            var chart = new SongChart(song);
            var connections = new BandConnections();
            foreach (Instrument i in System.Enum.GetValues(typeof(Instrument)))
                connections.Choose(i, TrackRef.Default);
            var before = Arranger.Arrange(chart, BandStyle.Named("Ballad"));
            var now = Arranger.Arrange(chart, BandStyle.Named("Ballad"), connections);
            CollectionAssert.AreEqual(before.Notes.Where(n => n.Part != BandPart.Lead1 && n.Part != BandPart.Lead2).Select(n => n.ToString()),
                                      now.Notes.Select(n => n.ToString()));
            CollectionAssert.AreEqual(before.Strokes.Select(s => (s.AtQuarter, s.Up, s.Velocity)), now.Strokes.Select(s => (s.AtQuarter, s.Up, s.Velocity)));
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
            Assert.AreEqual(7, tracks.Count);  // the doubling guitar is one choice with its twin
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
