using System.Collections.Generic;
using System.Linq;
using GuitarTutor.Core.Music;
using Newtonsoft.Json;

namespace GuitarTutor.Core.Songs
{
    /// <summary>
    /// A song in layers (see docs/plan.md, "Song data"): the skeleton (sections, chords, lyrics), the rhythm guitar
    /// (strumming patterns, chord voicings), lead lines and riffs, the timing (tempo map), and where each part came
    /// from. Positions are in quarter notes from the start of the song; the tempo map turns them into bars and seconds.
    /// </summary>
    public sealed class Song
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public SongInfo Info { get; set; } = new SongInfo();
        public TempoMap Tempo { get; set; } = new TempoMap(120, TimeSignature.FourFour);
        public List<Section> Sections { get; set; } = new List<Section>();
        public List<ChordEvent> Chords { get; set; } = new List<ChordEvent>();
        public List<LyricLine> Lyrics { get; set; } = new List<LyricLine>();
        public List<StrumPattern> Strumming { get; set; } = new List<StrumPattern>();
        public List<Voicing> Voicings { get; set; } = new List<Voicing>();
        public List<Riff> Riffs { get; set; } = new List<Riff>();
        /// <summary>Where sources disagree: the chords another source gives for a stretch (the chart editor offers them).</summary>
        public List<ChordAlternative> Alternatives { get; set; } = new List<ChordAlternative>();
        public List<Source> Sources { get; set; } = new List<Source>();

        /// <summary>The chord sounding at a position (null before the first chord or in a gap).</summary>
        public ChordEvent ChordAt(double quarter) =>
            Chords.LastOrDefault(c => c.AtQuarter <= quarter && quarter < c.AtQuarter + c.Quarters);

        /// <summary>The chords that start in, or carry into, bar <paramref name="bar"/>.</summary>
        public IEnumerable<ChordEvent> ChordsInBar(int bar)
        {
            var start = Tempo.QuarterOfBar(bar);
            var end = start + Tempo.SignatureAtBar(bar).QuartersPerBar;
            return Chords.Where(c => c.AtQuarter < end && c.AtQuarter + c.Quarters > start);
        }

        public Section SectionAt(double quarter) =>
            Sections.LastOrDefault(s => s.AtQuarter <= quarter && quarter < s.AtQuarter + s.Quarters);

        public Voicing VoicingFor(ChordEvent chord) =>
            Voicings.FirstOrDefault(v => v.Id == chord.Voicing) ?? Voicings.FirstOrDefault(v => v.Chord == chord.Name);
    }

    public sealed class SongInfo
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        /// <summary>The key as written, e.g. "Bm".</summary>
        public string Key { get; set; } = "";
        public int Capo { get; set; }
        /// <summary>The open strings, lowest first, e.g. E2 A2 D3 G3 B3 E4 for standard tuning.</summary>
        public List<string> Tuning { get; set; } = new List<string> { "E2", "A2", "D3", "G3", "B3", "E4" };
    }

    /// <summary>A named part of the song: Intro, Verse 1, Chorus...</summary>
    public sealed class Section
    {
        public string Name { get; set; } = "";
        public double AtQuarter { get; set; }
        public double Quarters { get; set; }
    }

    /// <summary>A chord and how long it lasts; <c>Voicing</c> names its shape in <see cref="Song.Voicings"/>.</summary>
    public sealed class ChordEvent
    {
        public string Name { get; set; } = "";
        public double AtQuarter { get; set; }
        public double Quarters { get; set; }
        public string Voicing { get; set; }
    }

    /// <summary>A line of lyrics; each word starts at a position, so the line can be shown with its chords.</summary>
    public sealed class LyricLine
    {
        public double AtQuarter { get; set; }
        public List<LyricWord> Words { get; set; } = new List<LyricWord>();
        [JsonIgnore] public string Text => string.Join(" ", Words.Select(w => w.Text));
    }

    public sealed class LyricWord
    {
        public string Text { get; set; } = "";
        public double AtQuarter { get; set; }
    }

    public enum Stroke
    {
        Rest,
        Down,
        Up,
        MutedDown,
        MutedUp,
    }

    /// <summary>One bar of strumming on a grid (Subdivision 8: eighth notes).</summary>
    public sealed class StrumPattern
    {
        public string Name { get; set; } = "";
        public int Subdivision { get; set; } = 8;
        public List<Stroke> Strokes { get; set; } = new List<Stroke>();
        /// <summary>The sections it is used in (empty: the whole song).</summary>
        public List<string> Sections { get; set; } = new List<string>();
    }

    /// <summary>A chord shape: per string from the lowest, the fret (-1 not played, 0 open) and finger (0 none, 1 index ... 4 little).</summary>
    public sealed class Voicing
    {
        public string Id { get; set; } = "";
        public string Chord { get; set; } = "";
        public List<int> Frets { get; set; } = new List<int>();
        public List<int> Fingers { get; set; } = new List<int>();
        public Barre Barre { get; set; }
    }

    public sealed class Barre
    {
        public int Fret { get; set; }
        public int Finger { get; set; } = 1;
        /// <summary>Strings covered, counted from the lowest (0) up.</summary>
        public int FirstString { get; set; }
        public int LastString { get; set; }
    }

    /// <summary>A lead line or riff, as tab notes.</summary>
    public sealed class Riff
    {
        public string Name { get; set; } = "";
        public List<TabNote> Notes { get; set; } = new List<TabNote>();
    }

    public sealed class TabNote
    {
        /// <summary>String from the lowest (0).</summary>
        public int StringIndex { get; set; }
        public int Fret { get; set; }
        public double AtQuarter { get; set; }
        public double Quarters { get; set; }
        /// <summary>As written in tab: "b" bend, "s" slide, "h" hammer-on, "p" pull-off, "~" vibrato; empty if none.</summary>
        public string Technique { get; set; } = "";
    }

    /// <summary>Another source's chords for a stretch of the song (as names, in order).</summary>
    public sealed class ChordAlternative
    {
        public double AtQuarter { get; set; }
        public double Quarters { get; set; }
        public List<string> Chords { get; set; } = new List<string>();
        public string Source { get; set; } = "";
    }

    /// <summary>Where a part of the song came from, and which parts it supplied.</summary>
    public sealed class Source
    {
        public string Kind { get; set; } = "";
        public string Url { get; set; } = "";
        public string Revision { get; set; } = "";
        public List<string> Supplies { get; set; } = new List<string>();
    }
}
