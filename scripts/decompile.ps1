# Decompiles the installed game (read-only reference for writing patches and rules; never committed to
# this repo) into a local git repo with one commit per game version, so `git diff` there shows what a
# game patch changed. Two Steam branches are kept (decision #78), one git branch each:
#   beta (default): ~/git/sts2-decompiled, the version the mod is developed on.
#   main:           ~/git/sts2-decompiled-main (a worktree of the same repo), Steam's default branch.
# Switch the game's Steam branch first, then pass the matching -Branch.
# `git -C ~/git/sts2-decompiled diff main beta --stat` shows what differs between them.
#
# Usage: ./scripts/decompile.ps1 [-Branch beta|main] [-GameDir <Slay the Spire 2 folder>]
# Needs: ilspycmd (dotnet tool install -g ilspycmd --version 9.1.0.7988), git.
param(
    [ValidateSet("beta", "main")][string]$Branch = "beta",
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2",
    [string]$Repo = (Join-Path $HOME "git\sts2-decompiled")
)
$ErrorActionPreference = "Stop"
$Out = if ($Branch -eq "beta") { $Repo } else { "$Repo-main" }

$dll = Join-Path $GameDir "data_sts2_windows_x86_64\sts2.dll"
$info = Join-Path $GameDir "release_info.json"
if (-not (Test-Path $dll)) { throw "sts2.dll not found at $dll (pass -GameDir)" }
if (-not (Get-Command ilspycmd -ErrorAction SilentlyContinue)) { throw "ilspycmd not installed: dotnet tool install -g ilspycmd --version 9.1.0.7988" }

$release = Get-Content $info -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force $Repo | Out-Null
if (-not (Test-Path (Join-Path $Repo ".git"))) { git -C $Repo init -q -b beta }
git -C $Repo config core.longpaths true # some generated file names are very long
# Before two branches were kept, the one branch (beta) was called main.
$branches = @(git -C $Repo branch --format "%(refname:short)")
if ($branches -notcontains "beta" -and (git -C $Repo branch --show-current) -eq "main") {
    git -C $Repo branch -m main beta
    $branches = @(git -C $Repo branch --format "%(refname:short)")
}
if ($Branch -eq "main" -and -not (Test-Path (Join-Path $Out ".git"))) {
    if ($branches -contains "main") { git -C $Repo worktree add -q $Out main }
    else { git -C $Repo worktree add -q --orphan -b main $Out }
}
if ((git -C $Out branch --show-current) -ne $Branch) { throw "$Out is on branch $(git -C $Out branch --show-current), not $Branch" }

# Replace everything except the git history.
# (.NET with the \\?\ prefix: Remove-Item fails on the very long generated file names.)
foreach ($item in Get-ChildItem $Out -Force | Where-Object { $_.Name -ne ".git" }) {
    $long = "\\?\" + $item.FullName
    if ($item.PSIsContainer) { [System.IO.Directory]::Delete($long, $true) } else { [System.IO.File]::Delete($long) }
}
# ilspycmd 9.1 targets the .NET 8 runtime; let it run on a newer one (only .NET 9 is needed here).
$env:DOTNET_ROLL_FORWARD = "Major"
ilspycmd -p -o $Out $dll
if ($LASTEXITCODE -ne 0) { throw "ilspycmd failed" }

$decompiler = (dotnet tool list -g | Select-String "ilspycmd" | ForEach-Object { ($_ -split "\s+")[1] })
[ordered]@{
    version             = $release.version
    commit              = $release.commit
    date                = $release.date
    branch              = $release.branch
    main_assembly_hash  = $release.main_assembly_hash
    sts2_dll_sha256     = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLower()
    decompiler          = "ilspycmd $decompiler"
} | ConvertTo-Json | ForEach-Object {
    # UTF-8 without a BOM (Windows PowerShell 5's -Encoding utf8 writes one; JSON readers reject it).
    [System.IO.File]::WriteAllText((Join-Path $Out "VERSION.json"), $_, (New-Object System.Text.UTF8Encoding $false))
}

git -C $Out add -A
git -C $Out commit -q -m "sts2 $($release.version) ($($release.commit), $($release.date))"
"Decompiled sts2 $($release.version) ($Branch) to $Out"
