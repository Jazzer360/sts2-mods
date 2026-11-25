<#
.SYNOPSIS
  Reads a dev-mode performance trace (decision #256): who allocates and who uses the CPU, with stacks, and the collections.

.DESCRIPTION
  Runs tools/TraceReport on the newest trace in the dev folder's traces (or the one given), which writes beside it a text summary
  (also printed) and the JSON the dev dashboard's Traces page reads. A trace still being recorded can't be read: turn Record a
  performance trace off (or close the game) first; the newest finished one is used. docs/DEVELOPMENT.md "Performance traces".

.PARAMETER Trace
  A .nettrace to read; none: the newest one finished in the traces folder.

.PARAMETER Folder
  The dev folder (the setting DevFolder); none: %USERPROFILE%\SpireQoL-dev.

.PARAMETER All
  Every trace in the folder that has no report yet (the dashboard lists each one with a report).

.PARAMETER Speedscope
  Also write a Full trace's CPU samples for https://www.speedscope.app (trace-....speedscope.json).

.EXAMPLE
  ./scripts/trace-report.ps1
  ./scripts/trace-report.ps1 -All
  ./scripts/trace-report.ps1 -Trace "$HOME\SpireQoL-dev\traces\trace-20261124-101500-full.nettrace" -Speedscope
#>
param(
    [string]$Trace,
    [string]$Folder,
    [switch]$All,
    [switch]$Speedscope
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tools/TraceReport'

if (-not $Folder) { $Folder = Join-Path $HOME 'SpireQoL-dev' }
$traces = Join-Path ([Environment]::ExpandEnvironmentVariables($Folder)) 'traces'

function Test-Finished([string]$path) {
    # dotnet-trace holds the file while it records.
    try { $stream = [IO.File]::Open($path, 'Open', 'Read', 'Read'); $stream.Dispose(); return $true } catch { return $false }
}

$targets = @()
if ($Trace) { $targets = @($Trace) }
else {
    if (-not (Test-Path $traces)) { Write-Host "No traces in $traces (turn on Record a performance trace in the settings menu's General page, in dev mode)." -ForegroundColor Yellow; exit 1 }
    $finished = Get-ChildItem $traces -Filter 'trace-*.nettrace' | Sort-Object Name -Descending | Where-Object { Test-Finished $_.FullName }
    if ($All) { $targets = @($finished | Where-Object { -not (Test-Path ([IO.Path]::ChangeExtension($_.FullName, '.json'))) } | ForEach-Object { $_.FullName }) }
    elseif ($finished) { $targets = @($finished[0].FullName) }
    if ($targets.Count -eq 0) { Write-Host "No finished trace to read in $traces." -ForegroundColor Yellow; exit 1 }
}

Write-Host "Building tools/TraceReport..." -ForegroundColor Cyan
dotnet build $project -c Release -v quiet -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "tools/TraceReport didn't build (dotnet build tools/TraceReport for the errors; it needs NuGet once to restore)." -ForegroundColor Red; exit 1 }
$exe = Join-Path $project 'bin/Release/TraceReport.dll'

foreach ($target in $targets) {
    Write-Host "Reading $target" -ForegroundColor Cyan
    $arguments = @($exe, $target)
    if ($Speedscope) { $arguments += '--speedscope' }
    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { Write-Host "  couldn't read it" -ForegroundColor Red }
}
