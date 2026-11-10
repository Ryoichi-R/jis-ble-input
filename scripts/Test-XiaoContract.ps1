#requires -Version 7.4
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hex = [IO.File]::ReadAllText((Join-Path $projectRoot 'contracts/xiao-protocol-v1/descriptor.hex')).Trim()
$canonical = [Convert]::FromHexString($hex)
$app = [IO.File]::ReadAllText((Join-Path $projectRoot 'src/JisBleInput.App/HidKeyboardMap.cs'))
$firmware = [IO.File]::ReadAllText((Join-Path $projectRoot 'firmware/xiao-nrf52840/src/HidDescriptor.h'))
function Get-HexBytes([string]$Text) {
    return [byte[]]@([regex]::Matches($Text, '0x([0-9a-fA-F]{2})\b') | ForEach-Object { [Convert]::ToByte($_.Groups[1].Value, 16) })
}
$appBytes = Get-HexBytes ($app -split 'Descriptor\s*=>|Descriptor\s*=')[1]
$fwDescriptor = Get-HexBytes ([regex]::Match($firmware, 'kHidDescriptor\[\]\s*=\s*\{([^}]+)\}').Groups[1].Value)
$fwHash = Get-HexBytes ([regex]::Match($firmware, 'kDescriptorHash\[\]\s*=\s*\{([^}]+)\}').Groups[1].Value)
if ([Convert]::ToHexString($appBytes) -ne $hex.ToUpperInvariant() -or [Convert]::ToHexString($fwDescriptor) -ne $hex.ToUpperInvariant()) { throw 'HID_DESCRIPTOR_MISMATCH' }
if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($canonical)) -ne [Convert]::ToHexString($fwHash)) { throw 'HID_DESCRIPTOR_HASH_MISMATCH' }
$toolchain = Get-Content -LiteralPath (Join-Path $projectRoot 'firmware/xiao-nrf52840/toolchain.json') -Raw | ConvertFrom-Json
$controller = Get-Content -LiteralPath (Join-Path $projectRoot 'firmware/xiao-nrf52840/src/Controller.h') -Raw
$version = [regex]::Match($controller, '(?m)^#define JBI_FIRMWARE_VERSION "([0-9]\.[0-9]\.[0-9])"').Groups[1].Value
if (-not $version -or $version -ne $toolchain.firmware -or $toolchain.protocol -ne 1) { throw 'FIRMWARE_VERSION_MISMATCH' }
Write-Output "HID contract: PASS ($($canonical.Length) bytes, $([Convert]::ToHexString($fwHash)))"
