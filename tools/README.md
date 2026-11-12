# Critic tooling

Used by the critic reviews (`claude/critic-review-prompt.md` in the project) and the autopilot runs (`claude/autopilot.md`).
Neither project is part of the solution, the game or CI.

| Tool | Run | What it gives |
|---|---|---|
| Balance audit | `dotnet run --project src/Hardwood.Cli -c Release -- audit --seasons 10 --seed 2026 --out a.md` | Every balance metric with its reference range; "Out of range" lines at the top |
| Critic bot | `dotnet run --project tools/CriticBot -c Release -- <nabl-strong\|nabl-weak\|local\|lithuania\|japan\|college\|college-strong\|nba-real-strong\|nba-real-mid\|nba-real-weak> 3 31` | A scripted 3-season career through the real game: records, board, money, morale, assistant tasks, inbox counts. The `nba-real-*` careers start a real-world career (the real NBA's strongest, 15th and weakest team) and log the top players, their contracts, and where the real players are each season. `nba-real-XXX` (e.g. `nba-real-OKC`) picks a franchise. Since v0.54.0 the bot prints the record chase in full and, when a club opens a season over its wage budget, the roster's contracts |
| Tactics lab | `dotnet run --project tools/TacticsLab -c Release -- 400` | Win % of every fixed offence and defence against an auto-coached opponent (6 matchups) |
| Screenshots | `HW_SCREENSHOT_DIR=shots dotnet test tests/Hardwood.Tests --filter ScreenshotTests` | 32 page screenshots at 1480 px |

The careers write saves to the temp folder as `critic-<label>.hwsave`.
| March Madness audit | `dotnet run --project src/Hardwood.Cli -c Release -- madness --seed 31 --tournaments 150 --out mm.md` | The college world alone: seed-v-seed win rates, titles by seed, top programs' strength against the bubble, college scoring (about 30 s for 100) |
| College margin check | `dotnet run --project src/Hardwood.Cli -c Release -- calib --seed 3 --games 3000` | Full-engine college games (neutral court): margin per point of strength gap and the spread around it. The college quick results use the same two numbers (`QuickSim`, #188) |
| Real NBA check | `dotnet run --project src/Hardwood.Cli -c Release -- realcheck --seed 2026 --seasons 4` | The real league (R2): the top 30 players by CA, every team's strength and simulated wins against its real 2025-26 and 2026-27 records, with the correlations (about 20 s for 3 seasons), and the market on opening day: the top 20's real contracts and asking prices, stars (CA 165+) under $60M, and payrolls against the cap; since v0.54.0 also the players not yet seen this season (with their CA, or "cut") and the real careers carried in. `audit --real` runs the balance audit on the real NBA |
