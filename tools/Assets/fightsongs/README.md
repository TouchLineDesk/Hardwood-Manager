# Fight songs, tier 1: real songs played from new note data (fight songs B, run 167)

Each `*.song` file is one school's real fight song whose **music was published in 1930 or earlier** (US public domain),
typed from a score into this text format. The game embeds the files (`Hardwood.Core.csproj`) and arranges each one for its
code-drawn band (`RealFightSongs.Arrange`): a drum roll and a fanfare, the chorus, a big last strain with the glockenspiel
and the horns, and a tag. No recording is ever used, and only the original tune is typed: later arrangements and changed
lyrics are separate copyrights. The chords are our own arrangement. The research table is `claude/fight-songs-pd.md`.

## The format
```
# a comment line (only whole lines starting with '#'; there are no end-of-line comments)
program: Michigan
title: The Victors
composer: Louis Elbel
published: 1898
status: public domain (…)
source: https://…
notes-from: …
checked: …
meter: 4
pickup: 1
tempo: 144
key: Eb
style: March
melody:
G4:2 Eb4:1 F4:1 | …
chords:
Eb | Eb | Bb7 | % | …
```
- **program:** the school as `ncaa_programs.json` writes it. **published:** the first publication year (1930 or earlier).
  **status / source / notes-from / checked:** why it's public domain, the confirming source, where the notes came from and how they were checked.
- **meter:** quarter-note beats in a bar (2 for 2/4, 4 for 4/4 or 2/2). **pickup** (optional): beats before the first full bar.
  **tempo:** quarter notes a minute. **key:** the tonic (the tune must end on it). **style:** `March` (oom-pah) or `Swing`/`Shuffle`.
- **melody:** `note:beats` with C4 = middle C; `r` is a rest; `_` holds the last note over the bar line.
- **chords:** one group per melody bar, splitting it evenly: Eb, Cm, Bb7, Am7, F#dim, C6, Cmaj7, C+; `%` repeats the last chord.
A bar that doesn't add up to `meter` beats, or a chord line with a different number of bars, fails the tests
(`Run167RealFightSongTests`) with the bar's number.

## Typing a song from LilyPond
`lily2fs.py song.ly 2 --pickup 1` turns a one-voice LilyPond melody (`\relative` or `\fixed`) into the melody lines,
split at the bar lines, and stops at a bar that doesn't add up.

## Songs so far
| File | Program | Song | Published |
|---|---|---|---|
| michigan.song | Michigan | The Victors (Louis Elbel) | 1898 |
| navy.song | Navy | Anchors Aweigh (Charles A. Zimmermann) | 1906 |
| purdue.song | Purdue | Hail Purdue! (Edward J. Wotawa), the refrain | 1913 |
| oregon.song | Oregon | Mighty Oregon (Albert Perfect) | 1916 |
