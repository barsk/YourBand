using System.Linq;
using GuitarTutor.Core.Band;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Performance;
using GuitarTutor.Core.Songs;
using NUnit.Framework;

namespace GuitarTutor.Core.Tests
{
    public class DrumPerformanceTests
    {
        static System.Collections.Generic.List<DrumStroke> Demo(string style = "Rock") =>
            DrumPerformance.Plan(Arranger.Arrange(new SongChart(DemoSong.Create()), BandStyle.Named(style)).Notes);

        [Test]
        public void Groove_HatsRightSnareLeftKickRightFoot()
        {
            var strokes = Demo();
            // bar 2 (quarters 4-8): eighth hats with the right hand, the backbeat with the left, the kick with the right foot
            var bar = strokes.Where(s => s.AtQuarter >= 4 && s.AtQuarter < 8).ToList();
            Assert.IsTrue(bar.Where(s => s.Piece == KitPiece.HiHat).All(s => s.Limb == Limb.RightHand));
            Assert.AreEqual(8, bar.Count(s => s.Piece == KitPiece.HiHat));
            CollectionAssert.AreEqual(new[] { 5.0, 7.0 }, bar.Where(s => s.Piece == KitPiece.Snare).Select(s => s.AtQuarter));
            Assert.IsTrue(bar.Where(s => s.Piece == KitPiece.Snare).All(s => s.Limb == Limb.LeftHand));
            Assert.IsTrue(bar.Where(s => s.Piece == KitPiece.Kick).All(s => s.Limb == Limb.RightFoot));
        }

        [Test]
        public void Fill_GoesHandToHand()
        {
            // the fill into the verse: four sixteenths from quarter 15 (snare, snare, high tom, low tom)
            var fill = Demo().Where(s => s.AtQuarter >= 15 && s.AtQuarter < 16 && s.Limb != Limb.RightFoot).ToList();
            CollectionAssert.AreEqual(new[] { KitPiece.Snare, KitPiece.Snare, KitPiece.Tom1, KitPiece.Tom2 }, fill.Select(s => s.Piece));
            CollectionAssert.AreEqual(new[] { Limb.LeftHand, Limb.RightHand, Limb.LeftHand, Limb.RightHand }, fill.Select(s => s.Limb));
        }

        [Test]
        public void NoHand_PlaysTwoThingsAtOnceOrFasterThanSixteenths()
        {
            foreach (var style in BandStyle.All)
            {
                var strokes = Demo(style.Name);
                foreach (var hand in new[] { Limb.RightHand, Limb.LeftHand })
                {
                    var times = strokes.Where(s => s.Limb == hand).Select(s => s.AtQuarter).ToList();
                    for (int i = 1; i < times.Count; ++i)
                        Assert.GreaterOrEqual(times[i] - times[i - 1], 0.25 - 1e-6, $"{style.Name} {hand} at {times[i]}");
                }
                // every crash is played by a hand
                Assert.AreEqual(7, strokes.Count(s => s.Piece == KitPiece.Crash), style.Name);
            }
        }
    }

    public class GuitarPerformanceTests
    {
        [Test]
        public void FingersFor_FingersCommonShapesAsGuitaristsDo()
        {
            // D (xx0232): index on G, ring on B, middle on high E
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 1, 3, 2 }, GuitarPerformance.FingersFor(new[] { -1, -1, 0, 2, 3, 2 }));
            // G (320003): middle on low E, index on A, ring on high E
            CollectionAssert.AreEqual(new[] { 2, 1, 0, 0, 0, 3 }, GuitarPerformance.FingersFor(new[] { 3, 2, 0, 0, 0, 3 }));
            // E (022100): middle, ring, index
            CollectionAssert.AreEqual(new[] { 0, 2, 3, 1, 0, 0 }, GuitarPerformance.FingersFor(new[] { 0, 2, 2, 1, 0, 0 }));
            // F#m barre (244222): the index across, ring and little on A and D
            CollectionAssert.AreEqual(new[] { 1, 3, 4, 1, 1, 1 }, GuitarPerformance.FingersFor(new[] { 2, 4, 4, 2, 2, 2 }));
        }

        [Test]
        public void Grips_FollowTheChartsShapes()
        {
            var chart = new SongChart(DemoSong.Create());
            var grips = GuitarPerformance.Grips(chart);
            Assert.AreEqual(chart.Song.Chords.Count, grips.Count);
            var g = grips.First(x => x.Chord == "G");
            CollectionAssert.AreEqual(new[] { 3, 2, 0, 0, 0, 3 }, g.Frets);
            Assert.AreEqual(2, g.Position);
            var bm = grips.First(x => x.Chord == "Bm");
            Assert.IsNotNull(bm.Barre, "Bm is a barre chord");
            Assert.AreEqual(2, bm.Barre.Value.fret);
        }

        [Test]
        public void Strums_ReadTheStrokesBackFromTheNotes()
        {
            var chart = new SongChart(DemoSong.Create());
            var a = Arranger.Arrange(chart, BandStyle.Named("Ballad"));
            var strums = GuitarPerformance.Strums(chart, a.Notes);
            // the intro's D-DU-UDU on G: down 0, down 1, up 1.5, up 2.5, down 3, up 3.5
            var bar = strums.Where(s => s.AtQuarter < 4).ToList();
            CollectionAssert.AreEqual(new[] { 0, 1, 1.5, 2.5, 3, 3.5 }, bar.Select(s => System.Math.Round(s.AtQuarter * 2) / 2));
            CollectionAssert.AreEqual(new[] { true, true, false, false, true, false }, bar.Select(s => s.Down));
            Assert.AreEqual(0, bar[0].LowString);  // G's downstroke from the low E
            Assert.AreEqual(5, bar[0].HighString);
            Assert.AreEqual(2, bar[2].LowString);  // the upstroke takes the top four
        }

        [Test]
        public void Bass_KeepsTheHandStill()
        {
            var a = Arranger.Arrange(new SongChart(DemoSong.Create()), BandStyle.Named("Rock"));
            var bass = BassPerformance.Plan(a.Notes);
            Assert.AreEqual(a.Part(BandPart.Bass).Count(), bass.Count);
            Assert.IsTrue(bass.All(n => n.Fret >= 0 && n.Fret <= 12));
            // each note sounds as arranged
            foreach (var (n, b) in a.Part(BandPart.Bass).OrderBy(n => n.AtQuarter).Zip(bass, (n, b) => (n, b)))
                Assert.AreEqual(n.Pitch, BassPerformance.Open[b.String] + b.Fret);
            // G1 (31) is the low E's 3rd fret, not an open string
            Assert.AreEqual((0, 3), (bass[0].String, bass[0].Fret));
        }

        [Test]
        public void Keys_RightHandTakesTheChord()
        {
            var a = Arranger.Arrange(new SongChart(DemoSong.Create()), BandStyle.Named("Ballad"));
            var presses = KeysPerformance.Plan(a.Notes);
            var first = presses.Where(p => p.AtQuarter == 0).OrderBy(p => p.Pitch).ToList();
            Assert.IsTrue(first.All(p => p.Right));
            CollectionAssert.AreEqual(new[] { 1, 3, 5 }, first.Select(p => p.Finger));
        }
    }
}
