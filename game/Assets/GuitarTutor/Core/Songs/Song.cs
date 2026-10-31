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
        /// <summary>The other instruments of the source (a Guitar Pro file's harmonica, lead guitar, vocal, bass): their notes.</summary>
        public List<SongPart> Parts { get; set; } = new List<SongPart>();
        /// <summary>
        /// Every track of a Guitar Pro file as written, the strummed guitar's too: what the band's instruments can play
        /// (docs/instrument-tracks.md). Empty for a song from Ultimate Guitar pages.
        /// </summary>
        public List<SongTrack> Tracks { get; set; } = new List<SongTrack>();

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
        /// <summary>Backing vocals (written in parentheses in chord sheets): the chart shows them dimmed.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Backing { get; set; }
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
        /// <summary>A stroke with an accent (UG's arrow with a > mark): played harder, drawn with a > over the arrow.</summary>
        AccentDown,
        AccentUp,
    }

    /// <summary>One bar of strumming on a grid (Subdivision 8: eighth notes).</summary>
    public sealed class StrumPattern
    {
        public string Name { get; set; } = "";
        public int Subdivision { get; set; } = 8;
        public List<Stroke> Strokes { get; set; } = new List<Stroke>();
        /// <summary>The sections it is used in (empty: the whole song).</summary>
        public List<string> Sections { get; set; } = new List<string>();
        /// <summary>Where the pattern comes from: "guitar-pro", "ultimate-guitar" or "default" (see SongOrigin); empty in older songs.</summary>
        public string Origin { get; set; } = "";
        /// <summary>
        /// What the band plays, when that is more than the pattern shown: the strokes bar after bar over the section, laid out
        /// from its first bar like a long pattern (see SongChart.StrokeIndex). The pattern itself stays the general one, short
        /// enough to remember and show; this is the song's own variations of it. Null: the band plays the pattern.
        /// </summary>
        public List<Stroke> Played { get; set; }

        /// <summary>The pattern the band plays: this one, or a pattern made of its <see cref="Played"/> strokes.</summary>
        public StrumPattern AsPlayed() => Played == null || Played.Count == 0
            ? this
            : new StrumPattern { Name = Name, Subdivision = Subdivision, Strokes = Played, Sections = Sections, Origin = Origin };
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
        /// <summary>
        /// As written in tab: "b" bend, "s" slide, "h" hammer-on, "p" pull-off, "~" vibrato; empty if none. A track's tab
        /// (<see cref="Chart.ChartTab"/>) also has "x" for a dead note, and "g" before the symbol for a ghost note.
        /// </summary>
        public string Technique { get; set; } = "";
    }

    /// <summary>
    /// One instrument of the source file besides the rhythm guitar. <see cref="Voice"/> says which of the band's two
    /// lead channels plays it (1 or 2; 0: not played), the players being stand-ins for whatever instrument it is.
    /// </summary>
    public sealed class SongPart
    {
        public string Name { get; set; } = "";
        /// <summary>The file's General MIDI program for the track (22: harmonica).</summary>
        public int Program { get; set; }
        /// <summary>"lead", "vocal" or "bass".</summary>
        public string Role { get; set; } = "lead";
        public int Voice { get; set; }
        public List<PartNote> Notes { get; set; } = new List<PartNote>();
    }

    /// <summary>A note of a part: its sounding MIDI pitch (capo included), when, how long, how hard.</summary>
    public sealed class PartNote
    {
        public int Pitch { get; set; }
        public double AtQuarter { get; set; }
        public double Quarters { get; set; }
        public int Velocity { get; set; } = 84;
    }

    /// <summary>
    /// A track of the source file, with its notes as played (repeats and jumps unrolled). A track whose notes are the
    /// same as an earlier one's (a second rhythm guitar doubling the first) names it in <see cref="SameAs"/> and has
    /// no notes of its own.
    /// </summary>
    public sealed class SongTrack
    {
        /// <summary>The track's place in the file (0-based).</summary>
        public int Index { get; set; }
        public string Name { get; set; } = "";
        /// <summary>The file's General MIDI program (25: steel-string guitar; 22: harmonica).</summary>
        public int Program { get; set; }
        /// <summary>What it seems to be: "guitar", "bass", "keys", "drums", "voice" or "other" (from its name, program and strings).</summary>
        public string Kind { get; set; } = "other";
        /// <summary>
        /// The open strings, lowest first, as note names, for a track written as tab (its notes have a string and a fret);
        /// empty for one that is not (piano, drums).
        /// </summary>
        public List<string> Tuning { get; set; } = new List<string>();
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int Capo { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string SameAs { get; set; }
        public List<TrackNote> Notes { get; set; } = new List<TrackNote>();
    }

    /// <summary>A note of a track.</summary>
    public sealed class TrackNote
    {
        public double AtQuarter { get; set; }
        public double Quarters { get; set; }
        /// <summary>The sounding MIDI pitch (capo included); on a drum track the General MIDI drum note (36 kick, 38 snare).</summary>
        public int Pitch { get; set; }
        /// <summary>The string from the lowest (0) and the fret as written (above the capo); -1 where the track is not tab.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore), System.ComponentModel.DefaultValue(-1)]
        public int StringIndex { get; set; } = -1;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore), System.ComponentModel.DefaultValue(-1)]
        public int Fret { get; set; } = -1;
        /// <summary>1-127, from the written dynamics (pp 31, mf 79, ff 111) and accents (louder) or a ghost note (softer).</summary>
        public int Velocity { get; set; } = 79;
        /// <summary>
        /// How it is played, a letter each: b bend, s slide, h hammer-on or pull-off (to the next note), ~ vibrato, m palm
        /// mute, x dead (muted) note, g ghost note, a accent, . staccato, r let ring, n harmonic, t tapped, z tremolo
        /// picking, q grace note; D or U the pick stroke the file records for its beat (down, up). Empty: plainly.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore), System.ComponentModel.DefaultValue("")]
        public string Technique { get; set; } = "";
        /// <summary>A bend's highest point, in semitones (1: a half step, 2: a whole step); 0 if none.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public double Bend { get; set; }
        /// <summary>
        /// A bend's shape as the file draws it: points of [where in the note, 0 its start to 1 its end; semitones above
        /// the note], so a bend, a release, a pre-bend or a bend and release plays as written; null if none.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<double[]> BendPoints { get; set; }
        /// <summary>
        /// A slide out of the note: "shift" (to the next note, picked again), "legato" (to the next note, not picked),
        /// "down" or "up" (out and away); "" none.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore), System.ComponentModel.DefaultValue("")]
        public string SlideOut { get; set; } = "";
        /// <summary>A slide into the note: "below" or "above" (from a few frets away); "" none.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore), System.ComponentModel.DefaultValue("")]
        public string SlideIn { get; set; } = "";
        /// <summary>Vibrato: "slight" or "wide"; "" none.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore), System.ComponentModel.DefaultValue("")]
        public string Vibrato { get; set; } = "";
        /// <summary>Tremolo picking: each repeated note's length in quarter notes (0.25: sixteenths); 0 if none.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public double TremoloQuarters { get; set; }
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
