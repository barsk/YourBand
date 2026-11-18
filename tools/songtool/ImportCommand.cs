using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using YourBand.Core.Import.UltimateGuitar;
using YourBand.Core.Songs;

namespace YourBand.SongTool
{
    /// <summary>
    /// <c>songtool import</c>: one song from a Guitar Pro file and/or Ultimate Guitar page URLs, as the control
    /// panel's import window runs it. The pages are fetched and read in memory (nothing is saved but the song), and
    /// the URLs go into the song's sources. Every step prints a line, for the window's log; the last line of a
    /// successful import is "Saved &lt;song.json&gt;".
    /// </summary>
    static class ImportCommand
    {
        public const string Usage = "songtool import [--gp <file>] [--strum-url <url>] [--chords-url <url>] [--tabs-url <url>] [--track n] [--out-dir <folder>] [-o <song.json>]\n"
            + "    With --gp: the Guitar Pro file, and --strum-url: the Ultimate Guitar chords page its strumming comes from.\n"
            + "    Without: --chords-url and/or --tabs-url (at least one) make the song.";

        public static int Run(string[] args)
        {
            string Option(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault()?.Trim();
            var gp = Option("--gp");
            var strumUrl = Option("--strum-url");
            var chordsUrl = Option("--chords-url");
            var tabsUrl = Option("--tabs-url");
            if (string.IsNullOrEmpty(gp) && string.IsNullOrEmpty(chordsUrl) && string.IsNullOrEmpty(tabsUrl))
                throw new ArgumentException("nothing to import: give a Guitar Pro file, or a chords page URL and/or a tabs page URL");
            if (!string.IsNullOrEmpty(gp) && (!string.IsNullOrEmpty(chordsUrl) || !string.IsNullOrEmpty(tabsUrl)))
                throw new ArgumentException("a Guitar Pro import takes its strumming from --strum-url, not --chords-url / --tabs-url");
            if (string.IsNullOrEmpty(gp) && !string.IsNullOrEmpty(strumUrl))
                throw new ArgumentException("--strum-url goes with a Guitar Pro file (--gp)");

            Song song;
            string tag;
            if (!string.IsNullOrEmpty(gp))
            {
                song = FromGuitarPro(gp, strumUrl, int.TryParse(Option("--track"), out var n) ? n : -1);
                var ext = Path.GetExtension(gp).TrimStart('.').ToLowerInvariant();
                tag = (ext == "" ? "gpx" : ext) + (string.IsNullOrEmpty(strumUrl) ? "" : "-ug");
            }
            else
            {
                song = FromUltimateGuitar(chordsUrl, tabsUrl);
                tag = string.IsNullOrEmpty(chordsUrl) ? "tabs" : "chords";  // (a song from a chords page and one from a tabs page are two songs)
            }

            var output = Option("-o");
            if (output == null)
            {
                var folder = Option("--out-dir") ?? SongsFolder();
                output = Path.Combine(folder, Slug(song.Info.Title, "song") + (tag == "" ? "" : "-" + tag) + ".song.json");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            var existed = File.Exists(output);
            SongJson.Save(song, output);
            Console.WriteLine(existed ? "Replaced the earlier import of this song." : "New song.");
            Console.WriteLine("Saved " + Path.GetFullPath(output));
            return 0;
        }

        static Song FromGuitarPro(string path, string strumUrl, int track)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("the Guitar Pro file is not there: " + path);
            List<StrumPattern> strumming = null;
            UgPage strumPage = null;
            if (!string.IsNullOrEmpty(strumUrl))
            {
                strumPage = Fetch(strumUrl, "strumming chords page");
                strumming = UgImporter.StrumPatterns(strumPage, out var unknown);
                Console.WriteLine($"  {strumming.Count} strumming pattern(s) on the page.");
                if (unknown.Count > 0)
                    Console.WriteLine($"  The page has unknown stroke codes (taken as rests): {string.Join(", ", unknown)}");
            }
            Console.WriteLine("Reading " + Path.GetFileName(path) + "...");
            var song = GpxImporter.Import(path, out var report, track, strumming, null);
            if (strumPage != null)
                song.Sources.Add(new Source { Kind = "ultimate-guitar-strumming", Url = SourceUrl(strumPage, strumUrl), Revision = strumPage.Revision, Supplies = { "strumming" } });
            Summary(song, report);
            return song;
        }

        static Song FromUltimateGuitar(string chordsUrl, string tabsUrl)
        {
            var chords = string.IsNullOrEmpty(chordsUrl) ? null : Fetch(chordsUrl, "chords page");
            var tabs = string.IsNullOrEmpty(tabsUrl) ? null : Fetch(tabsUrl, "tabs page");
            if (chords != null && tabs != null && !SameSong(chords, tabs))
                Console.WriteLine($"  Warning: the two pages are not the same song ({chords.Artist} - {chords.Title} and {tabs.Artist} - {tabs.Title}).");
            if (chords == null)
                Console.WriteLine("  Tabs page only: the chords are taken from it, so expect fewer of them than a chords page gives.");
            Console.WriteLine("Building the song...");
            var song = UgImporter.Import(chords ?? tabs, tabs, out var report, UgImporter.ChordsPerBarFromPage);
            if (chords == null)  // (the tabs page stood in for the chords page: one source, the tabs page)
                song.Sources.RemoveAll(s => s.Kind == "ultimate-guitar-chords");
            Summary(song, report);
            return song;
        }

        static void Summary(Song song, ImportReport report)
        {
            Console.WriteLine($"{song.Info.Artist} - {song.Info.Title} (key {song.Info.Key}, tuning {string.Join(" ", song.Info.Tuning)}, capo {song.Info.Capo})");
            foreach (var note in report.Notes)
                Console.WriteLine("  " + note);
        }

        static bool SameSong(UgPage a, UgPage b) =>
            string.Equals(a.Title.Trim(), b.Title.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Artist.Trim(), b.Artist.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>The page's own address when it gives one (a link a person can open), else the one typed.</summary>
        static string SourceUrl(UgPage page, string typed) => string.IsNullOrEmpty(page.Url) ? typed : page.Url;

        // ---------------------------------------------------------------- fetching
        static readonly HttpClient Http = MakeClient();

        static HttpClient MakeClient()
        {
            var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
            return client;
        }

        /// <summary>An Ultimate Guitar page, fetched and parsed in memory. Only that site's addresses are fetched.</summary>
        static UgPage Fetch(string url, string what)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
                throw new ArgumentException($"the {what} address is not a web address: {url}");
            if (!(uri.Host == "ultimate-guitar.com" || uri.Host.EndsWith(".ultimate-guitar.com", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"the {what} address is not an Ultimate Guitar page: {url}");
            Console.WriteLine($"Fetching the {what}...");
            string html;
            try
            {
                using var response = Http.GetAsync(uri).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    var code = (int)response.StatusCode;
                    throw new IOException(code == 403 || code == 429 || code == 503
                        ? $"Ultimate Guitar refused the request (HTTP {code}). Open the page in the browser, save it (\"Save page as\", HTML) and import that with songtool import-ug / import-gpx --strum instead."
                        : $"the {what} could not be fetched (HTTP {code}): {url}");
                }
                html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            catch (HttpRequestException e)
            {
                throw new IOException($"the {what} could not be fetched: {e.Message}");
            }
            catch (TaskCanceledException)
            {
                throw new IOException($"the {what} did not answer in 30 seconds: {url}");
            }
            var page = UgPage.Parse(html);
            Console.WriteLine($"  {page.Artist} - {page.Title} ({page.Kind}, {html.Length / 1024} KB, rating {page.Rating:0.0} from {page.Votes} votes)");
            return page;
        }

        // ---------------------------------------------------------------- where the song goes
        /// <summary>The songs-local folder next to the game's project: found upwards from the tool, then from where it is run.</summary>
        static string SongsFolder()
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var dir = new DirectoryInfo(start);
                for (int up = 0; dir != null && up < 8; ++up, dir = dir.Parent)
                {
                    var candidate = Path.Combine(dir.FullName, "songs-local");
                    if (Directory.Exists(candidate))
                        return candidate;
                }
            }
            return Path.Combine(Directory.GetCurrentDirectory(), "songs-local");
        }

        /// <summary>A file-name form of a title: lower case, letters and digits, words joined by hyphens.</summary>
        public static string Slug(string text, string fallback)
        {
            var sb = new StringBuilder();
            foreach (var c in (text ?? "").Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;
                if (c < 128 && char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-')
                    sb.Append('-');
            }
            var slug = sb.ToString().Trim('-');
            return slug.Length > 0 ? slug : fallback;
        }
    }
}
