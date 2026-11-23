# Fight songs, tier 1: real songs played from new note data (fight songs B, run 167)

Each `*.song` file is one school's real fight song whose **music was published in 1930 or earlier** (US public domain),
typed from a score into this text format. The game embeds the files (`Hardwood.Core.csproj`) and arranges each one for its
code-drawn band (`RealFightSongs.Arrange`): a drum roll and a fanfare, the chorus, a big last strain with the glockenspiel
and the horns, and a tag. No recording is ever used, and only the original tune is typed: later arrangements and changed
lyrics are separate copyrights. The chords are our own arrangement. The research table is `claude/fight-songs-pd.md`.

## The format
```
# a comment line
program: Michigan            # the school as ncaa_programs.json writes it
title: The Victors
composer: Louis Elbel
published: 1898              # the first publication year (must be 1930 or earlier)
status: …                    # why it is public domain
source: https://…            # the confirming source
notes-from: …                # where the notes were typed from
checked: …                   # how they were checked
meter: 4                     # quarter-note beats in a bar (2 for 2/4, 4 for 4/4 or 2/2)
pickup: 1                    # optional: beats before the first full bar
tempo: 144                   # quarter notes a minute
key: Eb                      # the tonic (the tune must end on it)
style: March                 # the band's accompaniment: March (oom-pah) or Swing/Shuffle
melody:
G4:2 Eb4:1 F4:1 | …          # note:beats, C4 = middle C; r = a rest; _ = hold the last note over the bar line
chords:
Eb | Eb | Bb7 | …            # one group per melody bar, splitting it evenly; Eb, Cm, Bb7, Am7, F#dim, C6, Cmaj7, C+, %
```
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
