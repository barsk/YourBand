// songtool: the game's core code from the command line.
//
//   songtool import-ug <chords-page.html> [--tabs <tabs-page.html>] -o <song.json>
//       Imports an Ultimate Guitar song from pages saved in the browser, prints the import report.
//   songtool show <song.json> [--bars a-b]
//       Prints the chart bar by bar: section, chords, lyrics, and the riffs' notes per bar.

using System;
using System.IO;
using System.Linq;
using System.Text;
using GuitarTutor.Core.Import.UltimateGuitar;
using GuitarTutor.Core.Songs;

namespace GuitarTutor.SongTool
{
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                if (args.Length >= 2 && args[0] == "import-ug")
                    return ImportUg(args);
                if (args.Length >= 2 && args[0] == "show")
                    return Show(args);
                Console.WriteLine("usage:\n  songtool import-ug <chords.html> [--tabs <tabs.html>] [--chords-per-bar n|auto] -o <song.json>\n  songtool show <song.json> [--bars a-b]");
                return 1;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("songtool: " + e.Message);
                return 2;
            }
        }

        static string Option(string[] args, string name) =>
            args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();

        static int ImportUg(string[] args)
        {
            var chords = UgPage.Load(args[1]);
            var tabsPath = Option(args, "--tabs");
            var tabs = tabsPath != null ? UgPage.Load(tabsPath) : null;
            var output = Option(args, "-o") ?? Path.ChangeExtension(args[1], ".song.json");
            var perBarOption = Option(args, "--chords-per-bar");
            var perBar = perBarOption == "auto" ? 0 : int.TryParse(perBarOption, out var n) ? n : 1;
            var song = UgImporter.Import(chords, tabs, out var report, perBar);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            SongJson.Save(song, output);
            Console.WriteLine($"{song.Info.Artist} - {song.Info.Title} (key {song.Info.Key}, tuning {string.Join(" ", song.Info.Tuning)}, capo {song.Info.Capo})");
            foreach (var note in report.Notes)
                Console.WriteLine("  " + note);
            Console.WriteLine("Saved " + Path.GetFullPath(output));
            return 0;
        }

        static int Show(string[] args)
        {
            var song = SongJson.Load(args[1]);
            var range = Option(args, "--bars");
            var end = song.Chords.Count > 0 ? song.Chords.Max(c => c.AtQuarter + c.Quarters) : 0;
            var bars = song.Tempo.BarAt(Math.Max(0, end - 0.001)).bar + 1;
            int first = 0, last = bars - 1;
            if (range != null)
            {
                var parts = range.Split('-');
                first = Math.Max(0, int.Parse(parts[0]) - 1);
                last = Math.Min(bars - 1, int.Parse(parts[parts.Length - 1]) - 1);
            }
            Console.WriteLine($"{song.Info.Artist} - {song.Info.Title}: {bars} bars, {song.Tempo.BpmAt(0)} BPM, "
                              + $"strumming {string.Join(" ", song.Strumming.FirstOrDefault()?.Strokes.Select(Symbol) ?? new string[0])}");
            string previousSection = null;
            for (int b = first; b <= last; ++b)
            {
                var start = song.Tempo.QuarterOfBar(b);
                var length = song.Tempo.SignatureAtBar(b).QuartersPerBar;
                var section = song.SectionAt(start)?.Name ?? "";
                if (section != previousSection)
                    Console.WriteLine($"[{section}]");
                previousSection = section;
                var chords = string.Join(" ", song.ChordsInBar(b).Select(c => c.AtQuarter < start ? "(" + c.Name + ")" : $"{c.Name}@{c.AtQuarter - start:0.#}"));
                var words = song.Lyrics.SelectMany(l => l.Words).Where(w => w.AtQuarter >= start && w.AtQuarter < start + length).Select(w => w.Text);
                var notes = song.Riffs.SelectMany(r => r.Notes).Count(n => n.AtQuarter >= start && n.AtQuarter < start + length);
                Console.WriteLine($"  {b + 1,4} {Clock(song.Tempo.SecondsAt(start)),6}  {chords,-22} {string.Join(" ", words)}{(notes > 0 ? $"   [{notes} riff notes]" : "")}");
            }
            if (song.Alternatives.Count > 0)
                Console.WriteLine($"{song.Alternatives.Count} stretches where the tabs page has other chords.");
            return 0;
        }

        static string Symbol(Stroke s) => s switch
        {
            Stroke.Down => "D", Stroke.Up => "U", Stroke.MutedDown => "d", Stroke.MutedUp => "u", _ => "-",
        };

        static string Clock(double seconds)
        {
            var s = (int)Math.Round(seconds);
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
