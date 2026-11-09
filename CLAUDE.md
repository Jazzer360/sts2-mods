# sts2-mods

Monorepo of Slay the Spire 2 mods (C# net9.0, Harmony, Godot). Design notes: docs/ROADMAP.md. Loader facts: docs/MODDING_NOTES.md.

- Build: `dotnet build` (also installs to `<game>/mods/<ModId>/`; `-p:InstallToGame=false` to skip).
- Game assemblies are referenced from the install via Directory.Build.props/targets. Never commit game DLLs or decompiled code.
- Decompiled game source (read-only reference): `~/git/sts2-decompiled` (same path on every machine; local-only, never pushed; see its VERSION.json). Look up real class/method names there before writing a Harmony patch; don't guess targets.
- `core/` is compiled into each mod via a linked `<Compile Include>`; it is not a separate DLL.
- Mod AssemblyName == manifest `id` == `<ModId>.json` filename.
- Every feature implements `IFeature` and is applied through `FeatureHost`, so one broken patch can't crash the game.

## Logs first

Before working on in-game feedback, read the game logs, starting with the `[audit] combat summary` lines (`%APPDATA%\SlayTheSpire2\logs\godot*.log`, newest first): `[SpireQoL]` errors, and `[SpireQoL] [audit]` lines (forecast and proc mismatches). Fix or note what they show, then clear the old log files once they're addressed.

## Automatic bug reports

Crashes and audit mismatches from players' games become GitHub issues labelled `auto-report` (via Sentry and the Sentry issues workflow). When adding a feature that can fail or predict something, make sure failures reach `ErrorReporter` (the frame loop does this for exceptions) and that predictions are checked by an audit.

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

The "Release check" workflow waits up to 20 minutes after a push to `main` for the current version's release (with its zip), so release right after pushing. It fails immediately on a version mismatch, a missing changelog section, or mod code changed since the last release without a version bump. Don't leave it red: if you can't run the script (game running, no `gh`), tell the user the release is still pending.

## UI rules

- Never reuse the game's glow colors (NCardHighlight cyan/gold/red) or cost colors (blue/green/red).
- Never cover a card's cost, title, enchantment tab or affliction overlay.
- Every free-floating indicator is a movable widget (core layout system): draggable when unlocked, anchored, resettable.
- **Every on-card indicator is an on-card tab** (`CardDecor.AddTab`): a copy of the card's enchantment tab hanging off the card's top edge (beneath the card art, above its glow), with a game icon (relic/power/enchantment/pile) and an optional red slash. One tab per source. Every tab answers "does something happen the moment I play this card, right now?" (exceptions: the Retain tab and Ethereal translucency). No numbers, except the amount on proc tabs that gain block, deal damage or summon. No text, except the replay tab (`2×`, total plays), which has no power or relic icon to show. Don't tag what the card text already says (added keywords, afflictions), except Retain/Sly and replay. Every tab kind has its own `Tabs` setting key, on by default. No text badges, borders or one-off visuals. The only exceptions are dimming (unplayable) and Ethereal translucency. Icons come from the game's own art (`GameIcons`). Document new tabs in the table in docs/FEATURES.md "On-card tabs".
- **Plain vs crossed is the core of the design language.** A plain tab means "playing this does something extra" (a proc, a replay, a reshuffle). A crossed tab (red slash) means "playing this might be bad for you": it helps the enemy (Enrage, Curl Up, waking Asleep), hurts you (Thorns, Galvanic, a Dazed into your pile), or gives up a relic's bonus (Art of War, Pael's Tears). It's a warning, not a verdict, so it's set whenever the effect is against you, even if the play is often still right. Enemy powers that help you (Burrowed, Flutter, The Gambit) get a plain tab. The one other use of the slash is the "Can't be played" tab (the blocker's icon, crossed, on a dimmed card).
- **Every switchable thing is in the settings menu.** A new proc rule, double-play source, card tab or tracker entry gets a row in `mods/SpireQoL/Menu/SettingsCatalog.cs` (what its tab marks and shows, what its tracker row counts). A new setting gets a control in `Menu/SettingsMenu.cs`.
