#requires -Version 7.4
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][ValidatePattern('^COM[0-9]+$')][string]$Port,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{16}$')][string]$BoardSerial,
    [string]$BuildPath,
    [switch]$Apply
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspaceRoot = $projectRoot
$cache = Join-Path $projectRoot '_internal/build-cache/firmware'
if (-not $BuildPath) { $BuildPath = Join-Path $cache 'build' }
$BuildPath = [IO.Path]::GetFullPath($BuildPath)
if (-not $BuildPath.StartsWith($workspaceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build must be inside workspace.' }
$receipt = Get-Content -LiteralPath (Join-Path $BuildPath 'receipt.json') -Raw | ConvertFrom-Json
if ($receipt.Status -ne 'Compiled' -or $receipt.Protocol -ne 1 -or $receipt.Firmware -notin '0.1.0', '0.1.1', '0.1.2' -or $receipt.Core -ne 'Seeeduino:nrf52@1.1.13') { throw 'UNSUPPORTED_FIRMWARE_RECEIPT' }
foreach ($name in @('xiao-nrf52840.ino.zip', 'xiao-nrf52840.ino.hex', 'xiao-nrf52840.ino.uf2')) {
    $entry = @($receipt.Files | Where-Object Name -EQ $name)
    if ($entry.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $BuildPath $name)).Hash -ne $entry[0].Sha256) { throw "FIRMWARE_HASH_MISMATCH: $name" }
}
& python (Join-Path $PSScriptRoot 'Test-FirmwareImage.py') (Join-Path $BuildPath 'xiao-nrf52840.ino.hex') (Join-Path $BuildPath 'xiao-nrf52840.ino.uf2') (Join-Path $BuildPath 'xiao-nrf52840.ino.zip')
if ($LASTEXITCODE -ne 0) { throw 'FIRMWARE_IMAGE_INVALID' }
# Read PnP identity only; do not probe COM ports or trigger 1200bps touch.
$device = @(Get-PnpDevice -PresentOnly -Class Ports | Where-Object { $_.FriendlyName -match "\($([regex]::Escape($Port))\)$" })
if ($device.Count -ne 1 -or $device[0].InstanceId -notmatch '^USB\\VID_2886&PID_0045') { throw 'NOT_XIAO_SENSE_BOOTLOADER: close the app and double-reset the Sense board manually.' }
$node = $device[0].InstanceId
$serial = ''
for ($level = 0; $level -lt 5; $level++) {
    if ($node -match '^USB\\VID_2886&PID_0045\\([0-9a-fA-F]{16})$') { $serial = $Matches[1]; break }
    $node = [string](Get-PnpDeviceProperty -InstanceId $node -KeyName 'DEVPKEY_Device_Parent').Data
}
if ($serial -ne $BoardSerial) { throw 'BOARD_ID_MISMATCH: refusing to upload.' }
$boardHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($serial.ToUpperInvariant())))
$package = Join-Path $BuildPath 'xiao-nrf52840.ino.zip'
Write-Output "Verified application-only firmware $($receipt.Firmware), board [$($boardHash.Substring(0, 8))], $Port."
if (-not $Apply) { Write-Output 'DRY_RUN: no device opened. Use -Apply only after retaining the previous known-good application and reading the recovery procedure.'; return }
$uploader = Join-Path $cache 'arduino-data/packages/Seeeduino/hardware/nrf52/1.1.13/tools/adafruit-nrfutil/win32/adafruit-nrfutil.exe'
if (-not (Test-Path -LiteralPath $uploader)) { throw 'UPLOADER_MISSING' }
if ($PSCmdlet.ShouldProcess("XIAO [$($boardHash.Substring(0, 8))] on $Port", 'Replace application only using USB DFU')) {
    & $uploader dfu serial -pkg $package -p $Port -b 115200 --singlebank
    if ($LASTEXITCODE -ne 0) { throw 'UPLOAD_UNCONFIRMED: double-reset and retry the retained image; do not erase flash or update bootloader/SoftDevice.' }
    Write-Output "DFU completed. Reconnect and verify HELLO firmware $($receipt.Firmware) in the app. Bluetooth/display/input acceptance is separate."
}
