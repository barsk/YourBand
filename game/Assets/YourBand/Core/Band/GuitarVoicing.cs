using System.Collections.Generic;
using System.Linq;

namespace YourBand.Core.Band
{
    /// <summary>
    /// What Shreddage (the band guitar's plugin) can voice. Held in its strumming mode, the notes of a chord are fingered on a
    /// guitar in standard tuning, a note to a string, and the strum and string keys act on that voicing. A chord that cannot be
    /// fingered that way is not voiced at all, and then every key is silent: a song in another tuning (DGCFAD, drop D, DADGAD)
    /// has such chords, two notes that only the low E string reaches (F2 and G2) being the plainest. Measured with plugin_probe
    /// on 43 chords of a song in DGCFAD (docs/shreddage-keyswitches.md): the chords every one of whose notes can go to a string
    /// of its own, each string reaching one semitone below its open note, sounded, the others (18 of them) were silent.
    /// <see cref="Voice"/> keeps a chord that can be fingered as it is, and otherwise moves as few of its notes an octave as
    /// makes it so (a note is left out only when no octave helps).
    /// </summary>
    public static class GuitarVoicing
    {
        /// <summary>The lowest and highest notes Shreddage plays (D#2 and E5; from F#5 up its keys are keyswitches).</summary>
        public const int Lowest = 39, Highest = 76;

        /// <summary>The open strings of a guitar in standard tuning, lowest first (E2 A2 D3 G3 B3 E4).</summary>
        static readonly int[] Open = { 40, 45, 50, 55, 59, 64 };

        /// <summary>How far up the neck a note may be fingered: past what a chord in range needs.</summary>
        const int MaxFret = 15;

        /// <summary>A note in the plugin's range: octaves up from below it, octaves down from above it.</summary>
        public static int Fold(int pitch)
        {
            while (pitch < Lowest)
                pitch += 12;
            while (pitch > Highest && pitch - 12 >= Lowest)
                pitch -= 12;
            return pitch;
        }

        /// <summary>Whether each of the notes (distinct, in range) can go to a string of its own, a string reaching one semitone below its open note.</summary>
        public static bool CanVoice(IReadOnlyCollection<int> pitches)
        {
            if (pitches.Count > Open.Length)
                return false;
            var holder = new int[Open.Length];  // the note on each string, or 0
            bool Place(int pitch, bool[] tried)
            {
                for (int s = 0; s < Open.Length; ++s)
                {
                    var fret = pitch - Open[s];
                    if (tried[s] || fret < -1 || fret > MaxFret)
                        continue;
                    tried[s] = true;
                    if (holder[s] == 0 || Place(holder[s], tried))
                    {
                        holder[s] = pitch;
                        return true;
                    }
                }
                return false;
            }
            return pitches.All(p => Place(p, new bool[Open.Length]));
        }

        /// <summary>
        /// The pitch each note of a chord is played at so that the chord can be voiced: itself (in range) if the chord can be
        /// fingered, else with the fewest octave moves (a downward move costs a little more than an upward one, so a low note
        /// goes up before a high one goes down). A note with no place is absent from the result, and two notes may end at one
        /// pitch. The notes the plugin is given are the distinct values.
        /// </summary>
        public static Dictionary<int, int> Voice(IEnumerable<int> pitches)
        {
            var raw = pitches.Distinct().OrderBy(p => p).ToList();
            var plain = raw.ToDictionary(p => p, Fold);
            if (CanVoice(plain.Values.Distinct().ToList()))
                return plain;
            // the choices for each note: its folded pitch, an octave or two either way, or left out
            var options = raw.Select(p =>
            {
                var home = Fold(p);
                return new[] { 0, 12, -12, 24, -24 }.Select(shift => (pitch: home + shift, cost: System.Math.Abs(shift) / 12.0 + (shift < 0 ? 0.01 : 0.0)))
                    .Where(o => o.pitch >= Lowest && o.pitch <= Highest).ToList();
            }).ToList();
            const double LeftOut = 5.0;
            var best = (cost: double.MaxValue, choice: (int?[])null);
            var chosen = new int?[raw.Count];
            var voiced = new List<int>();

            void Search(int i, double cost)
            {
                if (cost >= best.cost)
                    return;
                if (i == raw.Count)
                {
                    best = (cost, (int?[])chosen.Clone());
                    return;
                }
                foreach (var (pitch, extra) in options[i])
                {
                    var fresh = !voiced.Contains(pitch);
                    if (fresh)
                    {
                        voiced.Add(pitch);
                        if (!CanVoice(voiced))
                        {
                            voiced.RemoveAt(voiced.Count - 1);
                            continue;
                        }
                    }
                    chosen[i] = pitch;
                    Search(i + 1, cost + extra);
                    if (fresh)
                        voiced.RemoveAt(voiced.Count - 1);
                }
                chosen[i] = null;  // left out
                Search(i + 1, cost + LeftOut);
            }
            Search(0, 0.0);
            var result = new Dictionary<int, int>();
            for (int i = 0; i < raw.Count; ++i)
                if (best.choice != null && best.choice[i] is int pitch)
                    result[raw[i]] = pitch;
            return result;
        }
    }
}
