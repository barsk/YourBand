using System.Text.RegularExpressions;

namespace YourBand.Core.Music
{
    /// <summary>Recognises chord names as they are written in chord sheets: Bm, F#m7, Cmaj7, Dsus4, G/B, Am7b5, E5...</summary>
    public static class ChordName
    {
        static readonly Regex Pattern = new Regex(
            @"^[A-G](#|b)?(maj|min|m|M|dim|aug|sus|add|\+|°|ø)?[0-9]{0,2}((sus|add|maj|b|#)[0-9]{1,2})*(\([^)]*\))?(/[A-G](#|b)?)?$",
            RegexOptions.CultureInvariant);

        public static bool IsChord(string token) => !string.IsNullOrEmpty(token) && Pattern.IsMatch(token);
    }
}
