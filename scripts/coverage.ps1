<#
.SYNOPSIS
  How much of the outcome simulator its tests run (decision #151): the total, and the least-covered files, to find rules no test
  exercises yet.

.DESCRIPTION
  Runs the simulator tests with coverage (coverlet) over the simulator's own assembly (tests/SpireQoL.Simulator) and reads the report.
  Lines a test never runs are where a broken rule wouldn't fail anything. The report stays in dist/coverage (not committed).

.EXAMPLE
  ./scripts/coverage.ps1
  ./scripts/coverage.ps1 -Top 40
#>
param(
    [int]$Top = 25
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'dist/coverage'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

# The performance tests measure the simulator's work and time, which instrumentation inflates: they run in ./scripts/check.ps1.
$ErrorActionPreference = 'Continue'
$null = dotnet test (Join-Path $root 'tests/SpireQoL.Sim.Tests') --collect:"XPlat Code Coverage" --results-directory $out --filter 'FullyQualifiedName!~SimPerformanceTests' -nologo -v quiet 2>&1
$ErrorActionPreference = 'Stop'
if ($LASTEXITCODE -ne 0) { Write-Host 'The simulator tests failed; fix them first (./scripts/check.ps1).' -ForegroundColor Red; exit 1 }

$report = Get-ChildItem $out -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
if (-not $report) { Write-Host 'No coverage report was written.' -ForegroundColor Red; exit 1 }
[xml]$xml = Get-Content $report.FullName

# Lines per file, across the classes in it (partial classes, nested types, compiler-made ones).
$files = @{}
foreach ($class in $xml.coverage.packages.package.classes.class) {
    $file = $class.filename
    if ($file -like "*.g.cs") { continue } # generated (the game's names): nothing to test
    if (-not $files.ContainsKey($file)) { $files[$file] = @{ Lines = @{} } }
    foreach ($line in $class.lines.line) {
        $number = [int]$line.number
        $hit = [int]$line.hits -gt 0
        $files[$file].Lines[$number] = ($files[$file].Lines[$number] -or $hit)
    }
}

$rows = foreach ($file in $files.Keys) {
    $lines = $files[$file].Lines.Values
    $covered = ($lines | Where-Object { $_ }).Count
    [pscustomobject]@{
        File    = ($file -replace '.*[\\/]mods[\\/]SpireQoL[\\/]', '')
        Lines   = $lines.Count
        Missed  = $lines.Count - $covered
        Covered = if ($lines.Count -gt 0) { [math]::Round(100.0 * $covered / $lines.Count, 1) } else { 100 }
    }
}

$total = ($rows | Measure-Object -Property Lines -Sum).Sum
$missed = ($rows | Measure-Object -Property Missed -Sum).Sum
Write-Host ("Simulator: {0:N1}% of {1} lines run by a test ({2} never)." -f (100.0 * ($total - $missed) / [math]::Max(1, $total)), $total, $missed) -ForegroundColor Cyan
Write-Host "Most lines never run, by file:"
$rows | Where-Object { $_.Missed -gt 0 } | Sort-Object -Property Missed -Descending | Select-Object -First $Top |
    Format-Table -AutoSize File, Missed, Lines, @{ Label = 'Covered %'; Expression = { $_.Covered } }
Write-Host "Report: $($report.FullName)"
