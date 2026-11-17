# Play-test guide (v0.105.3)

Danny's own play-test is the gate for what gets built next (D76 B, decision #630). This guide is the short version of
the notebook page at https://claude.ai/artifact/9MjakW3Aqhy7hqiZgrHF3r, where the notes are written and where the next
build run reads them.

## Before you start
- Install the release zip (`HardwoodManager-v0.105.3-win-x64.zip`, or the newest release), unzip it anywhere and run the game.
- Your graphics packs (`prographics`, `collegegraphics` in `Desktop\FMNEWGANv2`) load at start as before.
- Every career is a real-world career. Saves go to the game's usual save folder; nothing from an older version is needed.

## What to try (about two to three hours)
1. Start a real career with a club you'd pick yourself.
2. Read the Inbox and Home before you press anything: is it clear what the board wants and what to do first?
3. Set your Rotation and Tactics, or let the assistant do it.
4. Watch your first game live: the court, the play-by-play, timeouts and the halftime card.
5. Press Continue through a month: how long does it take, and which messages matter?
6. Try one move: a signing, a trade bid or a player talk.
7. Check Standings, Leagues and a player page: can you find things in two clicks?
8. Play out the season and the playoffs, and read the board's review.
9. Go through the summer: re-signings, the draft and free agency.
10. Quit (main menu or close the window) and load your career once: it comes back on the day you left.

## How to report
One note per thing, tagged **Bored**, **Confused**, **Kept waiting**, **Felt wrong**, **Bug** or **Liked it**, with the
page and the game date from the top bar. Say what you expected and what happened.

## Known before you start
- **Saving (v0.80.0, D74 B):** the career saves every 7 days, at each phase change, after your decisions, on
  Settings' **Save now**, and when you go to the main menu or close the window. The top bar shows the last save under
  the date. A crash loses at most the days since then. Continue takes about 20 seconds a season (was about 90).
- Overseas clubs are generated, and national teams, FIBA and the Olympics aren't built yet.
- The open bug list is `claude/bug-hunt.md` in the project. Known Mediums you may meet in a first season: stars can
  sign for the minimum in small leagues (H61); the owner's target and verdict can disagree with the record (H70);
  some generated clubs carry real or former franchise names (H62); the tactics advice can contradict itself (K2).
  Note them anyway if they bother you: your notes decide the order.
- Since Nov 17 the builder ships only fixes until your notes arrive (the play-test freeze); every run still makes a
  new release, so play the newest one.
