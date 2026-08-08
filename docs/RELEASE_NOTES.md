# Release Notes — 1.1.0

## Highlights

v1.1 focuses on **reliability, recovery, editor safety, accessibility, performance readiness, and release automation**. The app remains fully offline with portable per-project folders.

## What’s new

- **Chapter–scene usability** — Shared scene inventory across Story Data, Documents (Scene List), and Manuscript; chapter pickers instead of raw IDs.
- **Safer snapshots** — Configurable retention (default 20); excess snapshots retired (not deleted); safety snapshots before migrate/import/restore/compile-export; daily automatic protection when content changes.
- **Crash journaling & autosave** — Project-local recovery journal; debounced autosave; Clear/Saving/Saved/Save failed/Recovery available status.
- **Recovery Centre** — Diagnose and repair damaged project roots even when the database will not open; journal recover-to-copy; confirmed snapshot restore.
- **Undo/redo** — Native text undo for manuscript prose; bounded model undo for structured document fields; participates in autosave/journaling.
- **Accessibility & performance** — High-contrast-friendly theme resources; accessible save-status text; performance ceilings documented for large projects.
- **Release automation** — One-command `scripts/build-release.ps1` producing ZIP, optional Inno Setup installer, optional MSIX, SHA-256 checksums, and a JSON release manifest. Signing is optional via env/params (never committed).

## Upgrade notes

- Project folders remain user-owned portable directories; upgrading the app does not move manuscripts.
- Application settings (including snapshot retention) stay under `%LocalAppData%\MasterBookWritingSystem\`.
- Inactive traditional/self-publishing workflow progress continues to be preserved when switching routes.
- Schema migrations run on open; a safety snapshot is created when migrations are pending.

## Signing

Unsigned installers/MSIX packages may trigger SmartScreen. Signed artifacts require supplying certificate parameters or `MBWS_SIGN_*` environment variables outside source control (see `docs/BUILDING_AND_PACKAGING.md`).
