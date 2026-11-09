# sts2-mods

A development monorepo for my Slay the Spire 2 mods. Everything shares one build setup and a small `core` library. Each mod still ships on its own.

> Work in progress. Nothing here is release-ready yet. StS2 is in beta and patches often, so expect things to break.

## Mods

| Mod | Folder | What it does | Status |
| --- | --- | --- | --- |
| **Spire QoL** | [`mods/SpireQoL`](mods/SpireQoL) | Display-only QoL: card modifier indicators, relic counters and reminders, block forecast, incoming damage, shuffle warning, intent forecast. Each feature can be toggled, and indicators can be placed anywhere. | 0.13.0 test build |

**Docs:** [Feature reference](docs/FEATURES.md) (everything the mods change) · [Tracker](docs/TRACKER.md) (work list) · [Changelog](CHANGELOG.md) (patch notes) · [Roadmap](docs/ROADMAP.md)

Future mods (Synergy Helper, Deck Metrics) give advice rather than plain QoL, so they will be separate mods. See [docs/ROADMAP.md](docs/ROADMAP.md).

## Layout

```
core/            shared code, compiled into every mod (no extra dependency for players)
mods/<ModId>/    one folder per shippable mod: <ModId>.csproj + <ModId>.json manifest
data/            (later) build-time data: tag extraction, curated YAML, patch diffs
docs/            roadmap and notes on how the game's mod loader works
tools/           (later) helper scripts
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

## Adding a new mod

1. Create `mods/<ModId>/<ModId>.csproj` with `<IsSts2Mod>true</IsSts2Mod>` and `<AssemblyName><ModId></AssemblyName>`.
2. Add `mods/<ModId>/<ModId>.json`. Its `id` must equal the assembly name.
3. Add a static class marked `[ModInitializer(nameof(Initialize))]`, then register features with `FeatureHost`.
4. Run `dotnet sln add mods/<ModId>/<ModId>.csproj`.

## License

[MIT](LICENSE). This project is not affiliated with Mega Crit. It contains no game code or assets.
