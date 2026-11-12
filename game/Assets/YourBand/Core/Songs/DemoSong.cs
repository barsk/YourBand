using System.Linq;
using YourBand.Core.Music;

namespace YourBand.Core.Songs
{
    /// <summary>
    /// A short made-up song with every layer filled in: what the game plays when no song is loaded, and what the
    /// screenshots in the repository show (imported songs and their lyrics stay out of git).
    /// </summary>
    public static class DemoSong
    {
        public static Song Create()
        {
            var song = new Song
            {
                Info = { Title = "Practice Room", Artist = "YourBand demo", Key = "G" },
                Tempo = new TempoMap(96, TimeSignature.FourFour),
            };
            // Intro 4 bars, verse 8, chorus 8, bridge 4, chorus 8, outro 4
            double at = 0;
            at = Part(song, at, "Intro", "G D Em C");
            var verse = at;
            at = Part(song, at, "Verse", "G D Em C G D C C");
            var chorus = at;
            at = Part(song, at, "Chorus", "C D G Em C D G G");
            var bridge = at;
            at = Part(song, at, "Bridge", "Bm C D D");
            var chorus2 = at;
            at = Part(song, at, "Chorus", "C D G Em C D G G");
            Part(song, at, "Outro", "G D Em C");

            Line(song, verse + 0, "Morning light across the kitchen floor", 0, 1, 1.5, 2.5, 3, 4);
            Line(song, verse + 8, "coffee waiting by the open door", 0, 1, 2, 2.5, 3, 4);
            Line(song, verse + 16, "strings in tune and the click is set", 0, 1, 1.5, 2, 2.5, 3, 4, 5);
            Line(song, verse + 24, "the best take is the one I haven't played yet", 0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 5);
            foreach (var c in new[] { chorus, chorus2 })
            {
                Line(song, c + 0, "Play it again, play it slow", 0, 1, 2, 4, 5, 6);
                Line(song, c + 8, "let the changes come and go", 0, 1, 2, 3, 4, 5);
                Line(song, c + 16, "count me in and I'll follow you", 0, 1, 1.5, 2, 3, 3.5, 4);
                Line(song, c + 24, "one more time the whole way through", -0.5, 0, 1, 1.5, 2, 3, 4);
            }
            Line(song, bridge + 0, "hold that chord and let it ring", 0, 1, 2, 3, 4, 5, 6);

            song.Strumming.Add(Pattern("Verse", new[] { "Verse", "Intro", "Outro" }, "D-DU-UDU"));
            song.Strumming.Add(Pattern("Chorus", new[] { "Chorus", "Bridge" }, "D-DUDUDU"));
            song.Strumming.Add(Pattern("Simple", new string[0], "D-D-D-DU"));

            song.Voicings.Add(Shape("G-320003", "G", new[] { 3, 2, 0, 0, 0, 3 }, new[] { 2, 1, 0, 0, 0, 3 }));
            song.Voicings.Add(Shape("D-xx0232", "D", new[] { -1, -1, 0, 2, 3, 2 }, new[] { 0, 0, 0, 1, 3, 2 }));
            song.Voicings.Add(Shape("Em-022000", "Em", new[] { 0, 2, 2, 0, 0, 0 }, new[] { 0, 2, 3, 0, 0, 0 }));
            song.Voicings.Add(Shape("C-x32010", "C", new[] { -1, 3, 2, 0, 1, 0 }, new[] { 0, 3, 2, 0, 1, 0 }));
            var bm = Shape("Bm-x24432", "Bm", new[] { -1, 2, 4, 4, 3, 2 }, new[] { 0, 1, 3, 4, 2, 1 });
            bm.Barre = new Barre { Fret = 2, Finger = 1, FirstString = 1, LastString = 5 };
            song.Voicings.Add(bm);
            foreach (var c in song.Chords)
                c.Voicing = song.Voicings.First(v => v.Chord == c.Name).Id;

            // An intro lick over G and D: G major pentatonic on the top strings
            var lick = new Riff { Name = "Intro lick" };
            (int s, int f, double q, string t)[] notes =
            {
                (3, 0, 0, ""), (3, 2, 0.5, "h"), (4, 0, 1, ""), (4, 3, 1.5, ""), (5, 3, 2, "~"),
                (4, 3, 4.5, ""), (4, 0, 5, ""), (3, 2, 5.5, ""), (3, 0, 6, "p"), (2, 0, 6.5, ""), (2, 2, 7, "s"),
            };
            foreach (var n in notes)
                lick.Notes.Add(new TabNote { StringIndex = n.s, Fret = n.f, AtQuarter = n.q, Quarters = 0.5, Technique = n.t });
            song.Riffs.Add(lick);

            song.Sources.Add(new Source { Kind = "demo", Supplies = { "everything" } });
            return song;
        }

        /// <summary>A section of whole-bar chords; returns where it ends.</summary>
        static double Part(Song song, double at, string name, string chords)
        {
            var names = chords.Split(' ');
            song.Sections.Add(new Section { Name = name, AtQuarter = at, Quarters = names.Length * 4 });
            for (int i = 0; i < names.Length; ++i)
            {
                // a chord held over the next bar becomes one longer chord, as imports give them
                var last = song.Chords.LastOrDefault();
                if (i > 0 && last != null && last.Name == names[i])
                    last.Quarters += 4;
                else
                    song.Chords.Add(new ChordEvent { Name = names[i], AtQuarter = at + i * 4, Quarters = 4 });
            }
            return at + names.Length * 4;
        }

        static void Line(Song song, double at, string text, params double[] offsets)
        {
            var words = text.Split(' ');
            if (words.Length != offsets.Length)
                throw new System.ArgumentException($"{words.Length} words but {offsets.Length} positions: {text}");
            var line = new LyricLine { AtQuarter = at + offsets[0] };
            for (int i = 0; i < words.Length; ++i)
                line.Words.Add(new LyricWord { Text = words[i], AtQuarter = at + offsets[i] });
            song.Lyrics.Add(line);
        }

        static StrumPattern Pattern(string name, string[] sections, string strokes) => new StrumPattern
        {
            Name = name,
            Subdivision = 8,
            Strokes = strokes.Select(c => c == 'D' ? Stroke.Down : c == 'U' ? Stroke.Up : Stroke.Rest).ToList(),
            Sections = sections.ToList(),
        };

        static Voicing Shape(string id, string chord, int[] frets, int[] fingers) => new Voicing
        {
            Id = id, Chord = chord, Frets = frets.ToList(), Fingers = fingers.ToList(),
        };
    }
}
