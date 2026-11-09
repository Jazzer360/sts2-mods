# sts2-mods

Slay the Spire 2 mods. **Spire QoL** is display-only: it shows information the game already has, without changing gameplay.

## Installing

1. **Download** the latest `SpireQoL-<version>.zip` from the [Releases page](https://github.com/Jazzer360/sts2-mods/releases/latest). Use the zip under **Assets**, not "Source code".
2. **Open your game folder.** In Steam, right-click **Slay the Spire 2** → **Manage** → **Browse local files**.
3. **Make a `mods` folder** there if there isn't one already.
4. **Extract the zip into `mods`.** You should end up with:
   ```
   Slay the Spire 2/
     mods/
       SpireQoL/
         SpireQoL.dll
         SpireQoL.json
         SpireQoL.pdb
   ```
   If you get `mods/SpireQoL/SpireQoL/...` (one folder too deep), move the inner folder up a level.
5. **Launch the game.** The first time it finds a mod, it asks whether to load mods. Choose to load them. The game then closes; start it again and the mod is active.

The game keeps modded progress separate from your unmodded saves.

**Using it:** press **F10** (or use the **Spire QoL settings** button on the Mods screen or the pause menu) for the settings menu, where every feature, card tab and tracker row can be switched on or off. In combat, press **F8** to unlock the layout and drag indicators around (F8 again to lock it), and **F9** to hide or show all overlays. Every feature and setting is described in the [feature reference](docs/FEATURES.md). Settings live in `mods/SpireQoL/SpireQoL.settings.cfg`, which is created on first run; edit it while the game is closed.

**Updating:** download the new zip and extract it over the old one, replacing the files. Your settings file is kept.

**Uninstalling:** delete the `mods/SpireQoL` folder.

**Problems?** The game's log is at `%APPDATA%\SlayTheSpire2\logs\godot.log`; the mod's lines start with `[SpireQoL]`. Please include it when [opening an issue](https://github.com/Jazzer360/sts2-mods/issues). StS2 is in beta and patches often, so a game update can break the mod until a new release comes out.

**Error reporting:** Spire QoL sends a report when one of its features hits an error, or when its own checks find that a prediction didn't match what the game did (for example, a forecast that was off). Reports go to the mod's Sentry project and become GitHub issues. A report contains the mod and game versions, the error, and the combat at that moment: your character, HP, powers, relics, hand, the enemies, and recent combat events. It contains **no personal information**: no name, Steam ID, machine name or IP address. Each problem is sent at most once per session. To turn it off, set `"ReportErrors": false` in `mods/SpireQoL/SpireQoL.settings.cfg`.

---

## For developers

A development monorepo: every mod shares one build setup and a small `core` library, but each mod ships on its own.

## Mods

| Mod | Folder | What it does | Status |
| --- | --- | --- | --- |
| **Spire QoL** | [`mods/SpireQoL`](mods/SpireQoL) | Display-only QoL: on-card indicators for card modifiers, relic and power counters, relic reminders, incoming damage, shuffle warning. Each feature can be toggled, and indicators can be placed anywhere. | [Latest release](https://github.com/Jazzer360/sts2-mods/releases/latest) |

**Docs:** [Feature reference](docs/FEATURES.md) (everything the mods change) · [Tracker](docs/TRACKER.md) (work list) · [Changelog](CHANGELOG.md) (patch notes) · [Roadmap](docs/ROADMAP.md)

Future mods (Synergy Helper, Deck Metrics) give advice rather than plain QoL, so they will be separate mods. See [docs/ROADMAP.md](docs/ROADMAP.md).

## Layout

```
core/            shared code, compiled into every mod (no extra dependency for players)
mods/<ModId>/    one folder per shippable mod: <ModId>.csproj + <ModId>.json manifest
scripts/         release.ps1: build, package and publish releases
data/            (later) build-time data: tag extraction, curated YAML, patch diffs
docs/            roadmap and notes on how the game's mod loader works
```

## Building

Requirements: the .NET 9 SDK and a local install of Slay the Spire 2. The build references `sts2.dll`, `GodotSharp.dll` and `0Harmony.dll` directly from your game folder. No game files are committed.

```bash
dotnet build
```

Each build also copies the mod into `<game>/mods/<ModId>/`. To skip that step:

```bash
dotnet build -p:InstallToGame=false
```

If the game isn't installed at the default Steam path, copy `Sts2.local.props.example` to `Sts2.local.props` and set `Sts2Dir`.

## Releasing

**Every mod update gets a release.** After a change that bumps a mod's version is committed and pushed to `main`, run:

```powershell
./scripts/release.ps1
```

It needs the game installed (the build compiles against the game's assemblies) and the [GitHub CLI](https://cli.github.com/) logged in (`gh auth login`). For each mod whose current version has no release yet, it:

- checks that the working tree is clean and in sync with `origin/main`, and that the `.json` and `.csproj` versions match;
- builds Release and zips `dist/<ModId>-<version>.zip`, which holds a `<ModId>/` folder with the dll, pdb and manifest;
- creates the GitHub release `<modid>-v<version>` with that zip, using the version's `CHANGELOG.md` section (plus install steps) as the notes.

`./scripts/release.ps1 -DryRun` builds and zips without publishing. `-Mod SpireQoL` limits it to one mod.

**CI keeps this honest.** The [Release check](.github/workflows/release-check.yml) workflow runs on every push and pull request. It fails when:

- a mod's `.json` and `.csproj` versions differ, or `CHANGELOG.md` has no `### <Mod Name> <version>` section;
- mod code (`mods/<ModId>`, `core/`, build files) changed since the mod's last release but the version wasn't bumped;
- on `main`: the current version still has no GitHub release with its zip after 20 minutes. A push to `main` waits (polling every 30 seconds) for the release you publish right after pushing, so the normal flow stays green. Version and changelog problems fail immediately.

The mods can't be built in GitHub's cloud: the game's assemblies are never committed, and GitHub's runners don't have the game.

## Automatic bug reports

Crashes and audit mismatches from players' games arrive as GitHub issues labelled `auto-report`, mirrored hourly from the mod's Sentry project by the [Sentry issues](.github/workflows/sentry-issues.yml) workflow. It needs the repo secret `SENTRY_AUTH_TOKEN` and the variables `SENTRY_ORG` and `SENTRY_PROJECT`.

## Adding a new mod

1. Create `mods/<ModId>/<ModId>.csproj` with `<IsSts2Mod>true</IsSts2Mod>` and `<AssemblyName><ModId></AssemblyName>`.
2. Add `mods/<ModId>/<ModId>.json`. Its `id` must equal the assembly name.
3. Add a static class marked `[ModInitializer(nameof(Initialize))]`, then register features with `FeatureHost`.
4. Run `dotnet sln add mods/<ModId>/<ModId>.csproj`.
5. Add a `### <Mod Name> <version>` section to `CHANGELOG.md`, then release it with `./scripts/release.ps1`.

## License

[MIT](LICENSE). This project is not affiliated with Mega Crit. It contains no game code or assets.
