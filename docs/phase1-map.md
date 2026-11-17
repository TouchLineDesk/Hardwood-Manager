# Phase 1 map: `GameSimulator.GameState` and `GameSession`

*Run 57 (Nov 15, 2026), Phase 1 part 1 of Danny's roadmap (#596). This map was built by reading the code on `main` at v0.79.4 and checking what it does with the characterization tests (`tests/Hardwood.Tests/CharacterizationTests.cs`). It goes by what the code does, not by how big each class is. Line numbers are approximate.*

## 1. `GameSimulator.GameState` (Engine/GameSimulator.cs, about 800 of the file's 1,028 lines)

The engine is statistical and possession-based. There is no court geometry. Each step resolves one possession from the ten players on the floor, their tactics and fatigue, and the game's own `Rng(seed)`.

| responsibility | where | random numbers | notes |
|---|---|---|---|
| Per-team live state (`Side`): box, on-court five, dressed list, fouls, energy, tactic in force, rotation targets | `Side`, ~120–226 | none | Built before the game. `CoachAi.Prepare`/`Remember` run first and change the clubs' film, so replaying a matchup needs fresh teams. |
| Game flow: period open and close, the clock, possession order, overtime, completion | `Step`, `OpenPeriod`, `ClosePeriod`, `BurnClock`, ~275–391, 905 | none | One `Step` is one possession. The live screen calls it directly. |
| **Possession resolution**: turnover, shooter, zone, defender, contest, skill, make or miss, block, and-one, assist, rebound, free throws, fouls, end-of-game fouling | `RunPossession` … `CommitFoul`, ~451–904 (about 450 lines) | **all of the game's draws** | This is the simulation. Its draw order is the game's result. |
| Substitutions and rotation: fatigue, fouls, closing time, blowouts, position fit, lineup requests from the live screen | `CheckSubstitutions`, `ReplacePlayer`, `RequestLineup`, `ApplyLineupRequest`, `DrainEnergy`, ~415–449, 915–1013 | some (inside `CheckSubstitutions`) | Lineup requests apply at the next possession boundary. |
| Tactics in play: the fit cache, the clutch preset, the halftime read (#268) | `SetTactics`, `FitFor`, `HalftimeAdjust`, `Side.ClutchTactic` | none | **The halftime read is skipped when the game is watched live** (`Interactive`): the coach makes his own changes. |
| **Commentary (play-by-play)**: phrase tables, the call for a made basket (assist, lead change, milestones), period recaps, plain event lines | **Moved to `Engine/PlayByPlay.cs` in v0.79.6 (run 58).** `GameState` keeps a `PlayByPlay?` (null without `keepLog`) and hands it events: `Line`, `MadeBasket` (with the assister, now returned by `RecordFieldGoal`), `FreeThrow`, `PeriodEnd`; `Log(...)` forwards to `Line` | **none**: the phrasing is picked by the log's position | Only exists with `keepLog`. Tests show the log never changes a result; `PlayByPlayTests` check the hand-off. |
| Game flow chart (`FlowPoint`s) | `RecordFlow`, ~338 | none | Log only. |

**Invariants (each now has a test):**
- A game's result is decided by the two clubs' state (roster, tactics, film), the rules, the seed and the date. **The play log never draws a random number** (`The_play_log_changes_nothing_in_the_result`).
- The same seed on fresh clubs gives the same box score, play-by-play and flow (`The_same_game_…`, pinned by hash in `A_seeded_game_gives_the_pinned_*`).
- **A live game stepped to the end is the same game as `Simulate` with the log on** (`A_live_game_stepped_to_the_end_matches_the_quick_game`), apart from the halftime read.
- Injuries are rolled after the final buzzer from the same random stream (only when a date is given).

## 2. `GameSession` (Season/GameSession.cs, 1,328 lines, plus 10 partials, about 1,930 lines)

| responsibility | where | random numbers (session `_rng`) |
|---|---|---|
| **Career set-up**: new game, the world, owners, budgets, the first expectations, the welcome | constructor, `NewGame`, `BuildSeason` | the schedule; budgets use their own seeded stream |
| **The day loop**: `AdvanceDay` = `BeginDay` (your game abroad or in college, then the NBA's games in schedule order, or the playoff day) + `EndDay` (everything else) | ~301–575 | one seed per game, in schedule order |
| Recording an NBA game: results, standings, season stats, career milestones, the real record chase, league records, the game report | `RecordRegularSeasonGame`, `ReportGame`, ~386–445, 589 | none |
| **The world's day**: every other league, college and the pyramid, through `GameWorld.PlayThrough` | `PlayWorld`, `CatchUpWorld` | **the world's own `_rng`** |
| Weekly and daily systems, in `EndDay`'s order: the studio show grades, the world, people (Mondays), the board (Mondays), the job market, training, recruiting, the trail, transfers, digest, jobs, results digest, press, inbox clean-up, AI trades (two dates), deadline notes, the All-Star game, the end of the regular season, playoff rounds, the champion, the summer | `EndDay` and the partials (`Media`, `Staff`, `Recruiting`, `CollegeTrail`, `Transfers`, `Inbox`) | many: their order is part of every result |
| The inbox and news policy: what's posted, what waits for the digest, clean-up | `Post`, `PostWorld`, `MaybePost*`, `Inbox` partial | press and locker topics |
| Your job: board, expectations, firing, applying, offers, coaching changes | ~670–880, `Club` partial | hire chances, coach names |
| Players and staff: talks, contracts, staff hiring, training, delegation | `Contracts`, `Staff` partials, `Talk` | talks, staff generation |
| Transfers and trades | `Transfers` partial, `AiTrades`, `ProposeTrade` | buyers, fees |
| **The summer**: begin the offseason, awards, re-signing, lottery and draft, free agency, the new season | `BeginOffseason` … `StartNewSeason`, ~929–1250, and `Offseason` | the shared session stream |
| Saving and loading | `SaveNow`, `Load`, `Restore*`; the stores in `Persistence/` | **each save draws one number from the session's stream and one from the world's** |

**The session's phases:** Preseason → RegularSeason (on opening night) → Postseason (when every scheduled game is played) → Offseason (when the playoffs are complete and your own season is over, or July 20) → the offseason's phases (`ReSigning` → `Draft` → `FreeAgency` → `Complete`) → `StartNewSeason` → Preseason.

**The persistence boundary:** `SaveNow` writes five stores, in this order:
- `SaveFile.Save`: the league, players, stats, prospects, clubs abroad and college players;
- `SaveSession`: the date, phase, the session's next seed, the schedule, results, box lines, playoffs, the offseason and the inbox;
- `SaveWorld`: the world's competitions and fixtures, and the world's next seed;
- `SavePeople`: morale and promises;
- `SaveCareer`: the coach, the job, the money, the head-to-head and side box scores (in `career_meta`).

It is 32 tables, and nearly all of them are rewritten on every save (other clubs' box lines are written once). That is the 90% of a managed season's time measured in the baseline.

**Invariants (each now has a test):**
- **A save loads back as the same career** (every result, table, bank balance, roster, season stats and the inbox), checked in the regular season, mid-playoffs and in the summer (two offseason phases).
- **Two loads of one save play the same future**, so a bug report's save replays exactly.
- **A save of a loaded career loads back the same again.**
- **Saving draws from both random streams.** A career that saves plays a different future from one that doesn't, and carrying on in memory after a save differs from loading that save. *Phase 2 note (D74): a faster save must keep these two draws, or every seeded career moves. Saving less often (D74 B) would change results by itself.* **Done in v0.80.0 (run 60):** the day loop saves every 7 days and at phase changes (`GameSession.SaveIfDue`); decisions, Save now and leaving still call `SaveNow`. Each save still makes both draws. No pinned hash moved: the characterization pins play without saves, and the save tests check invariants.
- **Watching your game live plays the same day as pressing Continue**, game for game, except for the halftime read (applied for you on Continue, left to you live).
- The baseline's fingerprints hold in CI: the 200-game table (22839796 / 22262109) and the headless season (OKC, 296382 / OKC 296763; re-pinned in run 101 when H78 changed the schedule's dates, was 296945 / 296650), and a managed career's first 30 days are pinned by hash.

## 3. A proposed split (one narrow step at a time, each with these tests unchanged)

The order runs from least risk to most. None of these steps changes the save format or the order of random numbers.

1. **Commentary out of `GameState`** into `Engine/PlayByPlay.cs`. It would take the phrase tables, `Call`, `PeriodRecap`, `Ordinal`, the period's points and the last assist. `GameState` would hand it the events it already logs. It draws no random numbers and is pinned by the play-by-play hash. It is also where Phase 5's match view would read events from, so the 2D prototype would watch real engine events rather than a second simulator.
   - *Risks:* the phrase choice uses the log's position, which must be kept exactly; the `_lastScorer` used by the flow chart stays in `GameState`.
   - *Effort:* about 120 lines moved.
   - **Done in v0.79.6 (run 58, PR #110):** about 90 lines moved; `_lastAssister` is gone (the assister is passed with the basket); every characterization pin and the bench fingerprints unchanged. The match view can subscribe here later.
2. **Recording a game out of `GameSession`** (`RecordRegularSeasonGame`: stats, milestones, the record chase, league records) into `Season/GameRecorder.cs`. It draws no random numbers. Its output is the session fingerprint and the inbox.
3. **The inbox and news policy** (`Post`, `PostWorld`, `MaybePostDigest/Results/Jobs`, the `Inbox` partial) into an `InboxDesk` that the session owns. No random numbers, but many callers.
4. **The day pipeline as an explicit list of steps** in `EndDay`'s current order, each a named method. This one touches the order of random numbers, so it's only safe with the session pins. It is also what a faster daily save (D74 A) would hook into: each step could mark what it changed.
5. **Possession resolution** into its own class. This is the largest and riskiest step (all of the game's draws). It comes last, if at all; the box score, play-by-play and bench pins guard it.

**Not proposed:** splitting files for their own sake, a second simulator for the match view, or changing how the session's and world's random streams are shared (that would move every seeded result).

## 4. What the tests found
- No determinism, save-compatibility or correctness problems. Every round trip held, and the pins match in Debug and Release.
- **The live screen skips the halftime read** (by design: `Interactive`). A coach who watches every game never gets the read he turned on in Tactics. This is a Phase 3 clarity item: the live screen's halftime card could offer the read as a suggestion. It is not a bug.
- **Saving consumes random numbers.** This doesn't hurt a player, but it ties D74 to results (see above).
