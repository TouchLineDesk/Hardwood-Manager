# Critic tooling

Used by the critic reviews (`claude/critic-review-prompt.md` in the project) and the autopilot runs (`claude/autopilot.md`).
Neither project is part of the solution, the game or CI.

| Tool | Run | What it gives |
|---|---|---|
| Balance audit | `dotnet run --project src/Hardwood.Cli -c Release -- audit --seasons 10 --seed 2026 --out a.md` | Every balance metric with its reference range; "Out of range" lines at the top |
| Critic bot | `dotnet run --project tools/CriticBot -c Release -- <nabl-strong\|nabl-weak\|local\|lithuania\|japan\|college\|college-strong> 3 31` | A scripted 3-season career through the real game: records, board, money, morale, assistant tasks, inbox counts |
| Tactics lab | `dotnet run --project tools/TacticsLab -c Release -- 400` | Win % of every fixed offence and defence against an auto-coached opponent (6 matchups) |
| Screenshots | `HW_SCREENSHOT_DIR=shots dotnet test tests/Hardwood.Tests --filter ScreenshotTests` | 32 page screenshots at 1480 px |

The careers write saves to the temp folder as `critic-<label>.hwsave`.
