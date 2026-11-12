using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace YourBand.Core.Import.UltimateGuitar
{
    /// <summary>
    /// An Ultimate Guitar page (chords or tabs) as saved from the browser. The page embeds its data as JSON in a
    /// <c>js-store</c> element; this reads the parts the game uses. The page layout changes from time to time, so
    /// everything that depends on it is here and nowhere else.
    /// </summary>
    public sealed class UgPage
    {
        public string Kind { get; private set; } = "";  // "Chords", "Tabs"
        public string Title { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public string Url { get; private set; } = "";
        public string Revision { get; private set; } = "";
        public string Key { get; private set; } = "";
        public int Capo { get; private set; }
        /// <summary>The open strings as written, lowest first, e.g. "E A D G B E".</summary>
        public string Tuning { get; private set; } = "";
        public int Votes { get; private set; }
        public double Rating { get; private set; }
        /// <summary>The sheet: text with [ch]chord[/ch], [tab]...[/tab] and [Section] markup.</summary>
        public string Content { get; private set; } = "";
        public List<UgStrumming> Strummings { get; } = new List<UgStrumming>();
        /// <summary>Chord shapes: per chord its variants, the most common first.</summary>
        public Dictionary<string, List<UgShape>> Shapes { get; } = new Dictionary<string, List<UgShape>>();

        static readonly Regex Store = new Regex("class=\"js-store\"\\s+data-content=\"([^\"]*)\"", RegexOptions.Singleline);

        public static UgPage Load(string path) => Parse(File.ReadAllText(path));

        public static UgPage Parse(string html)
        {
            var m = Store.Match(html);
            if (!m.Success)
                throw new InvalidDataException("no Ultimate Guitar page data (js-store) found: save the whole page from the browser");
            var data = JObject.Parse(WebUtility.HtmlDecode(m.Groups[1].Value))["store"]?["page"]?["data"]
                       ?? throw new InvalidDataException("unexpected page data layout");
            var tab = data["tab"] ?? new JObject();
            var view = data["tab_view"] ?? new JObject();
            var meta = view["meta"] ?? new JObject();

            var page = new UgPage
            {
                Kind = (string)tab["type"] ?? "",
                Title = (string)tab["song_name"] ?? "",
                Artist = (string)tab["artist_name"] ?? "",
                Url = (string)tab["tab_url"] ?? "",
                Revision = (string)view["wiki_tab"]?["revision_id"] ?? "",
                Key = (string)meta["tonality"] ?? (string)tab["tonality_name"] ?? "",
                Capo = (int?)meta["capo"] ?? (int?)tab["capo"] ?? 0,
                Tuning = (string)meta["tuning"]?["value"] ?? "E A D G B E",
                Votes = (int?)tab["votes"] ?? 0,
                Rating = (double?)tab["rating"] ?? 0,
                Content = ((string)view["wiki_tab"]?["content"] ?? "").Replace("\r\n", "\n"),
            };

            foreach (var s in view["strummings"] as JArray ?? new JArray())
                page.Strummings.Add(new UgStrumming
                {
                    Part = (string)s["part"] ?? "",
                    Denominator = (int?)s["denuminator"] ?? (int?)s["denominator"] ?? 8,  // (sic)
                    Bpm = (double?)s["bpm"] ?? 0,
                    Measures = (s["measures"] as JArray ?? new JArray()).Select(x => (int?)x["measure"] ?? 0).ToList(),
                });

            if (view["applicature"] is JObject shapes)
                foreach (var kv in shapes)
                    page.Shapes[kv.Key] = (kv.Value as JArray ?? new JArray()).Select(v => new UgShape
                    {
                        Id = (string)v["id"] ?? "",
                        Frets = (v["frets"] as JArray ?? new JArray()).Select(x => (int)x).ToList(),
                        Fingers = (v["fingers"] as JArray ?? new JArray()).Select(x => (int)x).ToList(),
                        Barres = (v["listCapos"] as JArray ?? new JArray()).Select(c => new UgBarre
                        {
                            Fret = (int?)c["fret"] ?? 0,
                            StartString = (int?)c["startString"] ?? 0,
                            LastString = (int?)c["lastString"] ?? 0,
                            Finger = (int?)c["finger"] ?? 1,
                        }).ToList(),
                    }).ToList();
            return page;
        }
    }

    /// <summary>A strumming pattern: one bar on a grid of 1/<c>Denominator</c> notes, as UG's stroke codes.</summary>
    public sealed class UgStrumming
    {
        public string Part = "";
        public int Denominator = 8;
        public double Bpm;
        public List<int> Measures = new List<int>();
    }

    /// <summary>A chord shape as UG stores it: per string from the high E down, fret (-1 not played) and finger.</summary>
    public sealed class UgShape
    {
        public string Id = "";
        public List<int> Frets = new List<int>();
        public List<int> Fingers = new List<int>();
        public List<UgBarre> Barres = new List<UgBarre>();
    }

    /// <summary>A barre ("capo" in UG's data): strings counted from the high E (0).</summary>
    public sealed class UgBarre
    {
        public int Fret, StartString, LastString, Finger = 1;
    }
}
