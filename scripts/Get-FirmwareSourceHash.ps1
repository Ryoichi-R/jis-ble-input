#requires -Version 7.4
Set-StrictMode -Version Latest
function Get-FirmwareSourceHash([string]$ProjectRoot) {
    $paths = @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'firmware/xiao-nrf52840') -Recurse -File | Where-Object Extension -In '.h', '.ino')
    $paths += Get-Item -LiteralPath (Join-Path $ProjectRoot 'firmware/xiao-nrf52840/toolchain.json')
    $binding = @($paths | Sort-Object FullName | ForEach-Object { [IO.Path]::GetRelativePath($ProjectRoot, $_.FullName).Replace('\', '/') + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash }) -join "`n"
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($binding)))
}
