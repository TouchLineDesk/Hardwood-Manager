# Hardwood Manager

A basketball management simulation in the spirit of Football Manager: a living world of pro leagues, continental cups, FIBA windows, the World Cup and the Olympics, with drafts, salary caps, tactics and player development.

**Status:** prototype phase 1 — a headless simulation core with a procedurally generated 30-team closed-franchise league and a possession-level stat-based match engine. No UI yet.

## Locked decisions

| Decision | Choice |
| --- | --- |
| Stack | C# / .NET 8 headless core, Avalonia desktop UI (later), SQLite |
| First prototype | Match engine + one full 30-team season with playoffs |
| Reference league | Closed franchise (NABL-style: draft, cap, trades) |
| Ratings | 36 visible attributes on 1–20; Current/Potential Ability on 1–200 |
| Data | Procedurally generated default world; editable database and editor later |

## Layout

```
src/Hardwood.Core/        headless simulation core (no UI dependencies)
  Model/                  Player, Team, League, Rules, attributes
  Generation/             PlayerGenerator, WorldGenerator, name pools
  Engine/                 GameSimulator (possession pipeline), EngineConfig (all tunables), BoxScore
  Season/                 ScheduleBuilder, Standings, SeasonSimulator (play-in + playoffs), LeagueAverages
src/Hardwood.Cli/         console runner
tests/Hardwood.Tests/     xUnit tests, including a calibration check
```

## Run it

```bash
dotnet build
dotnet run --project src/Hardwood.Cli -- season --seed 2026      # full season, standings, playoffs, leaders, calibration table
dotnet run --project src/Hardwood.Cli -- game --seed 7 --log     # one game with box score and play log
dotnet run --project src/Hardwood.Cli -- roster --team BOS       # a generated roster
dotnet test
```

## How the match engine works

Each possession is resolved from the ten players on the floor:

1. Clock: seconds consumed are drawn around the two teams' pace settings (or a short transition possession after a live-ball turnover).
2. Turnover check from ball security vs. defensive pressure; live-ball turnovers credit a steal and start a transition possession.
3. Non-shooting foul check; bonus free throws once a team is in the penalty.
4. Shooter chosen by usage (shot creation, shooting, tendencies, the team's star-usage slider); shot zone chosen from the shooter's tendencies and the team's three-point emphasis.
5. Make probability: `σ(zoneBase + β·skill − β·contest − β·fatigue + home + form)`, where contest comes from the matched defender, help defense and wingspan.
6. Shooting fouls, blocks, free throws, rebounds (offensive rebounds continue the possession), assists.
7. Fatigue drains on the floor and recovers on the bench; substitutions happen at dead balls on energy and foul trouble.

Every number is in `EngineConfig`. The `season` command prints league averages against real-world targets so each change can be checked.

## Design document

The full game design document lives in the project's Claude doc (Hardwood Manager — Game Design Document).
