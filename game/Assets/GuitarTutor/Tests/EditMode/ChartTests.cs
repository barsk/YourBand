using System.Linq;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Music;
using GuitarTutor.Core.Playback;
using GuitarTutor.Core.Songs;
using NUnit.Framework;

namespace GuitarTutor.Core.Tests
{
    public class ChartTests
    {
        static SongChart Demo() => new SongChart(DemoSong.Create());

        [Test]
        public void Bars_CoverTheWholeSong()
        {
            var chart = Demo();
            Assert.AreEqual(36, chart.Bars.Count);  // 4 + 8 + 8 + 4 + 8 + 4
            Assert.AreEqual(144, chart.EndQuarter);
            Assert.AreEqual(16, chart.BarOf(66).Index);  // 66 / 4 = 16.5: the 17th bar, numbered from 0
            Assert.IsNull(chart.BarOf(-1));
            Assert.IsNull(chart.BarOf(144));
        }

        [Test]
        public void Bars_KnowTheirSections()
        {
            var chart = Demo();
            CollectionAssert.AreEqual(new[] { "Intro", "Verse", "Chorus", "Bridge", "Chorus", "Outro" },
                chart.Sections.Select(s => s.Section.Name));
            Assert.AreEqual(4, chart.Sections[1].FirstBar);
            Assert.AreEqual(8, chart.Sections[1].Bars);
            Assert.IsTrue(chart.Bars[4].StartsSection);
            Assert.IsFalse(chart.Bars[5].StartsSection);
            Assert.AreEqual(3, chart.SectionIndexOf(21));
            // the two choruses are separate spans even though they share a name
            Assert.AreEqual(24, chart.Sections[4].FirstBar);
            Assert.AreEqual(4, chart.SectionIndexOf(24));
        }

        [Test]
        public void Bars_SplitLongChords()
        {
            var chart = Demo();
            // verse: G D Em C G D C C, so C lasts two bars (10 and 11)
            var bar11 = chart.Bars[11];
            Assert.AreEqual(1, bar11.Chords.Count);
            Assert.AreEqual("C", bar11.Chords[0].Chord.Name);
            Assert.IsTrue(bar11.Chords[0].CarriedIn);
            Assert.AreEqual(4, bar11.Chords[0].Quarters);
            Assert.IsFalse(chart.Bars[10].Chords[0].CarriedIn);
        }

        [Test]
        public void Bars_HoldTheirWordsAndNotes()
        {
            var chart = Demo();
            var bar4 = chart.Bars[4];  // the verse's first line starts here; its last word falls in the next bar
            CollectionAssert.AreEqual(new[] { "Morning", "light", "across", "the", "kitchen" }, bar4.Words.Select(w => w.Word.Text));
            Assert.AreEqual(2.5, bar4.Words[3].Offset);
            Assert.AreEqual("floor", chart.Bars[5].Words[0].Word.Text);
            // the chorus's last line has a pickup: "one" sounds in the bar before the line's bar
            var pickup = chart.Bars[17];  // the chorus starts at bar 12, its last line at bar 18
            Assert.AreEqual("one", pickup.Words.Last().Word.Text);
            Assert.AreEqual(3.5, pickup.Words.Last().Offset);
            Assert.AreEqual(5, chart.Bars[0].Notes.Count);
            Assert.AreEqual(6, chart.Bars[1].Notes.Count);
            Assert.AreEqual("Intro lick", chart.Bars[1].Notes[0].Riff.Name);
        }

        [Test]
        public void ChordNames_InOrderOfFirstUse()
        {
            CollectionAssert.AreEqual(new[] { "G", "D", "Em", "C", "Bm" }, Demo().ChordNames);
        }

        [Test]
        public void NextChange_SkipsRepeatsOfTheSameChord()
        {
            var chart = Demo();
            Assert.AreEqual("D", chart.NextChange(0).Name);
            // bar 10 is C, held through bar 11; the chorus starts on C too, so the next change is its D
            var next = chart.NextChange(41);
            Assert.AreEqual("D", next.Name);
            Assert.AreEqual(52, next.AtQuarter);
        }

        [Test]
        public void LineAt_FromFirstWordUntilTheNextLineOrTheTail()
        {
            var chart = Demo();
            Assert.AreEqual(-1, chart.LineAt(10));  // intro
            Assert.AreEqual(0, chart.NextLineAfter(10));
            Assert.AreEqual(0, chart.LineAt(16));
            Assert.AreEqual(0, chart.LineAt(23.9));
            Assert.AreEqual(1, chart.LineAt(24));
            // the bridge's line ends 4 quarters after its last word (at 6): the bars after show no line
            var bridge = chart.Sections[3].FirstBar * 4;
            var bridgeLine = Enumerable.Range(0, chart.LineCount).First(i => chart.Line(i).Words[0].AtQuarter == bridge);
            Assert.AreEqual(bridgeLine, chart.LineAt(bridge + 9.9));
            Assert.AreEqual(-1, chart.LineAt(bridge + 10));
            Assert.AreEqual(2, SongChart.WordsSung(chart.Line(0), 17.25));
        }

        [Test]
        public void LineAt_StartsALineAtItsPickup()
        {
            var chart = Demo();
            var chorus = chart.Sections[2].FirstBar * 4;
            Assert.AreEqual("one", chart.Line(chart.LineAt(chorus + 23.5)).Words[0].Text);
        }

        [Test]
        public void PatternsFor_SectionPatternsBeforeWholeSongOnes()
        {
            var chart = Demo();
            var chorus = chart.Sections[2].Section;
            CollectionAssert.AreEqual(new[] { "Chorus", "Simple" }, chart.PatternsFor(chorus).Select(p => p.Name));
            CollectionAssert.AreEqual(new[] { "Simple" }, chart.PatternsFor(null).Select(p => p.Name));
        }

        [Test]
        public void StrokeAt_FollowsTheEighths()
        {
            var chart = Demo();
            var pattern = chart.Song.Strumming[0];
            Assert.AreEqual((0, 0.0), chart.StrokeAt(pattern, 0));
            Assert.AreEqual((3, 0.5), chart.StrokeAt(pattern, 1.75));
            Assert.AreEqual((7, 0.0), chart.StrokeAt(pattern, 7.5));  // bar 1, last eighth
            Assert.AreEqual(-1, chart.StrokeAt(pattern, -1).index);   // count-in
            CollectionAssert.AreEqual(new[] { "1", "&", "2", "&", "3", "&", "4", "&" }, SongChart.Counts(pattern));
        }

        [Test]
        public void Rows_HoldFourBarsAndStartAtSections()
        {
            var chart = Demo();
            // intro 4, verse 8, chorus 8, bridge 4, chorus 8, outro 4
            CollectionAssert.AreEqual(new[] { 4, 4, 4, 4, 4, 4, 4, 4, 4 }, chart.Rows.Select(r => r.Bars));
            Assert.AreEqual(12, chart.Rows[3].FirstBar);
            Assert.IsTrue(chart.Rows[3].StartsSection);
            Assert.IsFalse(chart.Rows[4].StartsSection);
            Assert.AreEqual("Chorus", chart.Rows[4].Section.Name);
            Assert.AreEqual(3, chart.RowOf(13));
            Assert.AreEqual(4, chart.TypicalBarQuarters);

            // a section of nine bars: 4, 4 and 1
            var song = new Song { Tempo = new TempoMap(100, TimeSignature.FourFour) };
            song.Sections.Add(new Section { Name = "Chorus", AtQuarter = 0, Quarters = 36 });
            song.Sections.Add(new Section { Name = "Outro", AtQuarter = 36, Quarters = 8 });
            song.Chords.Add(new ChordEvent { Name = "A", AtQuarter = 0, Quarters = 44 });
            CollectionAssert.AreEqual(new[] { 4, 4, 1, 2 }, new SongChart(song).Rows.Select(r => r.Bars));
        }

        [Test]
        public void RowPosition_MovesOnToTheNextRowJustBeforeItIsPlayed()
        {
            var chart = Demo();  // the first row is quarters 0-16
            Assert.AreEqual(0, chart.RowPosition(-2, 1));  // count-in
            Assert.AreEqual(0, chart.RowPosition(14.9, 1));
            Assert.AreEqual(0.5, chart.RowPosition(15.5, 1), 1e-9);
            Assert.AreEqual(1, chart.RowPosition(16, 1), 1e-9);
            Assert.AreEqual(1, chart.RowPosition(17, 1), 1e-9);
            // the last row stays
            Assert.AreEqual(8, chart.RowPosition(143.9, 1), 1e-9);
        }

        [Test]
        public void Bars_FollowMeterChanges()
        {
            var song = new Song { Tempo = new TempoMap(100, TimeSignature.FourFour) };
            song.Tempo.SetMeter(1, new TimeSignature(3, 4));
            song.Chords.Add(new ChordEvent { Name = "A", AtQuarter = 0, Quarters = 10 });
            var chart = new SongChart(song);
            Assert.AreEqual(3, chart.Bars.Count);  // 4 + 3 + 3
            Assert.AreEqual(3, chart.Bars[2].Quarters);
            Assert.AreEqual(7, chart.Bars[2].AtQuarter);
            Assert.AreEqual(3, chart.Bars[2].Chords[0].Quarters);
        }
    }

    public class SongClockTests
    {
        [Test]
        public void Clock_PlaysPausesAndSeeks()
        {
            var clock = new SongClock(new TempoMap(120, TimeSignature.FourFour));  // 2 quarters a second
            Assert.AreEqual(0, clock.QuarterAt(10));
            clock.Play(10);
            Assert.AreEqual(4, clock.QuarterAt(12), 1e-9);
            clock.Pause(12);
            Assert.AreEqual(4, clock.QuarterAt(20), 1e-9);
            clock.Seek(16, 20);
            Assert.AreEqual(16, clock.QuarterAt(30), 1e-9);
            clock.Play(30);
            Assert.AreEqual(17, clock.QuarterAt(30.5), 1e-9);
        }

        [Test]
        public void Clock_CountsInBeforeTheStart()
        {
            var clock = new SongClock(new TempoMap(120, TimeSignature.FourFour));
            clock.Seek(-4, 0);
            clock.Play(0);
            Assert.AreEqual(-3, clock.QuarterAt(0.5), 1e-9);
            Assert.AreEqual(0, clock.QuarterAt(2), 1e-9);
            Assert.AreEqual(1, clock.QuarterAt(2.5), 1e-9);
        }

        [Test]
        public void Clock_SpeedSlowsTheSongAndFollowsTempoChanges()
        {
            var tempo = new TempoMap(120, TimeSignature.FourFour);
            tempo.SetTempo(4, 60);  // after 2 s, one quarter a second
            var clock = new SongClock(tempo);
            clock.Play(0);
            Assert.AreEqual(5, clock.QuarterAt(3), 1e-9);
            clock.SetSpeed(0.5, 3);
            Assert.AreEqual(6, clock.QuarterAt(5), 1e-9);  // 2 s at half speed: one more quarter
            Assert.AreEqual(0.5, clock.Speed);
        }
    }
}
