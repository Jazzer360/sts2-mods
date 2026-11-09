# sts2-mods

Monorepo of Slay the Spire 2 mods (C# net9.0, Harmony, Godot). Design notes: docs/ROADMAP.md. Loader facts: docs/MODDING_NOTES.md.

- Build: `dotnet build` (also installs to `<game>/mods/<ModId>/`; `-p:InstallToGame=false` to skip).
- Game assemblies are referenced from the install via Directory.Build.props/targets. Never commit game DLLs or decompiled code.
- Decompiled game source (read-only reference): `~/git/sts2-decompiled` (same path on every machine; local-only, never pushed; see its VERSION.json). Look up real class/method names there before writing a Harmony patch; don't guess targets.
- `core/` is compiled into each mod via a linked `<Compile Include>`; it is not a separate DLL.
- Mod AssemblyName == manifest `id` == `<ModId>.json` filename.
- Every feature implements `IFeature` and is applied through `FeatureHost`, so one broken patch can't crash the game.

## Documentation rules (required with every change)

Any change to mod behavior updates all three in the same commit:
1. `docs/FEATURES.md`: what the player sees, the settings, the game UI touched, and **every Harmony patch target** (Class.Method). It must stay a complete list of everything the mod changes.
2. `CHANGELOG.md`: a line under **Unreleased** for the affected mod (Added / Changed / Fixed / Removed).
3. `docs/TRACKER.md`: check off finished items, and add newly found relics, modifiers or sources.

## Releasing (required with every mod update)

Every change to a mod ships as a release:
1. Bump the version in **both** `mods/<ModId>/<ModId>.json` and `<ModId>.csproj`, and put the changelog lines under a `### <Mod Name> <version>` heading.
2. Commit and push to `main`.
3. Run `./scripts/release.ps1` (needs the game installed and `gh` logged in). It builds, zips `dist/<ModId>-<version>.zip` and creates the GitHub release `<modid>-v<version>`.

The "Release check" workflow fails on `main` until the current version has a release with its zip, and on any change to mod code since the last release without a version bump. Don't leave it red: if you can't run the script (game running, no `gh`), tell the user the release is still pending.

## UI rules

- Never reuse the game's glow colors (NCardHighlight cyan/gold/red) or cost colors (blue/green/red).
- Never cover a card's cost, title, enchantment tab or affliction overlay.
- Every free-floating indicator is a movable widget (core layout system): draggable when unlocked, anchored, resettable.
- **Every on-card indicator is an on-card tab** (`CardDecor.AddTab`): a copy of the card's enchantment tab hanging off the card's top edge (beneath the card art, above its glow), with a game icon (relic/power/enchantment/pile) and an optional red slash ("this breaks/blocks that"). One tab per source. No numbers, except on quantified proc tabs (a power/relic proc that gains block, deals damage or grants a stat: the source's icon plus the amount; docs/TRACKER.md §9). Every tab kind gets its own setting, on by default. No text badges, borders or one-off visuals. The only exceptions are dimming (unplayable) and Ethereal translucency. Icons come from the game's own art (`GameIcons`). Document new tabs in the table in docs/FEATURES.md "On-card tabs".
