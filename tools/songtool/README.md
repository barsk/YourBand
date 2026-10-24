# songtool

The game's core code (`game/Assets/GuitarTutor/Core`) from the command line, without starting Unity. It is
compiled straight from the game's sources, with the C# version Unity uses, and the Newtonsoft.Json that comes
with Unity (open the game project once so `game/Library` exists).

```
dotnet build tools/songtool -c Release
tools/songtool/bin/Release/net8.0/songtool.exe import-ug <chords-page.html> [--tabs <tabs-page.html>] [--chords-per-bar n|auto] -o <song.json>
tools/songtool/bin/Release/net8.0/songtool.exe show <song.json> [--bars 1-16]
```

Save the Ultimate Guitar pages from the browser ("Save page as", HTML) into `songs-local/pages/`, and write the
songs to `songs-local/`. That folder is not in git: song pages and lyrics are licensed content and stay on your
machine.

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
