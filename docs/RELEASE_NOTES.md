# Release Notes — 1.2.0

## Highlights

v1.2 delivers the **writer’s cockpit**: manuscript hierarchy (Books/Parts/Chapters/Scenes), in-chapter scene prose association, corkboard sync, project-wide search/replace with safe rollback, lightweight manuscript versions, drafting targets/sessions, and progress views. The app remains fully offline with portable per-project folders.

## What’s new

- **Manuscript hierarchy** — Books, optional Parts, Chapters, and Scenes with drag-and-drop and keyboard reordering; order persists across close/reopen.
- **Scene prose association** — Optional `mbws:scene` markers in chapter Markdown; legacy unmarked chapters stay editable; compile/export strips markers.
- **Corkboard + canonical sync** — One Scene inventory projected across Story Data, Documents Scene List, corkboard, and Manuscript via in-process change notifications.
- **Global search/replace** — Scoped preview with include/exclude, safety snapshot before write, compensation on failure/cancel, one-operation rollback.
- **Manuscript versions** — Lightweight archive file/manifest versions with readable before/after diff (separate from full project snapshots).
- **Safe split/merge** — Caret split and adjacent merge with validation, snapshots, and compensation.
- **Drafting progress** — Daily/weekly targets, Start/Pause/Resume/Stop timer, automatic session logging (5‑minute idle threshold), interrupted-session recovery, Progress views by date/chapter/scene/viewpoint.
- **Schema** — Project schema **9** (`AddDraftingProgressV12`); upgrades create a pre-migration safety snapshot.

## Upgrade notes

- Project folders remain user-owned portable directories; upgrading the app does not move manuscripts.
- Opening a v1.1 (schema 7) project migrates through hierarchy (v8) and drafting (v9) with a safety snapshot when migrations are pending.
- Application settings remain under `%LocalAppData%\MasterBookWritingSystem\`.
- Inactive traditional/self-publishing workflow progress continues to be preserved when switching routes.

## Signing

Unsigned installers/MSIX packages may trigger SmartScreen. Signed artifacts require supplying certificate parameters or `MBWS_SIGN_*` environment variables outside source control (see `docs/BUILDING_AND_PACKAGING.md`).

## Traceability

See `docs/V1.2_TRACEABILITY.md` for requirement → implementation → test mapping.
