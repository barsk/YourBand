using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AlphaTab.Importer;
using AlphaTab.Model;
using GuitarTutor.Core.Import.UltimateGuitar;
using GuitarTutor.Core.Music;
using GuitarTutor.Core.Songs;

namespace GuitarTutor.SongTool
{
    /// <summary>
    /// Builds a song from a Guitar Pro file (.gpx, .gp, .gp3/.gp4/.gp5) with alphaTab.
    ///
    /// What the current <see cref="Song"/> model holds: the busiest guitar track's notes become the tab (one riff)
    /// and, grouped into simultaneous sets, the chords (the file's own names where it writes them, else detected
    /// from the notes) and their voicings; the sections, tempo, time signatures, lyrics and song info come from
    /// the score; and the strumming is the track's strums as a per-bar grid on sixteenth-note slots, each stroke's
    /// direction from the file's recorded pick stroke, else an optional Ultimate Guitar strumming, else the
    /// standard rule (down on the beat, up on the off-beat). The richer Guitar Pro data — the other tracks (bass,
    /// drums, second guitar, vocals) and full technique detail (bend points, slide targets, dynamics) — is left
    /// out for a later model extension.
    /// </summary>
    public static class GpxImporter
    {
        static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        /// <summary>alphaTab's internal resolution: ticks per quarter note (verified against a real file).</summary>
        const double FallbackTicksPerQuarter = 960;

        /// <summary>A pause longer than this (in quarters) starts a new chord region.</summary>
        const double RegionGap = 0.5;

        /// <summary>A chord region shorter than this (in quarters) is a passing strum, merged into a neighbor.</summary>
        const double MinRegion = 0.75;

        /// <summary>Chord qualities, simplest first, as (suffix, intervals in semitones above the root).</summary>
        static readonly (string Name, int[] Intervals)[] Qualities =
        {
            ("5",    new[] { 0, 7 }),
            ("",     new[] { 0, 4, 7 }),
            ("m",    new[] { 0, 3, 7 }),
            ("sus2", new[] { 0, 2, 7 }),
            ("sus4", new[] { 0, 5, 7 }),
            ("7",    new[] { 0, 4, 7, 10 }),
            ("m7",   new[] { 0, 3, 7, 10 }),
            ("maj7", new[] { 0, 4, 7, 11 }),
            ("6",    new[] { 0, 4, 7, 9 }),
            ("m6",   new[] { 0, 3, 7, 9 }),
            ("dim",  new[] { 0, 3, 6 }),
            ("aug",  new[] { 0, 4, 8 }),
            ("m7b5", new[] { 0, 3, 6, 10 }),
            ("dim7", new[] { 0, 3, 6, 9 }),
            ("add9", new[] { 0, 4, 7, 14 }),
            ("6/9",  new[] { 0, 4, 7, 9, 14 }),
            ("maj9", new[] { 0, 4, 7, 11, 14 }),
            ("9",    new[] { 0, 4, 7, 10, 14 }),
            ("m9",   new[] { 0, 3, 7, 10, 14 }),
        };

        public static Song Import(string path, out ImportReport report, int trackIndex = -1, List<StrumPattern> ugStrumming = null,
            Dictionary<string, string> ugSectionMap = null)
        {
            report = new ImportReport();
            var score = ScoreLoader.LoadScoreFromBytes(File.ReadAllBytes(path));
            if (score.Tracks.Count == 0)
                throw new InvalidDataException("no tracks in the Guitar Pro file");

            var song = new Song();
            var tpq = TicksPerQuarter(score);

            // ------------------------------------------------------------ info
            song.Info.Title = score.Title ?? "";
            song.Info.Artist = score.Artist ?? "";
            var track = PickTrack(score, trackIndex, report);
            var staff = track.Staves[0];
            song.Info.Tuning = Tuning(staff);
            song.Info.Capo = (int)staff.Capo;
            song.Info.Key = Key(staff, report);

            // ------------------------------------------------------------ tempo + meters
            var bpm = Tempo(score, report);
            var first = score.MasterBars.Count > 0
                ? new TimeSignature((int)score.MasterBars[0].TimeSignatureNumerator, (int)score.MasterBars[0].TimeSignatureDenominator)
                : TimeSignature.FourFour;
            song.Tempo = new TempoMap(bpm, first);
            var current = first;  // a change is any bar whose signature differs from the bar before it (a single 2/4 bar goes back to 4/4)
            foreach (var mb in score.MasterBars)
            {
                var sig = new TimeSignature((int)mb.TimeSignatureNumerator, (int)mb.TimeSignatureDenominator);
                if (mb.Index > 0 && !sig.Equals(current))
                    song.Tempo.SetMeter((int)mb.Index, sig);
                current = sig;
            }

            // ------------------------------------------------------------ sections
            Sections(score, song, tpq);
            CarveLeadSections(score, track, song, tpq, report);

            // ------------------------------------------------------------ riffs + chords + voicings
            RiffsAndChords(score, staff, song, report, tpq);

            // ------------------------------------------------------------ strumming (the grid the track plays, with a direction per stroke)
            Strumming(score, staff, song, report, tpq, ugStrumming, ugSectionMap);

            // ------------------------------------------------------------ lyrics (from whichever track carries them)
            Lyrics(score, song, tpq, report);

            // ------------------------------------------------------------ the other instruments
            Parts(score, track, song, tpq, report);

            // ------------------------------------------------------------ provenance
            song.Sources.Add(new Source
            {
                Kind = "guitar-pro",
                Url = path,
                Revision = "",
                Supplies = { "sections", "chords", "riffs", "voicings", "strumming", "lyrics", "tempo" },
            });

            var end = song.Riffs.SelectMany(r => r.Notes).DefaultIfEmpty().Max(n => n.AtQuarter + n.Quarters);
            var bars = song.Tempo.BarAt(Math.Max(0, end - 1e-6)).bar + 1;
            report.Add($"{song.Sections.Count} sections, {song.Chords.Count} chords, "
                       + $"{song.Riffs.Sum(r => r.Notes.Count)} tab notes, {song.Lyrics.Count} lyric lines, "
                       + $"{bars} bars at {bpm:0.#} BPM in {first} (track '{track.Name}').");
            return song;
        }

        // ------------------------------------------------------------ the main guitar track
        /// <summary>
        /// The track to read the tab and chords from: an explicit index when given, else the busiest six-string
        /// guitar-like track (the most notes). The rhythm guitar strums full chords — far more notes than a lead
        /// line — and the chords come from that part, so the busiest guitar is the one to read. Failing a guitar
        /// by name or MIDI program, the first six-string, non-percussion track.
        /// </summary>
        static Track PickTrack(Score score, int index, ImportReport report)
        {
            if (index >= 0 && index < score.Tracks.Count)
                return score.Tracks[index];

            bool Guitar(Track t) =>
                t.Staves.Count > 0 && t.Staves[0].StringTuning.Tunings.Count == 6 && !t.Staves[0].IsPercussion
                && ((t.Name ?? "").Contains("Guitar", StringComparison.OrdinalIgnoreCase)
                    || (t.PlaybackInfo != null && t.PlaybackInfo.Program >= 24 && t.PlaybackInfo.Program <= 51));

            int Notes(Track t) =>
                t.Staves.SelectMany(st => st.Bars).SelectMany(b => b.Voices).SelectMany(v => v.Beats).Sum(b => b.Notes.Count);

            var guitars = score.Tracks.Where(Guitar).ToList();
            var pick = guitars.Count > 0
                ? guitars.OrderByDescending(Notes).First()
                : score.Tracks.FirstOrDefault(t =>
                    t.Staves.Count > 0 && t.Staves[0].StringTuning.Tunings.Count == 6 && !t.Staves[0].IsPercussion);
            if (pick == null)
                throw new InvalidDataException("no six-string, non-percussion track to read the tab from; pass --track N");
            if (guitars.Count > 1)
                report.Add($"Several guitar tracks; using '{pick.Name}' (the busiest, {Notes(pick)} notes). Pass --track N to choose.");
            else if (!Guitar(pick))
                report.Add($"No track named 'Guitar' found; using '{pick.Name}' (the first six-string track).");
            return pick;
        }

        // ------------------------------------------------------------ conversions
        /// <summary>alphaTab's ticks per quarter, derived from the first two bars (robust to a changed constant).</summary>
        static double TicksPerQuarter(Score score)
        {
            var bars = score.MasterBars;
            for (int i = 0; i + 1 < bars.Count; ++i)
            {
                var qpb = bars[i].TimeSignatureNumerator * 4.0 / bars[i].TimeSignatureDenominator;
                var ticks = bars[i + 1].Start - bars[i].Start;
                if (qpb > 0 && ticks > 0)
                    return ticks / qpb;
            }
            return FallbackTicksPerQuarter;
        }

        /// <summary>The open strings, lowest first, as note names (alphaTab's tuning is highest first).</summary>
        static List<string> Tuning(Staff staff)
        {
            var tunings = staff.StringTuning.Tunings;
            var list = new List<string>(tunings.Count);
            for (int i = tunings.Count - 1; i >= 0; --i)
                list.Add(MidiName((int)tunings[i]));
            return list;
        }

        static string MidiName(int midi) => NoteNames[((midi % 12) + 12) % 12] + (midi / 12 - 1);

        /// <summary>The MIDI pitch of a note, from the track's tuning and its fret (not from alphaTab's Tone).</summary>
        static int Pitch(Staff staff, Note note)
        {
            var tunings = staff.StringTuning.Tunings;
            var stringIndex = (int)note.String - 1;  // alphaTab: 1 = lowest string
            if (stringIndex < 0 || stringIndex >= tunings.Count)
                return -1;
            return (int)tunings[tunings.Count - 1 - stringIndex] + (int)note.Fret;
        }

        static string Key(Staff staff, ImportReport report)
        {
            var bar = staff.Bars.FirstOrDefault();
            if (bar == null)
                return "";
            var name = bar.KeySignature.ToString().Replace("Sharp", "#");
            if (bar.KeySignatureType == KeySignatureType.Minor)
                name += "m";
            return name;
        }

        static double Tempo(Score score, ImportReport report)
        {
            foreach (var mb in score.MasterBars)
            {
                var a = mb.TempoAutomations.FirstOrDefault(x => x.Type == AutomationType.Tempo);
                if (a != null && a.Value > 0)
                    return a.Value;
            }
            report.Add("No tempo in the file: using 120 BPM.");
            return 120;
        }

        // ------------------------------------------------------------ sections
        static void Sections(Score score, Song song, double tpq)
        {
            var bars = score.MasterBars;
            var sectionBars = new List<(int Index, string Text)>();
            for (int i = 0; i < bars.Count; ++i)
            {
                var text = bars[i].Section?.Text;
                if (!string.IsNullOrWhiteSpace(text))
                    sectionBars.Add((i, text.Trim()));
            }
            double SongEnd()
            {
                var last = bars[bars.Count - 1];
                var qpb = last.TimeSignatureNumerator * 4.0 / last.TimeSignatureDenominator;
                return (last.Start + qpb * tpq) / tpq;
            }
            for (int s = 0; s < sectionBars.Count; ++s)
            {
                var (index, text) = sectionBars[s];
                var start = bars[index].Start / tpq;
                var end = s + 1 < sectionBars.Count ? bars[sectionBars[s + 1].Index].Start / tpq : SongEnd();
                song.Sections.Add(new GuitarTutor.Core.Songs.Section
                {
                    Name = text,
                    AtQuarter = start,
                    Quarters = Math.Max(0, end - start),
                });
            }
        }

        // ------------------------------------------------------------ the other tracks: sections and parts
        static bool IsVocal(Track t) =>
            Regex.IsMatch(t.Name ?? "", "vocal|voice|sing|lyric|choir", RegexOptions.IgnoreCase)
            || (t.PlaybackInfo != null && t.PlaybackInfo.Program >= 52 && t.PlaybackInfo.Program <= 54);

        static bool IsBass(Track t) =>
            Regex.IsMatch(t.Name ?? "", "bass", RegexOptions.IgnoreCase)
            || (t.PlaybackInfo != null && t.PlaybackInfo.Program >= 32 && t.PlaybackInfo.Program <= 39)
            || (t.Staves.Count > 0 && t.Staves[0].StringTuning.Tunings.Count is > 0 and <= 4);

        /// <summary>The tracks that play a lead line besides the rhythm guitar: not its own, not drums, vocal or bass, with notes.</summary>
        static List<Track> LeadTracks(Score score, Track strumTrack) =>
            score.Tracks.Where(t => t != strumTrack && t.Staves.Count > 0 && !t.Staves[0].IsPercussion && !IsVocal(t) && !IsBass(t)
                                    && BarActivity(t, score).Any(a => a)).ToList();

        /// <summary>Per bar of the score, whether the track plays a note in it.</summary>
        static bool[] BarActivity(Track track, Score score)
        {
            var active = new bool[score.MasterBars.Count];
            var staff = track.Staves[0];
            foreach (var bar in staff.Bars)
                if (bar.Index >= 0 && bar.Index < active.Length)
                    active[(int)bar.Index] = bar.Voices.SelectMany(v => v.Beats).Any(b => b.Notes.Count > 0);
            return active;
        }

        /// <summary>
        /// A lead track that plays only here and there (a harmonica solo) makes its stretches sections of their own:
        /// "Harmonica Intro" inside an intro, else "Harmonica Solo"; what is left of the section after it is
        /// "(cont.)". A track that plays most of the song (a second guitar doing fills) is not a solo.
        /// </summary>
        static void CarveLeadSections(Score score, Track strumTrack, Song song, double tpq, ImportReport report)
        {
            if (song.Sections.Count == 0)
                return;
            var bars = score.MasterBars;
            double BarStart(int i) => bars[i].Start / tpq;
            double BarEnd(int i) => i + 1 < bars.Count ? bars[i + 1].Start / tpq : song.Sections.Max(s => s.AtQuarter + s.Quarters);
            foreach (var track in LeadTracks(score, strumTrack))
            {
                var active = BarActivity(track, score);
                if (active.Count(a => a) > 0.4 * active.Length)
                    continue;
                for (int i = 0; i < active.Length;)
                {
                    if (!active[i])
                    {
                        ++i;
                        continue;
                    }
                    var first = i;
                    while (i < active.Length && active[i])
                        ++i;
                    var last = i - 1;
                    if (last - first + 1 < 2)
                        continue;
                    // the stretch, a section at a time (it can cross one's end)
                    double from = BarStart(first), to = BarEnd(last);
                    while (from < to - 1e-6)
                    {
                        var s = song.Sections.FirstOrDefault(x => from >= x.AtQuarter - 1e-6 && from < x.AtQuarter + x.Quarters - 1e-6);
                        if (s == null)
                            break;
                        var end = Math.Min(to, s.AtQuarter + s.Quarters);
                        if (from <= s.AtQuarter + 1e-6 && end >= s.AtQuarter + s.Quarters - 1e-6)
                        {
                            from = end;  // it plays the whole section: the section keeps its name
                            continue;
                        }
                        var baseName = Regex.Replace(s.Name, @" \(cont\.\)$", "");
                        var name = $"{track.Name} " + (baseName.StartsWith("Intro", StringComparison.OrdinalIgnoreCase) ? "Intro" : "Solo");
                        var index = song.Sections.IndexOf(s);
                        song.Sections.RemoveAt(index);
                        var pieces = new List<GuitarTutor.Core.Songs.Section>();
                        if (from > s.AtQuarter + 1e-6)
                            pieces.Add(new GuitarTutor.Core.Songs.Section { Name = s.Name, AtQuarter = s.AtQuarter, Quarters = from - s.AtQuarter });
                        pieces.Add(new GuitarTutor.Core.Songs.Section { Name = name, AtQuarter = from, Quarters = end - from });
                        if (end < s.AtQuarter + s.Quarters - 1e-6)
                            pieces.Add(new GuitarTutor.Core.Songs.Section { Name = baseName + " (cont.)", AtQuarter = end, Quarters = s.AtQuarter + s.Quarters - end });
                        song.Sections.InsertRange(index, pieces);
                        from = end;
                    }
                }
            }
            if (song.Sections.Count > 0)
                report.Add("Sections: " + string.Join(", ", song.Sections.Select(x => x.Name)) + ".");
        }

        /// <summary>
        /// The file's other instruments as parts of the song: every track but the strummed guitar and the drums, with
        /// its notes. The first two lead tracks (harmonica, lead guitar...) get the band's lead channels 1 and 2.
        /// </summary>
        static void Parts(Score score, Track strumTrack, Song song, double tpq, ImportReport report)
        {
            var leads = LeadTracks(score, strumTrack);
            var masterByIndex = score.MasterBars.ToDictionary(mb => mb.Index);
            foreach (var track in score.Tracks)
            {
                if (track == strumTrack || track.Staves.Count == 0 || track.Staves[0].IsPercussion)
                    continue;
                var staff = track.Staves[0];
                var part = new SongPart
                {
                    Name = track.Name ?? "",
                    Program = (int)(track.PlaybackInfo?.Program ?? 0),
                    Role = IsVocal(track) ? "vocal" : IsBass(track) ? "bass" : "lead",
                };
                foreach (var bar in staff.Bars)
                {
                    if (!masterByIndex.TryGetValue(bar.Index, out var master))
                        continue;
                    foreach (var voice in bar.Voices)
                        foreach (var beat in voice.Beats)
                            foreach (var n in beat.Notes)
                            {
                                if (n.IsDead)
                                    continue;
                                var pitch = Pitch(staff, n);
                                pitch = pitch >= 0 ? pitch + (int)staff.Capo : (int)n.RealValue;
                                if (pitch < 0 || pitch > 127)
                                    continue;
                                var at = (master.Start + beat.DisplayStart) / tpq;
                                var length = Math.Max(0, beat.DisplayDuration) / tpq;
                                var last = part.Notes.LastOrDefault();
                                if (n.IsTieDestination && last != null && last.Pitch == pitch)
                                    last.Quarters = at + length - last.AtQuarter;  // a tied note goes on
                                else
                                    part.Notes.Add(new PartNote { Pitch = pitch, AtQuarter = at, Quarters = length });
                            }
                }
                if (part.Notes.Count == 0)
                    continue;
                if (part.Role == "lead" && leads.Contains(track))
                    part.Voice = song.Parts.Count(p => p.Voice > 0) < 2 ? song.Parts.Count(p => p.Voice > 0) + 1 : 0;
                song.Parts.Add(part);
            }
            if (song.Parts.Count > 0)
                report.Add("Parts: " + string.Join(", ", song.Parts.Select(p => $"{p.Name} ({p.Notes.Count} notes, "
                                                                                + (p.Voice > 0 ? $"lead {p.Voice}" : p.Role + ", not played") + ")")) + ".");
        }

        // ------------------------------------------------------------ riffs, chords, voicings
        /// <summary>A strummed chord beat: when it sounds, for how long, its pitches and per-string frets.</summary>
        sealed class Strum
        {
            public double At;
            public double Duration;
            public List<int> Pitches;
            public HashSet<int> Pcs;
            public int[] Frets;
            public string ChordName;   // the file's chord name in force at the strum (the latest chord marker), if it writes any
            public int Bass => Pitches.Count > 0 ? Pitches.Min() % 12 : -1;
        }

        /// <summary>A chord region: a run of strums sounding the same chord, with its detected name and shape.</summary>
        sealed class ChordRegion
        {
            public double Start;
            public double Duration;
            public HashSet<int> CorePcs;
            public int Bass;
            public int[] Frets;
            public string ChordName;   // the file's own name for the region, if its strums agree on one
        }

        static void RiffsAndChords(Score score, Staff staff, Song song, ImportReport report, double tpq)
        {
            var riff = new Riff { Name = staff.Track.Name };
            var voicings = new Dictionary<string, Voicing>();
            var strums = new List<Strum>();
            var masterByIndex = score.MasterBars.ToDictionary(mb => mb.Index);
            string writtenChord = null;   // the file's chord name in force: a chord marker holds until the next one

            foreach (var bar in staff.Bars)
            {
                if (!masterByIndex.TryGetValue(bar.Index, out var master))
                    continue;
                foreach (var voice in bar.Voices)
                {
                    foreach (var beat in voice.Beats)
                    {
                        writtenChord = ChordName(beat) ?? writtenChord;
                        var at = (master.Start + beat.DisplayStart) / tpq;
                        var length = Math.Max(0, beat.DisplayDuration) / tpq;
                        // (a dead note, the "x" of a muted strum, has fret 0 in the file but no pitch: not an open string)
                        var notes = beat.Notes.Where(n => n.String >= 1 && !n.IsDead).ToList();

                        // the tab: every note, at its position (a note tied from the one before is that note held on, not struck again)
                        foreach (var n in notes)
                        {
                            var held = n.IsTieDestination
                                ? riff.Notes.LastOrDefault(t => t.StringIndex == (int)n.String - 1 && t.Fret == (int)n.Fret && Math.Abs(t.AtQuarter + t.Quarters - at) < 1e-6)
                                : null;
                            if (held != null)
                            {
                                held.Quarters += length;
                                continue;
                            }
                            riff.Notes.Add(new TabNote
                            {
                                StringIndex = (int)n.String - 1,
                                Fret = (int)n.Fret,
                                AtQuarter = at,
                                Quarters = length,
                                Technique = Technique(n),
                            });
                        }

                        // the chords: only beats that sound two or more notes (strummed chords); a beat whose notes are all tied
                        // from the strum before is that strum held on (not another strum)
                        if (notes.Count >= 2 && notes.All(n => n.IsTieDestination))
                        {
                            if (strums.Count > 0 && Math.Abs(strums[strums.Count - 1].At + strums[strums.Count - 1].Duration - at) < 1e-6)
                                strums[strums.Count - 1].Duration += length;
                        }
                        else if (notes.Count >= 2)
                        {
                            var pitches = notes.Select(n => Pitch(staff, n)).Where(p => p >= 0).ToList();
                            if (pitches.Count >= 2)
                                strums.Add(new Strum
                                {
                                    At = at,
                                    Duration = length,
                                    Pitches = pitches,
                                    Pcs = new HashSet<int>(pitches.Select(p => p % 12)),
                                    Frets = FretsFor(notes, staff),
                                    ChordName = writtenChord,
                                });
                        }
                    }
                }
            }

            if (riff.Notes.Count > 0)
                song.Riffs.Add(riff);

            foreach (var region in ChordRegions(strums))
            {
                // the file's own chord name wins when its strums agree on one; otherwise the notes decide
                var name = region.ChordName ?? DetectChord(region.CorePcs, region.Bass);
                var frets = region.Frets;
                var voicingId = $"{name}-{VoicingFrets(frets)}";
                if (!voicings.TryGetValue(voicingId, out var voicing))
                {
                    voicing = new Voicing { Id = voicingId, Chord = name, Frets = new List<int>(frets), Fingers = new List<int>(frets.Length) };
                    voicings[voicingId] = voicing;
                }
                song.Chords.Add(new ChordEvent
                {
                    Name = name,
                    AtQuarter = region.Start,
                    Quarters = Math.Max(0, region.Duration),
                    Voicing = voicingId,
                });
            }

            song.Voicings.AddRange(voicings.Values);
            if (song.Chords.Count == 0)
                report.Add("No chords detected (the track has no simultaneous notes); the tab is there but the chart has no chord names.");
        }

        /// <summary>
        /// The strums, grouped into chord regions. A region is a run of strums with the same bass note (the root,
        /// stable within a chord) and no long pause; a region shorter than <see cref="MinRegion"/> is a passing
        /// strum (e.g. the leading-tone shape into the next chord) and is merged into the neighbor it most
        /// resembles. Each region's notes are the pitch classes that sound for more than half its duration, so a
        /// brief transition shape cannot pull the chord toward Gmaj7 or G6/9.
        /// </summary>
        static List<ChordRegion> ChordRegions(List<Strum> strums)
        {
            if (strums.Count == 0)
                return new List<ChordRegion>();

            // 1) raw regions: same bass, no long pause between strums
            var raw = new List<List<Strum>> { new List<Strum> { strums[0] } };
            for (int i = 1; i < strums.Count; ++i)
            {
                var s = strums[i];
                var prev = raw[raw.Count - 1][raw[raw.Count - 1].Count - 1];
                var gap = s.At - (prev.At + prev.Duration);
                if (s.Bass == prev.Bass && gap <= RegionGap)
                    raw[raw.Count - 1].Add(s);
                else
                    raw.Add(new List<Strum> { s });
            }

            // 2) merge each short region (a passing strum) into the neighbor it most resembles — the one it shares
            //    the most notes with (a leading-tone tail shares the previous chord; a pickup shares the next).
            //    On a tie it goes left, so a chord keeps its full length and the next starts on its first strum.
            var groups = new List<List<Strum>>(raw);
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; groups.Count >= 2 && i < groups.Count; ++i)
                {
                    var dur = groups[i].Sum(s => s.Duration);
                    if (dur >= MinRegion)
                        continue;
                    var target = i == 0 ? 1 : i == groups.Count - 1 ? i - 1 : Nearest(groups, i);
                    var into = groups[target];
                    if (target > i) into.InsertRange(0, groups[i]); else into.AddRange(groups[i]);
                    groups.RemoveAt(i);
                    merged = true;
                    break;
                }
            }

            // 3) each group's summary: the notes it mostly sounds, its bass, and its most common shape
            var regions = new List<ChordRegion>();
            for (int i = 0; i < groups.Count; ++i)
            {
                var g = groups[i];
                var start = g.Min(s => s.At);
                var end = g.Max(s => s.At + s.Duration);
                var nextStart = i + 1 < groups.Count ? groups[i + 1].Min(s => s.At) : double.PositiveInfinity;

                var total = g.Sum(s => s.Duration);
                var weight = new Dictionary<int, double>();
                foreach (var s in g)
                    foreach (var p in s.Pcs)
                        weight[p] = (weight.TryGetValue(p, out var w) ? w : 0) + s.Duration;
                var core = weight.Where(kv => kv.Value > 0.5 * total).Select(kv => kv.Key).ToHashSet();

                var bassWeight = new Dictionary<int, double>();
                foreach (var s in g)
                    bassWeight[s.Bass] = (bassWeight.TryGetValue(s.Bass, out var w) ? w : 0) + s.Duration;
                var bass = bassWeight.OrderByDescending(kv => kv.Value).First().Key;

                var frets = RegionFrets(g, total);

                // the file's own name for the region: the written name in force for most of its strum time, when it has one
                // (by time: a bass note struck just before a marker, or a first strum of the next chord, is not the chord)
                var writtenName = g.Where(s => s.ChordName != null).GroupBy(s => s.ChordName)
                    .OrderByDescending(x => x.Sum(s => s.Duration)).ThenBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => x.Key).FirstOrDefault();

                regions.Add(new ChordRegion
                {
                    Start = start,
                    Duration = Math.Max(0, Math.Min(end, nextStart) - start),
                    CorePcs = core,
                    Bass = bass,
                    Frets = frets,
                    ChordName = writtenName,
                });
            }
            return regions;
        }

        /// <summary>
        /// For the short region at <paramref name="i"/>, the index of the neighbor (i-1 or i+1) it shares the most
        /// pitch classes with. On a tie, the left neighbor.
        /// </summary>
        static int Nearest(List<List<Strum>> groups, int i)
        {
            var self = Union(groups[i]);
            var left = Union(groups[i - 1]);
            var right = Union(groups[i + 1]);
            int Overlap(HashSet<int> a, HashSet<int> b) => a.Count(p => b.Contains(p));
            return Overlap(self, right) > Overlap(self, left) ? i + 1 : i - 1;
        }

        static HashSet<int> Union(List<Strum> group)
        {
            var u = new HashSet<int>();
            foreach (var s in group)
                u.UnionWith(s.Pcs);
            return u;
        }

        /// <summary>The chord's frets per string, lowest first (-1 not played).</summary>
        static int[] FretsFor(List<Note> notes, Staff staff)
        {
            var count = staff.StringTuning.Tunings.Count;
            var frets = new int[count];
            Array.Fill(frets, -1);
            foreach (var n in notes)
            {
                var si = (int)n.String - 1;
                if (si >= 0 && si < count)
                    frets[si] = (int)n.Fret;
            }
            return frets;
        }

        /// <summary>
        /// The shape of a chord region: for each string, the fret most of the region's strum time plays on it, if the
        /// string sounds for a fifth of the region or more. A chord struck as a bass note and then the top strings (the
        /// G of Wonderwall: 3 2 . . then . . 0 3 3) is the whole shape, not whichever strum was the commonest.
        /// </summary>
        static int[] RegionFrets(List<Strum> group, double total)
        {
            var shape = new int[6];
            for (int str = 0; str < 6; ++str)
            {
                var weight = new Dictionary<int, double>();
                foreach (var s in group)
                    if (s.Frets[str] >= 0)
                        weight[s.Frets[str]] = (weight.TryGetValue(s.Frets[str], out var w) ? w : 0) + s.Duration;
                var best = weight.Count > 0 ? weight.OrderByDescending(kv => kv.Value).First() : default;
                shape[str] = weight.Count > 0 && weight.Values.Sum() >= 0.2 * total ? best.Key : -1;
            }
            // nothing reached a fifth (a region of many short strums): the commonest strum as before
            return shape.Any(f => f >= 0) ? shape : group.GroupBy(s => VoicingFrets(s.Frets)).OrderByDescending(x => x.Count()).First().First().Frets;
        }

        static string VoicingFrets(int[] frets) => string.Concat(frets.Select(f => f < 0 ? "x" : f.ToString()));

        /// <summary>The chord name the file writes for a beat, if it has one (else null: the notes decide).</summary>
        static string ChordName(Beat beat) =>
            beat.HasChord && beat.Chord != null && !string.IsNullOrWhiteSpace(beat.Chord.Name)
                ? beat.Chord.Name.Trim()
                : null;

        /// <summary>The one technique symbol the current model holds: bend, slide, hammer-on or vibrato.</summary>
        static string Technique(Note n)
        {
            if (n.Vibrato != VibratoType.None)
                return "~";
            if (n.BendType != BendType.None)
                return "b";
            if (n.SlideInType != SlideInType.None || n.SlideOutType != SlideOutType.None)
                return "s";
            if (n.IsHammerPullOrigin)
                return "h";
            return "";
        }

        /// <summary>
        /// A chord name from a set of pitch classes and its bass note. The root is the bass (the lowest note, which
        /// is the root in strummed voicings); among the qualities rooted there, the simplest one that covers the
        /// notes wins. Anchoring to the bass is what keeps Em7 from reading as G6 and G from reading as Gmaj7 on a
        /// stray note. If no bass-rooted chord fits (more strays than notes), other roots are tried as a fallback.
        /// </summary>
        static string DetectChord(HashSet<int> pcs, int bass)
        {
            if (pcs.Count == 0)
                return "";
            if (pcs.Count == 1)
                return NoteNames[pcs.First()];

            int Score(int root, int[] intervals)
            {
                var chord = new HashSet<int>(intervals.Select(i => (root + i) % 12));
                int covered = 0, strays = 0;
                foreach (var p in pcs)
                    if (chord.Contains(p)) covered++; else strays++;
                return covered * 4 - strays * 6 - intervals.Length;
            }

            var bestRoot = bass;
            var bestName = "";
            var bestScore = int.MinValue;
            foreach (var (name, intervals) in Qualities)
            {
                var s = Score(bass, intervals);
                if (s > bestScore)
                {
                    bestScore = s;
                    bestRoot = bass;
                    bestName = name;
                }
            }
            if (bestScore < 0)   // even the best bass-rooted chord leaves more strays than notes: try other roots
            {
                for (int root = 0; root < 12; ++root)
                {
                    if (root == bass)
                        continue;
                    foreach (var (name, intervals) in Qualities)
                    {
                        var s = Score(root, intervals) - 2;
                        if (s > bestScore)
                        {
                            bestScore = s;
                            bestRoot = root;
                            bestName = name;
                        }
                    }
                }
            }
            return NoteNames[bestRoot] + bestName;
        }

        // ------------------------------------------------------------ strumming
        /// <summary>
        /// The strumming: when an Ultimate Guitar strumming is given, each section's pattern is the page's pattern
        /// kept whole — a continuous multi-bar pattern the chart lays out from the section's first bar, repeating
        /// (SongChart.StrokeIndex). Otherwise the track's strums form a per-bar grid on sixteenth-note slots and
        /// each section's most common grid becomes its pattern, a stroke's direction from the file's recorded pick
        /// stroke where present, else the standard rule — down on the beat, up on the off-beat. (A GPX file often
        /// records no strokes at all.)
        /// </summary>
        /// <summary>The most bars of strumming a section's pattern shows: a longer one cannot be remembered.</summary>
        const int MaxShownBars = 3;

        /// <summary>The name of the strumming of a song with no sections: it has no section list, so the pattern is the whole song's.</summary>
        const string WholeSongName = "Song";

        static void Strumming(Score score, Staff staff, Song song, ImportReport report, double tpq, List<StrumPattern> ug, Dictionary<string, string> ugMap)
        {
            if (ug != null && ug.Count == 0)
                ug = null;
            var wholeSong = song.Sections.Count == 0;   // (a file with no section marks: one run of strumming for the whole song)
            var masterByIndex = score.MasterBars.ToDictionary(mb => mb.Index);

            // 1) per bar: which sixteenth slots strum, and the recorded direction of each strummed slot
            var byBar = new Dictionary<double, (char[] Grid, Stroke[] Recorded)>();
            foreach (var bar in staff.Bars)
            {
                if (!masterByIndex.TryGetValue(bar.Index, out var master))
                    continue;
                var barStart = master.Start / tpq;
                var slots = (int)Math.Round(master.TimeSignatureNumerator * 4.0 / master.TimeSignatureDenominator * 4);
                var grid = new char[slots];
                var rec = new Stroke[slots];
                Array.Fill(grid, '.');
                Array.Fill(rec, Stroke.Rest);
                // (the strums first, then each one's stroke: its direction by the rule depends on its neighbours)
                var active = new bool[slots];
                var strums = new List<(int Slot, Beat Beat)>();
                foreach (var voice in bar.Voices)
                    foreach (var beat in voice.Beats)
                    {
                        if (beat.Notes.Count(n => n.String >= 1) < 2)
                            continue;   // not a strummed chord
                        if (beat.Notes.Where(n => n.String >= 1).All(n => n.IsTieDestination))
                            continue;   // the strum before, held on
                        var slot = (int)Math.Round(((master.Start + beat.DisplayStart) / tpq - barStart) * 4);
                        if (slot < 0 || slot >= slots)
                            continue;
                        grid[slot] = 'X';
                        active[slot] = true;
                        strums.Add((slot, beat));
                    }
                foreach (var (slot, beat) in strums)
                    rec[slot] = Recorded(beat, RuleDown(active, slot));
                byBar[bar.Index] = (grid, rec);
            }
            if (byBar.Count == 0)
                return;

            // 2) group each bar's grid under the section that starts it
            var bySection = new Dictionary<string, List<(char[] Grid, Stroke[] Rec)>>();
            foreach (var kv in byBar.OrderBy(x => x.Key))   // bars in order, so “first to appear” is stable
            {
                var barStart = masterByIndex[kv.Key].Start / tpq;
                var section = song.Sections.FirstOrDefault(s => barStart >= s.AtQuarter - 1e-6 && barStart < s.AtQuarter + s.Quarters - 1e-6);
                if (section == null && !wholeSong)
                    continue;
                var name = section?.Name ?? WholeSongName;
                if (!bySection.TryGetValue(name, out var list))
                    bySection[name] = list = new List<(char[], Stroke[])>();
                list.Add(kv.Value);
            }
            if (bySection.Count == 0)
                return;

            // 3) per section: the Ultimate Guitar pattern when one is given (kept whole — a continuous multi-bar
            //    pattern the chart lays out from the section's first bar, repeating), else the file's most common
            //    grid with a direction for each of its active slots
            var sectionNames = bySection.Keys.ToList();
            var map = ug != null ? CheckMap(ugMap, ug, song, bySection, report) : null;
            var picks = new List<(string Section, StrumPattern Pattern, string How)>();
            var made = 0;
            var leftOut = new List<string>();
            foreach (var kv in bySection)
            {
                var ugFor = ug != null ? UgForSection(ug, kv.Key, sectionNames, map) : (null, "");
                StrumPattern pattern;
                if (ugFor.Pattern != null)
                {
                    // the page's pattern, as the page has it (what the strumming panel shows); what the band plays is the same laid
                    // out bar by bar as the chart would from the section's start (StrokeIndex), each stroke kept where the tab strums
                    // and left out where it does not (a held chord, a rest: a space in the tab)
                    pattern = new StrumPattern { Name = kv.Key, Subdivision = ugFor.Pattern.Subdivision, Sections = { kv.Key }, Origin = SongOrigin.UltimateGuitar };
                    pattern.Strokes.AddRange(ugFor.Pattern.Strokes);
                    var grids = kv.Value;
                    var ratio = ugFor.Pattern.Subdivision > 0 && 16 % ugFor.Pattern.Subdivision == 0 ? 16 / ugFor.Pattern.Subdivision : 0;   // the tab's slots per pattern slot
                    var perBar = ratio > 0 && grids.All(g => g.Grid.Length == grids[0].Grid.Length) && grids[0].Grid.Length % ratio == 0 ? grids[0].Grid.Length / ratio : 0;
                    if (perBar > 0 && ugFor.Pattern.Strokes.Count % perBar == 0)
                    {
                        var ugBars = ugFor.Pattern.Strokes.Count / perBar;
                        var played = new List<Stroke>();
                        var left = 0;
                        for (int bar = 0; bar < grids.Count; ++bar)
                            for (int j = 0; j < perBar; ++j)
                            {
                                var stroke = ugFor.Pattern.Strokes[bar % ugBars * perBar + j];
                                if (stroke != Stroke.Rest && grids[bar].Grid[j * ratio] != 'X')
                                {
                                    stroke = Stroke.Rest;
                                    ++left;
                                }
                                played.Add(stroke);
                            }
                        if (left > 0)
                        {
                            pattern.Played = played;
                            leftOut.Add($"{kv.Key} {left}");
                        }
                    }
                }
                else
                {
                    string Key(char[] g) => new string(g.Select(c => c == 'X' ? '1' : '0').ToArray());
                    var grids = kv.Value;
                    var keys = grids.Select(g => Key(g.Grid)).ToList();
                    pattern = new StrumPattern { Name = kv.Key, Subdivision = 16, Sections = { kv.Key }, Origin = SongOrigin.GuitarPro };
                    // the section's bars in a repeating run of the fewest bars that has every bar of the section in it
                    // (one bar when they all strum alike; the whole section when each differs, a one-off first-beat strum
                    // included); bars of different lengths (a change of meter) are too much for that
                    var period = keys.All(k => k.Length == keys[0].Length)
                        ? Enumerable.Range(1, keys.Count).First(p => Enumerable.Range(p, keys.Count - p).All(i => keys[i] == keys[i - p]))
                        : 0;
                    var exact = new List<Stroke>();
                    for (int bar = 0; bar < period; ++bar)
                    {
                        var matching = grids.Where((g, i) => i % period == bar).Select(g => g.Rec).ToList();
                        for (int i = 0; i < keys[bar].Length; ++i)
                            exact.Add(keys[bar][i] == '1' ? Direction(matching, i, Active(keys[bar])) : Stroke.Rest);
                    }
                    if (period > 0 && period <= MaxShownBars)
                        pattern.Strokes.AddRange(exact);
                    else
                    {
                        // a run too long to remember is not what the panel shows: that is the section's most common bar (ties
                        // broken by the one that establishes first in the section, bars being in order), and the band plays the run
                        var dominant = keys.GroupBy(k => k)
                            .OrderByDescending(x => x.Count())
                            .ThenBy(x => keys.IndexOf(x.First()))
                            .First().First();
                        var matching = grids.Where(g => Key(g.Grid) == dominant).Select(g => g.Rec).ToList();
                        for (int i = 0; i < dominant.Length; ++i)
                            pattern.Strokes.Add(dominant[i] == '1' ? Direction(matching, i, Active(dominant)) : Stroke.Rest);
                        if (period > 0)
                            pattern.Played = exact;
                    }
                }
                if (wholeSong)
                    pattern.Sections.Clear();
                song.Strumming.Add(pattern);
                if (ug != null)
                    picks.Add((kv.Key, ugFor.Pattern, ugFor.How));
                ++made;
            }
            report.Add(ug != null
                ? $"Strumming: {made} section pattern(s) from Ultimate Guitar (the chart lays each out from its section's first bar, repeating; a stroke is left out where the tab does not strum)."
                : $"Strumming: {made} section pattern(s) on a sixteenth grid (direction: recorded pick stroke → down on the beat, up on the off-beat).");
            if (ug != null)
                report.Add("UG strumming: " + string.Join(", ", picks.Select(p => $"{p.Section} → '{p.Pattern.Name}' ({p.How})")));
            if (leftOut.Count > 0)
                report.Add("UG strokes left out where the tab does not strum: " + string.Join(", ", leftOut) + ".");
        }

        /// <summary>
        /// The explicit section→pattern pairs (from <c>--strum-map</c>) that are usable: the section must be one of
        /// the sections that got a pattern, and the pattern one of the Ultimate Guitar patterns. Bad entries are
        /// reported and dropped, so the rest of the mapping still applies.
        /// </summary>
        static Dictionary<string, string> CheckMap(Dictionary<string, string> ugMap, List<StrumPattern> ug, Song song,
            Dictionary<string, List<(char[] Grid, Stroke[] Rec)>> bySection, ImportReport report)
        {
            if (ugMap == null || ugMap.Count == 0)
                return null;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in ugMap)
            {
                if (kv.Key != "*" && !song.Sections.Exists(s => string.Equals(s.Name, kv.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    report.Add($"Strumming: map entry '{kv.Key}={kv.Value}' ignored: no section named '{kv.Key}' in the file.");
                    continue;
                }
                if (kv.Key != "*" && !bySection.Keys.Any(k => string.Equals(k, kv.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    report.Add($"Strumming: map entry '{kv.Key}={kv.Value}' ignored: the section has no strummed bars.");
                    continue;
                }
                if (!ug.Any(p => string.Equals(p.Name, kv.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    report.Add($"Strumming: map entry '{kv.Key}={kv.Value}' ignored: no UG pattern named '{kv.Value}'.");
                    continue;
                }
                map[kv.Key] = kv.Value;
            }
            return map.Count > 0 ? map : null;
        }

        /// <summary>
        /// The per-bar strum grid a Guitar Pro file records (X = strummed slot, . = none) with the recorded pick
        /// direction (or R for none), for inspecting what the importer sees before it picks each section's pattern.
        /// </summary>
        public static void DiagStrum(string path, int trackIndex)
        {
            var report = new ImportReport();
            var score = ScoreLoader.LoadScoreFromBytes(File.ReadAllBytes(path));
            if (score.Tracks.Count == 0)
                throw new InvalidDataException("no tracks in the Guitar Pro file");
            var tpq = TicksPerQuarter(score);
            var track = PickTrack(score, trackIndex, report);
            var staff = track.Staves[0];
            var masterByIndex = score.MasterBars.ToDictionary(mb => mb.Index);
            Console.WriteLine($"track '{track.Name}' of {score.Tracks.Count}:");
            var n = 0;
            var distinct = new Dictionary<string, int>();
            foreach (var bar in staff.Bars)
            {
                if (!masterByIndex.TryGetValue(bar.Index, out var master))
                    continue;
                var barStart = master.Start / tpq;
                var slots = (int)Math.Round(master.TimeSignatureNumerator * 4.0 / master.TimeSignatureDenominator * 4);
                var grid = new char[slots];
                var rec = new Stroke[slots];
                Array.Fill(grid, '.');
                Array.Fill(rec, Stroke.Rest);
                var active = new bool[slots];
                var strums = new List<(int Slot, Beat Beat)>();
                foreach (var voice in bar.Voices)
                    foreach (var beat in voice.Beats)
                    {
                        if (beat.Notes.Count(x => x.String >= 1) < 2 || beat.Notes.Where(x => x.String >= 1).All(x => x.IsTieDestination))
                            continue;
                        var slot = (int)Math.Round(((master.Start + beat.DisplayStart) / tpq - barStart) * 4);
                        if (slot < 0 || slot >= slots)
                            continue;
                        grid[slot] = 'X';
                        active[slot] = true;
                        strums.Add((slot, beat));
                    }
                foreach (var (slot, beat) in strums)
                    rec[slot] = Recorded(beat, RuleDown(active, slot));
                    var key = new string(grid.Select(c => c == 'X' ? '1' : '0').ToArray());
                    distinct.TryGetValue(key, out var c); distinct[key] = c + 1;
                    var strokes = string.Join("", grid.Zip(rec, (g, s) => g == 'X' ? s.ToString()[0] : '.'));
                    Console.WriteLine($"bar {n,3} {master.TimeSignatureNumerator}/{master.TimeSignatureDenominator}  {new string(grid)}  {strokes}");
                    ++n;
            }
            Console.WriteLine($"\ndistinct grids: {distinct.Count}");
            foreach (var kv in distinct.OrderByDescending(x => x.Value))
                Console.WriteLine($"  x{kv.Value,-3} {kv.Key}");
        }

        /// <summary>
        /// The stroke a GPX beat records, or <see cref="Stroke.Rest"/> when it records neither a direction nor an accent:
        /// the pick stroke (muted where it is a palm mute or ghost), and an accent on the beat's notes (louder; its
        /// direction, when the file has no pick stroke, by the standard rule, <see cref="RuleDown"/>).
        /// </summary>
        static Stroke Recorded(Beat beat, bool ruleDown)
        {
            var accent = beat.Notes.Any(n => n.Accentuated != AccentuationType.None);
            var stroke = beat.PickStroke;
            if (stroke == PickStroke.None && !accent)
                return Stroke.Rest;
            var down = stroke == PickStroke.None ? ruleDown : stroke == PickStroke.Down;
            var muted = beat.IsPalmMute || beat.Notes.Any(n => n.IsGhost || n.IsDead);
            if (muted)
                return down ? Stroke.MutedDown : Stroke.MutedUp;
            if (accent)
                return down ? Stroke.AccentDown : Stroke.AccentUp;
            return down ? Stroke.Down : Stroke.Up;
        }

        static bool[] Active(string key) => key.Select(c => c == '1').ToArray();

        /// <summary>
        /// The direction of one active slot: the recorded pick stroke when the matching bars agree on it, else the
        /// standard rule (<see cref="RuleDown"/>). An accent (the recorded stroke is an accented one) must be on every one
        /// of them, as the direction must.
        /// </summary>
        static Stroke Direction(List<Stroke[]> recorded, int slot, bool[] active)
        {
            var rec = recorded.Select(r => r[slot]).Where(s => s != Stroke.Rest).ToList();
            if (rec.Count > 0 && rec.All(s => s == rec[0]))
                return rec[0];
            return RuleDown(active, slot) ? Stroke.Down : Stroke.Up;
        }

        /// <summary>
        /// The standard rule for the direction of a strum on the sixteenth grid (<paramref name="active"/>: the slots that
        /// strum): down on the beat, up on the off-beat; but strums on neighbouring sixteenths are the hand going down and
        /// up as fast as it can, so they alternate, by the slot's parity (down on the even ones: the beat and the "&amp;"
        /// down, the "e" and the "a" up).
        /// </summary>
        static bool RuleDown(bool[] active, int slot)
        {
            var fast = (slot > 0 && active[slot - 1]) || (slot + 1 < active.Length && active[slot + 1]);
            return slot % (fast ? 2 : 4) == 0;
        }

        /// <summary>
        /// Section-name synonyms, canonical first: the page and the file may come from different authors, and
        /// authors name the same kind of section differently. Compared after <see cref="CanonicalName"/>.
        /// </summary>
        static readonly (string Canonical, string[] Aliases)[] SectionNameSynonyms =
        {
            ("intro",        new[] { "introduction", "leadin", "opening", "start" }),
            ("verse",        new[] { "vrs", "stanza", "theme", "maintheme" }),
            ("chorus",       new[] { "refrain", "hook", "bigchorus", "finalchorus" }),
            ("bridge",       new[] { "middle", "middleeight", "interlude", "turnaround", "solobridge" }),
            ("solo",         new[] { "guitarsolo", "basssolo", "instrumentalsolo" }),
            ("instrumental", new[] { "inst", "riff" }),
            ("breakdown",    new[] { "drop" }),
            ("build",        new[] { "buildup", "ramp" }),
            ("outro",        new[] { "ending", "end", "coda", "tag", "final", "close" }),
            ("fill",         new[] { "transition" }),
        };

        /// <summary>
        /// A section name reduced for comparison: lowercased, punctuation removed, trailing numbers removed, common
        /// synonyms equated. “Verse 1” and “Stanza” both become “verse”; “Middle 8” and “Bridge” both become “bridge”.
        /// </summary>
        static string CanonicalName(string s)
        {
            var n = Regex.Replace(Regex.Replace(Regex.Replace((s ?? "").ToLowerInvariant(), @"\(cont\.\)", ""), "[^a-z0-9]+", ""), @"\d+$", "");  // ("Verse (cont.)" is the verse)
            foreach (var (canonical, aliases) in SectionNameSynonyms)
                if (n == canonical || aliases.Contains(n))
                    return canonical;
            return n;
        }

        /// <summary>
        /// The Ultimate Guitar pattern for a section, and how it was chosen: an explicit <c>--strum-map</c> entry
        /// (“mapped”), else one named after it (“named”), else the “main” pattern (the one not named after any
        /// section, “main”), else the first (“first”). Names are compared as canonical names (see
        /// <see cref="CanonicalName"/>), since the page and the file may come from different authors.
        /// </summary>
        static (StrumPattern Pattern, string How) UgForSection(List<StrumPattern> ug, string section, List<string> sections, Dictionary<string, string> map)
        {
            StrumPattern ByName(string name) =>
                ug.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (map != null && map.TryGetValue(section, out var mapped) && ByName(mapped) is { } p)
                return (p, "mapped");
            if (map != null && map.TryGetValue("*", out var all) && ByName(all) is { } q)
                return (q, "mapped");
            var named = ug.FirstOrDefault(p => CanonicalName(p.Name) == CanonicalName(section));
            if (named != null)
                return (named, "named");
            var main = ug.FirstOrDefault(p => !sections.Any(s => CanonicalName(p.Name) == CanonicalName(s)));
            if (main != null)
                return (main, "main");
            return (ug.FirstOrDefault(), "first");
        }

        // ------------------------------------------------------------ lyrics
        /// <summary>
        /// The words, in time order, from whichever track carries them. A new line starts after a gap of more than a
        /// bar or at a section boundary.
        /// </summary>
        static void Lyrics(Score score, Song song, double tpq, ImportReport report)
        {
            var masterByIndex = score.MasterBars.ToDictionary(mb => mb.Index);
            var sectionAt = new Dictionary<double, string>();
            foreach (var mb in score.MasterBars)
                if (mb.Section != null && !string.IsNullOrWhiteSpace(mb.Section.Text))
                    sectionAt[mb.Start / tpq] = mb.Section.Text.Trim();

            var words = new List<(double At, string Text)>();
            foreach (var track in score.Tracks)
            {
                foreach (var staff in track.Staves)
                {
                    foreach (var bar in staff.Bars)
                    {
                        if (!masterByIndex.TryGetValue(bar.Index, out var master))
                            continue;
                        foreach (var voice in bar.Voices)
                        {
                            foreach (var beat in voice.Beats)
                            {
                                var lyrics = beat.Lyrics;
                                if (lyrics == null)
                                    continue;
                                var at = (master.Start + beat.DisplayStart) / tpq;
                                foreach (var text in lyrics)
                                    if (!string.IsNullOrWhiteSpace(text))
                                        words.Add((at, text.Trim()));
                            }
                        }
                    }
                }
            }

            words.Sort((a, b) => a.At.CompareTo(b.At));
            LyricLine line = null;
            double lastAt = 0;
            string lastSection = null;
            foreach (var (at, text) in words)
            {
                string section = null;
                foreach (var kv in sectionAt)
                    if (kv.Key <= at)
                        section = kv.Value;
                if (line == null || at - lastAt > 4 || (section != null && section != lastSection))
                {
                    line = new LyricLine { AtQuarter = at };
                    song.Lyrics.Add(line);
                    lastSection = section;
                }
                line.Words.Add(new LyricWord { Text = text, AtQuarter = at });
                lastAt = at;
            }
        }
    }
}
