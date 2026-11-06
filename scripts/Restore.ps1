#requires -Version 7.4
[CmdletBinding()]
param([switch]$Online)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packages = Join-Path $projectRoot '_internal/build-cache/nuget-packages'
$projects = @(
    'tests/JisBleInput.Core.Tests/JisBleInput.Core.Tests.csproj',
    'preflight/SenderDiagnostic.Tests/SenderDiagnostic.Tests.csproj',
    'tests/JisBleInput.App.Tests/JisBleInput.App.Tests.csproj'
)
Push-Location $projectRoot
try {
    foreach ($relative in $projects) {
        $restoreArguments = @('restore', (Join-Path $projectRoot $relative), '--locked-mode', '--packages', $packages, '--configfile', (Join-Path $projectRoot 'NuGet.Config'))
        if ($Online) { $restoreArguments += @('--source', 'https://api.nuget.org/v3/index.json') }
        & dotnet @restoreArguments
        if ($LASTEXITCODE -ne 0) { throw "RESTORE_FAILED: $relative" }
    }
} finally { Pop-Location }
