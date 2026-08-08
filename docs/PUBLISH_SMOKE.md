# Publish smoke checklist (v1.2)

Run after `dotnet publish` or `.\scripts\build-release.ps1`.

```powershell
$publish = ".\artifacts\publish\win-x64"
$exe = Join-Path $publish "MasterBookWritingSystem.App.exe"
Test-Path $exe
Test-Path (Join-Path $publish "seed\workflow.json")
Test-Path (Join-Path $publish "seed\schemas\document-templates.json")
@(Get-ChildItem (Join-Path $publish "seed\templates\core\*.md")).Count -gt 0

# Optional GUI launch smoke (close the window after the disposable-project checklist below):
# Start-Process $exe
```

## Disposable-project GUI checklist

Use **copies** of projects (or Create Project into a temp folder). Never smoke-test against production manuscripts.

1. **Create / open** — Create a new project; close; reopen the same folder.
2. **Upgrade** — Open a v1.1 schema-v7 project copy; confirm prose unchanged and a `safety-migration-*` snapshot appears.
3. **Plan → draft** — Create a scene in Story Data; assign to a chapter (Manuscript tree); associate/draft prose; confirm one scene row only.
4. **Reorder** — Move Book/Part/Chapter/Scene; close and reopen; order unchanged.
5. **Search / replace / rollback** — Tools → Manuscript search: preview, apply one hit, one-operation rollback.
6. **Session** — Progress: start timer or draft enough words for auto-session; navigate away and back; timer still available; editor remains responsive.
7. **Compile** — Compile selected chapters; export has prose in hierarchy order and no `mbws:scene` markers.
8. **Recover** — With a journaled draft (or Recovery Centre entry), recover-to-copy without overwriting the live chapter unless confirmed.

Automated coverage for these flows: `V12AcceptanceTests` plus drafting/search/hierarchy suites (see `docs/V1.2_TRACEABILITY.md`).
