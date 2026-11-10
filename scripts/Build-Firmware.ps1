#requires -Version 7.4
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspaceRoot = $projectRoot
$cache = Join-Path $projectRoot '_internal/build-cache/firmware'
. (Join-Path $PSScriptRoot 'Get-FirmwareSourceHash.ps1')
$coreRoot = Join-Path $cache 'arduino-data/packages/Seeeduino/hardware/nrf52/1.1.13'
$cli = Join-Path $cache 'tools/arduino-cli-1.3.1/arduino-cli.exe'
$compiler = Join-Path $cache 'gcc9/bin'
if (-not (Test-Path -LiteralPath $cli) -or -not (Test-Path -LiteralPath $coreRoot)) { throw 'FIRMWARE_TOOLCHAIN_MISSING: Seeed nRF52 1.1.13 / Arduino CLI 1.3.1 must be prepared in the documented workspace cache.' }
if ((& $cli version) -notmatch 'Version: 1\.3\.1\b') { throw 'ARDUINO_VERSION_MISMATCH' }
if (-not (Test-Path -LiteralPath (Join-Path $compiler 'arm-none-eabi-g++.exe'))) { throw 'ARM_COMPILER_MISSING: prepare Seeed core pinned compiler 9-2019q4.' }
$securityPath = [IO.Path]::GetFullPath((Join-Path $coreRoot 'libraries/Bluefruit52Lib/src/BLESecurity.cpp'))
if (-not $securityPath.StartsWith($workspaceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to patch outside workspace.' }
$text = [IO.File]::ReadAllText($securityPath)
if (-not $text.Contains('jbi_allow_pairing')) {
    if ((Get-FileHash -LiteralPath $securityPath).Hash -ne '341085B8EF39027B2C97831EC8CA252DBBA2613F3147E668CC8E5867A99C290D') { throw 'SECURITY_PATCH_SOURCE_MISMATCH' }
    $text = "extern `"C`" bool jbi_allow_pairing(unsigned short) __attribute__((weak));`n" + $text
    $pattern = '(case BLE_GAP_EVT_SEC_PARAMS_REQUEST:\s*\{)'
    if ([regex]::Matches($text, $pattern).Count -ne 1) { throw 'SECURITY_PATCH_TARGET_MISMATCH' }
    $hook = '$1' + "`n      if (jbi_allow_pairing && !jbi_allow_pairing(conn_hdl)) {`n        sd_ble_gap_sec_params_reply(conn_hdl, BLE_GAP_SEC_STATUS_PAIRING_NOT_SUPP, NULL, NULL);`n        break;`n      }"
    $text = [regex]::Replace($text, $pattern, $hook)
    [IO.File]::WriteAllText($securityPath, $text, [Text.UTF8Encoding]::new($false))
}
if (-not $text.Contains('!jbi_allow_pairing(conn_hdl)')) { throw 'SECURITY_PAIRING_HOOK_MISSING' }
if ((Get-FileHash -LiteralPath $securityPath).Hash -ne '7CAC7F6BDDF86554929548861AA747E97BF42FA3CF8E061652E67D3CC37A58C2') { throw 'SECURITY_PATCH_HASH_MISMATCH' }
$output = [IO.Path]::GetFullPath((Join-Path $cache 'build'))
if (-not $output.StartsWith($workspaceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Outside workspace.' }
$null = New-Item -ItemType Directory -Path $output -Force
$compileLog = Join-Path $cache 'target-compile.log'
$pathMaps = @($projectRoot, $projectRoot.Replace('\', '/')) | Select-Object -Unique | ForEach-Object { '"-ffile-prefix-map=' + $_ + '=/_/jis-ble-input"' }
$mapFlags = $pathMaps -join ' '
$assemblerMaps = @($projectRoot, $projectRoot.Replace('\', '/')) | Select-Object -Unique | ForEach-Object { '"-Wa,--debug-prefix-map=' + $_ + '=/_/jis-ble-input"' }
$assemblerFlags = $mapFlags + ' ' + ($assemblerMaps -join ' ')
Push-Location $projectRoot
try {
    & $cli compile --config-file (Join-Path $cache 'arduino-cli.yaml') --fqbn 'Seeeduino:nrf52:xiaonRF52840Sense:softdevice=s140v6,debug=l0' --build-property "compiler.path=$($compiler.Replace('\', '/'))/" --build-property "compiler.c.extra_flags=$mapFlags" --build-property "compiler.cpp.extra_flags=$mapFlags" --build-property "compiler.S.extra_flags=$assemblerFlags" --build-path $output (Join-Path $projectRoot 'firmware/xiao-nrf52840') *> $compileLog
} finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $compileLog -Tail 30; throw 'FIRMWARE_COMPILE_FAILED' }
Get-Content -LiteralPath $compileLog -Tail 12
& python (Join-Path $PSScriptRoot 'Test-FirmwareImage.py') (Join-Path $output 'xiao-nrf52840.ino.hex')
if ($LASTEXITCODE -ne 0) { throw 'FIRMWARE_IMAGE_INVALID' }
$converter = Join-Path $coreRoot 'tools/uf2conv/uf2conv.py'
if ((Get-FileHash -LiteralPath $converter).Hash -ne 'C3E86D44992CF0956CD07A73DCA52BDD81F7CF149ADDE0642C15BAFED05A6202') { throw 'UF2_CONVERTER_MISMATCH' }
& python $converter (Join-Path $output 'xiao-nrf52840.ino.hex') -c -f 0xADA52840 -o (Join-Path $output 'xiao-nrf52840.ino.uf2')
if ($LASTEXITCODE -ne 0) { throw 'UF2_CONVERSION_FAILED' }
& python (Join-Path $PSScriptRoot 'Test-FirmwareImage.py') (Join-Path $output 'xiao-nrf52840.ino.hex') (Join-Path $output 'xiao-nrf52840.ino.uf2') (Join-Path $output 'xiao-nrf52840.ino.zip')
if ($LASTEXITCODE -ne 0) { throw 'UF2_IMAGE_INVALID' }
$toolchain = Get-Content -LiteralPath (Join-Path $projectRoot 'firmware/xiao-nrf52840/toolchain.json') -Raw | ConvertFrom-Json
[pscustomobject]@{ Status = 'Compiled'; SourceSha256 = (Get-FirmwareSourceHash $projectRoot); HardwareTested = $false; Core = 'Seeeduino:nrf52@1.1.13'; Protocol = $toolchain.protocol; Firmware = $toolchain.firmware; SecurityPatchHash = (Get-FileHash -LiteralPath $securityPath).Hash; Files = @(Get-ChildItem -LiteralPath $output -File | Where-Object Extension -In '.hex', '.bin', '.zip', '.uf2' | ForEach-Object { @{ Name = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } }) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'receipt.json') -Encoding utf8NoBOM
