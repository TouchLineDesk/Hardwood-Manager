# Save fixtures

Real saves made by past versions of the game, one per save format. `SaveCompatibilityTests` loads each one with
today's code, plays a few days and saves again, so an old career can never silently stop loading.

| File | Format | Made by |
|---|---|---|
| career-v2.hwsave | 2 | commit 35a844e (phase 5, v0.5.0) |
| career-v3.hwsave | 3 | commit 3ba1a90 (PR #1: ethnicity, draft record) |
| career-v4.hwsave | 4 | commit 44d78a1 (PR #2: portrait faces) |
| career-v5.hwsave | 5 | scaffolding branch (world beyond the NABL) |
| career-v6.hwsave | 6 | player-movement branch (buyout clauses, draft rights abroad) |
| career-v7.hwsave | 7 | tactics branch (systems, schemes, roles, matchups, presets) |
| career-v8.hwsave | 8 | morale branch (morale, chemistry, promises) |
| career-v9.hwsave | 9 | coach-and-board branch (coach, owners, job) |
| career-v10.hwsave | 10 | money-model branch (club budgets, open market, no draft) |
| career-v11.hwsave | 11 | college branch (118 college programs, graduates at 23) |

Each is `GameSession.NewGame(seed 2026, team 5)` advanced 12 days. **When the save format changes:** bump
`SaveFile.SchemaVersion`, make a save with the *previous* version's code, add it here as `career-v<N>.hwsave`, and
add a row to this table.
