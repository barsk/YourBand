using System;
using System.IO;
using System.Linq;
using System.Text;
using AlphaTab.Importer;
using AlphaTab.Model;

namespace GuitarTutor.SongTool
{
    /// <summary>What is in a Guitar Pro file: its tracks, and which of them play in each bar.</summary>
    static class GpxInfo
    {
        /// <summary>The beats of one track's bars (<c>from-to</c>, 1-based): each note's string, fret and flags.</summary>
        public static int Beats(string path, int track, string bars)
        {
            var score = ScoreLoader.LoadScoreFromBytes(File.ReadAllBytes(path));
            var range = bars.Split('-');
            var from = int.Parse(range[0]);
            var to = range.Length > 1 ? int.Parse(range[1]) : from;
            var staff = score.Tracks[track].Staves[0];
            for (int i = from - 1; i < to && i < staff.Bars.Count; ++i)
                foreach (var voice in staff.Bars[i].Voices)
                    foreach (var beat in voice.Beats)
                    {
                        var flags = new StringBuilder();
                        if (beat.IsPalmMute) flags.Append(" palm-mute");
                        if (beat.PickStroke != PickStroke.None) flags.Append(" pick-" + beat.PickStroke);
                        if (beat.IsRest) flags.Append(" rest");
                        Console.WriteLine($"bar {i + 1} beat {beat.Index} start {beat.DisplayStart} dur {beat.DisplayDuration}{flags}  "
                                          + string.Join(" ", beat.Notes.Select(n => $"s{n.String}:{(n.IsDead ? "x" : n.Fret.ToString())}"
                                              + (n.IsGhost ? "g" : "") + (n.IsHammerPullOrigin ? "h" : "") + (n.IsTieDestination ? "t" : "")
                                              + (n.IsLetRing ? "L" : "") + (n.IsPalmMute ? "m" : "") + (n.IsStaccato ? "s" : ""))));
                    }
            return 0;
        }

        public static int Print(string path)
        {
            var score = ScoreLoader.LoadScoreFromBytes(File.ReadAllBytes(path));
            Console.WriteLine($"{score.Artist} - {score.Title}: {score.MasterBars.Count} bars, {score.Tracks.Count} tracks");
            for (int t = 0; t < score.Tracks.Count; ++t)
            {
                var track = score.Tracks[t];
                var staff = track.Staves[0];
                var notes = staff.Bars.SelectMany(b => b.Voices).SelectMany(v => v.Beats).Sum(b => b.Notes.Count);
                var strings = staff.Tuning != null ? staff.Tuning.Count : 0;
                Console.WriteLine($"  [{t}] {track.Name} ({track.ShortName})  program {track.PlaybackInfo.Program}  "
                                  + $"{(staff.IsPercussion ? "percussion" : strings + " strings")}  {notes} notes");
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
