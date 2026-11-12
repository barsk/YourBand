using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using AlphaTab;
using AlphaTab.Importer;
using AlphaTab.Midi;
using AlphaTab.Model;

namespace YourBand.SongTool
{
    /// <summary>What is in a Guitar Pro file: its tracks, and which of them play in each bar.</summary>
    static class GpxInfo
    {
        /// <summary>The beats of one track's bars (<c>from-to</c>, 1-based): each note's string, fret and flags.</summary>
        public static int Beats(string path, int track, string bars)
        {
            var score = GpxImporter.LoadScore(path);
            var range = bars.Split('-');
            var from = int.Parse(range[0]);
            var to = range.Length > 1 ? int.Parse(range[1]) : from;
            var staff = score.Tracks[track].Staves[0];
            for (int i = from - 1; i < to && i < staff.Bars.Count; ++i)
            {
                if (staff.Bars[i].SimileMark != SimileMark.None)
                    Console.WriteLine($"bar {i + 1} simile mark {staff.Bars[i].SimileMark}");
                foreach (var voice in staff.Bars[i].Voices)
                    foreach (var beat in voice.Beats)
                    {
                        var flags = new StringBuilder();
                        if (beat.IsPalmMute) flags.Append(" palm-mute");
                        if (beat.PickStroke != PickStroke.None) flags.Append(" pick-" + beat.PickStroke);
                        if (beat.IsRest) flags.Append(" rest");
                        flags.Append(" dyn-" + beat.Dynamics);
                        if (beat.Fade != FadeType.None) flags.Append(" fade-" + beat.Fade);
                        if (beat.Vibrato != VibratoType.None) flags.Append(" vibrato");
                        if (!string.IsNullOrEmpty(beat.Text)) flags.Append(" text:'" + beat.Text + "'");
                        if (beat.HasChord && beat.Chord != null) flags.Append(" chord:'" + beat.Chord.Name + "'");
                        if (beat.BrushType != BrushType.None) flags.Append(" brush-" + beat.BrushType);
                        if (beat.Notes.Any(n => n.HarmonicType != HarmonicType.None)) flags.Append(" harmonic");
                        if (beat.Notes.Any(n => n.Accentuated != AccentuationType.None)) flags.Append(" accent");
                        if (beat.Notes.Any(n => n.SlideOutType != SlideOutType.None || n.SlideInType != SlideInType.None)) flags.Append(" slide");
                        Console.WriteLine($"bar {i + 1} beat {beat.Index} start {beat.DisplayStart} dur {beat.DisplayDuration}{flags}  "
                                          + string.Join(" ", beat.Notes.Select(n => (n.IsPercussion ? $"drum {n.PercussionArticulation} e{n.Element}v{n.Variation} {GpxImporter.DrumArticulation(n)?.ElementType}={GpxImporter.DrumArticulation(n)?.OutputMidiNumber} " : !n.IsStringed ? $"note art{n.PercussionArticulation} e{n.Element}v{n.Variation} o{n.Octave}t{n.Tone} real{n.RealValue} " : "") + $"s{n.String}:{(n.IsDead ? "x" : n.Fret.ToString())}"
                                              + (n.IsGhost ? "g" : "") + (n.IsHammerPullOrigin ? "h" : "") + (n.IsTieDestination ? "t" : "")
                                              + (n.IsLetRing ? "L" : "") + (n.IsPalmMute ? "m" : "") + (n.IsStaccato ? "s" : ""))));
                    }
            }
            return 0;
        }

        /// <summary>
        /// The order the bars are played in (repeats, alternate endings, Da Capo, Dal Segno, Coda, Fine), as alphaTab's player
        /// plays them, in runs of bars (1-based), with each bar's repeat marks and directions.
        /// </summary>
        public static int Order(string path)
        {
            var score = GpxImporter.LoadScore(path);
            foreach (var mb in score.MasterBars)
            {
                var marks = new StringBuilder();
                if (mb.IsRepeatStart) marks.Append(" |:");
                if (mb.IsRepeatEnd) marks.Append($" :| x{mb.RepeatCount}");
                if (mb.AlternateEndings != 0) marks.Append(" endings " + Convert.ToString((int)mb.AlternateEndings, 2));
                if (mb.Directions != null)
                    foreach (var d in mb.Directions)
                        marks.Append(" " + d);
                if (marks.Length > 0)
                    Console.WriteLine($"bar {mb.Index + 1}:{marks}");
            }
            var played = PlayOrder(score);
            var runs = new List<string>();
            for (int i = 0; i < played.Count;)
            {
                int j = i;
                while (j + 1 < played.Count && played[j + 1] == played[j] + 1)
                    ++j;
                runs.Add(i == j ? $"{played[i] + 1}" : $"{played[i] + 1}-{played[j] + 1}");
                i = j + 1;
            }
            Console.WriteLine($"played: {played.Count} bars of {score.MasterBars.Count}: {string.Join(", ", runs)}");
            return 0;
        }

        /// <summary>The master bars' indices in the order alphaTab's MIDI generator plays them.</summary>
        static List<int> PlayOrder(Score score)
        {
            var generator = new MidiFileGenerator(score, new Settings(), new AlphaSynthMidiFileHandler(new MidiFile()));
            generator.Generate();
            return generator.TickLookup.MasterBars.Select(m => (int)m.MasterBar.Index).ToList();
        }

        public static int Print(string path)
        {
            var score = GpxImporter.LoadScore(path);
            Console.WriteLine($"{score.Artist} - {score.Title}: {score.MasterBars.Count} bars, {score.Tracks.Count} tracks");
            for (int t = 0; t < score.Tracks.Count; ++t)
            {
                var track = score.Tracks[t];
                var staff = track.Staves[0];
                var notes = staff.Bars.SelectMany(b => b.Voices).SelectMany(v => v.Beats).Sum(b => b.Notes.Count);
                var strings = staff.Tuning != null ? staff.Tuning.Count : 0;
                Console.WriteLine($"  [{t}] {track.Name} ({track.ShortName})  program {track.PlaybackInfo.Program}  "
                                  + $"{(staff.IsPercussion ? "percussion" : strings + " strings")}  {notes} notes"
                                  + (staff.IsPercussion || staff.Tuning == null ? "" : "  tuning " + string.Join(" ", staff.Tuning.Select(v => (int)v)) + "  capo " + (int)staff.Capo));
            }
            Console.WriteLine("bars: one letter per track (the track's number of notes: . none, a few, # many)");
            for (int i = 0; i < score.MasterBars.Count; ++i)
            {
                var bar = score.MasterBars[i];
                var row = new StringBuilder();
                foreach (var track in score.Tracks)
                {
                    var staff = track.Staves[0];
                    var n = i < staff.Bars.Count ? staff.Bars[i].Voices.SelectMany(v => v.Beats).Sum(b => b.Notes.Count) : 0;
                    row.Append(n == 0 ? '.' : n < 6 ? '+' : '#');
                }
                Console.WriteLine($"  {i + 1,3}  {bar.TimeSignatureNumerator}/{bar.TimeSignatureDenominator}  {row}  {bar.Section?.Text?.Trim()}");
            }
            return 0;
        }
    }
}
