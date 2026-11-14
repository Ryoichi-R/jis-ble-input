#requires -Version 7.4
[CmdletBinding()]
param(
    [ValidateSet('win-arm64', 'win-x64')][string]$RuntimeIdentifier = 'win-arm64',
    [switch]$NoRestore,
    [string]$RestoreSource,
    [string]$PackagesPath,
    [string[]]$FallbackPackagesPath,
    [string]$FirmwareCachePath,
    [ValidatePattern('^[a-z0-9][a-z0-9.-]{0,40}$')][string]$PackageSuffix
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspaceRoot = $projectRoot
function Assert-WorkspacePath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($workspaceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to write outside workspaceRoot.'
    }
    return $full
}
$project = Join-Path $projectRoot 'src/JisBleInput.App/JisBleInput.App.csproj'
[xml]$definition = Get-Content -LiteralPath $project -Raw
$version = [string]$definition.Project.PropertyGroup.Version
$runtimeVersion = [string]$definition.Project.PropertyGroup.BundledRuntimeVersion
$arm64 = $RuntimeIdentifier -eq 'win-arm64'
$dist = Assert-WorkspacePath (Join-Path $projectRoot '_internal/dist')
$packageVersion = if ($PackageSuffix) { "$version-$PackageSuffix" } else { $version }
$zip = Join-Path $dist "jis-ble-input-$packageVersion-$RuntimeIdentifier.zip"
if (Test-Path -LiteralPath $zip) { throw 'ZIP_ALREADY_EXISTS: preserve the existing archive or increment the version before packaging.' }
if ($arm64) {
    if (-not $FirmwareCachePath) { $FirmwareCachePath = Join-Path $projectRoot '_internal/build-cache/firmware' }
    $FirmwareCachePath = [IO.Path]::GetFullPath($FirmwareCachePath)
    if (-not (Test-Path -LiteralPath (Join-Path $FirmwareCachePath 'build/receipt.json') -PathType Leaf)) {
        throw "FIRMWARE_BUILD_MISSING: $FirmwareCachePath. Run Prepare-FirmwareTools.ps1 and Build-Firmware.ps1, or specify a verified build cache with -FirmwareCachePath."
    }
}
& (Join-Path $PSScriptRoot 'Test-XiaoContract.ps1')
$stage = Assert-WorkspacePath (Join-Path $projectRoot "_internal/build-cache/packages/$RuntimeIdentifier/$([guid]::NewGuid().ToString('N'))")
$publish = Join-Path $stage 'publish'
if (-not $PackagesPath) { $PackagesPath = Join-Path $projectRoot '_internal/build-cache/nuget-packages' }
$restore = @('restore', $project, '-p:SelfContained=true', '--locked-mode', '--configfile', (Join-Path $projectRoot 'NuGet.Config'))
if ($RestoreSource) { $restore += @('--source', $RestoreSource) }
if ($PackagesPath) { $restore += @('--packages', (Assert-WorkspacePath $PackagesPath)) }
if (-not $NoRestore) {
    $previousFallback = $env:RestoreFallbackFolders
    try {
        if ($FallbackPackagesPath) {
            $env:RestoreFallbackFolders = ($FallbackPackagesPath | ForEach-Object { [IO.Path]::GetFullPath($_) }) -join ';'
        } elseif (-not $previousFallback) {
            $standardCache = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages'
            if (Test-Path -LiteralPath $standardCache) { $env:RestoreFallbackFolders = $standardCache }
        }
        & dotnet @restore
        if ($LASTEXITCODE -ne 0) {
            throw 'RESTORE_FAILED: Use -RestoreSource online, or provide caches containing the packs reported above. Locked restore requires both win-arm64 and win-x64 packs.'
        }
    } finally { $env:RestoreFallbackFolders = $previousFallback }
}
& dotnet publish $project -c Release -r $RuntimeIdentifier --self-contained true --no-restore -o $publish
if ($LASTEXITCODE -ne 0) { throw 'PUBLISH_FAILED' }
$assets = Get-Content -LiteralPath (Join-Path $projectRoot '_internal/build-cache/obj/JisBleInput.App/project.assets.json') -Raw | ConvertFrom-Json
$packages = @{
    "microsoft.netcore.app.runtime.$RuntimeIdentifier" = 'dotnet-runtime'
    "microsoft.windowsdesktop.app.runtime.$RuntimeIdentifier" = 'windows-desktop-runtime'
    "microsoft.netcore.app.host.$RuntimeIdentifier" = 'dotnet-host'
}
foreach ($id in $packages.Keys) {
    $package = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder "$id/$runtimeVersion"
        if ((Test-Path -LiteralPath (Join-Path $candidate 'LICENSE.TXT')) -or (Test-Path -LiteralPath (Join-Path $candidate 'LICENSE'))) { $package = $candidate; break }
    }
    if (-not $package) { throw "RUNTIME_LICENSE_SOURCE_MISSING: $id" }
    $destination = Assert-WorkspacePath (Join-Path $publish "licenses/$($packages[$id])")
    $null = New-Item -ItemType Directory -Path $destination -Force
    $license = Join-Path $package 'LICENSE.TXT'
    if (-not (Test-Path -LiteralPath $license)) { $license = Join-Path $package 'LICENSE' }
    Copy-Item -LiteralPath $license -Destination (Join-Path $destination 'LICENSE.TXT')
    $notice = Join-Path $package 'THIRD-PARTY-NOTICES.TXT'
    if (Test-Path -LiteralPath $notice) {
        Copy-Item -LiteralPath $notice -Destination (Join-Path $destination 'THIRD-PARTY-NOTICES.TXT')
    }
}
foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $portsPackage = Join-Path $folder "system.io.ports/$runtimeVersion"
    if (Test-Path -LiteralPath (Join-Path $portsPackage 'system.io.ports.nuspec')) { break }
}
[xml]$portsMetadata = Get-Content -LiteralPath (Join-Path $portsPackage 'system.io.ports.nuspec') -Raw
if ($portsMetadata.package.metadata.license.InnerText -ne 'MIT' -or $portsMetadata.package.metadata.version -ne $runtimeVersion) { throw 'PORTS_LICENSE_MISMATCH' }
$portsLicenses = Assert-WorkspacePath (Join-Path $publish 'licenses/system-io-ports')
$null = New-Item -ItemType Directory -Path $portsLicenses -Force
Copy-Item -LiteralPath (Join-Path $publish 'licenses/dotnet-runtime/LICENSE.TXT') -Destination (Join-Path $portsLicenses 'LICENSE.TXT')
Copy-Item -LiteralPath (Join-Path $portsPackage 'THIRD-PARTY-NOTICES.TXT') -Destination $portsLicenses
@{ Package = 'System.IO.Ports'; Version = $runtimeVersion; License = 'MIT'; LicenseSource = 'Microsoft .NET runtime MIT text, same runtime version; package declares MIT'; Copyright = [string]$portsMetadata.package.metadata.copyright; Repository = [string]$portsMetadata.package.metadata.repository.url; Commit = [string]$portsMetadata.package.metadata.repository.commit; WindowsAssemblySha256 = (Get-FileHash -LiteralPath (Join-Path $portsPackage 'runtimes/win/lib/net10.0/System.IO.Ports.dll')).Hash } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $portsLicenses 'source.json') -Encoding utf8NoBOM
if ($arm64) {
    & (Join-Path $PSScriptRoot 'Copy-FirmwarePackage.ps1') -PublishPath $publish -AppVersion $version -FirmwareCachePath $FirmwareCachePath
}
& (Join-Path $PSScriptRoot 'Write-ReleaseSbom.ps1') -PublishPath $publish -RuntimeIdentifier $RuntimeIdentifier
Import-Module (Join-Path $PSScriptRoot 'PublishLayout.psm1') -Force
$manifest = @(Get-ChildItem -LiteralPath $publish -File -Recurse | Where-Object Name -NE 'manifest.json' | ForEach-Object {
    [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($publish, $_.FullName).Replace('\', '/'); Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $publish 'manifest.json') -Encoding utf8NoBOM
$validation = Test-PublishLayout -PublishPath $publish -Version $version -RuntimeVersion $runtimeVersion -RuntimeIdentifier $RuntimeIdentifier
$null = New-Item -ItemType Directory -Path $dist -Force
$tempZip = Join-Path $stage 'package.zip'
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $tempZip
[IO.File]::Move($tempZip, $zip)
$validation
Write-Output "Publish path: $publish"
Write-Output "Package: $zip"
Write-Output "ZIP SHA-256: $((Get-FileHash -LiteralPath $zip).Hash)"
