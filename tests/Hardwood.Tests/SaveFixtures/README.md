# Save fixtures

Real saves made by past versions of the game. `SaveCompatibilityTests` loads each `real-v*.hwsave` with today's code,
plays a few days and saves again, so a real-world career can never silently stop loading.

| File | Format | Made by |
|---|---|---|
| real-v12.hwsave | 12 | v0.73.0 code (run 46): a real-world career, `GameSession.NewGame(seed 2026, team 5)` (the Toronto Raptors) advanced 12 days |
| madeup-v2.hwsave | 2 | commit 35a844e (phase 5, v0.5.0): a made-up (NABL) career, kept only to check that it is refused |

**The made-up world was removed in v0.74.0** (Danny's D61 B, #478). Made-up saves (formats 2–12, the NABL) no longer
load: `GameSession.Load` refuses them with `RealWorldData.MadeUpSaveMessage` before anything upgrades the file. Their
fixtures (`career-v2` … `career-v12`) were dropped. Real-world careers started in v0.51.0 at format 12, so there is no
older real save to keep.

**When the save format changes:** bump `SaveFile.SchemaVersion`, make a real-world save with the *previous* version's
code, add it here as `real-v<N>.hwsave`, and add a row to this table.
