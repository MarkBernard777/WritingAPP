#Requires -Version 5.1
<#
.SYNOPSIS
  Headless disposable-project smoke for v1.2 acceptance gates (no GUI).
#>
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Write-Host "Running V12AcceptanceTests on disposable temp projects..."
dotnet test `
  (Join-Path $repoRoot "tests\MasterBookWritingSystem.Tests\MasterBookWritingSystem.Tests.csproj") `
  -c Release `
  --nologo `
  --filter "FullyQualifiedName~V12AcceptanceTests"

if ($LASTEXITCODE -ne 0) {
    throw "V12 disposable smoke failed."
}

Write-Host "Disposable smoke passed. For GUI smoke, follow docs/PUBLISH_SMOKE.md on project copies only."
