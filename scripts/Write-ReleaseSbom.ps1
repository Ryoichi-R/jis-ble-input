#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishPath,
    [ValidateSet('win-arm64', 'win-x64')][string]$RuntimeIdentifier = 'win-arm64'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$definition = [IO.File]::ReadAllText((Join-Path $projectRoot 'src/JisBleInput.App/JisBleInput.App.csproj'))
$appVersion = [string]$definition.Project.PropertyGroup.Version
$runtimeVersion = [string]$definition.Project.PropertyGroup.BundledRuntimeVersion
$publish = [IO.Path]::GetFullPath($PublishPath)
if (-not $publish.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'SBOM_OUTPUT_OUTSIDE_PROJECT' }
$assets = [IO.File]::ReadAllText((Join-Path $projectRoot '_internal/build-cache/obj/JisBleInput.App/project.assets.json')) | ConvertFrom-Json -AsHashtable
$components = [Collections.Generic.List[object]]::new()
$targetKeys = @($assets.targets.Keys | Where-Object { $_.EndsWith("/$RuntimeIdentifier", [StringComparison]::Ordinal) })
if ($targetKeys.Count -ne 1) { throw "SBOM_TARGET_MISSING_OR_AMBIGUOUS: $RuntimeIdentifier" }
$target = $assets.targets[$targetKeys[0]]
$identities = @($target.Keys | Where-Object { $target[$_].type -eq 'package' })
foreach ($runtime in @("Microsoft.NETCore.App.Runtime.$RuntimeIdentifier", "Microsoft.WindowsDesktop.App.Runtime.$RuntimeIdentifier", "Microsoft.NETCore.App.Host.$RuntimeIdentifier")) {
    $identities += "$runtime/$runtimeVersion"
}
foreach ($identity in ($identities | Sort-Object -Unique)) {
    $id, $version = $identity.Split('/')
    $archive = $null
    foreach ($folder in $assets.packageFolders.Keys) {
        $packageRoot = Join-Path $folder ($identity.ToLowerInvariant())
        $candidate = Join-Path $packageRoot ($id.ToLowerInvariant() + '.' + $version + '.nupkg')
        if (Test-Path -LiteralPath $candidate) { $archive = $candidate; break }
    }
    if (-not $archive) { throw "SBOM_PACKAGE_ARCHIVE_MISSING: $identity" }
    [xml]$nuspec = [IO.File]::ReadAllText((Join-Path (Split-Path -Parent $archive) ($id.ToLowerInvariant() + '.nuspec')))
    $component = [ordered]@{
        type = 'library'; 'bom-ref' = "pkg:nuget/$id@$version"; name = $id; version = $version
        purl = "pkg:nuget/$id@$version"
        hashes = @(@{ alg = 'SHA-256'; content = (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() })
        properties = @(@{ name = 'jis-ble-input:scope'; value = 'Resolved build dependency graph; not every platform-specific asset is shipped.' })
    }
    $license = $nuspec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="license"]')
    if ($license -and $license.type -eq 'expression') { $component.licenses = @(@{ expression = $license.InnerText }) }
    $components.Add($component)
}
$components.Add([ordered]@{
        type = 'library'; 'bom-ref' = 'blehid-descriptor-v0.6.0'; name = 'BleHid keyboard descriptor (modified)'; version = '0.6.0'
        licenses = @(@{ license = @{ id = 'MIT' } })
        externalReferences = @(@{ type = 'vcs'; url = 'https://github.com/abhishek-raj/windows-ble-hid/tree/v0.6.0' })
    })
$compositions = @()
if ($RuntimeIdentifier -eq 'win-arm64') {
    $sdk = Join-Path $publish 'firmware/rebuild/Seeed_nRF52_Boards-1.1.13.tar.bz2'
    if ((Get-FileHash -LiteralPath $sdk).Hash -ne '5243BD456E8B7A3328725E17E6C6EFC9712662F5AA27B7DC807911AEB0E93071') { throw 'SBOM_SDK_HASH_MISMATCH' }
    $components.Add([ordered]@{
        type = 'framework'; 'bom-ref' = 'seeed-nrf52-sdk-1.1.13'; name = 'Seeed nRF52 SDK'; version = '1.1.13'
        hashes = @(@{ alg = 'SHA-256'; content = (Get-FileHash -LiteralPath $sdk).Hash.ToLowerInvariant() })
        description = 'Aggregate SDK source archive including nested libraries. Mixed license terms are preserved in the archive and firmware/licenses/sdk; not a per-file license conclusion.'
        externalReferences = @(@{ type = 'distribution'; url = 'https://files.seeedstudio.com/arduino/core/nRF52/Seeed_nRF52_Boards-1.1.13.tar.bz2' })
    })
    $firmwareVersion = (Get-Content -LiteralPath (Join-Path $publish 'firmware/compatibility.json') -Raw | ConvertFrom-Json).Firmware
    $image = Join-Path $publish 'firmware/xiao-nrf52840.ino.uf2'
    $components.Add([ordered]@{
        type = 'firmware'; 'bom-ref' = "jis-ble-input-fw-$firmwareVersion"; name = 'JIS BLE Keyboard Sense firmware'; version = $firmwareVersion
        hashes = @(@{ alg = 'SHA-256'; content = (Get-FileHash -LiteralPath $image).Hash.ToLowerInvariant() })
    })
    $compositions = @(@{ aggregate = 'incomplete'; assemblies = @('seeed-nrf52-sdk-1.1.13') })
}
$bom = [ordered]@{
    bomFormat = 'CycloneDX'; specVersion = '1.6'; serialNumber = 'urn:uuid:' + [guid]::NewGuid().ToString(); version = 1
    metadata = @{
        timestamp = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        component = @{ type = 'application'; 'bom-ref' = "jis-ble-input-$appVersion"; name = 'JIS BLE Input'; version = $appVersion; licenses = @(@{ license = @{ id = 'MIT' } }) }
    }
    components = @($components.ToArray())
}
if ($compositions.Count) { $bom.compositions = $compositions }
$bom | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $publish 'sbom.cdx.json') -Encoding utf8NoBOM
Write-Output "SBOM: $($components.Count) components for $RuntimeIdentifier."
