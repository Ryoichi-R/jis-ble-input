#requires -Version 7.4
[CmdletBinding()]
param(
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
$publish = Assert-WorkspacePath (Join-Path $projectRoot '_internal/build-cache/bin/JisBleInput.App/Release/net10.0-windows10.0.19041.0/win-arm64/publish')
if (-not $FirmwareCachePath) { $FirmwareCachePath = Join-Path $projectRoot '_internal/build-cache/firmware' }
$FirmwareCachePath = [IO.Path]::GetFullPath($FirmwareCachePath)
if (-not (Test-Path -LiteralPath (Join-Path $FirmwareCachePath 'build/receipt.json') -PathType Leaf)) {
    throw "FIRMWARE_BUILD_MISSING: $FirmwareCachePath. Run Prepare-FirmwareTools.ps1 and Build-Firmware.ps1, or specify a verified build cache with -FirmwareCachePath."
}
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
# Preserve the previous executable before replacing the publish candidate.
$oldExe = Join-Path $publish 'JisBleInput.exe'
if (Test-Path -LiteralPath $oldExe) {
    $backup = Assert-WorkspacePath (Join-Path $projectRoot "_internal/backups/$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))-$([guid]::NewGuid().ToString('N'))")
    $null = New-Item -ItemType Directory -Path $backup
    Copy-Item -LiteralPath $oldExe -Destination (Join-Path $backup 'JisBleInput.exe')
    if ((Get-FileHash -LiteralPath $oldExe).Hash -ne (Get-FileHash -LiteralPath (Join-Path $backup 'JisBleInput.exe')).Hash) { throw 'BACKUP_MISMATCH' }
}
& dotnet publish $project -c Release -r win-arm64 --self-contained true --no-restore
if ($LASTEXITCODE -ne 0) { throw 'PUBLISH_FAILED' }
$assets = Get-Content -LiteralPath (Join-Path $projectRoot '_internal/build-cache/obj/JisBleInput.App/project.assets.json') -Raw | ConvertFrom-Json
$packages = @{
    'microsoft.netcore.app.runtime.win-arm64' = 'dotnet-runtime'
    'microsoft.windowsdesktop.app.runtime.win-arm64' = 'windows-desktop-runtime'
    'microsoft.netcore.app.host.win-arm64' = 'dotnet-host'
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
& (Join-Path $PSScriptRoot 'Test-XiaoContract.ps1')
& (Join-Path $PSScriptRoot 'Copy-FirmwarePackage.ps1') -PublishPath $publish -AppVersion $version -FirmwareCachePath $FirmwareCachePath
& (Join-Path $PSScriptRoot 'Write-ReleaseSbom.ps1') -PublishPath $publish
Import-Module (Join-Path $PSScriptRoot 'PublishLayout.psm1') -Force
$validation = Test-PublishLayout -PublishPath $publish -Version $version -RuntimeVersion $runtimeVersion
$manifest = @(Get-ChildItem -LiteralPath $publish -File -Recurse | Where-Object Name -NE 'manifest.json' | ForEach-Object {
    [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($publish, $_.FullName).Replace('\', '/'); Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $publish 'manifest.json') -Encoding utf8NoBOM
$dist = Assert-WorkspacePath (Join-Path $projectRoot '_internal/dist')
$null = New-Item -ItemType Directory -Path $dist -Force
$packageVersion = if ($PackageSuffix) { "$version-$PackageSuffix" } else { $version }
$zip = Join-Path $dist "jis-ble-input-$packageVersion-win-arm64.zip"
if (Test-Path -LiteralPath $zip) { throw 'ZIP_ALREADY_EXISTS: preserve the existing archive or increment the version before packaging.' }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip
$validation
Write-Output "Package: $zip"
