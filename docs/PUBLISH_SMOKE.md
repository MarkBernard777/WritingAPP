# Publish smoke checklist (v1.1)

Run after `dotnet publish` or `.\scripts\build-release.ps1`.

```powershell
$publish = ".\artifacts\publish\win-x64"
$exe = Join-Path $publish "MasterBookWritingSystem.App.exe"
Test-Path $exe
Test-Path (Join-Path $publish "seed\workflow.json")
Test-Path (Join-Path $publish "seed\schemas\document-templates.json")
@(Get-ChildItem (Join-Path $publish "seed\templates\core\*.md")).Count -gt 0

# Optional GUI launch smoke (close the window after create/open/edit/save):
# Start-Process $exe
```

Automated service-level smoke covering create/open/edit/save/recover/export is asserted by `V11AcceptanceTests` and related recovery/export tests.
