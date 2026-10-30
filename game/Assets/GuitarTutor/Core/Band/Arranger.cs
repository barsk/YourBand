using System;
using System.Collections.Generic;
using System.Linq;
using GuitarTutor.Core.Chart;
using GuitarTutor.Core.Music;
using GuitarTutor.Core.Songs;

namespace GuitarTutor.Core.Band
{
    public enum BandPart
    {
        Drums,
        Bass,
        Keys,
        /// <summary>The band guitarist's rhythm: the song's strumming on its chord shapes.</summary>
        Guitar,
        /// <summary>The song's first lead line (harmonica, lead guitar...), on whatever plugin is chosen for it.</summary>
        Lead1,
        /// <summary>The second.</summary>
        Lead2,
    }

    /// <summary>A note the band plays: a MIDI note number (drums: the General MIDI drum map), when, how long, how hard.</summary>
    public readonly struct BandNote
    {
        public BandPart Part { get; }
        public int Pitch { get; }
        public double AtQuarter { get; }
        public double Quarters { get; }
        public int Velocity { get; }
        /// <summary>A single-string note of the guitarist's (not one string of a strum): played as a note even where the guitar strums.</summary>
        public bool Single { get; }

        public BandNote(BandPart part, int pitch, double atQuarter, double quarters, int velocity, bool single = false)
        {
            Part = part;
            Pitch = pitch;
            AtQuarter = atQuarter;
            Quarters = quarters;
            Velocity = velocity;
            Single = single;
        }

        public override string ToString() => $"{Part} {Pitch} at {AtQuarter} for {Quarters} ({Velocity})";
    }

    /// <summary>General MIDI drum notes the arranger uses.</summary>
    public static class Drum
    {
        public const int Kick = 36, SideStick = 37, Snare = 38, ClosedHat = 42, LowTom = 45, PedalHat = 44,
            OpenHat = 46, MidTom = 47, Crash = 49, HighTom = 50, Ride = 51;
    }

    /// <summary>
    /// A stroke of the band guitarist's pick: when, down or up, how hard, and whether it is muted. A strum-
    /// capable guitar (Shreddage) plays it as a strum keyswitch on its held chord; the other guitars play the
    /// per-string notes the arranger writes for it.
    /// </summary>
    public readonly struct BandStroke
    {
        public double AtQuarter { get; }
        public bool Up { get; }
        public int Velocity { get; }
        public bool Muted { get; }

        public BandStroke(double atQuarter, bool up, int velocity, bool muted)
        {
            AtQuarter = atQuarter;
            Up = up;
            Velocity = velocity;
            Muted = muted;
        }
    }

    /// <summary>A string of the band guitarist's held chord: its pitch, and the chord's span.</summary>
    public readonly struct VoicingNote
    {
        public int Pitch { get; }
        public double AtQuarter { get; }
        public double Quarters { get; }

        public VoicingNote(int pitch, double atQuarter, double quarters)
        {
            Pitch = pitch;
            AtQuarter = atQuarter;
            Quarters = quarters;
        }
    }

    /// <summary>The band's parts for a whole song.</summary>
    public sealed class Arrangement
    {
        public string Style { get; set; } = "";
        /// <summary>What the two lead channels play in this song (the parts' names), empty for none.</summary>
        public string[] LeadNames { get; set; } = { "", "" };
        public List<BandNote> Notes { get; } = new List<BandNote>();
        /// <summary>The guitarist's strokes (its strumming on the chords), in time order.</summary>
        public List<BandStroke> Strokes { get; } = new List<BandStroke>();
        /// <summary>The guitarist's chords as held voicings, one note per string, in time order.</summary>
        public List<VoicingNote> Voicings { get; } = new List<VoicingNote>();
        /// <summary>The stretches where the guitarist plays single-string notes instead of strumming (from, to), in time order.</summary>
        public List<(double From, double To)> SingleSpans { get; } = new List<(double, double)>();
        public IEnumerable<BandNote> Part(BandPart part) => Notes.Where(n => n.Part == part);
    }

    /// <summary>
    /// A band style: per 4/4 bar, the drum hits, the bass rhythm (which chord tone on each hit) and the keys
    /// rhythm. Hits are at quarter-note offsets in the bar.
    /// </summary>
    public sealed class BandStyle
    {
        public enum Tone { Root, Fifth, Octave, Approach }

        public string Name { get; set; } = "";
        public List<(int piece, double at, int velocity)> Drums { get; } = new List<(int, double, int)>();
        /// <summary>Drum hits for quiet sections (intro, verse, outro) where they differ: a side stick for the snare.</summary>
        public bool SideStickWhenQuiet { get; set; }
        public List<(double at, double length, Tone tone)> Bass { get; } = new List<(double, double, Tone)>();
        public List<(double at, double length)> Keys { get; } = new List<(double, double)>();
        public int KeysVelocity { get; set; } = 64;
        /// <summary>The keyboard sound that suits the style, as the engine's keyboard plugin names it (Numa Player).</summary>
        public string KeysSound { get; set; } = "";
        /// <summary>The keys' volume in the engine's mixer for that sound (an organ sustains: it sits lower).</summary>
        public double KeysGain { get; set; } = 1.3;

        public static readonly IReadOnlyList<BandStyle> All = new[] { Ballad(), Rock(), Pop() };

        public static BandStyle Named(string name) => All.FirstOrDefault(s => s.Name == name) ?? All[0];

        /// <summary>Slow and soft: eighth-note hats, side stick in the quiet sections, a bass that walks into each change.</summary>
        static BandStyle Ballad()
        {
            var s = new BandStyle { Name = "Ballad", SideStickWhenQuiet = true, KeysVelocity = 60, KeysSound = "2 - E_PIANO/1 - EP-Mark I" };
            for (int i = 0; i < 8; ++i)
                s.Drums.Add((Drum.ClosedHat, i * 0.5, i % 2 == 0 ? 72 : 50));
            s.Drums.Add((Drum.Kick, 0, 100));
            s.Drums.Add((Drum.Kick, 1.5, 70));
            s.Drums.Add((Drum.Kick, 2, 88));
            s.Drums.Add((Drum.Snare, 1, 90));
            s.Drums.Add((Drum.Snare, 3, 92));
            s.Bass.Add((0, 1.5, Tone.Root));
            s.Bass.Add((1.5, 0.5, Tone.Root));
            s.Bass.Add((2, 1, Tone.Fifth));
            s.Bass.Add((3, 1, Tone.Approach));
            s.Keys.Add((0, 2));
            s.Keys.Add((2, 2));
            return s;
        }

        /// <summary>Driving eighths: louder hats (open on the and of 4), kick on 1, 3 and the and of 3, eighth-note bass.</summary>
        static BandStyle Rock()
        {
            var s = new BandStyle { Name = "Rock", KeysVelocity = 78, KeysSound = "5 - ORGANS/4 - ROCK B3", KeysGain = 0.45 };
            for (int i = 0; i < 8; ++i)
                s.Drums.Add((i == 7 ? Drum.OpenHat : Drum.ClosedHat, i * 0.5, i % 2 == 0 ? 96 : 72));  // the foot lets the hat open on the and of 4
            s.Drums.Add((Drum.Kick, 0, 110));
            s.Drums.Add((Drum.Kick, 2, 104));
            s.Drums.Add((Drum.Kick, 2.5, 90));
            s.Drums.Add((Drum.Snare, 1, 110));
            s.Drums.Add((Drum.Snare, 3, 112));
            for (int i = 0; i < 8; ++i)
                s.Bass.Add((i * 0.5, 0.45, i == 7 ? Tone.Approach : Tone.Root));
            s.Keys.Add((0, 1.5));
            s.Keys.Add((1.5, 2.5));
            return s;
        }

        /// <summary>Lighter and syncopated: kick on the and of 2, hat open on the and of 4, keys pushed ahead of the beat.</summary>
        static BandStyle Pop()
        {
            var s = new BandStyle { Name = "Pop", KeysVelocity = 70, KeysSound = "1 - A_PIANO/1 - Model D 1983", KeysGain = 2.0 };
            for (int i = 0; i < 8; ++i)
                s.Drums.Add((i == 7 ? Drum.OpenHat : Drum.ClosedHat, i * 0.5, i % 2 == 0 ? 84 : 60));
            s.Drums.Add((Drum.Kick, 0, 104));
            s.Drums.Add((Drum.Kick, 1.5, 84));
            s.Drums.Add((Drum.Kick, 2, 96));
            s.Drums.Add((Drum.Snare, 1, 100));
            s.Drums.Add((Drum.Snare, 3, 102));
            s.Bass.Add((0, 1, Tone.Root));
            s.Bass.Add((1.5, 0.5, Tone.Octave));
            s.Bass.Add((2, 1, Tone.Root));
            s.Bass.Add((3.5, 0.5, Tone.Approach));
            s.Keys.Add((0, 1.5));
            s.Keys.Add((1.5, 1.5));
            s.Keys.Add((3, 1));
            return s;
        }
    }

    /// <summary>
    /// Writes the band's parts from the chart (see docs/plan.md, "Song data": the arrangement layer): drums from
    /// the style with a crash at each section's start and a fill into the next section, bass on the chord roots
    /// walking into each change, keys voiced close around middle C, each chord moving as little as it can from
    /// the one before, and the guitarist strumming the song's patterns on its chord shapes. With a capo the
    /// chord names are the shapes played above it: the band plays them that much higher.
    /// </summary>
    public static class Arranger
    {
        /// <summary>The style a song gets unless you pick one: ballad below 120 BPM, rock from there.</summary>
        public static BandStyle DefaultStyle(Song song) => BandStyle.Named(song.Tempo.BpmAt(0) < 120 ? "Ballad" : "Rock");

        public const int BassLow = 28, BassHigh = 40;      // roots from E1 up to E2
        public const int KeysLow = 55, KeysHigh = 77;      // G3 to F5
        const int KeysCentre = 64;

        /// <summary>
        /// The band's parts for a song in a style. With <paramref name="connections"/>, the rhythm guitar, drums, bass and
        /// keyboards play the tracks connected to them instead of the parts they make up, and the lead guitar and instrument 1 play
        /// theirs (nothing where none is: the band makes up no lead part); without, the band makes up its parts and the
        /// lead channels play the song's lead parts (<see cref="SongPart.Voice"/>), as before there were connections.
        /// </summary>
        public static Arrangement Arrange(SongChart chart, BandStyle style = null, BandConnections connections = null)
        {
            style = style ?? DefaultStyle(chart.Song);
            var arrangement = new Arrangement { Style = style.Name };
            var notes = arrangement.Notes;
            var random = new Random(1);  // the same small variations every time
            int Humanise(int velocity) => Math.Max(1, Math.Min(127, velocity + random.Next(-5, 6)));
            var chords = chart.Song.Chords.Where(c => Chord.TryParse(c.Name, out _)).OrderBy(c => c.AtQuarter).ToList();
            var capo = Math.Max(0, chart.Song.Info.Capo);
            List<int> lastVoicing = null;
            var drumsTrack = connections?.TrackFor(chart.Song, Instrument.Drums);
            var bassTrack = connections?.TrackFor(chart.Song, Instrument.Bass);
            var keysTrack = connections?.TrackFor(chart.Song, Instrument.Keys);
            var rhythmTrack = connections?.TrackFor(chart.Song, Instrument.RhythmGuitar);

            foreach (var bar in chart.Bars)
            {
                var last = bar.Index == chart.Bars.Count - 1;
                var fourFour = bar.Signature.Equals(TimeSignature.FourFour);
                var nextBar = last ? null : chart.Bars[bar.Index + 1];
                var fill = nextBar != null && nextBar.StartsSection && bar.Quarters >= 2;
                var quiet = style.SideStickWhenQuiet && IsQuiet(bar.Section);

                // ------------------------------------------------ drums (made up unless they play the song's drum track)
                if (drumsTrack == null && (bar.StartsSection || bar.Index == 0 || last))
                    notes.Add(new BandNote(BandPart.Drums, Drum.Crash, bar.AtQuarter, 2, Humanise(bar.Index == 0 ? 96 : 108)));
                if (drumsTrack == null && last)
                    notes.Add(new BandNote(BandPart.Drums, Drum.Kick, bar.AtQuarter, 0.5, 110));
                else if (drumsTrack == null)
                {
                    var hits = fourFour ? style.Drums : GenericBar(bar.Signature);
                    var fillFrom = fill ? bar.Quarters - 1 : double.MaxValue;
                    foreach (var (piece, at, velocity) in hits)
                    {
                        if (at >= bar.Quarters || at >= fillFrom && piece != Drum.Kick)
                            continue;
                        if (piece == Drum.ClosedHat && at == 0 && bar.StartsSection)
                            continue;  // the crash takes the first hat
                        var p = piece == Drum.Snare && quiet ? Drum.SideStick : piece;
                        notes.Add(new BandNote(BandPart.Drums, p, bar.AtQuarter + at, 0.25, Humanise(velocity)));
                    }
                    if (fill)
                        for (int i = 0; i < 4; ++i)
                        {
                            var piece = i < 2 ? Drum.Snare : i == 2 ? Drum.HighTom : Drum.LowTom;
                            notes.Add(new BandNote(BandPart.Drums, piece, bar.AtQuarter + fillFrom + i * 0.25, 0.25, Humanise(76 + i * 10)));
                        }
                }

                // ------------------------------------------------ bass and keys, chord by chord
                var bassHits = last ? new List<(double at, double length, BandStyle.Tone tone)> { (0, bar.Quarters, BandStyle.Tone.Root) }
                    : fourFour ? style.Bass : GenericBass(bar.Signature);
                var keysHits = last ? new List<(double at, double length)> { (0, bar.Quarters) }
                    : fourFour ? style.Keys : new List<(double at, double length)> { (0, bar.Quarters) };
                foreach (var (at, length, tone) in bassTrack != null ? NoHits : WithChordChanges(bassHits, bar, chords))
                {
                    var chord = ChordAt(chords, bar.AtQuarter + at, out var span);
                    if (chord == null)
                        continue;
                    // walk into the next chord only if it comes at the end of this bar
                    var endsHere = span.AtQuarter + span.Quarters <= bar.AtQuarter + bar.Quarters + 1e-6;
                    var next = endsHere ? chords.FirstOrDefault(c => c.AtQuarter >= span.AtQuarter + span.Quarters - 1e-6) : null;
                    var pitch = BassPitch(chord, tone, next != null ? Chord.Parse(next.Name) : null) + capo;
                    var end = Math.Min(bar.AtQuarter + at + length, span.AtQuarter + span.Quarters);
                    notes.Add(new BandNote(BandPart.Bass, pitch, bar.AtQuarter + at, end - bar.AtQuarter - at, Humanise(at == 0 ? 100 : 86)));
                }
                foreach (var (at, length, _) in keysTrack != null ? NoHits : WithChordChanges(keysHits.Select(k => (k.at, k.length, BandStyle.Tone.Root)).ToList(), bar, chords))
                {
                    var chord = ChordAt(chords, bar.AtQuarter + at, out var span);
                    if (chord == null)
                        continue;
                    var voicing = Voice(chord, lastVoicing);
                    lastVoicing = voicing;
                    var end = Math.Min(bar.AtQuarter + at + length, span.AtQuarter + span.Quarters);
                    var velocity = Humanise(style.KeysVelocity + (at == 0 ? 6 : 0));
                    foreach (var pitch in voicing)
                        notes.Add(new BandNote(BandPart.Keys, pitch + capo, bar.AtQuarter + at, end - bar.AtQuarter - at, velocity));
                }
            }
            // the drums, bass and keyboards that play the song's tracks: their notes as the file has them (a dead note's
            // pitch is its open string's: it is not one to play)
            foreach (var (track, part) in new[] { (drumsTrack, BandPart.Drums), (bassTrack, BandPart.Bass), (keysTrack, BandPart.Keys) })
                if (track != null)
                    foreach (var n in track.Notes)
                        if (!n.Technique.Contains('x'))
                            notes.Add(new BandNote(part, n.Pitch, n.AtQuarter, Math.Max(0.05, n.Quarters), n.Velocity));
            if (rhythmTrack != null && connections.PlaysPicked(chart.Song, Instrument.RhythmGuitar))
                PickedRhythm(rhythmTrack, arrangement);
            else if (rhythmTrack != null)
                TrackRhythm(chart, rhythmTrack, arrangement);
            else
                Strum(chart, chords, arrangement, Humanise);
            // the song's other instruments on the lead channels (their notes as the file has them)
            if (connections == null)
                foreach (var part in chart.Song.Parts.Where(p => p.Voice == 1 || p.Voice == 2))
                {
                    arrangement.LeadNames[part.Voice - 1] = part.Name;
                    foreach (var n in part.Notes)
                        notes.Add(new BandNote(part.Voice == 1 ? BandPart.Lead1 : BandPart.Lead2, n.Pitch, n.AtQuarter, Math.Max(0.05, n.Quarters), n.Velocity));
                }
            else
                foreach (var (instrument, part, channel) in new[] { (Instrument.LeadGuitar, BandPart.Lead1, 0), (Instrument.Instrument1, BandPart.Lead2, 1) })
                {
                    var track = connections.TrackFor(chart.Song, instrument);
                    if (track == null)
                        continue;
                    arrangement.LeadNames[channel] = track.Name;
                    foreach (var n in track.Notes)
                        if (!n.Technique.Contains('x'))  // (a dead note's pitch is its open string's: it is not one to play)
                            notes.Add(new BandNote(part, n.Pitch, n.AtQuarter, Math.Max(0.05, n.Quarters), n.Velocity));
                }
            notes.Sort((a, b) => a.AtQuarter != b.AtQuarter ? a.AtQuarter.CompareTo(b.AtQuarter) : a.Part.CompareTo(b.Part));
            return arrangement;
        }

        /// <summary>No hits: a part that plays a track makes up none.</summary>
        static readonly List<(double at, double length, BandStyle.Tone tone)> NoHits = new List<(double, double, BandStyle.Tone)>();

        // ------------------------------------------------------------ guitar
        /// <summary>A downstroke on every beat: for songs without a strumming pattern.</summary>
        static readonly StrumPattern Downstrokes = new StrumPattern
        {
            Name = "Downstrokes", Subdivision = 8,
            Strokes = { Stroke.Down, Stroke.Rest, Stroke.Down, Stroke.Rest, Stroke.Down, Stroke.Rest, Stroke.Down, Stroke.Rest },
        };

        /// <summary>How long a stroke takes to cross one string, in seconds (down, and the lighter up).</summary>
        const double DownSpread = 0.012, UpSpread = 0.009;

        /// <summary>How long before a chord's end the strummed strings let go, in seconds: time for the hand to change chord.</summary>
        const double ChordGap = 0.07;

        /// <summary>The open strings' notes, lowest first, with the capo: the song's tuning, else standard.</summary>
        public static int[] OpenStrings(SongInfo info)
        {
            var standard = new[] { 40, 45, 50, 55, 59, 64 };
            var tuning = info.Tuning?.Select(NoteNumber).ToArray();
            var open = tuning != null && tuning.Length == 6 && tuning.All(n => n > 0) ? tuning : standard;
            return open.Select(n => n + Math.Max(0, info.Capo)).ToArray();
        }

        /// <summary>A note name with its octave ("E2", "F#3", "Bb2") as a MIDI note number; 0 if it is not one.</summary>
        static int NoteNumber(string name)
        {
            if (string.IsNullOrEmpty(name) || "CDEFGAB".IndexOf(char.ToUpperInvariant(name[0])) < 0)
                return 0;
            int[] classes = { 0, 2, 4, 5, 7, 9, 11 };
            var pc = classes["CDEFGAB".IndexOf(char.ToUpperInvariant(name[0]))];
            var i = 1;
            for (; i < name.Length && (name[i] == '#' || name[i] == 'b'); ++i)
                pc += name[i] == '#' ? 1 : -1;
            return int.TryParse(name.Substring(i), out var octave) ? 12 * (octave + 1) + pc : 0;
        }

        /// <summary>
        /// A chord shape for a chord the song gives none for: the E or the A barre shape (open where it can be),
        /// major, minor, 7, m7 or maj7 by the chord's third and seventh; a power chord without a third. Frets per
        /// string from the lowest, -1 not played.
        /// </summary>
        public static List<int> GuitarShape(Chord chord)
        {
            var hasThird = chord.Intervals.Contains(3) || chord.Intervals.Contains(4);
            var seventh = chord.Intervals.Contains(10) ? 10 : chord.Intervals.Contains(11) ? 11 : 0;
            int[] e, a;
            if (!hasThird)
            {
                e = new[] { 0, 2, 2, -1, -1, -1 };
                a = new[] { -1, 0, 2, 2, -1, -1 };
            }
            else if (chord.IsMinor)
            {
                e = seventh == 10 ? new[] { 0, 2, 0, 0, 0, 0 } : new[] { 0, 2, 2, 0, 0, 0 };
                a = seventh == 10 ? new[] { -1, 0, 2, 0, 1, 0 } : new[] { -1, 0, 2, 2, 1, 0 };
            }
            else
            {
                e = seventh == 10 ? new[] { 0, 2, 0, 1, 0, 0 } : seventh == 11 ? new[] { 0, 2, 1, 1, 0, 0 } : new[] { 0, 2, 2, 1, 0, 0 };
                a = seventh == 10 ? new[] { -1, 0, 2, 0, 2, 0 } : seventh == 11 ? new[] { -1, 0, 2, 1, 2, 0 } : new[] { -1, 0, 2, 2, 2, 0 };
            }
            var eFret = ((chord.Root - 4) % 12 + 12) % 12;  // E is pitch class 4, A 9
            var aFret = ((chord.Root - 9) % 12 + 12) % 12;
            var (shape, fret) = eFret <= aFret ? (e, eFret) : (a, aFret);
            return shape.Select(f => f < 0 ? -1 : f + fret).ToList();
        }

        /// <summary>
        /// The band guitarist's rhythm: each section's strumming pattern (or a downstroke on every beat) on the
        /// chord shapes the chart shows. A stroke crosses the strings as a hand does (down: low to high; up: the
        /// top four, high to low), and each string rings until it is struck again or the chord changes; a muted
        /// stroke is a short chuck. The last bar is one chord, held.
        /// </summary>
        static void Strum(SongChart chart, List<ChordEvent> chords, Arrangement arrangement, Func<int, int> humanise)
        {
            var song = chart.Song;
            var open = OpenStrings(song.Info);
            var notes = arrangement.Notes;
            // single-string notes of the song's tab: played as notes, and no strumming over them
            var singles = SingleNotes(song, open, humanise, arrangement.SingleSpans);
            notes.AddRange(singles);
            var strikes = new List<(double at, int str, int pitch, int velocity, bool muted, double chordEnd)>();
            foreach (var bar in chart.Bars)
            {
                var last = bar.Index == chart.Bars.Count - 1;
                var pattern = last ? null : (chart.PatternsFor(bar.Section).FirstOrDefault(p => p.Strokes.Count > 0 && p.Subdivision > 0) ?? Downstrokes).AsPlayed();
                var slot = last ? bar.Quarters : 4.0 / pattern.Subdivision;
                for (int i = 0; i * slot < bar.Quarters - 1e-6; ++i)
                {
                    var stroke = last ? Stroke.Down : pattern.Strokes[chart.StrokeIndex(pattern, bar.Index, i)];
                    if (stroke == Stroke.Rest)
                        continue;
                    var at = bar.AtQuarter + i * slot;
                    if (arrangement.SingleSpans.Any(s => at >= s.From - 1e-6 && at < s.To - 1e-6))
                        continue;  // a single-string stretch of the tab: its notes play instead of the strum
                    var chord = ChordAt(chords, at, out var span);
                    if (chord == null)
                        continue;
                    var voicing = song.VoicingFor(span);
                    var frets = voicing != null && voicing.Frets.Count == 6 ? voicing.Frets : GuitarShape(chord);
                    var played = Enumerable.Range(0, 6).Where(s => frets[s] >= 0).ToList();
                    if (played.Count == 0)
                        continue;
                    var down = stroke == Stroke.Down || stroke == Stroke.MutedDown || stroke == Stroke.AccentDown;
                    var muted = stroke == Stroke.MutedDown || stroke == Stroke.MutedUp;
                    var accent = stroke == Stroke.AccentDown || stroke == Stroke.AccentUp;
                    var order = down ? played : (played.Count(s => s >= 2) >= 2 ? played.Where(s => s >= 2) : played).Reverse().ToList();
                    var onBeat = Math.Abs(i * slot - Math.Round(i * slot)) < 1e-6;
                    var velocity = muted ? 64 : accent ? (down ? 116 : 104) : !down ? 72 : onBeat ? 96 : 84;
                    arrangement.Strokes.Add(new BandStroke(at, !down, humanise(velocity), muted));
                    var perString = (down ? DownSpread : UpSpread) * song.Tempo.BpmAt(at) / 60.0;  // in quarter notes
                    // the chord lets go a little before its end: the hand needs a moment to reach the next chord
                    var chordEnd = Math.Max(at + 0.06, span.AtQuarter + span.Quarters - ChordGap * song.Tempo.BpmAt(at) / 60.0);
                    for (int k = 0; k < order.Count; ++k)
                        strikes.Add((at + k * perString, order[k], open[order[k]] + frets[order[k]], humanise(velocity), muted, chordEnd));
                }
            }
            // the held voicings, chord by chord: what a strum-capable guitar strums
            foreach (var span in chords)
            {
                var chord = Chord.Parse(span.Name);
                var voicing = song.VoicingFor(span);
                var frets = voicing != null && voicing.Frets.Count == 6 ? voicing.Frets : GuitarShape(chord);
                for (int s = 0; s < 6; ++s)
                    if (frets[s] >= 0)
                        arrangement.Voicings.Add(new VoicingNote(open[s] + frets[s], span.AtQuarter, span.Quarters));
            }
            // each string rings until it is struck again, or the chord ends
            foreach (var onString in strikes.GroupBy(s => s.str))
            {
                var list = onString.OrderBy(s => s.at).ToList();
                for (int k = 0; k < list.Count; ++k)
                {
                    var s = list[k];
                    var end = s.chordEnd;
                    if (k + 1 < list.Count)
                        end = Math.Min(end, list[k + 1].at);
                    var length = s.muted ? Math.Min(0.08, end - s.at) : end - s.at - 0.01;
                    if (length > 0.01)
                        notes.Add(new BandNote(BandPart.Guitar, s.pitch, s.at, length, s.velocity));
                }
            }
        }

        /// <summary>
        /// The band guitarist's rhythm from a track played picked (a fingerpicked part, Redbud Tree's): every note as a
        /// note, as the file has it, and no strokes, so a strum-capable guitar stays out of its strumming mode. A note rings
        /// for its length, or with let ring until the next note on its string (a bar at most), and a string sounds one
        /// note at a time. Dead notes are left out.
        /// </summary>
        static void PickedRhythm(SongTrack track, Arrangement arrangement)
        {
            const double ringAtMost = 4.0;  // quarters a let-ring note rings on, at most
            foreach (var onString in track.Notes.Where(n => !n.Technique.Contains('x')).GroupBy(n => n.StringIndex >= 0 ? n.StringIndex : -1 - n.Pitch))
            {
                var list = onString.OrderBy(n => n.AtQuarter).ToList();
                for (int k = 0; k < list.Count; ++k)
                {
                    var n = list[k];
                    var next = k + 1 < list.Count ? list[k + 1].AtQuarter : double.PositiveInfinity;
                    var end = n.Technique.Contains('r') ? Math.Min(next, n.AtQuarter + ringAtMost) : Math.Min(next, n.AtQuarter + n.Quarters);
                    arrangement.Notes.Add(new BandNote(BandPart.Guitar, n.Pitch, n.AtQuarter, Math.Max(0.05, end - n.AtQuarter - 0.01), GuitarVelocity(n.Velocity)));
                }
            }
        }

        /// <summary>
        /// The band guitarist's rhythm from a track of the song (docs/instrument-tracks.md), as the engine's two ways of
        /// playing a guitar take it, and as the guitarist's made-up part does. Where the track strikes two strings or more
        /// at once it strums: each strum is a stroke, down or up as the file records it (else down on the beat and up off
        /// it, as the importer's strumming has it), as hard as its notes, muted where the strings are dead or palm-muted.
        /// The chord held for a strum-capable guitar to strum is the chart's chord on the strings the track strikes in it
        /// (on each string the fret it strikes most there), one held chord per chord of the chart: a fingerpicked part's
        /// bass notes and pinches are strokes on one chord, not a new chord each time. The other guitars get the struck
        /// strings one by one, a stroke's width apart, each ringing for its note. Where the track plays one string at a
        /// time (a riff, a solo) the notes play as notes, with no strumming over them, as the song's own tab's
        /// single-string notes do.
        /// </summary>
        static void TrackRhythm(SongChart chart, SongTrack track, Arrangement arrangement)
        {
            const double together = 0.02;  // notes this close are struck together
            const double longRest = 2.0;   // a picked stretch with a rest this long (quarters) ends before it
            var song = chart.Song;
            var notes = arrangement.Notes;
            var onsets = new List<List<TrackNote>>();
            foreach (var n in track.Notes.OrderBy(n => n.AtQuarter))
            {
                if (onsets.Count > 0 && n.AtQuarter - onsets[onsets.Count - 1][0].AtQuarter < together)
                    onsets[onsets.Count - 1].Add(n);
                else
                    onsets.Add(new List<TrackNote> { n });
            }
            bool Dead(TrackNote n) => n.Technique.Contains('x');
            int StringOf(TrackNote n) => n.StringIndex >= 0 ? n.StringIndex : n.Pitch;

            // the picked stretches (single strings), and the strums between them
            var strums = new List<List<TrackNote>>();
            double? from = null;
            var end = 0.0;
            var picked = new List<TrackNote>();
            void Close(double to)
            {
                if (from == null)
                    return;
                arrangement.SingleSpans.Add((from.Value, Math.Max(from.Value + 0.05, to)));
                foreach (var n in picked)
                    notes.Add(new BandNote(BandPart.Guitar, n.Pitch, n.AtQuarter, Math.Max(0.05, Math.Min(n.Quarters, to - n.AtQuarter)), GuitarVelocity(n.Velocity), single: true));
                from = null;
                picked.Clear();
            }
            foreach (var onset in onsets)
            {
                var at = onset[0].AtQuarter;
                if (onset.Count >= 2)
                {
                    Close(Math.Min(at, end));
                    strums.Add(onset);
                    continue;
                }
                if (Dead(onset[0]))
                    continue;  // a scratch on a dead string, not a note
                if (from != null && at - end > longRest)
                    Close(end);
                from = from ?? at;
                picked.Add(onset[0]);
                end = Math.Max(end, at + onset[0].Quarters);
            }
            Close(end);

            // per bar, the sixteenths struck: whether the strumming is in eighths or sixteenths, for the down-up rule
            var slots = new Dictionary<int, HashSet<int>>();
            int SlotOf(double at, ChartBar bar) => (int)Math.Round((at - bar.AtQuarter) * 4);
            foreach (var s in strums)
                if (chart.BarOf(s[0].AtQuarter) is ChartBar bar)
                {
                    if (!slots.TryGetValue(bar.Index, out var set))
                        slots[bar.Index] = set = new HashSet<int>();
                    set.Add(SlotOf(s[0].AtQuarter, bar));
                }

            var strikes = new List<(double at, int str, int pitch, int velocity, bool muted, double end)>();
            foreach (var strum in strums)
            {
                var at = strum[0].AtQuarter;
                var sounding = strum.Where(n => !Dead(n)).OrderBy(StringOf).ToList();
                var muted = sounding.Count == 0 || strum.All(n => n.Technique.Contains('m') || Dead(n));
                bool down;
                if (strum.Any(n => n.Technique.Contains('D')))
                    down = true;
                else if (strum.Any(n => n.Technique.Contains('U')))
                    down = false;
                else
                {
                    var bar = chart.BarOf(at);
                    var slot = bar != null ? SlotOf(at, bar) : 0;
                    var set = bar != null && slots.TryGetValue(bar.Index, out var s) ? s : new HashSet<int>();
                    var fast = set.Contains(slot - 1) || set.Contains(slot + 1);
                    down = slot % (fast ? 2 : 4) == 0;
                }
                // as hard as the made-up part strums (Shreddage takes a strum's velocity as its speed and loudness), shifted a
                // little by the file's dynamics: the file's own velocities (p 47, mp 63) are slow, faint strums
                var onBeat = chart.BarOf(at) is ChartBar b && Math.Abs((at - b.AtQuarter) - Math.Round(at - b.AtQuarter)) < 1e-6;
                var accent = strum.Any(n => n.Technique.Contains('a'));
                var made = muted ? 64 : accent ? (down ? 116 : 104) : !down ? 72 : onBeat ? 96 : 84;
                var velocity = Math.Max(1, Math.Min(127, made + (strum.Max(n => n.Velocity) - 79) / 4));
                arrangement.Strokes.Add(new BandStroke(at, !down, velocity, muted));
                // the strings one by one, as a hand crosses them (down: low to high; up: high to low)
                var order = down ? sounding : Enumerable.Reverse(sounding).ToList();
                var perString = (down ? DownSpread : UpSpread) * song.Tempo.BpmAt(at) / 60.0;  // in quarter notes
                for (int k = 0; k < order.Count; ++k)
                    strikes.Add((at + k * perString, StringOf(order[k]), order[k].Pitch, velocity, muted, order[k].AtQuarter + order[k].Quarters));
            }

            // the held chords: per chord of the chart, the strings the track strums in it, each at its commonest note
            // there; a strum outside the chart's chords holds its own strings until the next strum
            var spans = song.Chords.OrderBy(c => c.AtQuarter).Select(c => (From: c.AtQuarter, To: c.AtQuarter + c.Quarters)).ToList();
            var covered = new bool[strums.Count];
            foreach (var (spanFrom, spanTo) in spans)
            {
                var inside = Enumerable.Range(0, strums.Count).Where(i => strums[i][0].AtQuarter >= spanFrom - 1e-6 && strums[i][0].AtQuarter < spanTo - 1e-6).ToList();
                foreach (var i in inside)
                    covered[i] = true;
                foreach (var onString in inside.SelectMany(i => strums[i]).Where(n => !Dead(n)).GroupBy(StringOf))
                {
                    var pitch = onString.GroupBy(n => n.Pitch).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
                    arrangement.Voicings.Add(new VoicingNote(pitch, spanFrom, spanTo - spanFrom));
                }
            }
            for (int i = 0; i < strums.Count; ++i)
                if (!covered[i])
                {
                    var at = strums[i][0].AtQuarter;
                    var next = i + 1 < strums.Count ? strums[i + 1][0].AtQuarter : double.PositiveInfinity;
                    foreach (var n in strums[i].Where(n => !Dead(n)))
                        arrangement.Voicings.Add(new VoicingNote(n.Pitch, at, Math.Max(0.05, Math.Min(next, at + n.Quarters) - at)));
                }
            arrangement.Voicings.Sort((a, b) => a.AtQuarter.CompareTo(b.AtQuarter));

            // each string rings for its note's length, or until it is struck again
            foreach (var onString in strikes.GroupBy(s => s.str))
            {
                var list = onString.OrderBy(s => s.at).ToList();
                for (int k = 0; k < list.Count; ++k)
                {
                    var s = list[k];
                    var stop = k + 1 < list.Count ? Math.Min(s.end, list[k + 1].at) : s.end;
                    var length = s.muted ? Math.Min(0.08, stop - s.at) : stop - s.at - 0.01;
                    if (length > 0.01)
                        notes.Add(new BandNote(BandPart.Guitar, s.pitch, s.at, length, s.velocity));
                }
            }
        }

        /// <summary>
        /// A guitar note's velocity from the file's (its dynamics: ppp 15 to fff 127), in the range the guitarist's
        /// made-up part plays in (p 80, mp 88, mf 96, f 104): a plugin guitar is faint and choked at the file's soft ones.
        /// </summary>
        static int GuitarVelocity(int fileVelocity) => Math.Max(1, Math.Min(127, 64 + (fileVelocity - 15) / 2));

        /// <summary>
        /// The single-string notes of the song's tab (the notes of a riff's onsets that have just one note) and the
        /// stretches they take: from the first single of a run to the next onset of two or more strings (a chord, which
        /// the guitar strums again), or to the run's end. Where the tab plays single strings the guitar plays them, as
        /// notes, and does not strum.
        /// </summary>
        static List<BandNote> SingleNotes(Song song, int[] open, Func<int, int> humanise, List<(double From, double To)> spans)
        {
            const double together = 0.02;  // notes this close are one onset
            const double MaxGap = 1.0;     // single notes further apart than this (a beat) are not one run
            var result = new List<BandNote>();
            var tab = song.Riffs.SelectMany(r => r.Notes).Where(n => n.StringIndex >= 0 && n.StringIndex < open.Length).OrderBy(n => n.AtQuarter).ToList();
            var onsets = new List<List<TabNote>>();
            foreach (var n in tab)
            {
                if (onsets.Count > 0 && n.AtQuarter - onsets[onsets.Count - 1][0].AtQuarter < together)
                    onsets[onsets.Count - 1].Add(n);
                else
                    onsets.Add(new List<TabNote> { n });
            }
            double? start = null;
            var lastAt = 0.0;
            var lastQuarters = 0.0;
            var runNotes = new List<TabNote>();
            double Tail() => lastAt + Math.Min(lastQuarters, 1.0);  // a run's last note sounds a beat at most
            void Close(double to)
            {
                if (start == null)
                    return;
                spans.Add((start.Value, Math.Max(start.Value + 0.05, to)));
                foreach (var n in runNotes)
                    result.Add(new BandNote(BandPart.Guitar, open[n.StringIndex] + n.Fret, n.AtQuarter,
                                            Math.Max(0.05, Math.Min(n.Quarters, to - n.AtQuarter)), humanise(88), single: true));
                start = null;
                runNotes.Clear();
            }
            foreach (var onset in onsets)
            {
                var at = onset[0].AtQuarter;
                if (onset.Count >= 2)
                {
                    Close(Math.Min(at, Tail()));  // a chord: the guitar strums again
                    continue;
                }
                if (start != null && at - lastAt > MaxGap)
                    Close(Math.Min(at, Tail()));  // too long a rest: the strumming goes on in it
                start = start ?? at;
                runNotes.Add(onset[0]);
                lastAt = at;
                lastQuarters = Math.Max(0.05, onset[0].Quarters);
            }
            Close(Tail());
            return result;
        }

        static bool IsQuiet(Section section)
        {
            var name = section?.Name.ToLowerInvariant() ?? "";
            return name.StartsWith("intro") || name.StartsWith("verse") || name.StartsWith("outro") || name.StartsWith("bridge");
        }

        /// <summary>Bars not in 4/4: hats on eighths, kick on 1, snare on the last beat.</summary>
        static List<(int, double, int)> GenericBar(TimeSignature signature)
        {
            var hits = new List<(int, double, int)>();
            var beat = 4.0 / signature.Denominator;
            for (double at = 0; at < signature.QuartersPerBar - 1e-6; at += beat / 2)
                hits.Add((Drum.ClosedHat, at, Math.Abs(at % beat) < 1e-6 ? 72 : 50));
            hits.Add((Drum.Kick, 0, 100));
            hits.Add((Drum.Snare, signature.QuartersPerBar - beat, 90));
            return hits;
        }

        static List<(double, double, BandStyle.Tone)> GenericBass(TimeSignature signature) =>
            new List<(double, double, BandStyle.Tone)> { (0, signature.QuartersPerBar, BandStyle.Tone.Root) };

        /// <summary>
        /// A bar's rhythm with a hit added where a chord changes between hits, and every hit cut short where the
        /// next one starts.
        /// </summary>
        static IEnumerable<(double at, double length, BandStyle.Tone tone)> WithChordChanges(
            IReadOnlyList<(double at, double length, BandStyle.Tone tone)> hits, ChartBar bar, List<ChordEvent> chords)
        {
            var all = hits.Where(h => h.at < bar.Quarters - 1e-6).ToList();
            foreach (var c in bar.Chords)
                if (!c.CarriedIn && all.All(h => Math.Abs(h.at - c.Offset) > 1e-6))
                    all.Add((c.Offset, bar.Quarters - c.Offset, BandStyle.Tone.Root));
            all.Sort((a, b) => a.at.CompareTo(b.at));
            for (int i = 0; i < all.Count; ++i)
            {
                var limit = i + 1 < all.Count ? all[i + 1].at : bar.Quarters;
                // a hit right at a chord change plays the new chord's root
                var tone = bar.Chords.Any(c => !c.CarriedIn && Math.Abs(c.Offset - all[i].at) < 1e-6 && c.Offset > 0) ? BandStyle.Tone.Root : all[i].tone;
                yield return (all[i].at, Math.Min(all[i].length, limit - all[i].at), tone);
            }
        }

        static Chord ChordAt(List<ChordEvent> chords, double quarter, out ChordEvent span)
        {
            span = chords.LastOrDefault(c => c.AtQuarter <= quarter + 1e-6 && quarter < c.AtQuarter + c.Quarters - 1e-6);
            return span != null ? Chord.Parse(span.Name) : null;
        }

        /// <summary>The bass note for a chord tone: roots (and slash basses) between E1 and E2.</summary>
        public static int BassPitch(Chord chord, BandStyle.Tone tone, Chord next)
        {
            int Low(int pitchClass) => BassLow + ((pitchClass - BassLow % 12) % 12 + 12) % 12;
            var root = Low(chord.Bass);
            switch (tone)
            {
                case BandStyle.Tone.Fifth:
                    return chord.Intervals.Contains(7) || chord.Intervals.Contains(6) || chord.Intervals.Contains(8)
                        ? Low(chord.Root) + chord.Intervals.First(i => i >= 6 && i <= 8)
                        : root;
                case BandStyle.Tone.Octave:
                    return root + 12;
                case BandStyle.Tone.Approach:
                    if (next == null || next.Bass == chord.Bass)
                        return root;
                    // a half step towards the next root, from the side it is nearer
                    var target = Low(next.Bass);
                    return target > root ? target - 1 : target + 1;
                default:
                    return root;
            }
        }

        /// <summary>
        /// Close voicings of the chord (three or four notes) between G3 and F5; the one that moves least from the
        /// last voicing (or, for the first chord, sits nearest E4).
        /// </summary>
        public static List<int> Voice(Chord chord, List<int> previous)
        {
            var classes = chord.Intervals.Select(i => (chord.Root + i) % 12).Distinct().ToList();
            if (classes.Count > 4)
                classes = classes.Where(pc => pc != (chord.Root + 7) % 12).Take(4).ToList();  // drop the fifth
            List<int> best = null;
            var bestCost = double.MaxValue;
            foreach (var bottom in classes)
                for (int octave = 3; octave <= 5; ++octave)
                {
                    var notes = new List<int>();
                    var start = 12 * (octave + 1) + bottom;
                    foreach (var pc in classes.OrderBy(pc => (pc - bottom + 12) % 12))
                        notes.Add(start + (pc - bottom + 12) % 12);
                    if (notes[0] < KeysLow || notes[notes.Count - 1] > KeysHigh)
                        continue;
                    var cost = previous == null || previous.Count == 0
                        ? Math.Abs(notes.Average() - KeysCentre)
                        : Movement(notes, previous);
                    if (cost < bestCost)
                    {
                        best = notes;
                        bestCost = cost;
                    }
                }
            return best ?? classes.Select(pc => 60 + pc).ToList();
        }

        // how far a voicing moves from the last: each note to the nearest note of the other
        static double Movement(List<int> a, List<int> b) =>
            a.Sum(x => b.Min(y => Math.Abs(x - y))) + b.Sum(y => a.Min(x => Math.Abs(x - y))) + 0.1 * Math.Abs(a.Average() - KeysCentre);
    }
}
