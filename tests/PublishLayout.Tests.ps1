[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseDeclaredVarsMoreThanAssignments', '', Justification = 'Pester exposes BeforeAll variables to the It blocks; the analyzer does not follow that scope.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', '', Justification = 'Pester passes container parameters into the BeforeAll blocks.')]
param([string]$Arm64Package, [string]$X64Package)

Describe 'ARM64 publish package validation' -Tag 'PackageIntegration', 'Arm64Package' {
    BeforeAll {
        $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
        $workspaceRoot = Join-Path $projectRoot '_internal/build-cache'
        Import-Module (Join-Path $projectRoot 'scripts/PublishLayout.psm1') -Force
        [xml]$definition = Get-Content (Join-Path $projectRoot 'src/JisBleInput.App/JisBleInput.App.csproj') -Raw
        $version = [string]$definition.Project.PropertyGroup.Version
        $runtime = [string]$definition.Project.PropertyGroup.BundledRuntimeVersion
        $firmware = (Get-Content -LiteralPath (Join-Path $projectRoot 'firmware/xiao-nrf52840/toolchain.json') -Raw | ConvertFrom-Json).firmware
        if (-not $Arm64Package) { $Arm64Package = Join-Path $projectRoot "_internal/dist/jis-ble-input-$version-win-arm64.zip" }
        $extracted = Join-Path $workspaceRoot "package-tests/$([guid]::NewGuid().ToString('N'))"
        $publish = Join-Path $extracted 'win-arm64'
        Expand-Archive -LiteralPath $Arm64Package -DestinationPath $publish
    }

    It 'checks the actual EXE architecture, embedded runtimes and required distribution files' {
        $result = Test-PublishLayout -PublishPath $publish -Version $version -RuntimeVersion $runtime
        $result.RuntimeVersion | Should -Be $runtime
        $result.Sha256 | Should -Match '^[A-F0-9]{64}$'
        $result.Sha256 | Should -Match '^[A-F0-9]{64}$'
    }

    It 'reports a missing firmware receipt before restore or publish' {
        $absent = Join-Path $workspaceRoot ([guid]::NewGuid().ToString('N'))
        { & (Join-Path $projectRoot 'scripts/Publish.ps1') -FirmwareCachePath $absent } | Should -Throw '*FIRMWARE_BUILD_MISSING*'
    }

    It 'rejects an executable with the wrong product version' {
        { Test-PublishLayout -PublishPath $publish -Version '0.0.0' -RuntimeVersion $runtime } | Should -Throw '*PUBLISH_VERSION_MISMATCH*'
    }

    It 'rejects an embedded runtime that differs from the build contract' {
        Mock Get-PublishedRuntime -ModuleName PublishLayout {
            [pscustomobject]@{ runtimeOptions = [pscustomobject]@{ includedFrameworks = @(
                [pscustomobject]@{ name = 'Microsoft.NETCore.App'; version = '10.0.8' },
                [pscustomobject]@{ name = 'Microsoft.WindowsDesktop.App'; version = '10.0.8' }
            ) } }
        }
        { Test-PublishLayout -PublishPath $publish -Version $version -RuntimeVersion $runtime } | Should -Throw '*PUBLISH_RUNTIME_MISMATCH*'
    }

    It 'rejects firmware built for a different application version' {
        Mock Get-Content -ModuleName PublishLayout -ParameterFilter { $LiteralPath -like '*compatibility.json' } { '{"AppVersion":"0.0.0","Firmware":"0.1.0","Protocol":1}' }
        { Test-PublishLayout -PublishPath $publish -Version $version -RuntimeVersion $runtime } | Should -Throw '*PUBLISH_FIRMWARE_VERSION_MISMATCH*'
    }

    It 'rejects firmware whose image does not match the recorded hash' {
        $invalidImages = @{ AppVersion = $version; Firmware = $firmware; Protocol = 1; Images = @(@{ Name = 'xiao-nrf52840.ino.uf2'; Sha256 = 'bad' }) } | ConvertTo-Json -Depth 4
        Mock Get-Content -ModuleName PublishLayout -ParameterFilter { $LiteralPath -like '*compatibility.json' } { $invalidImages }
        { Test-PublishLayout -PublishPath $publish -Version $version -RuntimeVersion $runtime } | Should -Throw '*PUBLISH_FIRMWARE_HASH_MISMATCH*'
    }

    It 'binds the publish candidate to the current firmware source' {
        . (Join-Path $projectRoot 'scripts/Get-FirmwareSourceHash.ps1')
        $compatibility = [IO.File]::ReadAllText((Join-Path $publish 'firmware/compatibility.json')) | ConvertFrom-Json
        $compatibility.SourceSha256 | Should -Be (Get-FirmwareSourceHash $projectRoot)
    }

    It 'binds the SBOM firmware image and SDK archive to the shipped files' {
        $bom = [IO.File]::ReadAllText((Join-Path $publish 'sbom.cdx.json')) | ConvertFrom-Json
        $bom.bomFormat | Should -Be 'CycloneDX'
        $bom.metadata.component.version | Should -Be $version
        @($bom.components.'bom-ref' | Sort-Object -Unique).Count | Should -Be $bom.components.Count
        foreach ($binding in @(
                @{ Ref = "jis-ble-input-fw-$firmware"; Path = 'firmware/xiao-nrf52840.ino.uf2' },
                @{ Ref = 'seeed-nrf52-sdk-1.1.13'; Path = 'firmware/rebuild/Seeed_nRF52_Boards-1.1.13.tar.bz2' })) {
            $component = @($bom.components | Where-Object 'bom-ref' -EQ $binding.Ref)
            $component.Count | Should -Be 1
            $component[0].hashes[0].content | Should -Be (Get-FileHash -LiteralPath (Join-Path $publish $binding.Path)).Hash.ToLowerInvariant()
        }
        @($bom.components | Where-Object name -EQ 'System.IO.Ports')[0].version | Should -Be $runtime
    }

    It 'rejects firmware recorded for another board definition or key descriptor' {
        foreach ($case in @(@{ Name = 'Fqbn'; Value = 'Seeeduino:nrf52:xiaonRF52840:softdevice=s140v6,debug=l0'; Error = '*PUBLISH_FIRMWARE_FQBN_MISMATCH*' },
                @{ Name = 'DescriptorSha256'; Value = ('0' * 64); Error = '*PUBLISH_FIRMWARE_DESCRIPTOR_MISMATCH*' })) {
            $altered = [IO.File]::ReadAllText((Join-Path $publish 'firmware/compatibility.json')) | ConvertFrom-Json
            $altered.($case.Name) = $case.Value
            $json = $altered | ConvertTo-Json -Depth 5
            Mock Get-Content -ModuleName PublishLayout -ParameterFilter { $LiteralPath -like '*compatibility.json' } { $json }
            { Test-PublishLayout -PublishPath $publish -Version $version -RuntimeVersion $runtime } | Should -Throw $case.Error
        }
    }

    It 'rejects a package missing the upstream license before distribution' {
        $fixture = Join-Path $workspaceRoot "scratchpad/jis-ble-package-tests/$([guid]::NewGuid().ToString('N'))"
        if (-not $fixture.StartsWith($workspaceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Outside workspace' }
        $null = New-Item -ItemType Directory -Path $fixture -Force
        foreach ($file in @('JisBleInput.exe', 'README.md', 'VALIDATION.md')) {
            [IO.File]::WriteAllText((Join-Path $fixture $file), 'synthetic fixture; never executed')
        }
        { Test-PublishLayout -PublishPath $fixture -Version $version -RuntimeVersion $runtime } | Should -Throw '*PUBLISH_FILE_MISSING: licenses/windows-ble-hid-LICENSE.txt*'
    }
}

Describe 'x64 publish package validation' -Tag 'PackageIntegration', 'X64Package' {
    BeforeAll {
        $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
        Import-Module (Join-Path $projectRoot 'scripts/PublishLayout.psm1') -Force
        [xml]$definition = Get-Content (Join-Path $projectRoot 'src/JisBleInput.App/JisBleInput.App.csproj') -Raw
        $version = [string]$definition.Project.PropertyGroup.Version
        $runtime = [string]$definition.Project.PropertyGroup.BundledRuntimeVersion
        if (-not $X64Package) { $X64Package = Join-Path $projectRoot "_internal/dist/jis-ble-input-$version-win-x64.zip" }
        $x64 = Join-Path $projectRoot "_internal/build-cache/package-tests/$([guid]::NewGuid().ToString('N'))/win-x64"
        Expand-Archive -LiteralPath $X64Package -DestinationPath $x64
    }

    It 'checks the extracted x64 executable, bundled runtimes and distribution files' {
        $result = Test-PublishLayout -PublishPath $x64 -Version $version -RuntimeVersion $runtime -RuntimeIdentifier win-x64
        $result.RuntimeIdentifier | Should -Be 'win-x64'
        $result.Sha256 | Should -Match '^[A-F0-9]{64}$'
    }

    It 'rejects the ARM64 machine code in an x64 bundle' {
        $exe = Join-Path $x64 'JisBleInput.exe'
        $file = [IO.File]::Open($exe, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite)
        try {
            $file.Position = 0x3C
            $offset = [IO.BinaryReader]::new($file, [Text.Encoding]::UTF8, $true).ReadInt32() + 4
            $file.Position = $offset
            $machine = [byte[]]@($file.ReadByte(), $file.ReadByte())
            $file.Position = $offset
            $file.Write([byte[]]@(0x64, 0xAA))
        } finally { $file.Dispose() }
        try {
            { Test-PublishLayout -PublishPath $x64 -Version $version -RuntimeVersion $runtime -RuntimeIdentifier win-x64 } |
                Should -Throw '*PUBLISH_PE_MACHINE_MISMATCH*'
        } finally {
            $file = [IO.File]::OpenWrite($exe)
            try { $file.Position = $offset; $file.Write($machine) }
            finally { $file.Dispose() }
        }
    }

    It 'rejects each ARM64-only x64 package member' {
        foreach ($relative in @('firmware', 'docs/XIAO-PROTOCOL.md', 'docs/XIAO-SPEED-TRIAL.md',
                'samples/xiao-japanese-trial.txt', 'samples/Compare-XiaoTrialText.ps1')) {
            $extra = Join-Path $x64 $relative
            $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $extra)
            [IO.File]::WriteAllText($extra, 'synthetic fixture')
            try {
                { Test-PublishLayout -PublishPath $x64 -Version $version -RuntimeVersion $runtime -RuntimeIdentifier win-x64 } |
                    Should -Throw "*PUBLISH_FORBIDDEN_FILE: $relative*"
            } finally { Remove-Item -LiteralPath $extra }
        }
    }

    It 'rejects a leftover file even when the current files are intact' {
        $extra = Join-Path $x64 'JisBleInput.old.exe'
        [IO.File]::WriteAllText($extra, 'synthetic fixture')
        try {
            { Test-PublishLayout -PublishPath $x64 -Version $version -RuntimeVersion $runtime -RuntimeIdentifier win-x64 } |
                Should -Throw '*PUBLISH_UNEXPECTED_FILE*'
        } finally { Remove-Item -LiteralPath $extra }
    }

    It 'rejects a changed common file' {
        $sample = Join-Path $x64 'samples/ascii-url.txt'
        $original = [IO.File]::ReadAllBytes($sample)
        [IO.File]::AppendAllText($sample, 'tampered')
        try {
            { Test-PublishLayout -PublishPath $x64 -Version $version -RuntimeVersion $runtime -RuntimeIdentifier win-x64 } |
                Should -Throw '*PUBLISH_MANIFEST_MISMATCH*'
        } finally { [IO.File]::WriteAllBytes($sample, $original) }
    }

}
