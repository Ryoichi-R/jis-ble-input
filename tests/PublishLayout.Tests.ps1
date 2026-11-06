[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseDeclaredVarsMoreThanAssignments', '', Justification = 'Pester exposes BeforeAll variables to the It blocks; the analyzer does not follow that scope.')]
param()

Describe 'ARM64 publish package validation' -Tag 'PackageIntegration' {
    BeforeAll {
        $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
        $workspaceRoot = Join-Path $projectRoot '_internal/build-cache'
        Import-Module (Join-Path $projectRoot 'scripts/PublishLayout.psm1') -Force
        $publish = Join-Path $projectRoot '_internal/build-cache/bin/JisBleInput.App/Release/net10.0-windows10.0.19041.0/win-arm64/publish'
        [xml]$definition = Get-Content (Join-Path $projectRoot 'src/JisBleInput.App/JisBleInput.App.csproj') -Raw
        $version = [string]$definition.Project.PropertyGroup.Version
        $runtime = [string]$definition.Project.PropertyGroup.BundledRuntimeVersion
    }

    It 'checks the actual EXE architecture, embedded runtimes and required distribution files' {
        $result = Test-PublishLayout -PublishPath $publish -Version $version -RuntimeVersion $runtime
        $result.RuntimeVersion | Should -Be $runtime
        $result.Sha256 | Should -Match '^[A-F0-9]{64}$'
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
        Mock Get-Content -ModuleName PublishLayout -ParameterFilter { $LiteralPath -like '*compatibility.json' } { '{"AppVersion":"1.7.0","Firmware":"0.1.0","Protocol":1,"Images":[{"Name":"xiao-nrf52840.ino.uf2","Sha256":"bad"}]}' }
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
                @{ Ref = 'jis-ble-input-fw-0.1.0'; Path = 'firmware/xiao-nrf52840.ino.uf2' },
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
