#requires -Version 7.4
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspaceRoot = $projectRoot
$cache = [IO.Path]::GetFullPath((Join-Path $projectRoot '_internal/build-cache/firmware'))
if (-not $cache.StartsWith($workspaceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Outside workspace.' }
$toolsRoot = Join-Path $cache 'tools'
$null = New-Item -ItemType Directory -Path $toolsRoot -Force
function Get-VerifiedArchive([string]$Name, [string]$Url, [string]$Hash, [string]$Folder) {
    $archive = Join-Path $toolsRoot $Name
    if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $Url -OutFile $archive }
    if ((Get-FileHash -LiteralPath $archive).Hash -ne $Hash) { throw "TOOL_HASH_MISMATCH: $Name" }
    $destination = Join-Path $toolsRoot $Folder
    if (-not (Test-Path -LiteralPath $destination)) {
        $null = New-Item -ItemType Directory -Path $destination
        Expand-Archive -LiteralPath $archive -DestinationPath $destination
    }
}
Get-VerifiedArchive 'arduino-cli_1.3.1_Windows_64bit.zip' 'https://github.com/arduino/arduino-cli/releases/download/v1.3.1/arduino-cli_1.3.1_Windows_64bit.zip' 'CFECE6F356FDC9CA003CC3F0A488470030719C8E0E7BFCE5E42AC9410D87441F' 'arduino-cli-1.3.1'
$zigArchive = Join-Path $toolsRoot 'zig-x86_64-windows-0.14.1.zip'
if (-not (Test-Path -LiteralPath $zigArchive)) { Invoke-WebRequest -Uri 'https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip' -OutFile $zigArchive }
if ((Get-FileHash -LiteralPath $zigArchive).Hash -ne '554F5378228923FFD558EAC35E21AF020C73789D87AFEABF4BFD16F2E6FEED2C') { throw 'ZIG_HASH_MISMATCH' }
if (-not (Test-Path -LiteralPath (Join-Path $toolsRoot 'zig-x86_64-windows-0.14.1/zig.exe'))) { Expand-Archive -LiteralPath $zigArchive -DestinationPath $toolsRoot }
# Only the host compiler differs; the MCU toolchain and firmware source stay fixed.
. (Join-Path $PSScriptRoot 'Get-FirmwareHostTools.ps1')
$hostTools = Get-FirmwareHostTools
$llvmFolder = Join-Path $toolsRoot $hostTools.Folder
if (-not (Test-Path -LiteralPath (Join-Path $llvmFolder 'bin/clang.exe'))) {
    $llvmArchive = Join-Path $toolsRoot $hostTools.Archive
    if (-not (Test-Path -LiteralPath $llvmArchive)) {
        Invoke-WebRequest -Uri ("https://github.com/llvm/llvm-project/releases/download/llvmorg-23.1.2/" + $hostTools.Archive) -OutFile $llvmArchive
    }
    if ((Get-FileHash -LiteralPath $llvmArchive).Hash -ne $hostTools.Sha256) { throw 'LLVM_HASH_MISMATCH' }
    $members = @('bin/clang.exe', 'bin/clang-format.exe', 'bin/lld-link.exe', 'bin/llvm-cov.exe', 'bin/llvm-profdata.exe', 'lib/clang/23/include', ('lib/clang/23/lib/windows/' + $hostTools.ProfileRuntime)) | ForEach-Object { $hostTools.Prefix + '/' + $_ }
    $null = New-Item -ItemType Directory -Path $llvmFolder -Force
    if ($hostTools.Compression -eq 'xz') {
        & tar -xf $llvmArchive -C $llvmFolder --strip-components=1 @members
    } else {
        $zstd = Get-Command zstd -ErrorAction SilentlyContinue
        if (-not $zstd) { throw 'LLVM_EXTRACT_REQUIRES_ZSTD: put the zstd CLI on PATH.' }
        # Native PowerShell 7.4 preserves bytes between native programs.
        & $zstd.Source -q -d --long=31 -c $llvmArchive | tar -xf - -C $llvmFolder --strip-components=1 @members
    }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $llvmFolder 'bin/clang.exe'))) { throw 'LLVM_EXTRACT_FAILED' }
}
$configuration = @"
network:
  connection_timeout: 1800s
board_manager:
  additional_urls:
    - https://files.seeedstudio.com/arduino/package_seeeduino_boards_index.json
directories:
  data: $($cache.Replace('\', '/'))/arduino-data
  downloads: $($cache.Replace('\', '/'))/arduino-downloads
  user: $($cache.Replace('\', '/'))/arduino-user
"@
$configPath = Join-Path $cache 'arduino-cli.yaml'
[IO.File]::WriteAllText($configPath, $configuration, [Text.UTF8Encoding]::new($false))
$cli = Join-Path $toolsRoot 'arduino-cli-1.3.1/arduino-cli.exe'
& $cli core update-index --config-file $configPath
if ($LASTEXITCODE -ne 0) { throw 'ARDUINO_INDEX_FAILED' }
# Refuse drift in the fixed core archive even if an online index changes.
$index = Get-Content -LiteralPath (Join-Path $cache 'arduino-data/package_seeeduino_boards_index.json') -Raw | ConvertFrom-Json
$package = @($index.packages | Where-Object name -EQ 'Seeeduino')
$platform = @($package[0].platforms | Where-Object { $_.architecture -eq 'nrf52' -and $_.version -eq '1.1.13' })
if ($platform.Count -ne 1 -or $platform[0].checksum -ne 'SHA-256:5243bd456e8b7a3328725e17e6c6efc9712662f5aa27b7dc807911aeb0e93071') { throw 'SEEED_INDEX_DRIFT' }
& $cli core install 'Seeeduino:nrf52@1.1.13' --skip-post-install --config-file $configPath
if ($LASTEXITCODE -ne 0) { throw 'ARDUINO_CORE_INSTALL_FAILED' }
# GCC 9 uses MAX_PATH-sensitive paths containing ../ segments on Windows.
$shortCompiler = Join-Path $cache 'gcc9'
if (-not (Test-Path -LiteralPath $shortCompiler)) { Copy-Item -LiteralPath (Join-Path $cache 'arduino-data/packages/Seeeduino/tools/arm-none-eabi-gcc/9-2019q4') -Destination $shortCompiler -Recurse }
Write-Output 'Firmware tools prepared in workspace. No drivers installed and no device opened.'
