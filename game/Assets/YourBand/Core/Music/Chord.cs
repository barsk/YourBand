using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace YourBand.Core.Music
{
    /// <summary>
    /// A chord name read as notes: its root, the intervals above the root (in semitones, the root as 0, extensions
    /// above the octave as 14, 17, 21), and the bass note (the root unless written as a slash chord).
    /// </summary>
    public sealed class Chord
    {
        static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        static readonly Regex Shape = new Regex(@"^([A-G][#b]?)([^/]*)(?:/([A-G][#b]?))?$", RegexOptions.CultureInvariant);

        public string Name { get; private set; } = "";
        /// <summary>Pitch class of the root: 0 is C, 11 is B.</summary>
        public int Root { get; private set; }
        /// <summary>Pitch class of the lowest note.</summary>
        public int Bass { get; private set; }
        public IReadOnlyList<int> Intervals { get; private set; } = new[] { 0, 4, 7 };

        public bool IsMinor => Intervals.Contains(3) && !Intervals.Contains(4);

        /// <summary>The chord's pitch classes (0..11), the root first.</summary>
        public IEnumerable<int> PitchClasses => Intervals.Select(i => (Root + i) % 12).Distinct();

        public override string ToString() => Name;

        /// <summary>The pitch class of a note name ("C#" is 1, "Bb" is 10), or -1.</summary>
        public static int PitchClass(string note)
        {
            if (string.IsNullOrEmpty(note))
                return -1;
            var index = Array.IndexOf(Names, note.Substring(0, 1));
            if (index < 0)
                return -1;
            foreach (var c in note.Substring(1))
                index += c == '#' ? 1 : c == 'b' ? -1 : 0;
            return (index % 12 + 12) % 12;
        }

        public static string NoteName(int pitchClass) => Names[(pitchClass % 12 + 12) % 12];

        public static bool TryParse(string name, out Chord chord)
        {
            chord = null;
            var m = Shape.Match(name?.Trim() ?? "");
            if (!m.Success)
                return false;
            var intervals = Quality(m.Groups[2].Value);
            if (intervals == null)
                return false;
            var root = PitchClass(m.Groups[1].Value);
            chord = new Chord
            {
                Name = name.Trim(),
                Root = root,
                Bass = m.Groups[3].Success ? PitchClass(m.Groups[3].Value) : root,
                Intervals = intervals,
            };
            return true;
        }

        public static Chord Parse(string name) =>
            TryParse(name, out var chord) ? chord : throw new FormatException("not a chord name: " + name);

        // The part after the root: "m7b5", "maj7", "sus4", "add9", "6", "(add9)"... null if it cannot be read.
        static IReadOnlyList<int> Quality(string q)
        {
            int? third = 4, seventh = null;
            int fifth = 7;
            bool major = false, diminished = false;
            var extra = new List<int>();

            bool Take(string s)
            {
                if (!q.StartsWith(s, StringComparison.Ordinal))
                    return false;
                q = q.Substring(s.Length);
                return true;
            }
            int? Number()
            {
                var digits = new string(q.TakeWhile(char.IsDigit).ToArray());
                if (digits.Length == 0)
                    return null;
                q = q.Substring(digits.Length);
                return int.Parse(digits);
            }

            q = q.Replace("(", "").Replace(")", "");
            if (Take("maj") || Take("Maj") || Take("M") || Take("Δ"))
                major = true;
            else if (Take("min") || Take("m") || Take("-"))
                third = 3;
            else if (Take("dim") || Take("°"))
            {
                third = 3;
                fifth = 6;
                diminished = true;
            }
            else if (Take("aug") || Take("+"))
                fifth = 8;
            else if (Take("ø"))
            {
                third = 3;
                fifth = 6;
                seventh = 10;
            }
            // m(maj7), mmaj7
            if (third == 3 && (Take("maj") || Take("M")))
                major = true;

            var n = Number();
            int Seventh() => major ? 11 : diminished ? 9 : 10;
            switch (n)
            {
                case null: break;
                case 5: third = null; break;
                case 2: extra.Add(2); break;
                case 6: extra.Add(9); break;
                case 69: extra.Add(9); extra.Add(14); break;
                case 7: seventh = Seventh(); break;
                case 9: seventh = Seventh(); extra.Add(14); break;
                case 11: seventh = Seventh(); extra.Add(14); extra.Add(17); break;
                case 13: seventh = Seventh(); extra.Add(14); extra.Add(21); break;
                default: return null;
            }

            while (q.Length > 0)
            {
                if (Take("sus2"))
                    third = 2;
                else if (Take("sus4") || Take("sus"))
                    third = 5;
                else if (Take("add"))
                {
                    var a = Number();
                    if (a == null)
                        return null;
                    extra.Add(a == 9 ? 14 : a == 11 ? 17 : a == 13 ? 21 : a == 2 ? 2 : a == 4 ? 5 : a == 6 ? 9 : -1);
                }
                else if (Take("maj") || Take("M"))
                {
                    if (Number() is int x && x >= 7)
                        seventh = 11;
                }
                else if (Take("b") || Take("-"))
                {
                    var a = Number();
                    if (a == 5) fifth = 6;
                    else if (a == 9) extra.Add(13);
                    else if (a == 13) extra.Add(20);
                    else return null;
                }
                else if (Take("#") || Take("+"))
                {
                    var a = Number();
                    if (a == 5) fifth = 8;
                    else if (a == 9) extra.Add(15);
                    else if (a == 11) extra.Add(18);
                    else return null;
                }
                else
                    return null;
            }
            if (extra.Contains(-1))
                return null;

            var set = new SortedSet<int> { 0, fifth };
            if (third != null)
                set.Add(third.Value);
            if (seventh != null)
                set.Add(seventh.Value);
            foreach (var e in extra)
                set.Add(e);
            return set.ToList();
        }
    }
}
