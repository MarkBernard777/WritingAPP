# Accessibility and performance readiness (v1.1)

Measured locally on 2026-08-08 (Windows, Debug `dotnet test`, single machine).
Ceilings in tests are intentionally generous for shared CI hardware; use these local numbers as a regression baseline, not as hard SLAs.

## Local timings (`PerformanceReadinessTests`)

| Scenario | Local result | Test ceiling |
|----------|--------------|--------------|
| Create project + seed 200 chapters | ~5.7 s | 4 min |
| List 200 chapter metadata (`GetAllAsync`) | ~8 ms | 15 s |
| Open project + list 200 chapters | ~164 ms | 2 min |
| Compile 200 chapters (includes safety snapshot + streamed export) | ~2.0 s | 5 min |
| List 40 snapshots (manifest + file presence, no checksum) | ~31 ms | 30 s |
| Validate one snapshot (checksums) | ~1 ms | 30 s |
| Load 200 characters + 200 scenes | ~54 ms | 2 min |
| Recovery diagnose (missing chapter file + journal) | ~27 ms | 2 min |

## Hotspots remaining

1. **Project create/open seeding** — document template seeding and EF migrate still dominate cold create/open cost.
2. **Compile safety snapshot** — every compile creates a safety snapshot of the project tree before export; largest cost for large projects.
3. **Manuscript preview** — Markdig HTML generation is debounced (250 ms) and offloaded, but `WebBrowser.NavigateToString` remains on the UI thread.
4. **SQLite `ClearAllPools`** — still called after many service operations; useful for file locks, but amplifies rapid refresh cost.
5. **Story Data UI refresh** — still loads characters, world, beats, chapters, and scenes together for the editor shell (service load itself is fast at ~200 rows).
6. **Full checksum validation** — intentionally deferred to Inspect/Restore; listing no longer hashes every snapshot file.

## Accessibility notes

- Theme brushes resolve through `SystemColors` for high-contrast legibility.
- Save state uses text + polite live regions / accessible names (not colour alone).
- No drag/drop surfaces existed; chapter reorder exposes Up/Down with accessible help text as the keyboard alternative.
- Controls use `MinHeight` / focus visuals suited to enlarged text and display scaling; avoid fixed tiny hit targets on primary actions.
