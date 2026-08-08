#Requires -Version 5.1
<#
.SYNOPSIS
  Shared helpers for Master Book-Writing System release automation.
  Dot-source from build-release.ps1 and Pester/equivalent tests. Never log secrets.
#>

Set-StrictMode -Version Latest

function Get-ReleaseRepoRoot {
    param([string]$StartPath = $PSScriptRoot)
    $candidate = (Resolve-Path -LiteralPath $StartPath).Path
    # When loaded from scripts/Release, walk up to repo root.
    while (-not [string]::IsNullOrWhiteSpace($candidate)) {
        if ((Test-Path -LiteralPath (Join-Path $candidate "src\MasterBookWritingSystem.App\MasterBookWritingSystem.App.csproj")) `
            -and (Test-Path -LiteralPath (Join-Path $candidate "seed"))) {
            return $candidate
        }
        $parent = Split-Path -Parent $candidate
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $candidate) {
            break
        }
        $candidate = $parent
    }
    throw "Could not locate repository root from '$StartPath'."
}

function Test-SemVerString {
    param([Parameter(Mandatory)][string]$Version)
    return [bool]($Version -match '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z\-\.]+))?(?:\+([0-9A-Za-z\-\.]+))?$')
}

function ConvertTo-ReleaseVersionInfo {
    param(
        [Parameter(Mandatory)][string]$Version
    )

    $trimmed = $Version.Trim()
    if (-not (Test-SemVerString -Version $trimmed)) {
        throw "Version '$trimmed' is not a supported semantic version (expected MAJOR.MINOR.PATCH[-prerelease])."
    }

    if ($trimmed -notmatch '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<pre>[0-9A-Za-z\-\.]+))?(?:\+(?<build>[0-9A-Za-z\-\.]+))?$') {
        throw "Failed to parse semantic version '$trimmed'."
    }

    $major = [int]$Matches['major']
    $minor = [int]$Matches['minor']
    $patch = [int]$Matches['patch']
    $pre = $null
    if ($Matches.Keys -contains 'pre') {
        $pre = $Matches['pre']
    }
    $fourPart = "$major.$minor.$patch.0"

    [pscustomobject]@{
        SemVer              = $trimmed
        Major               = $major
        Minor               = $minor
        Patch               = $patch
        Prerelease          = $pre
        AssemblyVersion     = $fourPart
        FileVersion         = $fourPart
        InformationalVersion = $trimmed
        MsixVersion         = $fourPart
        PackageFileVersion  = $trimmed
    }
}

function Get-CsprojVersion {
    param([Parameter(Mandatory)][string]$CsprojPath)
    if (-not (Test-Path -LiteralPath $CsprojPath)) {
        throw "Project file not found: $CsprojPath"
    }
    [xml]$xml = Get-Content -LiteralPath $CsprojPath -Raw
    $versionNode = Select-Xml -Xml $xml -XPath "//PropertyGroup/Version" | Select-Object -First 1
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.Node.InnerText)) {
        throw "Could not read <Version> from $CsprojPath."
    }
    return $versionNode.Node.InnerText.Trim()
}

function Resolve-ReleaseVersionInfo {
    param(
        [string]$OverrideVersion,
        [Parameter(Mandatory)][string]$CsprojPath
    )
    $raw = if ([string]::IsNullOrWhiteSpace($OverrideVersion)) {
        Get-CsprojVersion -CsprojPath $CsprojPath
    } else {
        $OverrideVersion.Trim()
    }
    return ConvertTo-ReleaseVersionInfo -Version $raw
}

function Assert-ReleaseWorkingState {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [switch]$AllowDirtyWorkingTree
    )

    $required = @(
        "src\MasterBookWritingSystem.App\MasterBookWritingSystem.App.csproj",
        "tests\MasterBookWritingSystem.Tests\MasterBookWritingSystem.Tests.csproj",
        "seed\workflow.json",
        "seed\schemas\document-templates.json",
        "seed\templates\core",
        "installer\MasterBookWritingSystem.iss",
        "scripts\build-release.ps1"
    )
    foreach ($rel in $required) {
        $path = Join-Path $RepoRoot $rel
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Unsupported working state: missing required path '$rel'."
        }
    }

    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) {
        Write-Warning "git was not found; skipping dirty working-tree check."
        return
    }

    Push-Location $RepoRoot
    try {
        $porcelain = @(& git status --porcelain --untracked-files=no 2>$null)
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "git status failed; skipping dirty working-tree check."
            return
        }
        # --untracked-files=no excludes '??' entries; remaining lines are tracked changes.
        $trackedChanges = @($porcelain | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($trackedChanges.Count -gt 0 -and -not $AllowDirtyWorkingTree) {
            $preview = ($trackedChanges | Select-Object -First 12) -join [Environment]::NewLine
            throw @"
Working tree has uncommitted tracked changes. Commit/stash them before release, or pass -AllowDirtyWorkingTree.
Changed tracked files:
$preview
"@
        }
    }
    finally {
        Pop-Location
    }
}

function Assert-NoArtifactCollision {
    param(
        [Parameter(Mandatory)][string[]]$Paths,
        [switch]$ForceOverwrite
    )
    foreach ($path in $Paths) {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        if ((Test-Path -LiteralPath $path) -and -not $ForceOverwrite) {
            throw "Release artifact already exists (refusing silent overwrite): $path. Pass -ForceOverwrite to replace."
        }
    }
}

function Find-IsccPath {
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) { return $path }
    }
    return $null
}

function Find-WindowsSdkTool {
    param([Parameter(Mandatory)][ValidateSet("makeappx.exe", "signtool.exe")][string]$ToolName)

    $cmd = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $kitsRoot)) {
        return $null
    }

    $found = @(Get-ChildItem -LiteralPath $kitsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object {
            $candidate = Join-Path $_.FullName "x64\$ToolName"
            if (Test-Path -LiteralPath $candidate) { $candidate }
        })
    if ($found.Count -eq 0) {
        return $null
    }
    return $found[0]
}

function Assert-PublishSeedLayout {
    param([Parameter(Mandatory)][string]$PublishDir)

    $exe = Join-Path $PublishDir "MasterBookWritingSystem.App.exe"
    $workflow = Join-Path $PublishDir "seed\workflow.json"
    $templates = Join-Path $PublishDir "seed\schemas\document-templates.json"
    $core = Join-Path $PublishDir "seed\templates\core"

    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Publish validation failed: missing executable at $exe"
    }

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

    $md = @(Get-ChildItem -LiteralPath $core -Filter "*.md" -File -ErrorAction SilentlyContinue)
    if ($md.Count -lt 1) {
        throw "Publish validation failed: no markdown templates under $core"
    }
}

function Get-FileSha256Hex {
    param([Parameter(Mandatory)][string]$Path)
    $hash = Get-FileHash -LiteralPath $Path -Algorithm SHA256
    return $hash.Hash.ToLowerInvariant()
}

function New-ReleaseArtifactDescriptor {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Kind
    )
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Cannot describe missing artifact: $Path"
    }
    $item = Get-Item -LiteralPath $Path
    return [ordered]@{
        kind     = $Kind
        name     = $item.Name
        path     = $item.FullName
        sizeBytes = [int64]$item.Length
        sha256   = (Get-FileSha256Hex -Path $item.FullName)
    }
}

function New-ReleaseManifestObject {
    param(
        [Parameter(Mandatory)]$VersionInfo,
        [string]$Commit,
        [Parameter(Mandatory)][datetime]$BuiltUtc,
        [Parameter(Mandatory)][bool]$TestsRan,
        [Parameter(Mandatory)][string]$Runtime,
        [Parameter(Mandatory)][object[]]$Artifacts,
        [string[]]$Notes = @()
    )

    return [ordered]@{
        schemaVersion = 1
        product       = "Master Book-Writing System"
        version       = $VersionInfo.SemVer
        assemblyVersion = $VersionInfo.AssemblyVersion
        msixVersion   = $VersionInfo.MsixVersion
        commit        = $Commit
        builtUtc      = $BuiltUtc.ToUniversalTime().ToString("o")
        tests         = [ordered]@{
            ran     = [bool]$TestsRan
            framework = "dotnet test"
            project = "tests/MasterBookWritingSystem.Tests/MasterBookWritingSystem.Tests.csproj"
        }
        runtime       = $Runtime
        artifacts     = @($Artifacts)
        notes         = @($Notes)
    }
}

function Write-ReleaseManifest {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$ManifestObject,
        [switch]$ForceOverwrite
    )
    Assert-NoArtifactCollision -Paths @($Path) -ForceOverwrite:$ForceOverwrite
    $json = $ManifestObject | ConvertTo-Json -Depth 8
    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
    # UTF-8 without BOM for stable hashing/tooling.
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine)
}

function Write-ChecksumsFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object[]]$Artifacts,
        [switch]$ForceOverwrite
    )
    Assert-NoArtifactCollision -Paths @($Path) -ForceOverwrite:$ForceOverwrite
    $lines = foreach ($artifact in $Artifacts) {
        "{0}  {1}" -f $artifact.sha256, $artifact.name
    }
    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, (($lines -join [Environment]::NewLine) + [Environment]::NewLine))
}

function Get-GitCommit {
    param([Parameter(Mandatory)][string]$RepoRoot)
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) { return "unknown" }
    Push-Location $RepoRoot
    try {
        $commit = (& git rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
            return "unknown"
        }
        return $commit.Trim()
    }
    finally {
        Pop-Location
    }
}

function Get-PlainTextFromSecureString {
    param([SecureString]$Secure)
    if ($null -eq $Secure) { return $null }
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    }
}

function Resolve-SigningConfiguration {
    param(
        [switch]$Sign,
        [string]$CertificatePath,
        [SecureString]$CertificatePassword,
        [string]$CertificateThumbprint,
        [string]$TimestampUrl
    )

    $path = if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
        $CertificatePath
    } else {
        $env:MBWS_SIGN_PFX_PATH
    }

    $thumb = if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $CertificateThumbprint
    } else {
        $env:MBWS_SIGN_THUMBPRINT
    }

    $passwordSecure = $CertificatePassword
    if ($null -eq $passwordSecure -and -not [string]::IsNullOrWhiteSpace($env:MBWS_SIGN_PFX_PASSWORD)) {
        $passwordSecure = ConvertTo-SecureString -String $env:MBWS_SIGN_PFX_PASSWORD -AsPlainText -Force
    }

    $ts = if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $TimestampUrl
    } elseif (-not [string]::IsNullOrWhiteSpace($env:MBWS_SIGN_TIMESTAMP_URL)) {
        $env:MBWS_SIGN_TIMESTAMP_URL
    } else {
        "http://timestamp.digicert.com"
    }

    $enabled = [bool]$Sign -or -not [string]::IsNullOrWhiteSpace($path) -or -not [string]::IsNullOrWhiteSpace($thumb)
    if (-not $enabled) {
        return [pscustomobject]@{
            Enabled     = $false
            Mode        = "None"
            CertificatePath = $null
            Thumbprint  = $null
            Password    = $null
            TimestampUrl = $ts
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($thumb)) {
        return [pscustomobject]@{
            Enabled     = $true
            Mode        = "Thumbprint"
            CertificatePath = $null
            Thumbprint  = $thumb.Trim()
            Password    = $null
            TimestampUrl = $ts
        }
    }

    if ([string]::IsNullOrWhiteSpace($path)) {
        throw "Signing was requested but neither -CertificatePath/MBWS_SIGN_PFX_PATH nor -CertificateThumbprint/MBWS_SIGN_THUMBPRINT was provided."
    }
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Signing certificate file was not found (path redacted)."
    }

    return [pscustomobject]@{
        Enabled     = $true
        Mode        = "Pfx"
        CertificatePath = (Resolve-Path -LiteralPath $path).Path
        Thumbprint  = $null
        Password    = $passwordSecure
        TimestampUrl = $ts
    }
}

function Invoke-AuthenticodeSign {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)]$Signing,
        [Parameter(Mandatory)][string]$SignToolPath
    )

    if (-not $Signing.Enabled) { return }

    $args = @(
        "sign",
        "/fd", "SHA256",
        "/tr", $Signing.TimestampUrl,
        "/td", "SHA256"
    )

    $plainPassword = $null
    try {
        if ($Signing.Mode -eq "Thumbprint") {
            $args += @("/sha1", $Signing.Thumbprint)
        }
        else {
            $args += @("/f", $Signing.CertificatePath)
            $plainPassword = Get-PlainTextFromSecureString -Secure $Signing.Password
            if (-not [string]::IsNullOrWhiteSpace($plainPassword)) {
                $args += @("/p", $plainPassword)
            }
        }
        $args += $FilePath

        # Do not Write-Host the argument list — it may contain a password.
        Write-Host "Signing $(Split-Path -Leaf $FilePath) with Authenticode (details redacted)..."
        $null = & $SignToolPath @args
        if ($LASTEXITCODE -ne 0) {
            throw "signtool sign failed for $(Split-Path -Leaf $FilePath) (exit $LASTEXITCODE)."
        }
    }
    finally {
        if ($null -ne $plainPassword) {
            $plainPassword = $null
            [GC]::Collect()
        }
    }
}

function Assert-AuthenticodeSignatureValid {
    param([Parameter(Mandatory)][string]$FilePath)

    $sig = Get-AuthenticodeSignature -LiteralPath $FilePath
    if ($sig.Status -ne "Valid") {
        throw "Authenticode verification failed for $(Split-Path -Leaf $FilePath): status=$($sig.Status) $($sig.StatusMessage)"
    }

    # Double-check with signtool when available.
    $signTool = Find-WindowsSdkTool -ToolName "signtool.exe"
    if ($signTool) {
        & $signTool verify /pa $FilePath | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "signtool verify failed for $(Split-Path -Leaf $FilePath) (exit $LASTEXITCODE)."
        }
    }
}

function New-MinimalPngBytes {
    param([int]$Width = 44, [int]$Height = 44)
    # 1x1 opaque PNG; Windows packaging accepts it for placeholder logos.
    # Base64 for a valid 1x1 PNG (not scaled visually, but valid packaging asset).
    $b64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO5W5W0AAAAASUVORK5CYII="
    return [Convert]::FromBase64String($b64)
}

function New-MsixPackagingLayout {
    param(
        [Parameter(Mandatory)][string]$PublishDir,
        [Parameter(Mandatory)][string]$LayoutDir,
        [Parameter(Mandatory)]$VersionInfo,
        [Parameter(Mandatory)][string]$Publisher
    )

    if (Test-Path -LiteralPath $LayoutDir) {
        Remove-Item -LiteralPath $LayoutDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $LayoutDir | Out-Null

    # Copy publish tree into layout root.
    Copy-Item -Path (Join-Path $PublishDir "*") -Destination $LayoutDir -Recurse -Force

    $assets = Join-Path $LayoutDir "Assets"
    New-Item -ItemType Directory -Force -Path $assets | Out-Null
    $png = New-MinimalPngBytes
    foreach ($name in @("StoreLogo.png", "Square150x150Logo.png", "Square44x44Logo.png")) {
        [System.IO.File]::WriteAllBytes((Join-Path $assets $name), $png)
    }

    $identityName = "MarkBernard.MasterBookWritingSystem"
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap rescap">
  <Identity Name="$identityName" Publisher="$Publisher" Version="$($VersionInfo.MsixVersion)" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>Master Book-Writing System</DisplayName>
    <PublisherDisplayName>Mark Bernard</PublisherDisplayName>
    <Description>Offline Master Book-Writing System</Description>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources>
    <Resource Language="en-us" />
  </Resources>
  <Applications>
    <Application Id="App" Executable="MasterBookWritingSystem.App.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="Master Book-Writing System"
        Description="Offline book writing system"
        BackgroundColor="transparent"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png" />
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@
    $manifestPath = Join-Path $LayoutDir "AppxManifest.xml"
    [System.IO.File]::WriteAllText($manifestPath, $manifest)
    return $LayoutDir
}
