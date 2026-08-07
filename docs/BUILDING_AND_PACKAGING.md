# Building and Packaging

Milestone 12 ships a **self-contained win-x64** build of Master Book-Writing System so users do not need a machine-wide .NET runtime. Seed templates are bundled beside the executable.

## Prerequisites

- .NET 10 SDK
- PowerShell 5+ (Windows)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) with `ISCC.exe` on `PATH` or in the default install location (required to produce the `.exe` installer)

## Versioning

The application version is defined in:

`src/MasterBookWritingSystem.App/MasterBookWritingSystem.App.csproj`

Properties: `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`.

Override for a one-off release:

```powershell
.\scripts\build-release.ps1 -Version 1.2.3
```

## One-command release

From the repository root:

```powershell
.\scripts\build-release.ps1
```

The script:

1. Restores and builds in Release
2. Runs `dotnet test`
3. Publishes self-contained `win-x64` with `PublishTrimmed=false`
4. Validates the publish folder (exe, DLLs, `seed/`)
5. Creates a portable ZIP
6. Compiles the Inno Setup installer (fails clearly if `ISCC.exe` is missing)

Equivalent publish command used by the script:

```powershell
dotnet publish `
  '.\src\MasterBookWritingSystem.App\MasterBookWritingSystem.App.csproj' `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishTrimmed=false `
  -o '.\artifacts\publish\win-x64'
```

## Artifacts

| Path | Purpose |
|------|---------|
| `artifacts/publish/win-x64/` | Full self-contained folder (portable run from here) |
| `artifacts/packages/MasterBookWritingSystem-<version>-win-x64.zip` | ZIP of the publish folder |
| `artifacts/installer/MasterBookWritingSystem-Setup-<version>.exe` | Per-user Inno Setup installer |

## Installer behavior

- Script: `installer/MasterBookWritingSystem.iss`
- Installs under `%LocalAppData%\MasterBookWritingSystem\App` (`PrivilegesRequired=lowest`)
- Copies the complete publish tree, including `seed/`
- Creates a Start Menu shortcut; optional desktop shortcut
- Uninstall removes only the installed app directory — **never** user book projects
- Application settings remain under `%LocalAppData%\MasterBookWritingSystem` (outside the `App` install folder where applicable)
- Book projects stay in user-selected folders

## Seed content

- Published builds include `seed/` next to `MasterBookWritingSystem.App.exe`
- Runtime lookup prefers `AppContext.BaseDirectory\seed`, then parent-folder walk (development fallback)
- Project creation **copies** templates into the new project folder and does not modify installed seed files
- Startup preflight aborts with a clear message if required seed files are missing

## Signing (disabled)

Code signing is **not** enabled in this repository.

- Do not commit certificates, passwords, or `.pfx` files
- `installer/MasterBookWritingSystem.iss` contains commented `SignTool` hooks for a future signed release
- **Unsigned development installers may trigger a Windows SmartScreen / reputation warning**

## Manual verification checklist

1. Run `dotnet build` and `dotnet test`
2. Run `.\scripts\build-release.ps1`
3. Launch from `artifacts\publish\win-x64\MasterBookWritingSystem.App.exe`
4. Copy the publish folder to a path **outside** the repository and launch again (proves seed discovery without parent-folder walking to the repo)
5. Create a new project; confirm templates appear under the project directories
6. Open/save manuscript, compile, export, snapshot/restore as smoke tests
7. Confirm the publish folder contains the runtime (no separate .NET install required)
8. Install then uninstall the Setup exe; confirm user projects remain
