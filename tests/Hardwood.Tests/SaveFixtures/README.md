# Save fixtures

Real saves made by past versions of the game. `SaveCompatibilityTests` loads each `real-v*.hwsave` with today's code,
plays a few days and saves again, so a real-world career can never silently stop loading.

| File | Format | Made by |
|---|---|---|
| real-v12.hwsave | 12 | v0.73.0 code (run 46): a real-world career, `GameSession.NewGame(seed 2026, team 5)` (the Toronto Raptors) advanced 12 days |
| real-v13.hwsave | 13 | v0.108.0 code (run 111): the same career (seed 2026, team 5, 12 days), with relegation release clauses on contracts |
| college-v0165.hwsave.gz | 13, college format 0 | v0.165.0 code (run 178, K30): a college career, Virginia Tech (ACC) on seed 31, `GameSession.NewGame(31, <Virginia Tech>)` played to Jan 8, 2027 with the assistant recruiting. Gzipped; `CollegeSaveCompatibilityTests` unpacks it |
| college-v0194.hwsave.gz | 13, college format 1 | v0.194.0 code (run 206), **the launch build** ("Your first college career", the build of Danny's first college career): the same career as `college-v0165` (Virginia Tech on seed 31, played to Jan 8, 2027 with the assistant recruiting), made with `Make_a_college_fixture_when_asked`. It carries the launch slices' saved state (rival coaches, your coaching book, player origins, March's weekday calendar; the legacy books are empty until the first national final). No later build may stop loading it |
| madeup-v2.hwsave | 2 | commit 35a844e (phase 5, v0.5.0): a made-up (NABL) career, kept only to check that it is refused |

**The made-up world was removed in v0.74.0** (Danny's D61 B, #478). Made-up saves (formats 2–12, the NABL) no longer
load: `GameSession.Load` refuses them with `RealWorldData.MadeUpSaveMessage` before anything upgrades the file. Their
fixtures (`career-v2` … `career-v12`) were dropped. Real-world careers started in v0.51.0 at format 12, so there is no
older real save to keep.

**When the save format changes:** bump `SaveFile.SchemaVersion`, make a real-world save with the *previous* version's
code, add it here as `real-v<N>.hwsave`, and add a row to this table.

## College saves (K30, run 178)

The college world is one JSON record in `world_meta['college']` (`WorldStore.CollegeState`), and since v0.166.0 it carries
its own format number in `world_meta['college_format']` (`CollegeMigrations.Current`; saves from v0.165.0 and older have
no row, which reads as format 0). `CollegeSaveCompatibilityTests` loads every `college-v*.hwsave.gz`, checks nothing came
back zeroed, saves and reloads it unchanged, and plays the same week from two separate loads. A save whose college format
is newer than the build is refused with a clear message and left untouched.

**The rule for any change to the college record:**
- A new field with a safe default (null, zero, empty) needs nothing more, but don't rename, retype or remove one: the
  record is read by name, and `Every_field_an_old_college_save_wrote_is_still_read` reads the old record with today's code and
  fails if any key it wrote, at any depth, no longer comes back.
- A change to what a field means (renamed, retyped, moved): bump `CollegeMigrations.Current`, add a `Step` that rewrites
  the old JSON, and add a fixture made with the *previous* build's code as `college-v<version>.hwsave.gz` (run
  `Make_a_college_fixture_when_asked` with `HW_MAKE_COLLEGE_FIXTURE=<file>` on that build), with a row in the table above.
- **The launch build** (Danny's first college career, v0.194.0, run 206): `college-v0194.hwsave.gz` is a college career
  made with that build, so no later build can stop loading his save.
