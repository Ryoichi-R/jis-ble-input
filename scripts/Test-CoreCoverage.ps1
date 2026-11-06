#requires -Version 7.4
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$tool = Join-Path $projectRoot '_internal/tools/coverage/dotnet-coverage.exe'
if (-not (Test-Path -LiteralPath $tool)) { throw 'COVERAGE_TOOL_MISSING: install dotnet-coverage 17.14.1 in _internal/tools/coverage.' }
if ((& $tool --version) -notmatch '^17\.14\.1(?:\+|$)') { throw 'COVERAGE_TOOL_VERSION_MISMATCH' }
$run = Join-Path $projectRoot ('_internal/build-cache/coverage/' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $run
$settings = Join-Path $PSScriptRoot 'core-coverage.xml'
$project = Join-Path $projectRoot 'tests/JisBleInput.Core.Tests/JisBleInput.Core.Tests.csproj'
Push-Location $projectRoot
try {
    & dotnet build $project -c Coverage --no-restore -p:DebugType=portable -p:DebugSymbols=true -p:Optimize=false -m:1 *> (Join-Path $run 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'COVERAGE_BUILD_FAILED' }
    $testDll = Join-Path $projectRoot '_internal/build-cache/bin/JisBleInput.Core.Tests/Coverage/net10.0/JisBleInput.Core.Tests.dll'
    $report = Join-Path $run 'cobertura.xml'
    & $tool collect --settings $settings --output-format cobertura --output $report dotnet $testDll *> (Join-Path $run 'tests.log')
    $exitCode = $LASTEXITCODE
    $testOutput = [IO.File]::ReadAllText((Join-Path $run 'tests.log'))
    if ($exitCode -ne 0 -or $testOutput -notmatch '(?m)^Passed: ([1-9][0-9]*)\s*$') { throw 'COVERAGE_TEST_FAILED' }
    $checks = [int]$Matches[1]
    [xml]$coverage = [IO.File]::ReadAllText($report)
    $covered = [int]$coverage.coverage.'lines-covered'
    $total = [int]$coverage.coverage.'lines-valid'
    $packages = @($coverage.coverage.packages.package)
    if ($total -lt 1 -or $packages.Count -ne 1 -or $packages[0].name -ne 'JisBleInput.Core') { throw 'COVERAGE_SCOPE_INVALID' }
    $percent = [math]::Round(100.0 * $covered / $total, 2)
    $status = if ($percent -ge 90) { 'Passed' } else { 'Failed' }
    [pscustomobject]@{
        Status = $status; Checks = $checks; Tool = 'dotnet-coverage 17.14.1'
        Scope = 'JisBleInput.Core.dll (all production Core code; WPF/diagnostic/SDK boundary excluded)'
        Covered = $covered; Total = $total; Percent = $percent; Threshold = 90
        ReportSha256 = (Get-FileHash -LiteralPath $report).Hash
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'receipt.json') -Encoding utf8NoBOM
    Write-Output "Core coverage: $covered/$total = $percent%; $checks checks; receipt: $run/receipt.json"
    if ($status -ne 'Passed') { throw 'CORE_LINE_COVERAGE_BELOW_90' }
} finally { Pop-Location }
