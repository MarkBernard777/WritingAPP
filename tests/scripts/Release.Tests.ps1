# Pester tests for release automation helpers.
# Run: Invoke-Pester -Path tests/scripts/Release.Tests.ps1
# Or:  .\scripts\test-release.ps1

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
. (Join-Path $repoRoot "scripts\Release\ReleaseCommon.ps1")

Describe "Release version validation" {
    It "derives assembly and MSIX versions from SemVer" {
        $info = ConvertTo-ReleaseVersionInfo -Version "1.1.0"
        $info.AssemblyVersion | Should -Be "1.1.0.0"
        $info.MsixVersion | Should -Be "1.1.0.0"
        $info.InformationalVersion | Should -Be "1.1.0"
    }

    It "rejects invalid versions" {
        { ConvertTo-ReleaseVersionInfo -Version "1.0" } | Should -Throw
        { ConvertTo-ReleaseVersionInfo -Version "v1.0.0" } | Should -Throw
    }
}

Describe "Release collision protection" {
    BeforeAll {
        $script:tempRoot = Join-Path $env:TEMP ("mbws-pester-" + [guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Force -Path $script:tempRoot | Out-Null
    }
    AfterAll {
        Remove-Item -LiteralPath $script:tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    It "refuses silent overwrite" {
        $path = Join-Path $script:tempRoot "a.zip"
        Set-Content -LiteralPath $path -Value "x"
        { Assert-NoArtifactCollision -Paths @($path) } | Should -Throw "*refusing silent overwrite*"
    }

    It "allows overwrite with -ForceOverwrite" {
        $path = Join-Path $script:tempRoot "b.zip"
        Set-Content -LiteralPath $path -Value "x"
        { Assert-NoArtifactCollision -Paths @($path) -ForceOverwrite } | Should -Not -Throw
    }
}

Describe "Publish seed validation" {
    BeforeAll {
        $script:pub = Join-Path $env:TEMP ("mbws-seed-" + [guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Force -Path (Join-Path $script:pub "seed\templates\core") | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path $script:pub "seed\schemas") | Out-Null
        Set-Content -LiteralPath (Join-Path $script:pub "seed\workflow.json") -Value "{}"
        Set-Content -LiteralPath (Join-Path $script:pub "seed\schemas\document-templates.json") -Value "[]"
        Set-Content -LiteralPath (Join-Path $script:pub "seed\templates\core\01.md") -Value "# t"
        $bytes = New-Object byte[] (1MB + 8)
        [System.IO.File]::WriteAllBytes((Join-Path $script:pub "MasterBookWritingSystem.App.exe"), $bytes)
    }
    AfterAll {
        Remove-Item -LiteralPath $script:pub -Recurse -Force -ErrorAction SilentlyContinue
    }

    It "accepts valid publish layout" {
        { Assert-PublishSeedLayout -PublishDir $script:pub } | Should -Not -Throw
    }

    It "fails when seed content is missing" {
        Remove-Item -LiteralPath (Join-Path $script:pub "seed\workflow.json") -Force
        { Assert-PublishSeedLayout -PublishDir $script:pub } | Should -Throw "*missing seed*"
    }
}

Describe "Release manifest generation" {
    It "includes version, commit, tests, runtime, artifacts and hashes" {
        $temp = Join-Path $env:TEMP ("mbws-manifest-" + [guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Force -Path $temp | Out-Null
        try {
            $file = Join-Path $temp "pkg.zip"
            Set-Content -LiteralPath $file -Value "payload"
            $versionInfo = ConvertTo-ReleaseVersionInfo -Version "1.0.2"
            $artifact = New-ReleaseArtifactDescriptor -Path $file -Kind "portable-zip"
            $manifest = New-ReleaseManifestObject `
                -VersionInfo $versionInfo `
                -Commit "deadbeef" `
                -BuiltUtc ([datetime]::UtcNow) `
                -TestsRan $true `
                -Runtime "win-x64 self-contained net10.0-windows" `
                -Artifacts @($artifact) `
                -Notes @("tool missing")
            $manifest.version | Should -Be "1.0.2"
            $manifest.commit | Should -Be "deadbeef"
            $manifest.tests.ran | Should -Be $true
            $manifest.runtime | Should -Match "win-x64"
            $manifest.artifacts[0].sha256.Length | Should -Be 64
            $out = Join-Path $temp "release-manifest-1.0.2.json"
            Write-ReleaseManifest -Path $out -ManifestObject $manifest
            Test-Path -LiteralPath $out | Should -Be $true
            { Write-ReleaseManifest -Path $out -ManifestObject $manifest } | Should -Throw "*refusing silent overwrite*"
        }
        finally {
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe "Missing packaging tools" {
    It "Find-IsccPath returns null or a path ending in ISCC.exe" {
        $path = Find-IsccPath
        if ($null -ne $path) {
            $path | Should -Match "ISCC\.exe$"
        }
    }

    It "Find-WindowsSdkTool returns null or an existing file" {
        $path = Find-WindowsSdkTool -ToolName "makeappx.exe"
        if ($null -ne $path) {
            Test-Path -LiteralPath $path | Should -Be $true
        }
    }
}
