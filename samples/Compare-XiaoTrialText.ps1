#requires -Version 7.4
[CmdletBinding()]
param(
    [string]$ExpectedPath = (Join-Path $PSScriptRoot 'xiao-japanese-trial.txt'),
    [string]$ReceivedPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-ComparableText([string]$Path) {
    $file = Get-Item -LiteralPath $Path
    $text = [IO.File]::ReadAllText($file.FullName, [Text.UTF8Encoding]::new($false, $true))
    return $text.Replace("`r`n", "`n").Replace("`r", "`n")
}

function Get-TextHash([string]$Text) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text)))
}

function Get-UnitAt([string]$Text, [int]$Index) {
    if ($Index -ge $Text.Length) { return 'EOF' }
    return 'U+{0:X4}' -f [int]$Text[$Index]
}

$expected = Read-ComparableText $ExpectedPath
$result = [ordered]@{ Result = 'EXPECTED'; ExpectedUnits = $expected.Length; ExpectedSha256 = Get-TextHash $expected }
if (!$ReceivedPath) { [pscustomobject]$result; return }

$received = Read-ComparableText $ReceivedPath
$result.ReceivedUnits = $received.Length
$result.ReceivedSha256 = Get-TextHash $received
if ([string]::Equals($expected, $received, [StringComparison]::Ordinal)) {
    $result.Result = 'MATCH'
    [pscustomobject]$result
    return
}

$index = 0
$commonLength = [Math]::Min($expected.Length, $received.Length)
while ($index -lt $commonLength -and [int]$expected[$index] -eq [int]$received[$index]) { $index++ }
$lines = $expected.Substring(0, $index).Split("`n")
$result.Result = 'MISMATCH'
$result.Line = $lines.Length
$result.Column = $lines[-1].Length + 1
$result.Expected = Get-UnitAt $expected $index
$result.Received = Get-UnitAt $received $index
[pscustomobject]$result
exit 1
