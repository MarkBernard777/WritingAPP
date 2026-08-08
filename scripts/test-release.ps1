#Requires -Version 5.1
<#
.SYNOPSIS
  Script-level tests for release automation helpers (Pester if available, else equivalent asserts).
#>
[CmdletBinding()]
param(
    [switch]$PesterOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$common = Join-Path $repoRoot "scripts\Release\ReleaseCommon.ps1"
. $common

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "ASSERT FAILED: $Message" }
}

function Assert-Throws([scriptblock]$Script, [string]$MessageMatch) {
    $threw = $false
    try { & $Script } catch {
        $threw = $true
        if ($MessageMatch -and ($_.Exception.Message -notmatch $MessageMatch)) {
            throw "ASSERT FAILED: exception message did not match /$MessageMatch/. Was: $($_.Exception.Message)"
        }
    }
    if (-not $threw) { throw "ASSERT FAILED: expected exception matching /$MessageMatch/" }
}

$pester = Get-Module -ListAvailable -Name Pester | Sort-Object Version -Descending | Select-Object -First 1
if ($pester -and $PesterOnly) {
    Import-Module Pester -MinimumVersion 5.0 -ErrorAction SilentlyContinue
    if (-not (Get-Module Pester)) {
        Import-Module Pester -ErrorAction Stop
    }
}

# Equivalent script-level tests (always run)
Write-Host "Running release helper tests..."

$info = ConvertTo-ReleaseVersionInfo -Version "1.1.0"
Assert-True ($info.AssemblyVersion -eq "1.1.0.0") "AssemblyVersion for 1.1.0"
Assert-True ($info.MsixVersion -eq "1.1.0.0") "MsixVersion for 1.1.0"
Assert-True ($info.InformationalVersion -eq "1.1.0") "InformationalVersion for 1.1.0"

$pre = ConvertTo-ReleaseVersionInfo -Version "1.2.3-beta.1"
Assert-True ($pre.AssemblyVersion -eq "1.2.3.0") "prerelease assembly"
Assert-True ($pre.SemVer -eq "1.2.3-beta.1") "prerelease semver retained"

Assert-Throws { ConvertTo-ReleaseVersionInfo -Version "10" } "semantic version"
Assert-Throws { ConvertTo-ReleaseVersionInfo -Version "1.0" } "semantic version"
Assert-Throws { ConvertTo-ReleaseVersionInfo -Version "v1.0.0" } "semantic version"

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("mbws-release-tests-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
try {
    $existing = Join-Path $tempRoot "MasterBookWritingSystem-1.0.2-win-x64.zip"
    Set-Content -LiteralPath $existing -Value "collision"
    Assert-Throws { Assert-NoArtifactCollision -Paths @($existing) } "refusing silent overwrite"
    Assert-NoArtifactCollision -Paths @($existing) -ForceOverwrite
    Assert-NoArtifactCollision -Paths @((Join-Path $tempRoot "missing.zip"))

    $fakePublish = Join-Path $tempRoot "publish"
    New-Item -ItemType Directory -Force -Path (Join-Path $fakePublish "seed\templates\core") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $fakePublish "seed\schemas") | Out-Null
    # Tiny exe should fail size check
    Set-Content -LiteralPath (Join-Path $fakePublish "MasterBookWritingSystem.App.exe") -Value "tiny"
    Set-Content -LiteralPath (Join-Path $fakePublish "seed\workflow.json") -Value "{}"
    Set-Content -LiteralPath (Join-Path $fakePublish "seed\schemas\document-templates.json") -Value "[]"
    Set-Content -LiteralPath (Join-Path $fakePublish "seed\templates\core\01.md") -Value "# t"
    Assert-Throws { Assert-PublishSeedLayout -PublishDir $fakePublish } "too small"

    # Large enough exe + seed passes
    $exePath = Join-Path $fakePublish "MasterBookWritingSystem.App.exe"
    $bytes = New-Object byte[] (1MB + 16)
    [System.IO.File]::WriteAllBytes($exePath, $bytes)
    Assert-PublishSeedLayout -PublishDir $fakePublish

    Remove-Item -LiteralPath (Join-Path $fakePublish "seed\workflow.json") -Force
    Assert-Throws { Assert-PublishSeedLayout -PublishDir $fakePublish } "missing seed file"

    $versionInfo = ConvertTo-ReleaseVersionInfo -Version "1.0.2"
    $artifact = New-ReleaseArtifactDescriptor -Path $existing -Kind "portable-zip"
    Assert-True ($artifact.sha256.Length -eq 64) "sha256 length"
    $manifest = New-ReleaseManifestObject `
        -VersionInfo $versionInfo `
        -Commit "abc123" `
        -BuiltUtc ([datetime]::UtcNow) `
        -TestsRan $true `
        -Runtime "win-x64 self-contained net10.0-windows" `
        -Artifacts @($artifact) `
        -Notes @("Inno Setup (ISCC.exe) unavailable; installer was not built.")
    Assert-True ($manifest.version -eq "1.0.2") "manifest version"
    Assert-True ($manifest.tests.ran -eq $true) "manifest tests"
    Assert-True ($manifest.artifacts.Count -eq 1) "manifest artifacts"
    Assert-True ($manifest.commit -eq "abc123") "manifest commit"

    $manifestPath = Join-Path $tempRoot "release-manifest-1.0.2.json"
    Write-ReleaseManifest -Path $manifestPath -ManifestObject $manifest
    Assert-True (Test-Path -LiteralPath $manifestPath) "manifest written"
    Assert-Throws { Write-ReleaseManifest -Path $manifestPath -ManifestObject $manifest } "refusing silent overwrite"

    $sumsPath = Join-Path $tempRoot "SHA256SUMS-1.0.2.txt"
    Write-ChecksumsFile -Path $sumsPath -Artifacts @($artifact)
    $sums = Get-Content -LiteralPath $sumsPath -Raw
    Assert-True ($sums -match $artifact.sha256) "checksums contain hash"
    Assert-True ($sums -match [regex]::Escape($artifact.name)) "checksums contain name"

    # Missing tools should resolve to null without throwing
    $missingIscc = Find-IsccPath
    # May or may not be installed; just ensure function returns string or $null
    Assert-True (($null -eq $missingIscc) -or ($missingIscc -like "*ISCC.exe")) "Find-IsccPath shape"
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Optional Pester mirror when module is present
if ($pester) {
    Write-Host "Pester $($pester.Version) available; equivalent coverage already executed above."
}

Write-Host "All release helper tests passed."
