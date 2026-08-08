# Building and Packaging

Master Book-Writing System ships a **self-contained win-x64** build so users do not need a machine-wide .NET runtime. Seed templates are bundled beside the executable.

Current product release: **1.2.0** (see `docs/RELEASE_NOTES.md` and `docs/V1.2_TRACEABILITY.md`).

## Prerequisites

- .NET 10 SDK
- PowerShell 5.1+ (Windows)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) with `ISCC.exe` on `PATH` or in the default install location (**optional** — ZIP/MSIX still build without it)
- Windows SDK packaging tools (`makeappx.exe`, `signtool.exe`) for MSIX / Authenticode (**optional**)

## Versioning

The checked-in application version lives in:

`src/MasterBookWritingSystem.App/MasterBookWritingSystem.App.csproj`

Properties: `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`.

The release script accepts a semantic version (for example `1.2.0`) and derives:

| Field | Example from `1.2.0` |
|-------|----------------------|
| SemVer / InformationalVersion | `1.2.0` |
| AssemblyVersion / FileVersion | `1.2.0.0` |
| MSIX Identity Version | `1.2.0.0` |

MSBuild properties are passed for the release build only; the script does **not** rewrite the `.csproj` unless you change it yourself.

## One-command release

From the repository root:

### Unsigned validation / test build (no certificates)

```powershell
.\scripts\test-release.ps1
.\scripts\build-release.ps1 -AllowDirtyWorkingTree -ForceOverwrite
```

Use `-AllowDirtyWorkingTree` only for local validation while you have intentional uncommitted script/docs changes. Production releases should run on a clean tracked tree **without** that switch.

Omit `-ForceOverwrite` to fail if a same-version ZIP/manifest already exists (no silent overwrite).

### Signed release (certificate supplied outside Git)

Never commit `.pfx` files, passwords, or thumbprints. Prefer environment variables in a private shell / CI secret store:

```powershell
$env:MBWS_SIGN_PFX_PATH = "C:\secure\path\to\cert.pfx"
$env:MBWS_SIGN_PFX_PASSWORD = "<password>"   # do not echo / log
$env:MBWS_SIGN_TIMESTAMP_URL = "http://timestamp.digicert.com"
$env:MBWS_MSIX_PUBLISHER = "CN=Your Certificate Subject"

.\scripts\build-release.ps1 -Sign -ForceOverwrite
```

Or thumbprint from the current user / machine store (no PFX password):

```powershell
$env:MBWS_SIGN_THUMBPRINT = "<thumbprint>"
.\scripts\build-release.ps1 -Sign
```

Explicit parameters (password as `SecureString`):

```powershell
$pwd = Read-Host -AsSecureString "PFX password"
.\scripts\build-release.ps1 `
  -Sign `
  -CertificatePath "C:\secure\path\to\cert.pfx" `
  -CertificatePassword $pwd `
  -MsixPublisher "CN=Your Certificate Subject"
```

After signing, the script runs Authenticode verification and **fails the release** if verification fails. Passwords and PFX paths are never written to the release manifest or checksum files.

### Optional skips

```powershell
.\scripts\build-release.ps1 -SkipInstaller -SkipMsix
.\scripts\build-release.ps1 -SkipTests   # not recommended for real releases
```

## What the script does

1. Validates a supported repo layout and (by default) a clean **tracked** git working tree — does not delete user projects or unrelated folders such as `dist/`
2. Restores, builds Release, runs all tests
3. Publishes self-contained single-file `win-x64` with version properties applied
4. Verifies `MasterBookWritingSystem.App.exe` and required `seed/` content beside it
5. Creates a versioned portable ZIP (refuses silent overwrite)
6. Builds the Inno Setup installer when `ISCC.exe` is available
7. Builds an MSIX when `makeappx.exe` is available
8. Optionally signs installer/MSIX and verifies Authenticode
9. Writes `SHA256SUMS-<version>.txt` and `release-manifest-<version>.json`

## Artifacts

| Path | Purpose |
|------|---------|
| `artifacts/publish/win-x64/` | Full self-contained folder |
| `artifacts/packages/MasterBookWritingSystem-<version>-win-x64.zip` | Portable ZIP |
| `artifacts/installer/MasterBookWritingSystem-Setup-<version>.exe` | Per-user Inno Setup installer (if ISCC present) |
| `artifacts/packages/MasterBookWritingSystem-<version>-x64.msix` | MSIX package (if MakeAppx present) |
| `artifacts/packages/SHA256SUMS-<version>.txt` | SHA-256 checksums for distributable packages |
| `artifacts/packages/release-manifest-<version>.json` | Version, commit, UTC time, tests, runtime, artifact names/sizes/hashes |

## Installer behavior

- Script: `installer/MasterBookWritingSystem.iss`
- Installs under `%LocalAppData%\MasterBookWritingSystem\App` (`PrivilegesRequired=lowest`)
- Copies the complete publish tree, including `seed/`
- Creates a Start Menu shortcut; optional desktop shortcut
- Uninstall removes only the installed app directory — **never** user book projects

## Seed content

- Published builds include `seed/` next to `MasterBookWritingSystem.App.exe`
- Runtime lookup prefers `AppContext.BaseDirectory\seed`, then parent-folder walk (development fallback)
- Project creation **copies** templates into the new project folder and does not modify installed seed files

## Script tests

```powershell
.\scripts\test-release.ps1
```

Covers version validation, collision protection, missing seed content, checksum/manifest generation, and tool discovery shape. If Pester is installed, `tests/scripts/Release.Tests.ps1` mirrors the same cases:

```powershell
Invoke-Pester -Path .\tests\scripts\Release.Tests.ps1
```

## Manual verification checklist

1. `.\scripts\test-release.ps1`
2. `.\scripts\build-release.ps1` (clean tree for a real release)
3. Launch `artifacts\publish\win-x64\MasterBookWritingSystem.App.exe`
4. Copy the publish folder outside the repo and launch again
5. Create a project; confirm templates appear
6. Install/uninstall Setup exe when produced; confirm user projects remain
7. For signed builds, confirm `Get-AuthenticodeSignature` reports `Valid` on the installer/MSIX
