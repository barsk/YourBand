using System.Linq;
using System.Net;
using GuitarTutor.Core.Import.UltimateGuitar;
using GuitarTutor.Core.Songs;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GuitarTutor.Core.Tests
{
    /// <summary>The Ultimate Guitar importer, on made-up pages in UG's format (no real song text in the repository).</summary>
    public class UltimateGuitarTests
    {
        const string ChordsContent =
            "[Intro]\n" +
            "[ch]Bm[/ch]  [ch]A[/ch]  [ch]E[/ch]    x2\n" +
            "\n" +
            "[Verse 1]\n" +
            "[tab][ch]Bm[/ch]          [ch]A[/ch]          [ch]E[/ch]\n" +
            "one two three four five six seven[/tab]\n" +
            "[tab]      [ch]G[/ch]           [ch]D[/ch]\n" +
            "pick eight nine ten[/tab]\n";

        static string TabsContent(string intro) =>
            "[Intro]\n" +
            $"Part 1  {intro}\n" +
            "e|----------------|\n" +
            "B|--3---5b--------|\n" +
            "G|----------------|\n" +
            "D|----------------|\n" +
            "A|----------------|\n" +
            "E|----------------| x2\n" +
            "\n" +
            "[Verse 1]\n" +
            "Bm          A          E\n" +
            "one two three four five six seven\n" +
            "e|--2-----|\n" +
            "B|--------|\n" +
            "G|-----4--|\n" +
            "D|--------|\n" +
            "A|--------|\n" +
            "E|--------|\n" +
            "      G           D           A\n" +
            "pick eight nine ten\n";

        static string Page(string kind, string content, bool withStrumAndShapes)
        {
            var view = new JObject
            {
                ["meta"] = new JObject { ["tonality"] = "Bm", ["tuning"] = new JObject { ["value"] = "E A D G B E" } },
                ["wiki_tab"] = new JObject { ["content"] = content, ["revision_id"] = "3" },
            };
            if (withStrumAndShapes)
            {
                view["strummings"] = new JArray(new JObject
                {
                    ["part"] = "Whole song", ["denuminator"] = 8, ["bpm"] = 112,
                    ["measures"] = new JArray(new[] { 1, 202, 1, 101, 202, 101, 1, 101 }.Select(m => new JObject { ["measure"] = m })),
                });
                view["applicature"] = new JObject
                {
                    ["Bm"] = new JArray(new JObject
                    {
                        ["id"] = "x24432", ["frets"] = new JArray(2, 3, 4, 4, 2, -1), ["fingers"] = new JArray(0, 2, 4, 3, 0, 0),
                        ["listCapos"] = new JArray(new JObject { ["fret"] = 2, ["startString"] = 0, ["lastString"] = 4, ["finger"] = 1 }),
                    }),
                };
            }
            var store = new JObject
            {
                ["store"] = new JObject
                {
                    ["page"] = new JObject
                    {
                        ["data"] = new JObject
                        {
                            ["tab"] = new JObject { ["type"] = kind, ["song_name"] = "Test Song", ["artist_name"] = "Tester", ["tab_url"] = "https://example/" + kind },
                            ["tab_view"] = view,
                        },
                    },
                },
            };
            return "<html><body><div class=\"js-store\" data-content=\"" + WebUtility.HtmlEncode(store.ToString()) + "\"></div></body></html>";
        }

        static UgPage ChordsPage => UgPage.Parse(Page("Chords", ChordsContent, true));
        static UgPage TabsPage(string intro = "00:00-00:17") => UgPage.Parse(Page("Tabs", TabsContent(intro), false));

        [Test]
        public void Page_ReadsTheEmbeddedData()
        {
            var page = ChordsPage;
            Assert.AreEqual("Chords", page.Kind);
            Assert.AreEqual("Test Song", page.Title);
            Assert.AreEqual("Bm", page.Key);
            Assert.AreEqual(112, page.Strummings[0].Bpm);
            Assert.AreEqual("x24432", page.Shapes["Bm"][0].Id);
            StringAssert.Contains("[ch]Bm[/ch]", page.Content);
        }

        [Test]
        public void Sheet_ChordsColumnsRepeatsAndTabs()
        {
            var sheet = ChordSheet.Parse(ChordsContent);
            CollectionAssert.AreEqual(new[] { "Intro", "Verse 1" }, sheet.Sections.Select(s => s.Name));
            var intro = sheet.Sections[0].Lines[0].Chords;
            Assert.AreEqual(2, intro.Repeat);
            CollectionAssert.AreEqual(new[] { 0, 4, 7 }, intro.Chords.Select(c => c.Column));  // columns without the tags
            var verse = sheet.Sections[1].Lines[0].Chords;
            Assert.AreEqual("one two three four five six seven", verse.Lyric);

            var tabs = ChordSheet.Parse(TabsContent("00:00-00:17"));
            var block = tabs.Sections[0].Lines.First(l => l.Tab != null).Tab;
            Assert.AreEqual(6, block.Strings.Count);
            Assert.AreEqual(2, block.Repeat);
            Assert.AreEqual((0.0, 17.0), block.Time);
            CollectionAssert.AreEqual(new[] { "Bm", "A", "E" }, tabs.Sections[1].Lines[0].Chords.Chords.Select(c => c.Name));  // plain-text chords
        }

        [Test]
        public void Import_GuessesChordLengthsInWholeBars()
        {
            var song = UgImporter.Import(ChordsPage, null, out _);
            // Bm A E: a bar each, E stretched to fill four bars; G D: a bar each; the intro twice
            var lengths = song.Chords.Select(c => c.Quarters / 4).ToArray();
            CollectionAssert.AreEqual(new double[] { 1, 1, 2, 1, 1, 2, 1, 1, 2, 1, 1 }, lengths);
            Assert.AreEqual("Verse 1", song.SectionAt(33).Name);
            Assert.AreEqual(32, song.Sections[1].AtQuarter);
        }

        [Test]
        public void Import_PlacesWordsUnderTheirChords()
        {
            var song = UgImporter.Import(ChordsPage, null, out _);
            var line = song.Lyrics[0];
            Assert.AreEqual(32, line.Words[0].AtQuarter);                               // "one" under Bm
            // "four" is at column 14, two columns after the A (column 12): two thirds of a quarter into the A
            // (A to E is 12 columns over 4 quarters), rounded to sixteenths
            var a = song.Chords.First(c => c.Name == "A" && c.AtQuarter >= 32);
            Assert.AreEqual(a.AtQuarter + 0.75, line.Words.First(w => w.Text == "four").AtQuarter, 1e-9);
            var pickup = song.Lyrics[1].Words[0];
            Assert.AreEqual("pick", pickup.Text);
            Assert.Less(pickup.AtQuarter, song.Chords.First(c => c.Name == "G").AtQuarter);  // before the chord: a pickup
        }

        [Test]
        public void Import_ShapesStrummingAndTuning()
        {
            var song = UgImporter.Import(ChordsPage, null, out var report);
            var bm = song.VoicingFor(song.Chords[0]);
            CollectionAssert.AreEqual(new[] { -1, 2, 4, 4, 3, 2 }, bm.Frets);  // x24432, lowest string first
            Assert.AreEqual(1, bm.Barre.FirstString);
            Assert.AreEqual(5, bm.Barre.LastString);
            CollectionAssert.AreEqual(new[] { Stroke.Down, Stroke.Rest, Stroke.Down, Stroke.Up, Stroke.Rest, Stroke.Up, Stroke.Down, Stroke.Up },
                                      song.Strumming[0].Strokes);
            CollectionAssert.AreEqual(new[] { "E2", "A2", "D3", "G3", "B3", "E4" }, song.Info.Tuning);
            Assert.IsTrue(report.Notes.Any(n => n.Contains("No chord shapes") && n.Contains("G")));
        }

        [Test]
        public void Merge_TabsPageGivesRiffsAndAlternatives()
        {
            var song = UgImporter.Import(ChordsPage, TabsPage(), out var report);
            Assert.IsTrue(report.Notes.Any(n => n.Contains("fits")), string.Join("\n", report.Notes));
            var intro = song.Riffs.First(r => r.Name.StartsWith("Intro"));
            // 0:00-0:17 is the block twice; the first note is at column 2 of 16 of the first pass
            var pass = song.Tempo.QuarterAt(17) / 2;
            Assert.AreEqual(2.0 / 16 * pass, intro.Notes[0].AtQuarter, 1e-6);
            Assert.AreEqual(4, intro.Notes.Count);  // two notes, played twice
            Assert.AreEqual("b", intro.Notes[1].Technique);
            Assert.AreEqual(4, intro.Notes[0].StringIndex);  // the B string

            var fill = song.Riffs.First(r => r.Name.StartsWith("Fill"));
            var e = song.Chords.First(c => c.Name == "E" && c.AtQuarter >= 32);
            Assert.That(fill.Notes.All(n => n.AtQuarter >= e.AtQuarter && n.AtQuarter < e.AtQuarter + e.Quarters));

            var alternative = song.Alternatives.Single();
            CollectionAssert.AreEqual(new[] { "G", "D", "A" }, alternative.Chords);
        }

        [Test]
        public void Merge_TimeMarksScaleAWrongGuess()
        {
            // the recording's intro is half the guessed length: chords last half as long
            var song = UgImporter.Import(ChordsPage, TabsPage("00:00-00:09"), out var report);
            Assert.IsTrue(report.Notes.Any(n => n.Contains("x0.5")), string.Join("\n", report.Notes));
            Assert.AreEqual(2, song.Chords[0].Quarters);
        }
    }
}
