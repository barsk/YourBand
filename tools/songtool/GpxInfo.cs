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
