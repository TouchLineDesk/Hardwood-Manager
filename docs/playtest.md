# Play-test guide (v0.112.1)

Danny's own play-test is the gate for what gets built next (D76 B, decision #630). This guide is the short version of
the notebook page at https://claude.ai/artifact/9MjakW3Aqhy7hqiZgrHF3r, where the notes are written and where the next
build run reads them.

## Before you start
- Install the newest release zip (`HardwoodManager-v0.112.1-win-x64.zip` when this was written; any later one is better),
  unzip it anywhere and run the game.
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
- The open bug list is `claude/bug-hunt.md` in the project. Fixes shipped since v0.105.3 (some still being re-checked): contradictory tactics advice
  (K2), real franchise names on generated clubs (H62), stars on minimum deals in small leagues (H61, v0.107.0), board
  verdicts that disagree with the record (H70, v0.109.0), role players leading the NBA in scoring (H105, v0.108.2) and
  relegation that cost nothing (K7, v0.110.0), made-up players leading NBA stat tables (H110, v0.110.3) and stars
  retiring with years left on their deals (K1 part 1, v0.110.4), real rotation players leaving for scraps abroad when
  their deals end, and made-up "stars" abroad topping the summer's free agents (K1 part 2 and H123, v0.111.0), and contenders losing their own young star to the wage budget (H122, v0.112.0: the owner now stretches up to 35% over to keep a CA 150+ player under 26), and the owner's confidence collapsing after a playoff season a few wins short of the target (H70, v0.112.1: such a season now costs at most about 19–27 points, and "Critical" needs a losing record). Still open and likely in a first season: real players drain out of the
  NBA over a few seasons in a relegation world (K1), and leagues abroad score too little (K3).
  Note them anyway if they bother you: your notes decide the order.
- **Relegation (v0.106.0+):** the last club in each NBA division plays the G League division winner, best of three, for
  its place. If the board tells you to "stay up", that is the job; going down costs you its confidence.
- Since Nov 17 the builder ships only fixes until your notes arrive (the play-test freeze); every run still makes a
  new release, so play the newest one.
