<#
.SYNOPSIS
  A wider code review than the build's analyzers (decision #152): JetBrains InspectCode over the solution, summarized by kind of
  finding and by file. Run it now and then (after a big change), not on every commit: it's advice, not a gate.

.DESCRIPTION
  Finds what the build's rules don't: redundant code (casts, conditions always true, unused assignments), unused public members,
  possible null dereferences, simpler forms. Uses the repo's pinned InspectCode (.config/dotnet-tools.json; `dotnet tool restore`
  once) and writes the full report to dist/inspect/inspect.sarif. For unused public and internal members by name, see
  scripts/dead-code.mjs too.

.EXAMPLE
  ./scripts/inspect.ps1
  ./scripts/inspect.ps1 -Top 40
#>
param(
    [int]$Top = 25
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
dotnet tool restore | Out-Null
$out = Join-Path $root 'dist/inspect'
New-Item -ItemType Directory -Force $out | Out-Null
$report = Join-Path $out 'inspect.sarif'

# InspectCode picks an older MSBuild (Visual Studio's) unless the SDK's is named, and then finds no files.
$sdk = (dotnet --list-sdks | Select-Object -Last 1) -replace '^(\S+) \[(.+)\]$', '$2\$1'
$msbuild = Join-Path $sdk 'MSBuild.dll'
if (-not (Test-Path $msbuild)) { Write-Host "MSBuild not found at $msbuild" -ForegroundColor Red; exit 1 }
if (Test-Path $report) { Remove-Item $report }

Write-Host 'Inspecting the solution (a few minutes)...' -ForegroundColor Cyan
$ErrorActionPreference = 'Continue'
$null = dotnet jb inspectcode sts2-mods.sln --output="$report" --format=Sarif --severity=WARNING --no-build `
    "--toolset-path=$msbuild" "--exclude=**/GameNames.g.cs" 2>&1
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $report)) { Write-Host 'InspectCode wrote no report (build the solution first: ./scripts/check.ps1).' -ForegroundColor Red; exit 1 }

$sarif = Get-Content $report -Raw | ConvertFrom-Json
$results = @($sarif.runs[0].results)
Write-Host ("{0} findings." -f $results.Count) -ForegroundColor Cyan
Write-Host 'By kind:'
$results | Group-Object ruleId | Sort-Object Count -Descending | Select-Object -First $Top |
    Format-Table -AutoSize Count, @{ Label = 'Kind'; Expression = { $_.Name } }
Write-Host 'By file:'
$results | Group-Object { $_.locations[0].physicalLocation.artifactLocation.uri } | Sort-Object Count -Descending | Select-Object -First 15 |
    Format-Table -AutoSize Count, @{ Label = 'File'; Expression = { $_.Name } }
Write-Host "Report: $report"
