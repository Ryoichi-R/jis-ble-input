#requires -Version 7.4
# Checks XIAO firmware C++ formatting with the pinned clang-format and firmware/xiao-nrf52840/.clang-format.
# Default: report only (Status NOT_CONFORMING is never reported as Passed). -Enforce fails on any gap.
# -Fix rewrites the files; it changes the firmware source hash, so rebuild and create a new candidate afterwards.
[CmdletBinding()]
param([switch]$Enforce, [switch]$Fix)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$firmware = Join-Path $projectRoot 'firmware/xiao-nrf52840'
. (Join-Path $PSScriptRoot 'Get-FirmwareHostTools.ps1')
$clangFormat = Join-Path $projectRoot ('_internal/build-cache/firmware/tools/' + (Get-FirmwareHostTools).Folder + '/bin/clang-format.exe')
if (-not (Test-Path -LiteralPath $clangFormat)) { throw 'CLANG_FORMAT_MISSING: run scripts/Prepare-FirmwareTools.ps1 (LLVM 23.1.2 Windows ARM64).' }
if (-not (Test-Path -LiteralPath (Join-Path $firmware '.clang-format'))) { throw 'CLANG_FORMAT_CONFIG_MISSING' }
$files = @(Get-ChildItem -LiteralPath $firmware -Recurse -File | Where-Object Extension -In '.h', '.ino', '.cpp' | Sort-Object FullName)
if ($files.Count -eq 0) { throw 'CLANG_FORMAT_NO_FILES' }
$results = foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($projectRoot, $file.FullName).Replace('\', '/')
    # Pass paths directly (no PowerShell pipe) so line endings and encoding are not rewritten.
    # .ino has no registered extension; clang-format then defaults to C++, which is correct here.
    if ($Fix) {
        & $clangFormat --style=file -i $file.FullName
        if ($LASTEXITCODE -ne 0) { throw "CLANG_FORMAT_FAILED: $relative" }
    }
    $output = @(& $clangFormat --style=file --dry-run $file.FullName 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "CLANG_FORMAT_CHECK_FAILED: $relative" }
    $diagnostics = @($output | Where-Object { "$_" -match 'warning: code should be clang-formatted' })
    [pscustomobject]@{ File = $relative; Replacements = $diagnostics.Count }
}
$nonConforming = @($results | Where-Object Replacements -GT 0)
$results | ForEach-Object { '{0}: {1} replacement(s)' -f $_.File, $_.Replacements }
$status = if ($nonConforming.Count -eq 0) { 'Passed' } else { 'NOT_CONFORMING' }
"C++ format: $status ($($nonConforming.Count)/$($results.Count) files need formatting; clang-format 23.1.2)"
if ($Enforce -and $nonConforming.Count -gt 0) { throw 'FIRMWARE_CPP_FORMAT_FAILED' }
