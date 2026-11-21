<#
.SYNOPSIS
  Mutation testing of the outcome simulator (Stryker.NET, decision #151): changes the simulator's code one small edit at a time (a
  < for a <=, a + for a -, a removed line) and runs the tests against each. An edit no test notices (a surviving mutant) is a rule
  the tests don't hold.

.DESCRIPTION
  Slow: the whole simulator takes a long while, so run it on the files you're working on (-Mutate), or overnight. Uses the repo's
  pinned Stryker (.config/dotnet-tools.json, `dotnet tool restore` once) and the settings in tests/SpireQoL.Sim.Tests/stryker-config.json
  (the performance tests are left out: mutants change the work they count). The report is dist/stryker/reports/mutation-report.html.

.EXAMPLE
  ./scripts/mutation.ps1 -Mutate '**/Sim/SimChance.cs'
  ./scripts/mutation.ps1 -Mutate '**/Sim/Models/BlockModels.cs'
  ./scripts/mutation.ps1          # every simulator file: hours
#>
param(
    [string]$Mutate
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
dotnet tool restore | Out-Null

# Stryker's project analysis needs MSBuild named: the SDK this repo builds with.
$sdk = (dotnet --list-sdks | Select-Object -Last 1) -replace '^(\S+) \[(.+)\]$', '$2\$1'
$msbuild = Join-Path $sdk 'MSBuild.dll'
if (-not (Test-Path $msbuild)) { Write-Host "MSBuild not found at $msbuild" -ForegroundColor Red; exit 1 }

$arguments = @('tool', 'run', 'dotnet-stryker', '--msbuild-path', $msbuild, '--output', (Join-Path $root 'dist/stryker'))
if ($Mutate) { $arguments += @('--mutate', $Mutate) }
Push-Location (Join-Path $root 'tests/SpireQoL.Sim.Tests')
try { & dotnet @arguments } finally { Pop-Location }
