<#
.SYNOPSIS
  Restores, builds, tests, publishes, zips, and builds the Inno Setup installer for Milestone 12.

.DESCRIPTION
  Produces:
    artifacts/publish/win-x64/
    artifacts/packages/MasterBookWritingSystem-<version>-win-x64.zip
    artifacts/installer/MasterBookWritingSystem-Setup-<version>.exe

  Requires the .NET SDK and Inno Setup (ISCC.exe). Unsigned installers may trigger
  a Windows SmartScreen reputation warning.
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "..")).Path
}

function Get-AppVersion([string]$csprojPath, [string]$overrideVersion) {
    if (-not [string]::IsNullOrWhiteSpace($overrideVersion)) {
        return $overrideVersion.Trim()
    }

    [xml]$xml = Get-Content -LiteralPath $csprojPath
    $versionNode = Select-Xml -Xml $xml -XPath "//PropertyGroup/Version" | Select-Object -First 1
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.Node.InnerText)) {
        throw "Could not read <Version> from $csprojPath. Pass -Version explicitly."
    }

    return $versionNode.Node.InnerText.Trim()
}

function Find-Iscc {
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    return $null
}

function Assert-PublishLayout([string]$publishDir) {
    $exe = Join-Path $publishDir "MasterBookWritingSystem.App.exe"
    $workflow = Join-Path $publishDir "seed\workflow.json"
    $templates = Join-Path $publishDir "seed\schemas\document-templates.json"
    $core = Join-Path $publishDir "seed\templates\core"

    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Publish validation failed: missing executable at $exe"
    }

    # Single-file publish may leave few or no managed DLLs beside the exe; seed must still be external.
    $exeInfo = Get-Item -LiteralPath $exe
    if ($exeInfo.Length -lt 1MB) {
        throw "Publish validation failed: executable looks too small for a self-contained single-file build ($exe)."
    }

    if (-not (Test-Path -LiteralPath $workflow)) {
        throw "Publish validation failed: missing seed file $workflow"
    }

    if (-not (Test-Path -LiteralPath $templates)) {
        throw "Publish validation failed: missing seed file $templates"
    }

    if (-not (Test-Path -LiteralPath $core)) {
        throw "Publish validation failed: missing seed folder $core"
    }

    $md = Get-ChildItem -LiteralPath $core -Filter "*.md" -File -ErrorAction SilentlyContinue
    if ($null -eq $md -or $md.Count -lt 1) {
        throw "Publish validation failed: no markdown templates under $core"
    }

    Write-Host "Publish layout validated (self-contained exe, seed)."
}

$repoRoot = Get-RepoRoot
Set-Location $repoRoot

$appCsproj = Join-Path $repoRoot "src\MasterBookWritingSystem.App\MasterBookWritingSystem.App.csproj"
$solution = Join-Path $repoRoot "MasterBookWritingSystem.slnx"
if (-not (Test-Path -LiteralPath $solution)) {
    $solution = Join-Path $repoRoot "MasterBookWritingSystem.sln"
}

$version = Get-AppVersion -csprojPath $appCsproj -overrideVersion $Version
Write-Host "Building release version $version"

$publishDir = Join-Path $repoRoot "artifacts\publish\win-x64"
$packagesDir = Join-Path $repoRoot "artifacts\packages"
$installerDir = Join-Path $repoRoot "artifacts\installer"
$issPath = Join-Path $repoRoot "installer\MasterBookWritingSystem.iss"

New-Item -ItemType Directory -Force -Path $publishDir, $packagesDir, $installerDir | Out-Null
if (Test-Path -LiteralPath $publishDir) {
    Get-ChildItem -LiteralPath $publishDir -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "dotnet restore..."
dotnet restore $repoRoot
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed ($LASTEXITCODE)." }

Write-Host "dotnet restore app (Release win-x64)..."
dotnet restore $appCsproj `
    -r win-x64 `
    -p:Configuration=Release
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore app win-x64 failed ($LASTEXITCODE)."
}

Write-Host "dotnet build -c Release..."
if (Test-Path -LiteralPath $solution) {
    dotnet build $solution -c Release --no-restore
} else {
    dotnet build $appCsproj -c Release --no-restore
}
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)." }

if (-not $SkipTests) {
    Write-Host "dotnet test -c Release..."
    $testProject = Join-Path $repoRoot "tests\MasterBookWritingSystem.Tests\MasterBookWritingSystem.Tests.csproj"
    dotnet test $testProject -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed ($LASTEXITCODE)." }
}

Write-Host "dotnet publish (self-contained win-x64)..."
dotnet publish $appCsproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishTrimmed=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

Assert-PublishLayout -publishDir $publishDir

$zipPath = Join-Path $packagesDir "MasterBookWritingSystem-$version-win-x64.zip"
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Write-Host "Creating ZIP $zipPath"
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$iscc = Find-Iscc
if (-not $iscc) {
    throw @"
Inno Setup compiler (ISCC.exe) was not found.
Install Inno Setup 6 and ensure ISCC.exe is on PATH, or install to the default Program Files location.
ZIP artifact was still created at:
  $zipPath
"@
}

$setupOut = Join-Path $installerDir "MasterBookWritingSystem-Setup-$version.exe"
if (Test-Path -LiteralPath $setupOut) {
    Remove-Item -LiteralPath $setupOut -Force
}

Write-Host "Compiling installer with $iscc"
& $iscc `
    "/DAppVersion=$version" `
    "/DPublishDir=$publishDir" `
    "/DOutputDir=$installerDir" `
    $issPath
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)." }

if (-not (Test-Path -LiteralPath $setupOut)) {
    throw "Installer output missing: $setupOut"
}

Write-Host ""
Write-Host "Release artifacts:"
Write-Host "  $publishDir"
Write-Host "  $zipPath"
Write-Host "  $setupOut"
Write-Host ""
Write-Host "NOTE: This installer is unsigned. Windows SmartScreen may warn on first run."
Write-Host "Code-signing hooks exist in installer/MasterBookWritingSystem.iss but are disabled."
