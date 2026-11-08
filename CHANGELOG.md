# Changelog

All notable changes to the mods in this repo. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow [SemVer](https://semver.org/). Each mod is versioned separately; entries are grouped under the mod they affect.

Every pull request or commit that changes behavior adds a line under **Unreleased**. When a mod is released, its Unreleased entries move under a version heading with the date and the game version it was tested on.

## [Unreleased]

### Spire QoL
#### Added
- Mod scaffold: entry point and per-feature isolation, so a feature that fails to load turns itself off and the others keep working.
- Feature stubs (no visible effect yet): Card Indicators, Relic Counters, Relic Reminders, Block Forecast, Incoming Damage, Shuffle Warning, Intent Forecast.

#### Planned
- One-shot relic reminders: Vambrace, Throwing Axe, Burning Sticks, Unsettling Lamp, Permafrost, Pael's Eye (once per combat); Music Box, Rainbow Ring (once per turn).

#### Changed
- Split the planned "Combat Math" into **Block Forecast** and **Incoming Damage**. "Keyword Badges" grew into **Card Indicators** (adds Ethereal translucency and modifier markers).

### Repo
#### Added
- Monorepo build setup that references the game's assemblies from your install and copies built mods into `<game>/mods/`.
- Docs: feature reference (`docs/FEATURES.md`), work tracker (`docs/TRACKER.md`), roadmap, and notes on how the mod loader works.
