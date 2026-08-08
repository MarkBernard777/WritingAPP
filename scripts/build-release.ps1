<#
.SYNOPSIS
  Repeatable release automation: restore, test, publish, package, optional sign.

.DESCRIPTION
  Produces under artifacts/:
    publish/win-x64/
    packages/MasterBookWritingSystem-<version>-win-x64.zip
    installer/MasterBookWritingSystem-Setup-<version>.exe   (when Inno Setup is available)
    packages/MasterBookWritingSystem-<version>-x64.msix     (when MakeAppx is available)
    packages/SHA256SUMS.txt
    packages/release-manifest-<version>.json

  Signing uses -CertificatePath / -CertificateThumbprint or environment variables
  MBWS_SIGN_PFX_PATH, MBWS_SIGN_PFX_PASSWORD, MBWS_SIGN_THUMBPRINT, MBWS_SIGN_TIMESTAMP_URL,
  MBWS_MSIX_PUBLISHER. Secrets are never written to logs or the release manifest.

.EXAMPLE
  .\scripts\build-release.ps1 -AllowDirtyWorkingTree

.EXAMPLE
  .\scripts\build-release.ps1 -Version 1.1.0 -Sign -CertificatePath $env:MBWS_SIGN_PFX_PATH
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$SkipTests,
    [switch]$SkipInstaller,
    [switch]$SkipMsix,
    [switch]$ForceOverwrite,
    [switch]$AllowDirtyWorkingTree,
    [switch]$Sign,
    [string]$CertificatePath = "",
    [SecureString]$CertificatePassword,
    [string]$CertificateThumbprint = "",
    [string]$TimestampUrl = "",
    [string]$MsixPublisher = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$commonPath = Join-Path $PSScriptRoot "Release\ReleaseCommon.ps1"
. $commonPath

$repoRoot = Get-ReleaseRepoRoot -StartPath $PSScriptRoot
Set-Location $repoRoot

$appCsproj = Join-Path $repoRoot "src\MasterBookWritingSystem.App\MasterBookWritingSystem.App.csproj"
$testProject = Join-Path $repoRoot "tests\MasterBookWritingSystem.Tests\MasterBookWritingSystem.Tests.csproj"
$solution = Join-Path $repoRoot "MasterBookWritingSystem.slnx"
if (-not (Test-Path -LiteralPath $solution)) {
    $solution = Join-Path $repoRoot "MasterBookWritingSystem.sln"
}

Assert-ReleaseWorkingState -RepoRoot $repoRoot -AllowDirtyWorkingTree:$AllowDirtyWorkingTree

$versionInfo = Resolve-ReleaseVersionInfo -OverrideVersion $Version -CsprojPath $appCsproj
Write-Host "Building release version $($versionInfo.SemVer) (assembly/MSIX $($versionInfo.AssemblyVersion))"

$signing = Resolve-SigningConfiguration `
    -Sign:$Sign `
    -CertificatePath $CertificatePath `
    -CertificatePassword $CertificatePassword `
    -CertificateThumbprint $CertificateThumbprint `
    -TimestampUrl $TimestampUrl

$publisher = if (-not [string]::IsNullOrWhiteSpace($MsixPublisher)) {
    $MsixPublisher
} elseif (-not [string]::IsNullOrWhiteSpace($env:MBWS_MSIX_PUBLISHER)) {
    $env:MBWS_MSIX_PUBLISHER
} else {
    "CN=Mark Bernard"
}

$publishDir = Join-Path $repoRoot "artifacts\publish\win-x64"
$packagesDir = Join-Path $repoRoot "artifacts\packages"
$installerDir = Join-Path $repoRoot "artifacts\installer"
$msixLayoutDir = Join-Path $repoRoot "artifacts\msix-layout"
$issPath = Join-Path $repoRoot "installer\MasterBookWritingSystem.iss"

$zipPath = Join-Path $packagesDir "MasterBookWritingSystem-$($versionInfo.PackageFileVersion)-win-x64.zip"
$setupOut = Join-Path $installerDir "MasterBookWritingSystem-Setup-$($versionInfo.PackageFileVersion).exe"
$msixPath = Join-Path $packagesDir "MasterBookWritingSystem-$($versionInfo.PackageFileVersion)-x64.msix"
$checksumsPath = Join-Path $packagesDir "SHA256SUMS-$($versionInfo.PackageFileVersion).txt"
$manifestPath = Join-Path $packagesDir "release-manifest-$($versionInfo.PackageFileVersion).json"

# Collision-check required outputs up front; optional installer/MSIX are checked when those tools run.
Assert-NoArtifactCollision -Paths @($zipPath, $checksumsPath, $manifestPath) -ForceOverwrite:$ForceOverwrite

New-Item -ItemType Directory -Force -Path $publishDir, $packagesDir, $installerDir | Out-Null
# Clean only this publish output directory; never touch user projects or unrelated dist/ leftovers.
if (Test-Path -LiteralPath $publishDir) {
    Get-ChildItem -LiteralPath $publishDir -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

$msbuildVersionArgs = @(
    "-p:Version=$($versionInfo.SemVer)",
    "-p:AssemblyVersion=$($versionInfo.AssemblyVersion)",
    "-p:FileVersion=$($versionInfo.FileVersion)",
    "-p:InformationalVersion=$($versionInfo.InformationalVersion)"
)

Write-Host "dotnet restore..."
dotnet restore $repoRoot
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed ($LASTEXITCODE)." }

Write-Host "dotnet restore app (Release win-x64)..."
dotnet restore $appCsproj -r win-x64 -p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw "dotnet restore app win-x64 failed ($LASTEXITCODE)." }

Write-Host "dotnet build -c Release..."
if (Test-Path -LiteralPath $solution) {
    & dotnet build $solution -c Release --no-restore @msbuildVersionArgs
} else {
    & dotnet build $appCsproj -c Release --no-restore @msbuildVersionArgs
}
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)." }

$testsRan = $false
if (-not $SkipTests) {
    Write-Host "dotnet test -c Release..."
    # Rebuild test host against Release outputs with the same version properties.
    & dotnet test $testProject -c Release @msbuildVersionArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed ($LASTEXITCODE)." }
    $testsRan = $true
}

Write-Host "dotnet publish (self-contained win-x64)..."
& dotnet publish $appCsproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishTrimmed=false `
    @msbuildVersionArgs `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

Assert-PublishSeedLayout -publishDir $publishDir
Write-Host "Publish layout validated (self-contained exe + seed)."

if ($ForceOverwrite -and (Test-Path -LiteralPath $zipPath)) {
    Remove-Item -LiteralPath $zipPath -Force
}
Write-Host "Creating ZIP $zipPath"
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$notes = New-Object System.Collections.Generic.List[string]
$artifacts = New-Object System.Collections.Generic.List[object]
$artifacts.Add((New-ReleaseArtifactDescriptor -Path $zipPath -Kind "portable-zip"))

$signTool = Find-WindowsSdkTool -ToolName "signtool.exe"
if ($signing.Enabled -and -not $signTool) {
    throw "Signing was requested but signtool.exe was not found in PATH or Windows SDK bins."
}

# Optional Inno Setup installer
$iscc = Find-IsccPath
if ($SkipInstaller) {
    $notes.Add("Installer skipped (-SkipInstaller).")
}
elseif (-not $iscc) {
    $notes.Add("Inno Setup (ISCC.exe) unavailable; installer was not built.")
    Write-Warning "ISCC.exe not found - skipping Windows installer. ZIP was still created."
}
else {
    Assert-NoArtifactCollision -Paths @($setupOut) -ForceOverwrite:$ForceOverwrite
    if ($ForceOverwrite -and (Test-Path -LiteralPath $setupOut)) {
        Remove-Item -LiteralPath $setupOut -Force
    }
    Write-Host "Compiling installer with $iscc"
    & $iscc `
        "/DAppVersion=$($versionInfo.PackageFileVersion)" `
        "/DPublishDir=$publishDir" `
        "/DOutputDir=$installerDir" `
        $issPath
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $setupOut)) {
        throw "Installer output missing: $setupOut"
    }

    if ($signing.Enabled) {
        Invoke-AuthenticodeSign -FilePath $setupOut -Signing $signing -SignToolPath $signTool
        Assert-AuthenticodeSignatureValid -FilePath $setupOut
    }
    else {
        $notes.Add("Installer is unsigned (SmartScreen may warn).")
    }

    $artifacts.Add((New-ReleaseArtifactDescriptor -Path $setupOut -Kind "innosetup-installer"))
}

# Optional MSIX
$makeAppxPath = Find-WindowsSdkTool -ToolName "makeappx.exe"
if ($SkipMsix) {
    $notes.Add("MSIX skipped (-SkipMsix).")
}
elseif ([string]::IsNullOrWhiteSpace($makeAppxPath)) {
    $notes.Add("Windows SDK MakeAppx unavailable; MSIX was not built.")
    Write-Warning "makeappx.exe not found - skipping MSIX package."
}
else {
    Assert-NoArtifactCollision -Paths @($msixPath) -ForceOverwrite:$ForceOverwrite
    if ($ForceOverwrite -and (Test-Path -LiteralPath $msixPath)) {
        Remove-Item -LiteralPath $msixPath -Force
    }
    Write-Host "Building MSIX packaging layout..."
    $null = New-MsixPackagingLayout -PublishDir $publishDir -LayoutDir $msixLayoutDir -VersionInfo $versionInfo -Publisher $publisher

    Write-Host "Packing MSIX with makeappx.exe"
    & $makeAppxPath pack /d $msixLayoutDir /p $msixPath /o
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed ($LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $msixPath)) {
        throw "MSIX output missing: $msixPath"
    }

    if ($signing.Enabled) {
        Invoke-AuthenticodeSign -FilePath $msixPath -Signing $signing -SignToolPath $signTool
        Assert-AuthenticodeSignatureValid -FilePath $msixPath
    }
    else {
        $notes.Add("MSIX is unsigned; sideload/install may require developer mode or a signed package.")
    }

    $artifacts.Add((New-ReleaseArtifactDescriptor -Path $msixPath -Kind "msix"))
}

$manifestObject = New-ReleaseManifestObject `
    -VersionInfo $versionInfo `
    -Commit (Get-GitCommit -RepoRoot $repoRoot) `
    -BuiltUtc ([datetime]::UtcNow) `
    -TestsRan $testsRan `
    -Runtime "win-x64 self-contained net10.0-windows" `
    -Artifacts $artifacts.ToArray() `
    -Notes $notes.ToArray()

Write-ChecksumsFile -Path $checksumsPath -Artifacts $artifacts.ToArray() -ForceOverwrite:$ForceOverwrite
Write-ReleaseManifest -Path $manifestPath -ManifestObject $manifestObject -ForceOverwrite:$ForceOverwrite
$artifacts.Add((New-ReleaseArtifactDescriptor -Path $checksumsPath -Kind "checksums"))
$artifacts.Add((New-ReleaseArtifactDescriptor -Path $manifestPath -Kind "release-manifest"))

# Refresh checksums/manifest to include themselves? Prefer checksums listing package payloads only.
# Keep SHA256SUMS focused on distributable packages (zip/installer/msix); manifest references them.

Write-Host ""
Write-Host "Release artifacts:"
Write-Host "  $publishDir"
foreach ($artifact in $artifacts) {
    Write-Host ("  [{0}] {1} ({2} bytes, sha256={3})" -f $artifact.kind, $artifact.path, $artifact.sizeBytes, $artifact.sha256)
}
Write-Host ""
if (-not $signing.Enabled) {
    Write-Host "Signing was not enabled. Provide -Sign with certificate parameters/env vars for a signed release."
}
Write-Host "Done."
