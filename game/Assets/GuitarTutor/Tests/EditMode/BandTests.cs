using System.Linq;
using GuitarTutor.Core.Band;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Engine;
using GuitarTutor.Core.Music;
using GuitarTutor.Core.Playback;
using GuitarTutor.Core.Songs;
using NUnit.Framework;

namespace GuitarTutor.Core.Tests
{
    public class ChordTests
    {
        static int[] Of(string name) => Chord.Parse(name).Intervals.ToArray();

        [Test]
        public void Parse_ReadsCommonChordNames()
        {
            CollectionAssert.AreEqual(new[] { 0, 3, 7 }, Of("Bm"));
            Assert.AreEqual(11, Chord.Parse("Bm").Root);
            CollectionAssert.AreEqual(new[] { 0, 4, 7 }, Of("A"));
            CollectionAssert.AreEqual(new[] { 0, 3, 7, 10 }, Of("F#m7"));
            Assert.AreEqual(6, Chord.Parse("F#m7").Root);
            CollectionAssert.AreEqual(new[] { 0, 4, 7, 11 }, Of("Cmaj7"));
            CollectionAssert.AreEqual(new[] { 0, 4, 7, 10 }, Of("G7"));
            CollectionAssert.AreEqual(new[] { 0, 5, 7 }, Of("Dsus4"));
            CollectionAssert.AreEqual(new[] { 0, 2, 7 }, Of("Asus2"));
            CollectionAssert.AreEqual(new[] { 0, 3, 6, 10 }, Of("Am7b5"));
            CollectionAssert.AreEqual(new[] { 0, 7 }, Of("E5"));
            CollectionAssert.AreEqual(new[] { 0, 4, 7, 14 }, Of("Cadd9"));
            CollectionAssert.AreEqual(new[] { 0, 3, 6, 9 }, Of("Bdim7"));
            CollectionAssert.AreEqual(new[] { 0, 4, 8 }, Of("Caug"));
            CollectionAssert.AreEqual(new[] { 0, 4, 7, 9 }, Of("C6"));
            CollectionAssert.AreEqual(new[] { 0, 4, 7, 10, 14 }, Of("D9"));
            Assert.AreEqual(10, Chord.Parse("Bb").Root);
            Assert.IsTrue(Chord.Parse("Em").IsMinor);
        }

        [Test]
        public void Parse_SlashChordsKeepTheirBass()
        {
            var c = Chord.Parse("G/B");
            Assert.AreEqual(7, c.Root);
            Assert.AreEqual(11, c.Bass);
            Assert.AreEqual(2, Chord.Parse("D").Bass);
        }

        [Test]
        public void Parse_RefusesWhatIsNotAChord()
        {
            Assert.IsFalse(Chord.TryParse("Verse", out _));
            Assert.IsFalse(Chord.TryParse("N.C.", out _));
            Assert.IsFalse(Chord.TryParse("", out _));
        }
    }

    public class ArrangerTests
    {
        static Arrangement Demo(string style = "Ballad") =>
            Arranger.Arrange(new SongChart(DemoSong.Create()), BandStyle.Named(style));

        [Test]
        public void Drums_PlayEveryBarWithCrashesAndFills()
        {
            var a = Demo();
            var drums = a.Part(BandPart.Drums).ToList();
            // a crash on each section's first bar (6 sections) and on the last bar
            Assert.AreEqual(7, drums.Count(n => n.Pitch == Drum.Crash));
            Assert.IsTrue(drums.Any(n => n.Pitch == Drum.Crash && n.AtQuarter == 16));  // the verse
            // the intro and verse are quiet: side stick; the chorus has the snare
            Assert.IsTrue(drums.Any(n => n.Pitch == Drum.SideStick && n.AtQuarter == 17));
            Assert.IsTrue(drums.Any(n => n.Pitch == Drum.Snare && n.AtQuarter == 49));
            // a fill in the last beat before the verse: four sixteenths from 15, no hats there
            CollectionAssert.AreEqual(new[] { 15, 15.25, 15.5, 15.75 },
                drums.Where(n => n.AtQuarter >= 15 && n.AtQuarter < 16 && n.Pitch != Drum.Kick).Select(n => n.AtQuarter));
            // every bar has a kick on its first beat
            for (int bar = 0; bar < 36; ++bar)
                Assert.IsTrue(drums.Any(n => n.Pitch == Drum.Kick && n.AtQuarter == bar * 4), "bar " + bar);
        }

        [Test]
        public void Bass_PlaysRootsInRangeAndWalksIntoChanges()
        {
            var bass = Demo().Part(BandPart.Bass).ToList();
            Assert.IsTrue(bass.All(n => n.Pitch >= Arranger.BassLow - 1 && n.Pitch <= Arranger.BassHigh + 8));
            Assert.AreEqual(31, bass.First().Pitch);  // G1, the first chord's root
            // bar 0 is G, bar 1 is D (D2 is 38): beat 4 of bar 0 walks in from a half step below
            Assert.AreEqual(37, bass.First(n => n.AtQuarter == 3).Pitch);
            // bars 10 and 11 are one C: no walk in the middle of it
            Assert.AreEqual(36, bass.First(n => n.AtQuarter == 43).Pitch);
            // notes never last past their chord
            Assert.IsTrue(bass.All(n => n.Quarters > 0 && n.Quarters <= 4));
        }

        [Test]
        public void Keys_VoiceChordsCloseAndSmoothly()
        {
            var keys = Demo().Part(BandPart.Keys).ToList();
            Assert.IsTrue(keys.All(n => n.Pitch >= Arranger.KeysLow && n.Pitch <= Arranger.KeysHigh));
            var first = keys.Where(n => n.AtQuarter == 0).Select(n => n.Pitch % 12).OrderBy(p => p).ToArray();
            CollectionAssert.AreEqual(new[] { 2, 7, 11 }, first);  // G B D
            // from one chord to the next, no note jumps more than a fifth
            var times = keys.Select(n => n.AtQuarter).Distinct().OrderBy(t => t).ToList();
            for (int i = 1; i < times.Count; ++i)
            {
                var a = keys.Where(n => n.AtQuarter == times[i - 1]).Select(n => n.Pitch).ToList();
                var b = keys.Where(n => n.AtQuarter == times[i]).Select(n => n.Pitch).ToList();
                Assert.IsTrue(b.All(x => a.Min(y => System.Math.Abs(x - y)) <= 7), "at " + times[i]);
            }
        }

        [Test]
        public void Arrangement_FollowsChordChangesInsideABar()
        {
            var song = DemoSong.Create();
            song.Chords.Clear();
            song.Chords.Add(new ChordEvent { Name = "C", AtQuarter = 0, Quarters = 3 });
            song.Chords.Add(new ChordEvent { Name = "F", AtQuarter = 3, Quarters = 1 });
            song.Chords.Add(new ChordEvent { Name = "G", AtQuarter = 4, Quarters = 4 });
            song.Sections.Clear();
            song.Lyrics.Clear();
            song.Riffs.Clear();
            var a = Arranger.Arrange(new SongChart(song), BandStyle.Named("Ballad"));
            var bassAt3 = a.Part(BandPart.Bass).Single(n => n.AtQuarter == 3);
            Assert.AreEqual(29, bassAt3.Pitch);  // F1: the change, not a walk
            Assert.IsTrue(a.Part(BandPart.Keys).Any(n => n.AtQuarter == 3 && n.Pitch % 12 == 5));
            Assert.IsTrue(a.Part(BandPart.Keys).Where(n => n.AtQuarter == 2).All(n => n.AtQuarter + n.Quarters <= 3 + 1e-9));
        }

        [Test]
        public void Styles_AllArrangeTheDemo()
        {
            foreach (var style in BandStyle.All)
            {
                var a = Demo(style.Name);
                Assert.AreEqual(style.Name, a.Style);
                Assert.Greater(a.Part(BandPart.Drums).Count(), 36 * 8);
                Assert.Greater(a.Part(BandPart.Bass).Count(), 36);
                Assert.Greater(a.Part(BandPart.Keys).Count(), 36 * 3);
            }
            Assert.AreEqual("Ballad", Arranger.DefaultStyle(DemoSong.Create()).Name);
        }
    }

    public class EngineProtocolTests
    {
        [Test]
        public void Load_ListsNotesCompactly()
        {
            var song = DemoSong.Create();
            var a = Arranger.Arrange(new SongChart(song));
            var line = EngineProtocol.Load(song.Info.Title, song.Tempo, a, "2 - E_PIANO/1 - EP-Mark I");
            StringAssert.DoesNotContain("\n", line);
            var (type, message) = EngineProtocol.Read(line);
            Assert.AreEqual("load", type);
            Assert.AreEqual(a.Notes.Count, message["notes"].Count());
            Assert.AreEqual(96.0, (double)message["tempos"][0][1]);
            var first = message["notes"][0];
            Assert.AreEqual(5, first.Count());
            Assert.IsNull(EngineProtocol.Read("not json").type);
        }

        [Test]
        public void TransportMessages_CarryTheirValues()
        {
            var (type, play) = EngineProtocol.Read(EngineProtocol.Play(16, 4));
            Assert.AreEqual("play", type);
            Assert.AreEqual(16.0, (double)play["from"]);
            Assert.AreEqual(4.0, (double)play["countIn"]);
            var (_, mix) = EngineProtocol.Read(EngineProtocol.Mix(BandPart.Keys, 0.45));
            Assert.AreEqual("keys", (string)mix["part"]);
            Assert.AreEqual(0.45, (double)mix["gain"], 1e-9);
            Assert.AreEqual("seek", EngineProtocol.Read(EngineProtocol.Seek(8)).type);
            // every style names a keyboard sound and a level for it
            Assert.IsTrue(BandStyle.All.All(st => st.KeysSound.Contains("/") && st.KeysGain > 0));
        }

        [Test]
        public void Clock_WorksOutThePositionNow()
        {
            var clock = new ClockSnapshot(8, 1000, 2, true, double.NaN, 1000);
            Assert.AreEqual(9, clock.QuarterAt(1500), 1e-9);
            var stopped = new ClockSnapshot(8, 1000, 0, false, double.NaN, 1000);
            Assert.AreEqual(8, stopped.QuarterAt(5000));
        }

        [Test]
        public void LocalTransport_CountsInThenPlays()
        {
            double now = 0;
            var t = new LocalTransport(new TempoMap(120, TimeSignature.FourFour), () => now);
            t.Play(8, 4);
            Assert.AreEqual(4, t.Quarter, 1e-9);
            Assert.AreEqual(8, t.CountInUntil);
            now = 2.5;  // 5 quarters later
            Assert.AreEqual(9, t.Quarter, 1e-9);
            Assert.IsTrue(double.IsNaN(t.CountInUntil));
            t.Pause();
            Assert.IsFalse(t.Playing);
        }
    }
}
