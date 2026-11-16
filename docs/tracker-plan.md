# The 2D game tracker (D67 B) — plan

*Danny's pick D67 B (Nov 14, #567): build the 2D game tracker and make it the main view of the live game; the play by
play, box score and shot map stay beside it. His roadmap's Phase 5 (#594–#597) sets the rules: no second simulator,
no change to results, pause/step/speed, small enough to discard, and anything not simulated is labelled as such.*

## What the engine can give (checked in the code, run 80)
- `GameSimulator` is **possession-based and statistical**: each possession resolves a turnover, a foul, or a shot from
  a zone (rim, short mid, long mid, corner three, above-the-break three), then a rebound. It has no court geometry,
  no positions and no movement.
- It already knew, at the moment it resolved them, every shot (shooter, zone, made, assister, blocker), free throw,
  turnover, steal, foul, rebound and period end, and who is on the floor. Until run 80 it only wrote those as
  commentary text (and missed shots not at all).

## Option chosen: visualise the engine's own events (Phase 5 option 1)
- **Core (run 80):** `PlayEvent` / `GameResult.Plays`: one record per play, written beside the commentary for games
  that keep a log (live and watched games). It reads no random numbers, so every result, box score and log line is
  unchanged (`Run80TrackerTests`, `LiveGameTests`, the characterization tests). It is not saved.
- **Desktop (run 80):** `TrackerLayout` places the ten players for a play — a standard half-court set for the side
  with the ball, each defender between his man and the basket, and the player who made the play moved to where it
  happened (the shot's zone, the line, the glass). **These spots are presentation only** and the court's legend says
  so. The live court draws the markers (surname tags, team colours, a gold ring on the players in the play), the
  ball and the shot's path, over the game's shots (now faint).
- **Controls:** the existing Play/Pause and speed, plus **Step** (one possession while paused). The caption over the
  court says what the play was.

## Not doing (Phase 5 "not yet")
Movement AI, a spatial simulation, a second simulator, 3D, heavy animation tooling.

## Later slices (in order; each is small and can be dropped)
1. **Motion between frames:** markers glide from the last frame's spots to the next (presentation tween, ~200 ms),
   so a possession reads as one action; the ball travels pass → shot.
2. **Beside it:** a compact live box score tab next to the play by play; the shot map stays on its tab.
3. **Big view:** a larger court (the play by play folding under it) for wide windows; check 1480 px and 130% text.
4. **Veto check D78** lists what shipped (see `claude/for-danny.md`).

## Risks
- Spots are made up, so they must never be read as analysis (the legend says so; nothing reads them back).
- A shooter subbed off at the dead ball that ended his possession is still drawn for that play.
- Memory: one small record per play (about 450 a game) for logged games only.
