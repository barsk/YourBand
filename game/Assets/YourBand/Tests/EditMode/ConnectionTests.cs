using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YourBand.Core.Band;
using YourBand.Core.Chart;
using YourBand.Core.Engine;
using YourBand.Core.Songs;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace YourBand.Core.Tests
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
        public void AnInstrumentChosenNone_IsOutOfTheBand_AndTheChoiceIsKept()
        {
            var song = GuitarProSong();
            var chart = new SongChart(song);
            var connections = new BandConnections();
            var all = Arranger.Arrange(chart, BandStyle.Named("Ballad"), connections);
            Assert.IsTrue(all.Part(BandPart.Drums).Any() && all.Part(BandPart.Bass).Any() && all.Part(BandPart.Keys).Any()
                          && all.Part(BandPart.Guitar).Any() && all.Picks.Count > 0);  // (the guitar plays its track: its string keys)
            Assert.IsFalse(connections.IsOut(Instrument.Bass));

            // out: no track, no part made up, nothing connected automatically (the song has a bass track, and the piano is
            // too short for the keys to play it, so the keys' part would be made up)
            foreach (var instrument in new[] { Instrument.Drums, Instrument.Bass, Instrument.Keys, Instrument.RhythmGuitar })
                connections.Choose(instrument, TrackRef.None);
            Assert.IsTrue(connections.IsOut(Instrument.Bass));
            Assert.IsNull(connections.TrackFor(song, Instrument.Bass));
            Assert.IsNull(connections.TrackFor(song, Instrument.RhythmGuitar));
            var out_ = Arranger.Arrange(chart, BandStyle.Named("Ballad"), connections);
            Assert.AreEqual(0, out_.Part(BandPart.Drums).Count());
            Assert.AreEqual(0, out_.Part(BandPart.Bass).Count());
            Assert.AreEqual(0, out_.Part(BandPart.Keys).Count());
            Assert.AreEqual(0, out_.Part(BandPart.Guitar).Count());
            Assert.AreEqual(0, out_.Strokes.Count + out_.Voicings.Count + out_.Picks.Count + out_.SingleSpans.Count);
            Assert.AreEqual(25, out_.Part(BandPart.Lead1).Count());  // the others play on (the lead guitar's track)

            // back in, on its track (the others stay out)
            connections.Choose(Instrument.Bass, TrackRef.Of(song.Tracks[5]));
            Assert.IsFalse(connections.IsOut(Instrument.Bass));
            Assert.AreEqual(60, Arranger.Arrange(chart, BandStyle.Named("Ballad"), connections).Part(BandPart.Bass).Count());

            // the choice is in the band file, and the engine is told it (-2)
            var read = BandConnections.Read(BandConnections.Write(connections));
            Assert.IsTrue(read.IsOut(Instrument.RhythmGuitar));
            Assert.IsFalse(read.IsOut(Instrument.Bass));
            var (_, tracks) = EngineProtocol.Read(EngineProtocol.Tracks(song, read));
            var rhythm = ((JArray)tracks["instruments"]).Single(i => (string)i["id"] == "rhythm");
            Assert.AreEqual(TrackRef.NoneIndex, (int)rhythm["track"]);
            Assert.AreEqual(5, (int)((JArray)tracks["instruments"]).Single(i => (string)i["id"] == "bass")["track"]);  // its track again
        }

        [Test]
        public void Leads_PlayTheirConnectedTracks()
        {
            var song = GuitarProSong();
            song.Tracks[1].Notes[0].Technique = "x";  // a dead note on the harmonica: a click
            var chart = new SongChart(song);
            var connections = new BandConnections();
            var a = Arranger.Arrange(chart, BandStyle.Named("Ballad"), connections);
            Assert.AreEqual(25, a.Part(BandPart.Lead1).Count());  // Guitar 1
            Assert.AreEqual(20, a.Part(BandPart.Lead2).Count());  // the harmonica
            var dead = a.Part(BandPart.Lead2).OrderBy(n => n.AtQuarter).First();
            Assert.IsTrue(dead.Velocity <= 30 && dead.Quarters < 0.2);  // its dead note very short and very soft
            CollectionAssert.AreEqual(new[] { "Guitar 1", "Harmonica" }, a.LeadNames);
            connections.Choose(Instrument.LeadGuitar, TrackRef.Default);
            var none = Arranger.Arrange(chart, BandStyle.Named("Ballad"), connections);
            Assert.AreEqual(0, none.Part(BandPart.Lead1).Count());  // the band makes up no lead part
            Assert.AreEqual("", none.LeadNames[0]);
        }

        [Test]
        public void ALeadsBends_AreItsPitchWheel()
        {
            var song = GuitarProSong();
            var lead = song.Tracks[2];  // Guitar 1, the lead guitar
            lead.Notes.Clear();
            List<double[]> Points(params double[] values) =>
                Enumerable.Range(0, values.Length / 2).Select(i => new[] { values[2 * i], values[2 * i + 1] }).ToList();
            // a whole-step bend and release over the first half of a note, a plain note, a pre-bend held a whole step,
            // then a half-step bend that stays up to the next note
            lead.Notes.Add(new TrackNote { AtQuarter = 4, Quarters = 2, Pitch = 67, Technique = "b", Bend = 2, BendPoints = Points(0, 0, 0.25, 2, 0.5, 0) });
            lead.Notes.Add(new TrackNote { AtQuarter = 6, Quarters = 0.5, Pitch = 69 });  // (ends before the pre-bend's lead)
            lead.Notes.Add(new TrackNote { AtQuarter = 7, Quarters = 1, Pitch = 67, Technique = "b", Bend = 2, BendPoints = Points(0, 2, 1, 2) });
            lead.Notes.Add(new TrackNote { AtQuarter = 8, Quarters = 1, Pitch = 67, Technique = "b", Bend = 1, BendPoints = Points(0, 0, 0.5, 1) });
            lead.Notes.Add(new TrackNote { AtQuarter = 9, Quarters = 1, Pitch = 65 });
            var a = Arranger.Arrange(new SongChart(song), BandStyle.Named("Ballad"), new BandConnections());

            var wheel = a.Wheel.Where(w => w.Part == BandPart.Lead1).ToList();
            // up and back, then nothing to the note's end
            Assert.AreEqual(0, WheelAt(wheel, 4.0), 1e-6);
            Assert.AreEqual(2, WheelAt(wheel, 4.5), 1e-6);
            Assert.AreEqual(1, WheelAt(wheel, 4.75), 0.05);
            Assert.AreEqual(0, WheelAt(wheel, 5.5), 1e-6);
            // a pre-bend: up before the note (after the one before), held, back in the middle as the next note starts
            Assert.AreEqual(0, WheelAt(wheel, 6.9), 1e-6);
            Assert.AreEqual(2, WheelAt(wheel, 6.99), 1e-6);
            Assert.AreEqual(2, WheelAt(wheel, 7.9), 1e-6);
            Assert.AreEqual(0, WheelAt(wheel, 8.0), 1e-6);
            // up to its end; the next note starts unbent
            Assert.AreEqual(1, WheelAt(wheel, 8.5), 1e-6);
            Assert.AreEqual(1, WheelAt(wheel, 8.99), 1e-6);
            Assert.AreEqual(0, WheelAt(wheel, 9.2), 1e-6);
            Assert.IsFalse(a.Wheel.Any(w => w.Part != BandPart.Lead1));  // the harmonica on instrument 1 bends nothing
        }

        /// <summary>Where a part's wheel is at a moment, as the engine moves it: evenly between its points (the last of
        /// several at one moment counts).</summary>
        static double WheelAt(List<WheelPoint> wheel, double at)
        {
            var before = wheel.LastOrDefault(w => w.AtQuarter <= at + 1e-9);
            var after = wheel.FirstOrDefault(w => w.AtQuarter > at + 1e-9);
            if (wheel.All(w => w.AtQuarter > at + 1e-9))
                return 0;
            if (after.AtQuarter <= before.AtQuarter)
                return before.Semitones;
            return before.Semitones + (at - before.AtQuarter) / (after.AtQuarter - before.AtQuarter) * (after.Semitones - before.Semitones);
        }

        [Test]
        public void TheTechniques_AreHowTheNotesArePlayed()
        {
            var song = GuitarProSong();
            var lead = song.Tracks[2];  // Guitar 1, the lead guitar
            lead.Notes.Clear();
            void Add(double at, double quarters, int pitch, string technique = "", int stringIndex = 2) =>
                lead.Notes.Add(new TrackNote { AtQuarter = at, Quarters = quarters, Pitch = pitch, StringIndex = stringIndex, Fret = 5, Velocity = 79, Technique = technique });
            Add(4, 2, 64, "~");
            lead.Notes[0].Vibrato = "slight";
            Add(6, 1, 64, "s");  // a legato slide up a semitone to the next note
            lead.Notes[1].SlideOut = "legato";
            Add(7, 1, 65);
            Add(8, 1, 64, "h");  // a hammer-on to the next note
            Add(9, 1, 66);
            Add(10, 1, 64, "m");  // palm-muted
            Add(11, 1, 64, ".");  // staccato
            Add(12, 1, 64, "z");  // tremolo picking in sixteenths
            lead.Notes[lead.Notes.Count - 1].TremoloQuarters = 0.25;
            Add(13, 1, 52, "x");  // a dead note
            var a = Arranger.Arrange(new SongChart(song), BandStyle.Named("Ballad"), new BandConnections());
            var notes = a.Part(BandPart.Lead1).OrderBy(n => n.AtQuarter).ToList();
            var wheel = a.Wheel.Where(w => w.Part == BandPart.Lead1).ToList();
            var perSecond = song.Tempo.BpmAt(4) / 60.0;

            // vibrato: nothing for 0.15 s, then a wobble a quarter semitone each way
            Assert.AreEqual(0, WheelAt(wheel, 4 + 0.1 * perSecond), 1e-6);
            var wobble = wheel.Where(w => w.AtQuarter > 4.6 && w.AtQuarter < 5.9).Select(w => w.Semitones).ToList();
            Assert.IsTrue(wobble.Max() > 0.2 && wobble.Min() < -0.2 && wobble.All(s => Math.Abs(s) <= 0.25 + 1e-6));
            // the slide glides up a semitone at its note's end; the note it slides to is not picked: softer
            Assert.AreEqual(1, WheelAt(wheel, 6.999), 0.02);
            Assert.AreEqual(0, WheelAt(wheel, 7.2), 1e-6);
            var picked = notes.Single(n => n.AtQuarter == 6).Velocity;
            Assert.AreEqual(picked - 25, notes.Single(n => n.AtQuarter == 7).Velocity);
            Assert.AreEqual(picked - 25, notes.Single(n => n.AtQuarter == 9).Velocity);  // hammered on
            // palm-muted: shorter, softer, the palm mute's controller around it
            var muted = notes.Single(n => n.AtQuarter == 10);
            Assert.IsTrue(muted.Quarters <= 0.5 && muted.Velocity == picked - 12);
            CollectionAssert.AreEqual(new[] { (10.0, 127), (10 + muted.Quarters, 0) },
                                      a.Controls.Where(c => c.Part == BandPart.Lead1 && c.Controller == Arranger.PalmMuteController).Select(c => (c.AtQuarter, c.Value)));
            Assert.AreEqual(0.5, notes.Single(n => n.AtQuarter == 11).Quarters, 1e-6);  // staccato: half as long
            CollectionAssert.AreEqual(new[] { 12.0, 12.25, 12.5, 12.75 }, notes.Where(n => n.AtQuarter >= 12 && n.AtQuarter < 13).Select(n => n.AtQuarter));
            var dead = notes.Single(n => n.AtQuarter == 13);  // a click
            Assert.IsTrue(dead.Velocity <= 30 && dead.Quarters < 0.2);

            // the bass bends only where nothing else of it rings
            var bass = song.Tracks[5];
            bass.Notes.Clear();
            bass.Notes.Add(new TrackNote { AtQuarter = 0, Quarters = 1, Pitch = 40, StringIndex = 0, Fret = 0, Technique = "b", Bend = 1,
                                           BendPoints = new List<double[]> { new[] { 0.0, 0.0 }, new[] { 0.5, 1.0 } } });
            bass.Notes.Add(new TrackNote { AtQuarter = 2, Quarters = 2, Pitch = 45, StringIndex = 1, Fret = 0, Technique = "b", Bend = 1,
                                           BendPoints = new List<double[]> { new[] { 0.0, 0.0 }, new[] { 0.5, 1.0 } } });
            bass.Notes.Add(new TrackNote { AtQuarter = 2.5, Quarters = 1, Pitch = 40, StringIndex = 0, Fret = 0 });  // rings under it
            var connections = new BandConnections();
            connections.Choose(Instrument.Bass, TrackRef.Of(bass));
            var b = Arranger.Arrange(new SongChart(song), BandStyle.Named("Ballad"), connections);
            var bassWheel = b.Wheel.Where(w => w.Part == BandPart.Bass).ToList();
            Assert.AreEqual(1, WheelAt(bassWheel, 0.75), 1e-6);  // alone: bent
            Assert.IsFalse(bassWheel.Any(w => w.AtQuarter >= 2 && Math.Abs(w.Semitones) > 1e-6));  // another note rings: not
        }

        [Test]
        public void DrumsAndBass_PlayTheirTracksInsteadOfMadeUpParts()
        {
            var song = GuitarProSong();
            song.Tracks[5].Notes[1].Technique = "x";  // a dead bass note: a click
            var chart = new SongChart(song);
            var madeUp = Arranger.Arrange(chart, BandStyle.Named("Ballad"));
            var connections = new BandConnections();
            var a = Arranger.Arrange(chart, BandStyle.Named("Ballad"), connections);
            CollectionAssert.AreEqual(song.Tracks[6].Notes.Select(n => (n.Pitch, n.AtQuarter, n.Velocity)),
                                      a.Part(BandPart.Drums).Select(n => (n.Pitch, n.AtQuarter, n.Velocity)));
            Assert.AreEqual(60, a.Part(BandPart.Bass).Count());
            // the piano is too short to be connected automatically: the keys make up their part as before (the same notes; their velocities vary a little differently)
            // (a chord's notes in pitch order: the arrangement's sort by time leaves their order open)
            CollectionAssert.AreEqual(madeUp.Part(BandPart.Keys).Select(n => (n.AtQuarter, n.Pitch, n.Quarters)).OrderBy(k => k),
                                      a.Part(BandPart.Keys).Select(n => (n.AtQuarter, n.Pitch, n.Quarters)).OrderBy(k => k));
        }

        [Test]
        public void TheRhythmGuitarsTrack_HoldsTheChordAndRingsEachString()
        {
            var song = GuitarProSong();
            song.Chords.Clear();
            song.Chords.Add(new ChordEvent { Name = "Am", AtQuarter = 0, Quarters = 2.5 });
            song.Chords.Add(new ChordEvent { Name = "Am", AtQuarter = 2.5, Quarters = 1.5 });  // the same chord again: held on
            song.Chords.Add(new ChordEvent { Name = "C", AtQuarter = 4, Quarters = 4 });
            var rhythm = song.Tracks[3];
            rhythm.Notes.Clear();
            void Play(double at, params (int str, int fret)[] strings)
            {
                foreach (var (s, f) in strings)
                    rhythm.Notes.Add(new TrackNote { AtQuarter = at, Quarters = 0.25, Pitch = new[] { 40, 45, 50, 55, 59, 64 }[s] + f, StringIndex = s, Fret = f, Velocity = 79 });
            }
            // Redbud Tree's way: a bass note under pinches on the top strings
            Play(0, (1, 0), (3, 0), (4, 0), (5, 0));
            Play(1, (3, 0), (4, 0), (5, 0));
            Play(2, (1, 0), (3, 0));
            Play(3, (4, 0));  // one string
            Play(4, (1, 3), (3, 0), (4, 0), (5, 0));  // the next chord, C3 in the bass
            Play(6, (3, 2), (4, 0));  // A3 on the G string: off the chord held, a new one from here
            var connections = new BandConnections();
            connections.Choose(Instrument.RhythmGuitar, TrackRef.Of(rhythm));
            var a = Arranger.Arrange(new SongChart(song), BandStyle.Named("Ballad"), connections);

            Assert.AreEqual(0, a.Strokes.Count);  // no strum keys: every note is its string struck
            Assert.AreEqual(0, a.SingleSpans.Count);
            CollectionAssert.AreEqual(new[] { (0.0, 4.0, "45 55 59 64"), (4.0, 2.0, "48 55 59 64"), (6.0, 2.0, "48 57 59 64") },
                                      a.Voicings.GroupBy(v => (v.AtQuarter, v.Quarters))
                                                .Select(g => (g.Key.AtQuarter, g.Key.Quarters, string.Join(" ", g.Select(v => v.Pitch).OrderBy(p => p)))));
            Assert.AreEqual(16, a.Picks.Count);
            var perSecond = song.Tempo.BpmAt(0) / 60.0;
            var bass = a.Picks.Where(p => p.Rank == 0 && p.AtQuarter < 4).ToList();
            // the bass rings under the pinches until it is struck again, then through the same chord again until a moment
            // before the chord changes
            Assert.AreEqual(2 - 0.01 * perSecond, bass[0].Quarters, 1e-6);
            Assert.AreEqual(2 - 0.07 * perSecond, bass[1].Quarters, 1e-6);
            // a stroke crosses the strings low to high; each note is its place in the held chord
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, a.Picks.Where(p => p.AtQuarter < 0.5).Select(p => p.Rank));
            Assert.AreEqual(2, a.Picks.Single(p => Math.Abs(p.AtQuarter - 3) < 1e-6).Rank);  // the B string alone: the third lowest
            Assert.AreEqual(1, a.Picks.First(p => p.AtQuarter >= 6).Rank);  // A3, the second lowest of the new chord
            // the other guitars play the same notes, ringing as long
            CollectionAssert.AreEqual(a.Picks.Select(p => (p.AtQuarter, p.Quarters)).OrderBy(x => x),
                                      a.Part(BandPart.Guitar).Select(n => (n.AtQuarter, n.Quarters)).OrderBy(x => x));
        }

        [Test]
        public void TheRhythmGuitarsTrack_InAnotherTuning_HoldsChordsShreddageCanVoice()
        {
            // Black Hole Sun is in DGCFAD: its G7 has F2 and G2, which only the low E string of a standard-tuned guitar reaches, and
            // Shreddage voices no chord of the kind (every string key silent: measured with plugin_probe)
            var song = GuitarProSong();
            song.Chords.Clear();
            song.Chords.Add(new ChordEvent { Name = "G7", AtQuarter = 0, Quarters = 4 });
            var rhythm = song.Tracks[3];
            rhythm.Notes.Clear();
            var open = new[] { 38, 43, 48, 53, 57, 62 };
            void Play(double at, params (int str, int fret)[] strings)
            {
                foreach (var (s, f) in strings)
                    rhythm.Notes.Add(new TrackNote { AtQuarter = at, Quarters = 0.25, Pitch = open[s] + f, StringIndex = s, Fret = f, Velocity = 79 });
            }
            Play(0, (0, 3), (1, 0));  // F2 and G2 together
            Play(0.5, (3, 2), (4, 2), (5, 0));
            Play(1, (0, 3), (3, 2));
            Play(1.5, (1, 0), (4, 2), (5, 0));
            var connections = new BandConnections();
            connections.Choose(Instrument.RhythmGuitar, TrackRef.Of(rhythm));
            var a = Arranger.Arrange(new SongChart(song), BandStyle.Named("Ballad"), connections);

            Assert.Greater(a.Voicings.Count, 0);
            foreach (var chord in a.Voicings.GroupBy(v => (v.AtQuarter, v.Quarters)))
            {
                var notes = chord.Select(v => v.Pitch).Distinct().ToList();
                Assert.IsTrue(GuitarVoicing.CanVoice(notes), "the chord at " + chord.Key.AtQuarter + ": " + string.Join(" ", notes.OrderBy(p => p)));
                // the string keys count the notes of the chord as it is held
                foreach (var pick in a.Picks.Where(p => p.AtQuarter >= chord.Key.AtQuarter - 1e-6 && p.AtQuarter < chord.Key.AtQuarter + chord.Key.Quarters - 1e-6))
                    Assert.Less(pick.Rank, notes.Count);
            }
            Assert.Greater(a.Picks.Count, 5);  // the notes are still played
            Assert.AreEqual(0, a.Strokes.Count);
        }

        [Test]
        public void TheRhythmGuitarsTrack_StrumsStringByString_WithItsTechniques()
        {
            var song = GuitarProSong();
            song.Chords.Clear();
            song.Chords.Add(new ChordEvent { Name = "Em", AtQuarter = 0, Quarters = 8 });
            var rhythm = song.Tracks[3];
            rhythm.Notes.Clear();
            void Strike(double at, int[] strings, string technique = "")
            {
                foreach (var s in strings)
                    rhythm.Notes.Add(new TrackNote { AtQuarter = at, Quarters = 0.5, Pitch = 40 + 5 * s, StringIndex = s, Fret = 0, Velocity = 79, Technique = technique });
            }
            TrackNote Single(double at, int str, int pitch, string technique = "", double quarters = 0.5)
            {
                var n = new TrackNote { AtQuarter = at, Quarters = quarters, Pitch = pitch, StringIndex = str, Fret = 0, Velocity = 79, Technique = technique };
                rhythm.Notes.Add(n);
                return n;
            }
            var top = new[] { 2, 3, 4, 5 };
            Strike(0, top);  // down, on the beat
            Strike(0.5, top);  // up, off it
            Strike(1.5, top, "x");  // dead strings: a chuck
            Single(2, 3, 55, "m");  // palm-muted
            Single(2.5, 4, 60, "h");  // hammered on to the next note on its string
            Single(3, 4, 62);
            var bent = Single(4, 4, 62, "b", 1);
            bent.BendPoints = new List<double[]> { new[] { 0.0, 0.0 }, new[] { 0.5, 1.0 }, new[] { 1.0, 1.0 } };
            Single(6, 5, 64, "x");  // a dead note alone
            var connections = new BandConnections();
            connections.Choose(Instrument.RhythmGuitar, TrackRef.Of(rhythm));
            var a = Arranger.Arrange(new SongChart(song), BandStyle.Named("Ballad"), connections);
            var perSecond = song.Tempo.BpmAt(0) / 60.0;

            Assert.AreEqual(0, a.Strokes.Count);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, a.Picks.Where(p => p.AtQuarter < 0.25).Select(p => p.Rank));  // down: low to high
            CollectionAssert.AreEqual(new[] { 3, 2, 1, 0 }, a.Picks.Where(p => p.AtQuarter >= 0.5 && p.AtQuarter < 0.75).Select(p => p.Rank));  // up: high to low
            var chuck = a.Picks.Where(p => p.AtQuarter >= 1.5 && p.AtQuarter < 1.75).ToList();
            Assert.AreEqual(4, chuck.Count);  // the dead strum strikes the held strings, briefly
            Assert.IsTrue(chuck.All(p => p.Quarters <= 0.08 + 1e-9));
            // the palm mute: shorter and softer, with its controller around it
            var muted = a.Picks.Single(p => Math.Abs(p.AtQuarter - 2) < 1e-6);
            Assert.IsTrue(muted.Quarters <= 0.25 + 1e-9);
            CollectionAssert.AreEqual(new[] { (2.0, 127), (2.0 + muted.Quarters, 0) },
                                      a.Controls.Where(c => c.Part == BandPart.Guitar && c.Controller == Arranger.PalmMuteController).Select(c => (c.AtQuarter, c.Value)));
            // after a hammer-on the next note on its string is softer: it is not picked
            var hammered = a.Picks.Single(p => Math.Abs(p.AtQuarter - 3) < 1e-6);
            Assert.AreEqual(a.Picks.Single(p => Math.Abs(p.AtQuarter - 2.5) < 1e-6).Velocity - 25, hammered.Velocity);
            // the bend: it rings for its own length, what rings before it lets go as it is struck, and the wheel bends it
            Assert.AreEqual(1.0, a.Picks.Single(p => Math.Abs(p.AtQuarter - 4) < 1e-6).Quarters, 1e-6);
            Assert.IsTrue(a.Picks.Where(p => p.AtQuarter < 4).All(p => p.AtQuarter + p.Quarters <= 4 + 1e-9));
            Assert.IsTrue(a.Wheel.Any(w => w.Part == BandPart.Guitar && w.AtQuarter > 4 && w.AtQuarter < 5 && w.Semitones > 0.9));
            // the dead note alone: a click for the guitars that play notes, no string key
            Assert.IsFalse(a.Picks.Any(p => Math.Abs(p.AtQuarter - 6) < 1e-6));
            Assert.IsTrue(a.Part(BandPart.Guitar).Any(n => Math.Abs(n.AtQuarter - 6) < 1e-6 && n.Velocity <= 30 && n.Quarters <= 0.06 * perSecond + 1e-9));
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
