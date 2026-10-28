# songtool

The game's core code (`game/Assets/GuitarTutor/Core`) from the command line, without starting Unity. It is
compiled straight from the game's sources, with the C# version Unity uses, and the Newtonsoft.Json that comes
with Unity (open the game project once so `game/Library` exists).

```
dotnet build tools/songtool -c Release
tools/songtool/bin/Release/net8.0/songtool.exe import-ug <chords-page.html> [--tabs <tabs-page.html>] [--chords-per-bar n|auto] -o <song.json>
tools/songtool/bin/Release/net8.0/songtool.exe import-gpx <file.gpx> [--track n] [--strum <chords-page.html|strum.json>] -o <song.json>
tools/songtool/bin/Release/net8.0/songtool.exe export-strum <chords-page.html> -o <strum.json>
tools/songtool/bin/Release/net8.0/songtool.exe show <song.json> [--bars 1-16]
```

Save the Ultimate Guitar pages from the browser ("Save page as", HTML) into `songs-local/pages/`, and Guitar Pro
files (`.gpx`, or `.gp`/`.gp3`/`.gp4`/`.gp5`) into `songs-local/gpx/`; write the songs to `songs-local/`. That
folder is not in git: song pages and lyrics are licensed content and stay on your machine.

## Guitar Pro import (`import-gpx`)

Besides the strummed guitar, the other tracks come along: their notes are `parts` of the song (harmonica, lead
guitar, vocal, bass; drums not yet), the first two lead tracks (not vocal or bass) playing on the band's lead
channels 1 and 2. A lead track that plays only here and there makes its stretches sections of their own
("Harmonica Intro" in an intro, else "Harmonica Solo"; what is left of the section is "(cont.)"); `gpx-info
<file>` shows which tracks play in which bars.

Reads a Guitar Pro file with [alphaTab](https://www.nuget.org/packages/AlphaTab) (the `AlphaTab` NuGet package,
kept in this tool so the game never takes the dependency) and writes the same `song.json` the UG importer does.
It imports what the current song model holds:

- the main guitar track's notes as the tab (one riff, exact string/fret/timing, plus the one technique symbol the
  model keeps: bend, slide, hammer-on, vibrato). Dead notes (the "x" of a muted strum, which the file stores as
  fret 0) are left out of the tab and the chords — they would read as open strings — and make a muted stroke when
  the strums are taken from the file. A note tied from the one before (the tie arc) is that note held on, not struck
  again: it lengthens the tab note, and a beat of tied notes only is the strum before, held (not another strum);
  `gpx-info <file> --beats <track> <bars>` lists a track's notes with their flags;
- the chords and their voicings. The strums are grouped into regions — a run of strums with the same bass note and
  no long pause, with a brief passing strum (e.g. a leading-tone shape into the next chord) merged into the
  neighbor it most resembles. Where the file writes a chord name it is used as-is; otherwise the name is
  **detected** from the pitch classes that dominate the region, anchored to its bass note (the root): that is what
  keeps an Em7 from reading as a G6 and a G from reading as a Gmaj7 on a stray note;
- the sections, tempo, time signatures, lyrics and song info (title, artist, key, capo, tuning);
- the strumming, as described below.

`--track n` picks the track to read the tab from (by index). By default it is the busiest six-string guitar track
(the most notes): the rhythm guitar strums full chords, so it carries the chords, while a lead line is sparser.
The richer Guitar Pro data — the other tracks (bass, drums, second guitar, vocals) and full technique detail
(bend points, slide targets, dynamics) — is not imported yet; that needs a later song-model extension.

### Strumming (`--strum`)

With `--strum <chords-page.html>` (read in this same process) or `--strum <strum.json>` (written by
`export-strum`), each section's pattern is the **Ultimate Guitar pattern kept whole** — a continuous multi-bar
pattern (one, two, three or more bars; the page's subdivision, sixteenth notes for these pages). The chart lays each
pattern out from the section's first bar and repeats it (`SongChart.StrokeIndex`): a two-bar pattern gives the
section's bars 1–2 the whole pattern, bars 3–4 it again, and so on. A section always starts at the beginning of its
pattern, even when the previous one ended mid-pattern.

Without `--strum`, the track's strums become a per-bar grid on **sixteenth-note slots** (the `StrumPattern`
subdivision is 16) and each section's bars become its pattern: one bar when they all strum alike, else the shortest
run of bars that repeats through the section (the whole section when each bar differs, so a one-off strum such as a
chord struck on beat 1 of one bar is kept; the chart lays it out from the section's first bar like the Ultimate
Guitar patterns). Bars of different lengths (a change of meter) fall back to the section's most common bar. A
stroke's **direction** is, in order:

1. the file's recorded pick stroke (`PickStroke`), muted where it is a palm mute or ghost; a GPX file often records
   no strokes at all, so this usually does not fire;
2. the standard rule: **down on the beat, up on the off-beat**.

A beat whose notes carry an **accent** in the file becomes an accented stroke (louder: the band plays it at velocity
116 down, 104 up, against 72–96 for the others), in the direction of its pick stroke or by the standard rule; when the
section's matching bars do not all accent it, it is a plain stroke. A palm-muted beat stays muted.

**Matching a pattern to a section.** The page and the file may come from different authors, who name the same kind
of section differently, so names are compared in a canonical form: lowercase, punctuation and trailing numbers
removed, common synonyms equated (“stanza” = “verse”, “refrain”/“hook” = “chorus”, “introduction” = “intro”,
“middle 8”/“interlude” = “bridge”, “coda”/“tag”/“ending” = “outro”, …). The choice is, in order: an explicit
`--strum-map` entry, else a pattern named after the section, else the “main” pattern (the one not named after any
section of the file), else the first pattern. With `--strum` the report prints the result per section, e.g.
`Verse 1 → 'Verse' (named), Bridge 1 → 'Verse' (first)`, so a mismatched name is visible.

`--strum-map "Section=Pattern;…"` assigns patterns explicitly, where the section `*` stands for every section and
names match as the report prints them (case-insensitive). An entry with an unknown section or pattern is reported
and dropped, the rest still apply. This is the escape hatch when the two sources' naming is beyond the synonyms.

`export-strum <chords-page.html>` writes just a page's strumming patterns to a `strum.json` (the rest of the page
is skipped) so one UG strumming can be reused across several GPX files or inspected on its own.

Note: Guitar Pro stores lyrics per syllable, so words come out split ("mi ner"); merging them into words is a
later refinement.

## Experiment E3 (2026-10-21): Wicked Game

Both Wicked Game pages (chords and tabs), imported into one layered song:

- 8 sections, 100 chords, 36 lyric lines (11 of them backing vocals, written in parentheses on the page), 133 bars at 112 BPM, 4:45. The album version is about 4:49, so the
  guessed chord lengths hold over the whole song.
- Timing check: the guess makes the intro 0:34; the tabs page marks it 0:00-0:34 in the recording.
- The tabs page: all 19 of its lyric lines found in the chords page (matched on similar text, as the pages word
  some lines differently), the same chords everywhere, 25 riffs and fills (247 notes): 2 placed by the
  recording's time marks, 21 fills in the last chord of their lines, 2 solo blocks spread over the instrumental.
- Bm, A and E shapes from the chords page; four strumming patterns (D - D U - U D U for the whole song).

What the first guess cannot know yet: the exact rhythm of riffs (ASCII tab has no note lengths; notes are spread
by their columns) and lines that do not follow the "a bar per chord, filled to whole bars" pattern. "Learn by
playing" (M3) and the chart editor correct these.

Backing vocals (2026-10-22): words in parentheses become a line of their own, marked as backing (the chart shows
it dimmed and in italics). A line's words never start before the previous line's last word, and words before a
line's first chord (a pickup) are timed at a speaking pace, at most a bar ahead: before, chords written close
together put Wicked Game's chorus pickup two bars early, into the backing vocals.
