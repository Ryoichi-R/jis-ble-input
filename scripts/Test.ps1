#requires -Version 7.4
[CmdletBinding()]
param([switch]$NoRestore, [switch]$Package)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspaceRoot = $projectRoot
$runDirectory = [IO.Path]::GetFullPath((Join-Path $projectRoot "_internal/build-cache/validation/$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))-$([guid]::NewGuid().ToString('N'))"))
if (-not $runDirectory.StartsWith($workspaceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to write outside workspaceRoot.'
}
$null = New-Item -ItemType Directory -Path $runDirectory
$results = [Collections.Generic.List[object]]::new()
$status = 'Failed'

function Invoke-DotnetCheck {
    param([string]$Name, [string[]]$Arguments, [switch]$RequireChecks)
    $log = Join-Path $runDirectory "$Name.log"
    & dotnet @Arguments *> $log
    $code = $LASTEXITCODE
    $count = $null
    if ($RequireChecks) {
        $match = [regex]::Match([IO.File]::ReadAllText($log), '(?m)^Passed: (\d+)\s*$')
        if ($match.Success) { $count = [int]$match.Groups[1].Value }
    }
    $results.Add([pscustomobject]@{ Name = $Name; ExitCode = $code; PassedChecks = $count; Log = $log })
    if ($code -ne 0 -or ($RequireChecks -and (-not $count -or $count -lt 1))) {
        Get-Content -LiteralPath $log -Tail 20 | Write-Host
        throw "CHECK_FAILED: $Name (exit $code)"
    }
    Write-Host "$Name : PASS $(if ($RequireChecks) { "($count checks)" })"
}

Push-Location $workspaceRoot
try {
    & (Join-Path $PSScriptRoot 'Test-XiaoContract.ps1')
    $projects = [ordered]@{
        core = Join-Path $projectRoot 'tests/JisBleInput.Core.Tests/JisBleInput.Core.Tests.csproj'
        diagnostic = Join-Path $projectRoot 'preflight/SenderDiagnostic.Tests/SenderDiagnostic.Tests.csproj'
        ui = Join-Path $projectRoot 'tests/JisBleInput.App.Tests/JisBleInput.App.Tests.csproj'
    }
    foreach ($name in $projects.Keys) {
        if (-not $NoRestore) {
            $restoreArguments = @('restore', $projects[$name], '--locked-mode', '--configfile', (Join-Path $projectRoot 'NuGet.Config'))
            $restoreArguments += @('--packages', (Join-Path $projectRoot '_internal/build-cache/nuget-packages'))
            Invoke-DotnetCheck "$name-restore" $restoreArguments
        }
        Invoke-DotnetCheck "$name-build" @('build', $projects[$name], '-c', 'Release', '--no-restore', '-m:1')
        # These are console/WPF check programs, not VSTest projects. Reject a
        # successful process that did not report any executed checks.
        Invoke-DotnetCheck "$name-tests" @('run', '--project', $projects[$name], '-c', 'Release', '--no-build', '--no-restore') -RequireChecks
    }
    if ($Package) {
        Import-Module Pester -MinimumVersion 5.7.1
        $configuration = New-PesterConfiguration
        $configuration.Run.Path = Join-Path $projectRoot 'tests/PublishLayout.Tests.ps1'
        $configuration.Run.PassThru = $true
        $configuration.TestRegistry.Enabled = $false
        $configuration.TestDrive.Enabled = $false
        $packageResult = Invoke-Pester -Configuration $configuration
        $results.Add([pscustomobject]@{ Name = 'package-tests'; PassedChecks = $packageResult.PassedCount; FailedChecks = $packageResult.FailedCount })
        if ($packageResult.PassedCount -lt 1 -or $packageResult.FailedCount -gt 0 -or $packageResult.FailedContainersCount -gt 0) {
            throw 'CHECK_FAILED: package-tests'
        }
    }
    $status = 'Passed'
}
finally {
    Pop-Location
    [pscustomobject]@{ Status = $status; HardwareInputTested = $false; Checks = @($results.ToArray()) } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runDirectory 'receipt.json') -Encoding utf8NoBOM
    Write-Host "Receipt: $(Join-Path $runDirectory 'receipt.json')"
}
