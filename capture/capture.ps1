<#
.SYNOPSIS
  Takes fresh Spire QoL screenshots from the real game and commits them to the gh-pages branch.

.DESCRIPTION
  1. Builds the capture helper mod (capture/SpireQoLCapture) against your installed game.
  2. Installs it into <game>/mods for this run only, and swaps Spire QoL's settings file for capture
     settings (defaults, error reporting off). Both are put back afterwards, even if something fails.
  3. Launches the game without Steam (--force-steam=off): its saves then live under
     %APPDATA%\SlayTheSpire2\default\, apart from your Steam profile, and nothing syncs to Steam Cloud.
     Every scene runs in a new run that is never saved, so no run history, progress or achievements.
  4. The helper plays each scene in capture/scenes, saves the screenshots, and quits the game.
  5. Copies the cropped screenshots into site/shots, writes site/shots/shots.json, commits, and pushes
     (the Pages workflow then publishes the site).

  Don't touch the mouse or keyboard while it runs: hovers and clicks are real input to the game window.

.PARAMETER Only
  Comma-separated parts of scene names to run (e.g. "lethal,intent"). Other screenshots are kept.

.PARAMETER NoCommit
  Copy the screenshots into site/shots but don't commit or push.

.PARAMETER NoPush
  Commit but don't push.

.PARAMETER GameDir
  The Slay the Spire 2 install folder.
#>
param(
    [string]$Only,
    [switch]$NoCommit,
    [switch]$NoPush,
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2",
    [int]$TimeoutMinutes = 20
)

$ErrorActionPreference = "Stop"
$captureDir = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $captureDir "..")).Path
$scenesDir = Join-Path $captureDir "scenes"
$outDir = Join-Path $captureDir "out"
$shotsDir = Join-Path $repoRoot "site\shots"
$modsDir = Join-Path $GameDir "mods"
$helperInstall = Join-Path $modsDir "SpireQoLCapture"
$qolDir = Join-Path $modsDir "SpireQoL"
$qolSettings = Join-Path $qolDir "SpireQoL.settings.cfg"
$settingsBackup = Join-Path $env:TEMP "SpireQoL.settings.cfg.capture-backup"
$exe = Join-Path $GameDir "SlayTheSpire2.exe"

function Fail($msg) { Write-Host "capture: $msg" -ForegroundColor Red; exit 1 }

if (-not (Test-Path $exe)) { Fail "no game at '$GameDir' (pass -GameDir)" }
if (-not (Test-Path (Join-Path $qolDir "SpireQoL.dll"))) { Fail "Spire QoL isn't installed in '$qolDir'" }
if (Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue) { Fail "the game is running; close it first" }
if (Test-Path $settingsBackup) { Fail "a settings backup from an earlier capture is still at '$settingsBackup': copy it back to '$qolSettings' (or delete it), then run again" }

$branch = (git -C $repoRoot rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne "gh-pages") { Fail "run this from the gh-pages branch (this checkout is on '$branch')" }

# --- build the helper ------------------------------------------------------------------------------
Write-Host "Building the capture helper..."
dotnet build (Join-Path $captureDir "SpireQoLCapture\SpireQoLCapture.csproj") -c Release -nologo -v q "-p:Sts2Dir=$GameDir"
if ($LASTEXITCODE -ne 0) { Fail "helper build failed" }
$helperBin = Join-Path $captureDir "SpireQoLCapture\bin\Release"

$qolVersion = (Get-Content (Join-Path $qolDir "SpireQoL.json") -Raw | ConvertFrom-Json).version
$gameVersion = ""
$releaseInfo = Join-Path $GameDir "release_info.json"
if (Test-Path $releaseInfo) { $gameVersion = (Get-Content $releaseInfo -Raw | ConvertFrom-Json).version }
Write-Host "Spire QoL $qolVersion, game $gameVersion"

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force $outDir | Out-Null

$hadSettings = Test-Path $qolSettings
$exitCode = -1
try {
    # --- install the helper and capture settings (put back in finally) ------------------------------
    New-Item -ItemType Directory -Force $helperInstall | Out-Null
    Copy-Item (Join-Path $helperBin "SpireQoLCapture.dll"), (Join-Path $helperBin "SpireQoLCapture.json") $helperInstall -Force
    if ($hadSettings) { Move-Item $qolSettings $settingsBackup -Force }
    Copy-Item (Join-Path $captureDir "capture-settings.json") $qolSettings -Force

    # --- run the game -------------------------------------------------------------------------------
    $gameArgs = @(
        "--force-steam=off",
        "--windowed", "--resolution", "1920x1080",
        "--log-file", "`"$(Join-Path $outDir 'godot.log')`"",
        "`"--spireqol-capture=$scenesDir`"",
        "`"--spireqol-capture-out=$outDir`""
    )
    if ($Only) { $gameArgs += "`"--spireqol-capture-only=$Only`"" }
    Write-Host "Launching the game (separate saves, no Steam). Hands off the mouse and keyboard until it closes..."
    $proc = Start-Process -FilePath $exe -ArgumentList $gameArgs -WorkingDirectory $GameDir -PassThru
    if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        Write-Host "capture: timed out after $TimeoutMinutes minutes; closing the game" -ForegroundColor Yellow
        $proc.Kill()
        $proc.WaitForExit()
    }
    $exitCode = $proc.ExitCode
}
finally {
    if (Test-Path $helperInstall) { Remove-Item $helperInstall -Recurse -Force }
    if (Test-Path $qolSettings) { Remove-Item $qolSettings -Force }
    if ($hadSettings) { Move-Item $settingsBackup $qolSettings -Force }
    Write-Host "Restored Spire QoL's settings and removed the capture helper."
}

# --- collect --------------------------------------------------------------------------------------
$resultsPath = Join-Path $outDir "results.json"
if (-not (Test-Path $resultsPath)) { Fail "the game exited ($exitCode) without results; see $outDir\capture.log and $outDir\godot.log" }
$results = Get-Content $resultsPath -Raw | ConvertFrom-Json
foreach ($s in $results.scenes) {
    if ($s.ok) { Write-Host ("  ok      {0}" -f $s.name) -ForegroundColor Green }
    else { Write-Host ("  FAILED  {0}: {1}" -f $s.name, $s.error) -ForegroundColor Red }
}

New-Item -ItemType Directory -Force $shotsDir | Out-Null
$manifestPath = Join-Path $shotsDir "shots.json"
$existing = @()
if ($Only -and (Test-Path $manifestPath)) {
    $old = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $newNames = @($results.shots | ForEach-Object { $_.name })
    $existing = @($old.shots | Where-Object { $newNames -notcontains $_.name })
}
foreach ($shot in $results.shots) {
    Copy-Item (Join-Path $outDir "$($shot.name).png") (Join-Path $shotsDir "$($shot.name).png") -Force
}
$manifest = [ordered]@{
    modVersion  = $qolVersion
    gameVersion = $gameVersion
    captured    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")
    shots       = @($existing) + @($results.shots)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding utf8
Write-Host "$(@($results.shots).Count) screenshots in site/shots."

if ($NoCommit) { exit 0 }
git -C $repoRoot add site/shots
git -C $repoRoot diff --cached --quiet
if ($LASTEXITCODE -eq 0) { Write-Host "No screenshot changes to commit."; exit 0 }
git -C $repoRoot commit -m "Screenshots: Spire QoL $qolVersion on game $gameVersion"
if ($NoPush) { exit 0 }
git -C $repoRoot push origin gh-pages
