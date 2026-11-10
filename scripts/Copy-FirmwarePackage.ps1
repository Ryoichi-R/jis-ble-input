#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishPath, [Parameter(Mandatory)][string]$AppVersion, [string]$FirmwareCachePath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspaceRoot = $projectRoot
$PublishPath = [IO.Path]::GetFullPath($PublishPath)
if (-not $PublishPath.StartsWith($workspaceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Outside workspace.' }
$localCache = Join-Path $projectRoot '_internal/build-cache/firmware'
$cache = if ($FirmwareCachePath) { [IO.Path]::GetFullPath($FirmwareCachePath) } else { $localCache }
$build = Join-Path $cache 'build'
. (Join-Path $PSScriptRoot 'Get-FirmwareSourceHash.ps1')
$receiptPath = Join-Path $build 'receipt.json'
if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) { throw "FIRMWARE_BUILD_MISSING: $receiptPath" }
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
$toolchain = Get-Content -LiteralPath (Join-Path $projectRoot 'firmware/xiao-nrf52840/toolchain.json') -Raw | ConvertFrom-Json
if ($receipt.Status -ne 'Compiled' -or $receipt.Firmware -ne $toolchain.firmware -or $receipt.Protocol -ne $toolchain.protocol) { throw 'FIRMWARE_RECEIPT_INVALID' }
if ($receipt.SourceSha256 -ne (Get-FirmwareSourceHash $projectRoot)) { throw 'FIRMWARE_SOURCE_DRIFT' }
$appExe = Join-Path $PublishPath 'JisBleInput.exe'
if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) { throw 'PUBLISHED_APP_MISSING' }
$publishedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($appExe).ProductVersion
if ($publishedVersion -ne $AppVersion -or $toolchain.core.securityPatchedSha256 -ne $receipt.SecurityPatchHash) { throw 'FIRMWARE_COMPATIBILITY_MISMATCH' }
$firmware = Join-Path $PublishPath 'firmware'
$null = New-Item -ItemType Directory -Path $firmware -Force
foreach ($name in @('xiao-nrf52840.ino.hex', 'xiao-nrf52840.ino.zip', 'xiao-nrf52840.ino.uf2')) {
    $entry = @($receipt.Files | Where-Object Name -EQ $name)
    if ($entry.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $build $name)).Hash -ne $entry[0].Sha256) { throw "FIRMWARE_HASH_MISMATCH: $name" }
    Copy-Item -LiteralPath (Join-Path $build $name) -Destination (Join-Path $firmware $name)
}
Copy-Item -LiteralPath (Join-Path $build 'receipt.json') -Destination (Join-Path $firmware 'build-receipt.json')
Copy-Item -LiteralPath (Join-Path $build 'xiao-nrf52840.ino.elf') -Destination $firmware
Copy-Item -LiteralPath (Join-Path $projectRoot 'firmware/xiao-nrf52840/README.md') -Destination $firmware
$null = New-Item -ItemType Directory -Path (Join-Path $firmware 'xiao-nrf52840') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'firmware/xiao-nrf52840/README.md') -Destination (Join-Path $firmware 'xiao-nrf52840/README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'firmware/xiao-nrf52840/toolchain.json') -Destination $firmware
$rebuild = Join-Path $firmware 'rebuild/jis-ble-input'
foreach ($relative in @('firmware/xiao-nrf52840', 'contracts/xiao-protocol-v1')) {
    $destination = Join-Path $rebuild (Split-Path -Parent $relative)
    $null = New-Item -ItemType Directory -Path $destination -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot $relative) -Destination $destination -Recurse -Force
}
foreach ($relative in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'scripts/Build-Firmware.ps1', 'scripts/Get-FirmwareSourceHash.ps1', 'scripts/Get-FirmwareHostTools.ps1', 'scripts/Test-Firmware.ps1', 'scripts/Test-FirmwareFormat.ps1', 'scripts/Test-FirmwareImage.py', 'scripts/Prepare-FirmwareTools.ps1', 'scripts/Flash-Firmware.ps1', 'scripts/Test-XiaoContract.ps1', 'scripts/Generate-XiaoVectors.py', 'tests/firmware_image_spec.py', 'src/JisBleInput.App/HidKeyboardMap.cs', 'licenses/windows-ble-hid-LICENSE.txt')) {
    $destination = Join-Path $rebuild $relative
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot $relative) -Destination $destination
}
$coreRoot = Join-Path $cache 'arduino-data/packages/Seeeduino/hardware/nrf52/1.1.13'
$sdkArchive = Join-Path $cache 'arduino-downloads/packages/Seeed_nRF52_Boards-1.1.13.tar.bz2'
if (-not (Test-Path -LiteralPath $sdkArchive)) { $sdkArchive = @(Get-ChildItem -LiteralPath (Join-Path $cache 'arduino-downloads') -Recurse -File -Filter 'Seeed_nRF52_Boards-1.1.13.tar.bz2')[0].FullName }
if ((Get-FileHash -LiteralPath $sdkArchive).Hash -ne '5243BD456E8B7A3328725E17E6C6EFC9712662F5AA27B7DC807911AEB0E93071') { throw 'SDK_ARCHIVE_HASH_MISMATCH' }
Copy-Item -LiteralPath $sdkArchive -Destination (Join-Path $firmware 'rebuild/Seeed_nRF52_Boards-1.1.13.tar.bz2')
$sdkLicense = Join-Path $firmware 'licenses/sdk'
$null = New-Item -ItemType Directory -Path $sdkLicense -Force
foreach ($relative in @('LICENSE', 'libraries/Bluefruit52Lib/LICENSE', 'libraries/Adafruit_TinyUSB_Arduino/LICENSE', 'libraries/Adafruit_nRFCrypto/LICENSE', 'libraries/Adafruit_nRFCrypto/src/cortex-m4/license.txt', 'libraries/Adafruit_nRFCrypto/src/nrf_cc310/license.txt', 'libraries/Adafruit_LittleFS/src/littlefs/LICENSE.md', 'cores/nRF5/nordic/nrfx/LICENSE', 'cores/nRF5/freertos/License/license.txt')) {
    $destination = Join-Path $sdkLicense $relative
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
    Copy-Item -LiteralPath (Join-Path $coreRoot $relative) -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $coreRoot 'libraries/Bluefruit52Lib/examples/Peripheral/StandardFirmataBLE/LICENSE.txt') -Destination (Join-Path $sdkLicense 'LGPL-2.1.txt')
$nordic = [IO.File]::ReadAllText((Join-Path $coreRoot 'cores/nRF5/nordic/softdevice/s140_nrf52_7.3.0_API/include/ble_gap.h'))
[regex]::Match($nordic, '(?s)\A/\*.*?\*/').Value | Set-Content -LiteralPath (Join-Path $sdkLicense 'Nordic-SoftDevice-API.txt') -Encoding utf8NoBOM
# Retain linkable build inputs for local relinking with the LGPL core.
$objects = Join-Path $localCache "linkable-objects/$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $objects -Force
foreach ($file in Get-ChildItem -LiteralPath $build -File -Recurse | Where-Object Extension -In '.o', '.a') {
    $destination = Join-Path $objects ([IO.Path]::GetRelativePath($build, $file.FullName))
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
$objectZip = Join-Path $firmware 'rebuild/linkable-objects.zip'
Compress-Archive -Path (Join-Path $objects '*') -DestinationPath $objectZip -Force
$descriptor = [Convert]::FromHexString([IO.File]::ReadAllText((Join-Path $projectRoot 'contracts/xiao-protocol-v1/descriptor.hex')).Trim())
$descriptorSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($descriptor))
[pscustomobject]@{ AppVersion = $AppVersion; Firmware = $receipt.Firmware; Protocol = $receipt.Protocol; Board = 'XIAO nRF52840 Sense'; Fqbn = $toolchain.fqbn; DescriptorSha256 = $descriptorSha256; SourceSha256 = $receipt.SourceSha256; HardwareTested = $receipt.HardwareTested; Images = @($receipt.Files) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $firmware 'compatibility.json') -Encoding utf8NoBOM
