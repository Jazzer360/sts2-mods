# Spire QoL site (gh-pages branch)

This branch holds the Spire QoL website and the tooling that screenshots it from the real game. It shares no history with `main`; the mods themselves live there.

```
site/
  features.json     every feature: its text and which screenshots show it
  build.mjs         builds site/dist (Node 18+, no dependencies)
  serve.mjs         serves site/dist on http://localhost:8123
  style.css
  shots/            screenshots + shots.json, written by capture.ps1 (committed)
capture/
  capture.ps1       launches the game, runs the scenes, commits the screenshots
  capture-settings.json  Spire QoL settings used while capturing
  scenes/*.scene    one example per feature
  SpireQoLCapture/  the capture helper mod (builds against your installed game)
.github/workflows/pages.yml   builds and publishes the site
```

## What the site shows

- **Features** (`index.html`): every feature in `site/features.json`, with its screenshots. The build checks this list against the `##` sections of `docs/FEATURES.md` on `main` and warns about any feature the site doesn't cover (and any it names that the reference no longer has).
- **Install** (`install.html`): install, update and uninstall steps, Main Saves, multiplayer, error reporting, and a download button for the newest release's zip.
- **Changelog** (`changelog.html`): every `### Spire QoL x.y.z` section of `CHANGELOG.md` on `main`, newest first, with each release's date and link, and a search box.

The changelog and the feature reference are read from `main` when the site builds, and releases from GitHub's API. The download buttons also ask GitHub for the newest release when the page loads, so they stay current between builds. The changelog updates the next time the site builds: after a capture, or by hand (`gh workflow run pages.yml --ref gh-pages`).

## Taking screenshots

Needs Windows, the game installed, Spire QoL installed in `<game>/mods/SpireQoL`, the .NET 9 SDK, and git with push access. Close the game first.

```powershell
./capture/capture.ps1                    # every scene, then commit and push
./capture/capture.ps1 -Only lethal,shop  # just these scenes (others keep their screenshots)
./capture/capture.ps1 -NoCommit          # look before committing
./capture/capture.ps1 -GameDir "D:\Games\Slay the Spire 2"
```

**Don't touch the mouse or keyboard while it runs.** Hovering an intent and opening the settings menu are real input to the game window. The game closes by itself when it's done (or after 20 minutes).

What it does:

1. Builds `capture/SpireQoLCapture` against your installed game.
2. Copies the helper into `<game>/mods/SpireQoLCapture`, and swaps Spire QoL's `SpireQoL.settings.cfg` for `capture-settings.json` (defaults, error reporting off), so the screenshots show the defaults and capture fights never send reports. Both are put back when it finishes, even after an error. If the script itself is killed partway, the next run stops and tells you where your settings backup is.
3. Starts `SlayTheSpire2.exe --force-steam=off`. Without Steam the game keeps its saves under `%APPDATA%\SlayTheSpire2\default\`, a separate tree from your Steam profile (`...\steam\<id>\`), and nothing syncs to Steam Cloud. The capture profile unlocks everything there; your own progress is untouched. Every scene starts a run with saving off, so no run is saved and nothing reaches run history, progress, achievements or stats. The game's log for the capture goes to `capture/out/godot.log`, not your normal logs.
4. The helper runs each scene and saves the full frame (`capture/out/full/`) and the crop as WebP, at most 1600 px wide (`capture/out/<name>.webp`), logging to `capture/out/capture.log`, then quits the game.
5. Copies the crops and `shots.json` (shot sizes, captions, mod and game versions) into `site/shots`, commits "Screenshots: Spire QoL x on game y" and pushes. The push publishes the site.

A failed scene is listed in red at the end with its line and reason; the others still update.

### Writing a scene

A scene is a text file in `capture/scenes`, run in file-name order, each in a new run. `#` starts a comment.

```
title Proc Indicators          # the heading it belongs to (shown in results)
section In fights
character Ironclad             # Ironclad, Silent, Defect, Regent, Necrobinder
relic add LETTER_OPENER        # anything else is a dev console command
fight SLIMES_NORMAL 5          # an encounter, with a fixed seed for its first moves
wait combat-started
power FEEL_NO_PAIN_POWER 3 0   # power <id> <amount> <target>: 0 is you, 1.. the enemies
hand STRIKE_IRONCLAD TRUE_GRIT DEFEND_IRONCLAD POMMEL_STRIKE+ SHRUG_IT_OFF
wait 1.5
caption Each tab is a power or relic that playing the card sets off right now.
shot proc-tabs hand pad 30
```

Ids are the game's model ids (class names in SCREAMING_SNAKE_CASE: `TrueGrit` is `TRUE_GRIT`). Keep examples realistic: a character's starter cards mixed with the cards that show the feature, set up with real plays over a turn or two, against enemies that really have the mechanic (the Spiny Toad's thorns, the Louse Progenitor's Curl Up).

Directives (everything else goes to the game's dev console as the local player: `fight`, `relic add`, `power`, `potion`, `energy`, `block`, `damage`, `enchant <id> <amount> <hand index>`, `event`, `room Shop|RestSite|...`, `kill`, `heal`...):

| Directive | Does |
| --- | --- |
| `title`, `section`, `character`, `seed` | scene settings, anywhere in the file |
| `no-run` | don't start a run (main menu shots) |
| `wait combat-started` / `turn-started` / `room` / `<seconds>` | waits for the game |
| `hand A B C...` | replaces your hand with these cards, left to right; `NAME+` is upgraded |
| `add <Pile> A B...` | adds cards to Draw, Discard, Exhaust, Hand or Deck |
| `clear-hand`, `clear-pile <Pile>`, `move-pile <From> <To>` | empties or moves a pile |
| `hp player <n>`, `hp enemy <i> <n>` | sets HP |
| `play <CARD_ID> [enemy N]` | plays that card from your hand the way the game does when you drop it (energy, targeting, every hook), and waits for it to resolve. Set up states this way so counters, powers and orbs come from real plays |
| `endturn` | ends your turn (follow with `wait turn-started`) |
| `card-reward [A B C]` | opens a card reward with these cards (or three random ones) |
| `hover <region>`, `click <region>`, `key <Godot key name>` | real input: `hover intent 1`, `click text Guide`, `key F10` |
| `close-modals` | closes popups |
| `caption <text>` | the caption for the next shot |
| `shot <name> [region] [+ region...] [pad N] [below N]` | saves a screenshot cropped around the regions (the whole frame when none); pad (all sides) and below (extra at the bottom) are pixels at 1080p |

Regions come from the game's own nodes, so crops hold at any resolution: `full`, `hand`, `card N`, `enemy N`, `enemies`, `intent N`, `player`, `widget <Spire QoL feature id>` (`incoming-damage`, `intent-forecast`, `relic-counters`, `rest-hp`, `run-reminders`), `node <node name>`, `text "<label>"`, and `rect x y w h` (fractions of the window).

A new feature on the site: add it to `site/features.json` (with the `docs/FEATURES.md` heading it covers), write its scene, and run `capture.ps1 -Only <scene>`.

## Building the site locally

```powershell
node site/build.mjs     # reads CHANGELOG.md and docs/FEATURES.md from main on GitHub
node site/serve.mjs     # http://localhost:8123
```

With a local checkout of `main` instead: set `CHANGELOG_PATH` and `FEATURES_PATH` to its files. `SITE_OFFLINE=1` skips the network (no release dates or download link).

## Publishing

`.github/workflows/pages.yml` builds and deploys on every push to this branch that touches `site/`, and by hand (`gh workflow run pages.yml --ref gh-pages`). One-time repository setup:

1. **Settings → Pages → Build and deployment → Source: GitHub Actions.**
2. **Settings → Environments → github-pages → Deployment branches:** add `gh-pages` (by default only the default branch may deploy).
