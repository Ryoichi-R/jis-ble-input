#requires -Version 7.4
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$cache = Join-Path $projectRoot '_internal/build-cache/firmware'
$zig = Join-Path $cache 'tools/zig-x86_64-windows-0.14.1/zig.exe'
if (-not (Test-Path -LiteralPath $zig)) { throw 'HOST_COMPILER_MISSING: prepare the documented Zig 0.14.1 x64 compiler (native x64 or Windows ARM64 emulation).' }
$output = [IO.Path]::GetFullPath((Join-Path $cache 'host-tests'))
if (-not $output.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Outside workspace.' }
$null = New-Item -ItemType Directory -Path $output -Force
$exe = Join-Path $output 'controller-spec.exe'
# Keep Zig caches inside the workspace as well.
$previousZigCache = $env:ZIG_GLOBAL_CACHE_DIR
$previousLocalCache = $env:ZIG_LOCAL_CACHE_DIR
try {
    $env:ZIG_GLOBAL_CACHE_DIR = Join-Path $cache 'zig-x64-global-cache'
    $env:ZIG_LOCAL_CACHE_DIR = Join-Path $cache 'zig-x64-cache'
    & $zig c++ -std=c++11 -fno-exceptions -fno-rtti -Wall -Wextra -Werror (Join-Path $projectRoot 'firmware/xiao-nrf52840/tests/controller_spec.cpp') -o $exe *> (Join-Path $output 'compile.log')
}
finally {
    $env:ZIG_GLOBAL_CACHE_DIR = $previousZigCache
    $env:ZIG_LOCAL_CACHE_DIR = $previousLocalCache
}
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $output 'compile.log') -Tail 30; throw 'FIRMWARE_HOST_COMPILE_FAILED' }
$result = & $exe (Join-Path $projectRoot 'contracts/xiao-protocol-v1/vectors.tsv')
$result
if ($LASTEXITCODE -ne 0 -or ($result -join "`n") -notmatch 'Passed: ([1-9][0-9]*)') { throw 'FIRMWARE_HOST_TEST_FAILED' }
$checks = [int]$Matches[1]
& python (Join-Path $projectRoot 'tests/firmware_image_spec.py') *> (Join-Path $output 'image-tests.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $output 'image-tests.log'); throw 'FIRMWARE_IMAGE_TESTS_FAILED' }
# Line coverage of the host-testable pure logic (src/*.h). The .ino SDK boundary is not host-compilable
# and stays a hardware item. MSVC's STL needs C++14, so this instrumented build is separate from the C++11 build above.
. (Join-Path $PSScriptRoot 'Get-FirmwareHostTools.ps1')
$llvm = Join-Path $cache ('tools/' + (Get-FirmwareHostTools).Folder + '/bin')
if (-not (Test-Path -LiteralPath (Join-Path $llvm 'clang.exe'))) { throw 'COVERAGE_TOOL_MISSING: run scripts/Prepare-FirmwareTools.ps1 (LLVM 23.1.2 for the host; needs MSVC C++ tools and the Windows SDK).' }
$coverageExe = Join-Path $output 'controller-spec-coverage.exe'
& (Join-Path $llvm 'clang.exe') -std=c++14 -fno-exceptions -fno-rtti -D_HAS_EXCEPTIONS=0 -Wall -Wextra -Werror -fprofile-instr-generate -fcoverage-mapping -fuse-ld=lld (Join-Path $projectRoot 'firmware/xiao-nrf52840/tests/controller_spec.cpp') -o $coverageExe *> (Join-Path $output 'coverage-compile.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $output 'coverage-compile.log') -Tail 30; throw 'FIRMWARE_COVERAGE_COMPILE_FAILED' }
$rawProfile = Join-Path $output 'controller-spec.profraw'
$coverageProfile = Join-Path $output 'controller-spec.profdata'
Remove-Item -LiteralPath $rawProfile, $coverageProfile -ErrorAction SilentlyContinue
$previousProfileFile = $env:LLVM_PROFILE_FILE
try {
    $env:LLVM_PROFILE_FILE = $rawProfile
    $coverageResult = & $coverageExe (Join-Path $projectRoot 'contracts/xiao-protocol-v1/vectors.tsv')
}
finally { $env:LLVM_PROFILE_FILE = $previousProfileFile }
if ($LASTEXITCODE -ne 0 -or ($coverageResult -join "`n") -notmatch "Passed: $checks\b") { throw 'FIRMWARE_COVERAGE_RUN_FAILED' }
& (Join-Path $llvm 'llvm-profdata.exe') merge -sparse $rawProfile -o $coverageProfile
if ($LASTEXITCODE -ne 0) { throw 'FIRMWARE_COVERAGE_MERGE_FAILED' }
$summary = & (Join-Path $llvm 'llvm-cov.exe') export -summary-only "-instr-profile=$coverageProfile" $coverageExe | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'FIRMWARE_COVERAGE_EXPORT_FAILED' }
$coverageFiles = @($summary.data[0].files | Where-Object { $_.filename -match '[\\/]firmware[\\/]xiao-nrf52840[\\/]src[\\/][^\\/]+\.h$' } | ForEach-Object {
        [pscustomobject]@{ File = 'src/' + [IO.Path]::GetFileName($_.filename); Lines = [int]$_.summary.lines.count; Covered = [int]$_.summary.lines.covered }
    })
foreach ($required in 'src/Controller.h', 'src/Pairing.h', 'src/Protocol.h') {
    if (@($coverageFiles | Where-Object File -EQ $required).Count -ne 1) { throw "FIRMWARE_COVERAGE_FILE_MISSING: $required" }
}
$totalLines = [int]($coverageFiles | Measure-Object Lines -Sum).Sum
$coveredLines = [int]($coverageFiles | Measure-Object Covered -Sum).Sum
$linePercent = [math]::Round(100.0 * $coveredLines / $totalLines, 2)
$coverageFiles | ForEach-Object { '{0}: {1}/{2} lines' -f $_.File, $_.Covered, $_.Lines }
"Line coverage (src/*.h): $coveredLines/$totalLines = $linePercent%"
if ($linePercent -lt 90) { throw "FIRMWARE_LINE_COVERAGE_BELOW_90: $linePercent" }
# Formatting is reported, not enforced, until the source is reformatted with the next firmware change.
$formatSummary = (& (Join-Path $PSScriptRoot 'Test-FirmwareFormat.ps1') | Select-Object -Last 1)
$formatSummary
if ("$formatSummary" -notmatch '^C\+\+ format: (Passed|NOT_CONFORMING) \((\d+)/(\d+) files') { throw 'FIRMWARE_FORMAT_CHECK_FAILED' }
$cppFormat = [pscustomobject]@{ Status = $Matches[1]; FilesNeedingFormat = [int]$Matches[2]; Files = [int]$Matches[3]; Enforced = $false; Tool = 'clang-format 23.1.2, firmware/xiao-nrf52840/.clang-format' }
[pscustomobject]@{
    Status = 'Passed'; Checks = $checks; ImageSafetyTests = 10; HardwareTested = $false; CppFormat = $cppFormat
    LineCoverage = [pscustomobject]@{ Percent = $linePercent; Covered = $coveredLines; Total = $totalLines; Threshold = 90; Scope = 'firmware/xiao-nrf52840/src/*.h (host-testable logic; .ino SDK boundary excluded)'; Tool = 'LLVM 23.1.2 clang/llvm-cov, C++14 instrumented build'; Files = $coverageFiles }
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'receipt.json') -Encoding utf8NoBOM
