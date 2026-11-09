<#
.SYNOPSIS
  Builds each mod, packages it as a zip players can extract into <game>/mods, and publishes it
  as a GitHub release. Run it after every mod update that's committed and pushed to main.

.DESCRIPTION
  The game's assemblies can't be committed or downloaded in CI, so releases are built here, on a
  machine with the game installed. CI (.github/workflows/release-check.yml) then fails until each
  mod's current version has a release with its zip, so an unreleased update can't go unnoticed.

  For each mod in mods/ (or just the ones named with -Mod):
    1. Reads the version from <ModId>.json and checks <ModId>.csproj matches.
    2. Skips it if release "<modid>-v<version>" already exists.
    3. Takes the release notes from the "### <Mod Name> <version>" section of CHANGELOG.md.
    4. Builds Release (without installing to the game) and zips dist/<ModId>-<version>.zip,
       holding a <ModId>/ folder with the dll, pdb and manifest.
    5. Creates the GitHub release with the zip attached (tag on the current main commit).

.PARAMETER Mod
  Mod ids to release (default: every mod under mods/).

.PARAMETER DryRun
  Build and zip, but don't publish. Skips the clean-tree and pushed-to-main checks.

.EXAMPLE
  ./scripts/release.ps1
.EXAMPLE
  ./scripts/release.ps1 -Mod SpireQoL -DryRun
#>
[CmdletBinding()]
param(
    [string[]]$Mod,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Fail([string]$msg) { Write-Host "error: $msg" -ForegroundColor Red; exit 1 }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail 'dotnet SDK not found.' }
if (-not $DryRun -and -not (Get-Command gh -ErrorAction SilentlyContinue)) { Fail 'GitHub CLI (gh) not found. Install it and run "gh auth login".' }

if (-not $DryRun) {
    if (git status --porcelain) { Fail 'Working tree has uncommitted changes. Commit (with docs + changelog) first.' }
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    if ($branch -ne 'main') { Fail "Releases are made from main (on '$branch')." }
    git fetch --quiet origin main
    if ((git rev-parse HEAD).Trim() -ne (git rev-parse origin/main).Trim()) { Fail 'main is not in sync with origin/main. Push (or pull) first.' }
}

$changelog = Get-Content -Raw -Encoding UTF8 CHANGELOG.md
$mods = Get-ChildItem mods -Directory | Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).json") }
if ($Mod) { $mods = $mods | Where-Object { $Mod -contains $_.Name } }
if (-not $mods) { Fail 'No matching mods found.' }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$released = 0

foreach ($m in $mods) {
    $id = $m.Name
    $manifest = Get-Content -Raw (Join-Path $m.FullName "$id.json") | ConvertFrom-Json
    $version = $manifest.version
    $name = $manifest.name
    $csproj = [xml](Get-Content -Raw (Join-Path $m.FullName "$id.csproj"))
    $projVersion = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
    if ($projVersion -ne $version) { Fail "$id.csproj Version ($projVersion) doesn't match $id.json version ($version)." }

    $tag = "$($id.ToLower())-v$version"
    if (-not $DryRun) {
        # "release not found" goes to stderr, which Windows PowerShell turns into a terminating
        # error under ErrorActionPreference=Stop; relax it just for this probe.
        & { $ErrorActionPreference = 'Continue'; gh release view $tag 2>&1 | Out-Null }
        if ($LASTEXITCODE -eq 0) { Write-Host "$name $version is already released ($tag), skipping."; continue }
    }

    # Release notes: the "### <name> <version>" section, up to the next ### or ## heading.
    $pattern = '(?ms)^### ' + [regex]::Escape("$name $version") + '[^\r\n]*\r?\n(.*?)(?=^##(#)? |\z)'
    $match = [regex]::Match($changelog, $pattern)
    if (-not $match.Success) { Fail "CHANGELOG.md has no '### $name $version' section." }
    $notes = $match.Groups[1].Value.Trim()

    Write-Host "Building $name $version..." -ForegroundColor Cyan
    dotnet build (Join-Path $m.FullName "$id.csproj") -c Release -p:InstallToGame=false -v quiet -nologo
    if ($LASTEXITCODE -ne 0) { Fail "Build failed for $id." }

    $out = Join-Path $m.FullName 'bin\Release'
    $stage = Join-Path $dist "stage\$id"
    if (Test-Path (Join-Path $dist 'stage')) { Remove-Item -Recurse -Force (Join-Path $dist 'stage') }
    New-Item -ItemType Directory -Force $stage | Out-Null
    foreach ($f in @("$id.dll", "$id.pdb", "$id.json", "$id.pck")) {
        $src = Join-Path $out $f
        if (Test-Path $src) { Copy-Item $src $stage }
    }
    if (-not (Test-Path (Join-Path $stage "$id.dll"))) { Fail "Build output has no $id.dll." }

    $zip = Join-Path $dist "$id-$version.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    # Entries are named "<ModId>/<file>" by hand: Compress-Archive and ZipFile.CreateFromDirectory
    # both write backslash paths under Windows PowerShell, which other platforms' unzip tools mishandle.
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $stage -File) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, "$id/$($file.Name)", [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $archive.Dispose() }
    Remove-Item -Recurse -Force (Join-Path $dist 'stage')
    Write-Host "Packaged $zip"

    if ($DryRun) { Write-Host "Dry run: not publishing $tag. Release notes would be:`n$notes"; continue }

    $install = @"
**Install:** download ``$id-$version.zip`` below and extract it into your game's ``mods`` folder, so you end up with ``Slay the Spire 2/mods/$id/$id.dll``. Full steps: [README](https://github.com/Jazzer360/sts2-mods#installing).

"@
    $notesFile = Join-Path $dist "$id-$version-notes.md"
    Set-Content -Encoding UTF8 $notesFile ($install + $notes)
    gh release create $tag $zip --title "$name $version" --notes-file $notesFile --target (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { Fail "gh release create failed for $tag." }
    Remove-Item $notesFile
    Write-Host "Released $name $version ($tag)." -ForegroundColor Green
    $released++
}

if (-not $DryRun -and $released -eq 0) { Write-Host 'Nothing to release: every mod version already has a release.' }
