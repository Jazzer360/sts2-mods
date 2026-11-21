<#
.SYNOPSIS
  Everything a commit should pass, in one command: every project built with warnings as errors (the analyzers, the banned RNG, the
  style rules: decision #148), then the simulator tests and the catalog tests.

.DESCRIPTION
  The same bar the release holds (scripts/release.ps1 builds the same way), so a commit that passes this releases cleanly. Nothing is
  installed into the game. The catalog tests read the installed game; -NoGame skips them (the simulator tests need no game).

  Opt-in pre-push hook (runs this before every push): git config core.hooksPath .githooks

.EXAMPLE
  ./scripts/check.ps1
  ./scripts/check.ps1 -NoGame
#>
param(
    [switch]$NoGame
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$projects = @('mods/SpireQoL', 'mods/MainSaves', 'tests/SpireQoL.Sim.Tests')
if (-not $NoGame) { $projects += 'tests/SpireQoL.Catalog.Tests' }
$failed = @()

foreach ($project in $projects) {
    Write-Host "Building $project..." -ForegroundColor Cyan
    $output = dotnet build $project -warnaserror -p:InstallToGame=false -v quiet -nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $failed += "build: $project"
        $output | Select-String -Pattern ' error ' | ForEach-Object { $_.Line.Trim() } | Sort-Object -Unique | Select-Object -First 15 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    }
}

if ($failed.Count -eq 0) {
    $suites = @('tests/SpireQoL.Sim.Tests')
    if (-not $NoGame) { $suites += 'tests/SpireQoL.Catalog.Tests' }
    foreach ($suite in $suites) {
        Write-Host "Testing $suite..." -ForegroundColor Cyan
        $output = dotnet test $suite --no-build -nologo 2>&1
        $summary = $output | Select-String -Pattern 'Passed!|Failed!' | Select-Object -Last 1
        if ($summary) { Write-Host "  $($summary.Line.Trim())" }
        if ($LASTEXITCODE -ne 0) {
            $failed += "tests: $suite"
            $output | Select-String -Pattern '\[FAIL\]' | Select-Object -First 15 | ForEach-Object { Write-Host "  $($_.Line.Trim())" -ForegroundColor Red }
        }
    }
}

if ($failed.Count -gt 0) {
    Write-Host "Check failed: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host 'Check passed.' -ForegroundColor Green
