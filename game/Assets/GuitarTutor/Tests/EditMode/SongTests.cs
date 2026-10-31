using System.IO;
using System.Linq;
using GuitarTutor.Core.Music;
using GuitarTutor.Core.Songs;
using NUnit.Framework;

namespace GuitarTutor.Core.Tests
{
    public class SongTests
    {
        /// <summary>A few bars shaped like the Wicked Game chart: Bm A E E, with a lyric line and the Bm shape.</summary>
        static Song Example()
        {
            var song = new Song
            {
                Info = { Title = "Example", Artist = "Test", Key = "Bm" },
                Tempo = new TempoMap(112, TimeSignature.FourFour),
            };
            song.Sections.Add(new Section { Name = "Verse 1", AtQuarter = 0, Quarters = 16 });
            song.Chords.Add(new ChordEvent { Name = "Bm", AtQuarter = 0, Quarters = 4, Voicing = "Bm-x24432" });
            song.Chords.Add(new ChordEvent { Name = "A", AtQuarter = 4, Quarters = 4 });
            song.Chords.Add(new ChordEvent { Name = "E", AtQuarter = 8, Quarters = 8 });
            song.Lyrics.Add(new LyricLine
            {
                AtQuarter = 0,
                Words = { new LyricWord { Text = "la", AtQuarter = 0 }, new LyricWord { Text = "la", AtQuarter = 6 } },
            });
            song.Strumming.Add(new StrumPattern
            {
                Name = "Whole song",
                Strokes = { Stroke.Down, Stroke.Rest, Stroke.Down, Stroke.Up, Stroke.Rest, Stroke.Up, Stroke.Down, Stroke.Up },
            });
            song.Voicings.Add(new Voicing
            {
                Id = "Bm-x24432", Chord = "Bm",
                Frets = { -1, 2, 4, 4, 3, 2 }, Fingers = { 0, 1, 3, 4, 2, 1 },
                Barre = new Barre { Fret = 2, Finger = 1, FirstString = 1, LastString = 5 },
            });
            song.Sources.Add(new Source { Kind = "test", Supplies = { "chords", "lyrics" } });
            return song;
        }

        [Test]
        public void ChordAt_FindsTheSoundingChord()
        {
            var song = Example();
            Assert.AreEqual("Bm", song.ChordAt(0).Name);
            Assert.AreEqual("A", song.ChordAt(7.9).Name);
            Assert.AreEqual("E", song.ChordAt(15).Name);
            Assert.IsNull(song.ChordAt(16));
        }

        [Test]
        public void ChordsInBar_IncludesChordsCarriedOver()
        {
            var song = Example();
            CollectionAssert.AreEqual(new[] { "E" }, song.ChordsInBar(3).Select(c => c.Name));  // E started in bar 2
            CollectionAssert.AreEqual(new[] { "A" }, song.ChordsInBar(1).Select(c => c.Name));
        }

        [Test]
        public void VoicingFor_ByIdThenByChordName()
        {
            var song = Example();
            Assert.AreEqual("Bm-x24432", song.VoicingFor(song.Chords[0]).Id);
            Assert.IsNull(song.VoicingFor(song.Chords[1]));
        }

        [Test]
        public void Json_RoundTripKeepsEverything()
        {
            var song = Example();
            var json = SongJson.Write(song);
            StringAssert.Contains("\"strokes\": [", json);
            StringAssert.Contains("\"down\"", json);  // enums as words
            StringAssert.DoesNotContain("\"text\": \"la la\"", json);  // computed text is not stored
            var back = SongJson.Read(json);
            Assert.AreEqual(json, SongJson.Write(back));
            Assert.AreEqual(1, back.Tempo.Tempos.Count);  // the file's tempo replaces the default, not adds to it
            Assert.AreEqual(112, back.Tempo.BpmAt(0));
            Assert.AreEqual("la la", back.Lyrics[0].Text);
        }

        [Test]
        public void Json_KeepsATrackNotesTechniqueShapes()
        {
            var song = Example();
            var track = new SongTrack { Index = 0, Name = "Lead", Kind = "guitar" };
            track.Notes.Add(new TrackNote
            {
                AtQuarter = 1, Quarters = 2, Pitch = 67, StringIndex = 3, Fret = 2, Technique = "b~s", Bend = 2,
                BendPoints = new System.Collections.Generic.List<double[]> { new[] { 0.0, 0.0 }, new[] { 0.25, 2.0 }, new[] { 0.5, 0.0 } },
                Vibrato = "wide", SlideOut = "legato", SlideIn = "below", TremoloQuarters = 0.25,
            });
            track.Notes.Add(new TrackNote { AtQuarter = 3, Quarters = 1, Pitch = 69, StringIndex = 3, Fret = 4 });  // plainly
            song.Tracks.Add(track);
            var json = SongJson.Write(song);
            var back = SongJson.Read(json).Tracks[0].Notes;
            CollectionAssert.AreEqual(new[] { 0.0, 0.0, 0.25, 2.0, 0.5, 0.0 }, back[0].BendPoints.SelectMany(p => p));
            Assert.AreEqual(("wide", "legato", "below", 0.25), (back[0].Vibrato, back[0].SlideOut, back[0].SlideIn, back[0].TremoloQuarters));
            // a plain note writes none of them
            Assert.IsNull(back[1].BendPoints);
            Assert.AreEqual(("", "", "", 0.0), (back[1].Vibrato, back[1].SlideOut, back[1].SlideIn, back[1].TremoloQuarters));
            StringAssert.DoesNotContain("\"vibrato\": \"\"", json);
        }

        [Test]
        public void Json_RefusesANewerFormat()
        {
            var json = SongJson.Write(Example()).Replace("\"version\": 1", "\"version\": 99");
            Assert.Throws<InvalidDataException>(() => SongJson.Read(json));
        }
    }
}
