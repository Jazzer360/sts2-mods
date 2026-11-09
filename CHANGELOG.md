# Changelog

All notable changes to the mods in this repo. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow [SemVer](https://semver.org/). Each mod is versioned separately; entries are grouped under the mod they affect.

Every pull request or commit that changes behavior adds a line under **Unreleased**. When a mod is released, its Unreleased entries move under a version heading with the date and the game version it was tested on.

## [Unreleased]

### Spire QoL 0.23.3
#### Fixed
- Mummified Hand's tab only shows on a Power when another card in your hand still costs energy or stars. Before, every Power got it, even when there was nothing left to make free.

### Spire QoL 0.23.2
#### Added
- Settings menu: a **Reset defaults** button (click twice). It resets features, options, card tabs and tracker rows, and keeps hotkeys, widget positions and the error-reporting choice.
#### Changed
- Settings menu: the game's own cursor everywhere (no hand cursor on checkboxes and buttons), and the discard pile icon next to Shuffle Warning (also on the Card tabs page outside combat).

### Spire QoL 0.23.1
#### Removed
- The "can't be played" tab and card dimming (Chains of Binding, Ringing, Smog, Velvet Choker). The game already crosses out the cost of every such card, so the red slash now only ever means "playing this might be bad for you".

### Spire QoL 0.23.0
#### Added
- **Settings menu** (F10, or the "Spire QoL settings" button on the Mods screen and the pause menu): every feature, option, on-card tab and tracker row in one place. Relics and powers are listed with their game icon and name, what their card tab marks and shows, and what their tracker row counts, with a checkbox for each, All on / All off per column, search, and an "only what's in this fight" filter. Changes apply immediately, including turning whole features on and off. Esc closes it; game hotkeys are blocked while it's open.
#### Changed
- `Relics` now only controls the Relic Counters tray; relic card tabs have their own switches (`Tabs` `proc:<Relic>`). Relics you had hidden keep their tabs hidden (migrated once).
- Turn timers (Horn Cleat, Candelabra, Happy Flower...) are switched on per relic instead of all at once; `ShowTurnTimers` is migrated and removed.
- Error reporting can be switched on and off while the game runs.

### Spire QoL 0.22.1
#### Changed
- **Error reporting is live:** this build includes the Sentry DSN, so crashes and audit mismatches are reported (setting `ReportErrors`, on by default; see README "Error reporting").

### Spire QoL 0.22.0
#### Added
- **Error reporting** (`ReportErrors`, on by default): crashes and audit mismatches are sent to the mod's Sentry project with the game state at that moment (combat, player, powers, relics, enemies, hand with tabs and play facts, recent combat history and mod log). No personal data. Each problem is sent at most once per session. Uses the game's own Sentry library through a separate client, so the game's reporting is untouched. *This build has no Sentry DSN yet, so reporting stays off until a release that includes one.*

### Repo
#### Added
- "Sentry issues" workflow: mirrors new Sentry issues to GitHub issues labelled `auto-report` every hour, resolves Sentry issues when their GitHub issue is closed, and reopens on regressions.

### Spire QoL 0.21.0: review pass against the three pillars
#### Added
- **Incoming Damage counts your end-of-turn damage to enemies:** Hailstorm, The Bomb, Screaming Flagon, Stone Calendar and Charon's Ashes (and, with a single enemy, Lightning orbs, Parrying Shield, Forgotten Soul) land before enemies act, so an enemy they kill no longer counts as attacking.
- **Incoming Damage applies HP-loss effects:** Tungsten Rod, Beating Remnant and Buffer, in the game's order, on every hit that reaches your HP.
- **New tags for what your attack sets off on enemies:** Thorns on an enemy (damage to you), Curl Up, Personal Hive, Asleep / Slumber (crossed); Burrowed, Flutter, Plow, Shriek, The Gambit; killing an enemy (Gremlin Horn; crossed for Infested, Steam Eruption, Surprise, Crab Rage, Ravenous); breaking block (Hand Drill).
- **New tags for losing HP from a card:** Centennial Puzzle, Self-Forming Clay, Demon Tongue, Emotion Chip, Red Skull.
- **Replay tab:** replay on the card itself (Hidden Gem, Transfigure, Sword Sage, enchantments) shows as a text tab `2×` (total plays).
- **Sovereign Blade** shows the powers buffing it: Seeking Edge, Parry, Sword Sage, and Conqueror on an enemy.
- **Pael's Eye** is a crossed tab on every card while it's armed and you haven't played a card this turn.
- **Audits:**
  - The proc audit now does a budget check: draws, exhausts, discards, created cards, orbs, summons and powers beyond what the card and its tabs explain are logged as "unexplained" (missing tags). Shortfalls against our card model are logged as `[model]` lines.
  - Proc damage is checked against an exact range per tab (each target's own modifiers), for damage to enemies and to you.
  - The forecast audit checks end-of-turn block too, and splits the history by whose turn each entry happened on (Poison at the start of your next turn no longer counts).
  - Both audits log a one-line summary at the end of each combat (checked / matched).
- `ShowTurnTimers` setting (off by default).

#### Changed
- **Relic Reminders merged into Proc Indicators:** one rule table and one audit for every tag. Relic tab settings keys are now `proc:<Relic>`.
- **Relic Counters** shows counters you push by playing. Relics that only count turns (Horn Cleat, Candelabra, Captain's Wheel, Chandelier, Sparkling Rouge, Stone Calendar, Happy Flower, Pendulum, Pollinous Core) are opt-in (`ShowTurnTimers`).
- Burning Sticks' tag marks skills that exhaust when played (not Ethereal skills at end of turn), in line with "happens when you play it".

#### Removed
- The Goopy "added Exhaust" tab: the game already prints added keywords on the card.
- The double-play tab for replay enchantments (now the `2×` replay tab).
- Pael's Tears and Pael's Eye rows in the relic tray (they're crossed tabs on cards).

### Spire QoL 0.20.0
#### Fixed
- **Tags showing random enchantment icons with a number** (seen as a Slither or Sharp tag with a "2"). Icon textures we'd cached were freed by the game (its preload cache unloads between rooms), and setting one threw an error partway through laying out a tag. That left the tag showing the enchantment it was copied from, including its number. The 0.16.0 session hit this about 47,000 times. Cached icons are now checked and reloaded, and a tag whose layout fails is hidden instead of left half-drawn.
- A per-frame error can no longer flood the game log: it's logged in full once, then as a count.

#### Added
- **Incoming Damage shows what Osty absorbs** (Necrobinder): a "− N" row with Die For You's icon, red if the enemy turn kills Osty. Osty takes the part of enemy attacks that gets through your block, up to its HP; the overkill reaches you, so your own total is correct too.
- **Procs of procs:** block from any proc counts toward Juggernaut; Storm's lightning counts toward Metronome; The Sealed Throne's stars count toward Black Hole.
- New proc sources: **Metronome** (orbs channeled), **Bone Flute** (Osty attacks), **Rupture** (cards that cost HP), **Black Hole** on star gain.
- **Audit logging** (`[SpireQoL] [audit]` in godot.log), for finding bugs:
  - Forecast audit: logs when the HP you or Osty lost on the enemy turn differs from the forecast, with the breakdown and the game's damage entries.
  - Proc audit: logs when a card play sets off something with no tab, a relic tab doesn't fire, or proc block or damage differs from the tabs.

### Spire QoL 0.19.0: proc indicators
#### Added
- **Proc Indicators** (new feature, `proc-indicators`): every power or relic that playing a card would set off right now gets a tab on that card, with the source's icon. About 70 sources, from a sweep of the game code: your powers (Afterimage, Rage, Feel No Pain, Spirit of Ash, Sleight of Flesh, Juggernaut, Storm, Envenom, Devour Life...), your relics (Intimidating Helmet, Mini Regent, Daughter of the Wind, Lost Wisp, Tingsha, Charon's Ashes...), and enemy powers that punish your plays (Enrage, Sneaky, Imitation Learning, Skittish, Galvanic...). Full list in FEATURES.md.
  - **Numbers for block, damage and summon:** e.g. Feel No Pain on True Grit shows the block you'll gain. Block uses the game's own block math.
  - **Crossed** when playing the card helps the enemy or hurts you.
  - **One step removed:** cards that exhaust or discard other cards, draw, or create cards count those too (True Grit, Second Wind, Fiend Fire, Prepared, Storm of Steel...). Corruption counts as exhausting skills.
  - Counter relics (Kunai, Shuriken, Ornamental Fan...) show only on the card that completes the count.
  - Cards that can't be played (Unplayable statuses and curses, blocked cards) get no proc tabs; cards you can't afford yet still do.
- **Per-tab settings:** `Tabs` lists every on-card tab kind (added as each first appears), with group switches `relic:*`, `proc:*`, `double-play:*`. Plus `ProcNumbers` and `ProcIndirect`.
- Incoming Damage counts **Feel No Pain block from Ethereal cards** that exhaust at end of turn.

#### Changed
- When a card has more tabs than fit across it, tabs keep their size and only the gaps shrink (overlapping if needed), so the row never spills past the card. Before, the whole row shrank.
- Galvanized is now a crossed Galvanic proc tab showing the damage you take. Permafrost is a proc tab showing its block.
- `ShowDoublePlay` and `DimBlockedCards` became `Tabs` entries (`double-play:*`, `blocked`); saved values carry over.
- Burning Sticks' reminder also marks skills under Corruption.

#### Removed
- Innate / Eternal tabs and `ShowInnateEternalBadges`: neither can change mid-combat.
- Throwing Axe's relic reminder tab, which duplicated its double-play tab.

### Spire QoL 0.18.0
#### Changed
- **Double-play tabs show the icon of what's causing the extra play** instead of always Echo Form: Burst, Duplication, One-Two Punch, Signal Boost, Tag Team, Throwing Axe, or the card's replay enchantment. A card doubled by two sources gets one tab for each.
- The mod description in the game's mod list no longer mentions Block Forecast (removed) or Intent Forecast (not built yet).

### Spire QoL 0.17.0
#### Fixed
- A tag icon could show tinted green (seen as a green Retain icon on Sovereign Blade). When a reused card node had last shown a disabled enchantment, the tag copied the game's "disabled" setting that runs the icon through the tab's green colorize shader. Tags now keep their icons untinted and have their own copy of the backdrop shader, so a disabled enchantment on the card no longer greys them.
- Tags could linger on a reused card node if its overlay was recreated; the old tag layer is now freed.

### Spire QoL 0.16.0
#### Changed
- On-card tag backdrops slide down a bit (tucked about 17% under the card) while the icon stays where it was, evening out the gap above and below the icon.

#### Fixed
- Shuffle Warning missed draw cards that weren't on its list: **Prepared**, Huddle Up, Dagger Throw, Escape Plan, Pillage, Scrawl (draws to a full hand), Compile Driver (one per orb type), Restlessness (only card in hand) and Mad Science with Wisdom.

### Spire QoL 0.15.0
#### Changed
- On-card tags sit higher: tucked 25% under the card's top edge (was 40%).
- Tag icons are 85% size and centered in the part of the tag that sticks out above the card.
- Shuffle Warning uses the **discard pile** icon instead of the draw pile icon.

#### Verified in game
- Tags render beneath the card art and above the card glow (0.14.0).

### Spire QoL 0.14.0
#### Changed
- **On-card tags draw beneath the card art but above the card's glow,** on their own layer inserted right after the glow node. They're tucked about 40% under the card's top edge, so the card covers the tucked part and only the icon end sticks out.
- **Tags are icon-only.** No more play count on Echo Form tags (covers Throwing Axe and every other double-play source), `1` on this-turn-only Retain/Sly, or damage on Galvanized.

### Spire QoL 0.13.0
#### Changed
- **On-card tags** are rotated a quarter turn, so they hang up off the card's top edge like an enchantment tab hangs off the side. Icon and number stay upright. Only a sliver overlaps the frame (was 30%, now 15%), keeping clear of the card name.

#### Removed
- **Block Forecast** widget. Incoming Damage already shows end-of-turn block, and when nothing is incoming the block total doesn't matter. Its sources are listed under Incoming Damage now.

### Spire QoL 0.12.0: one visual language for everything on a card
#### Changed
- **Every on-card indicator is now an on-card tab:** a copy of the card's enchantment tab along its top edge, with a game icon, an optional number, and a red slash for "breaks / blocked by".
  - **Shuffle Warning:** a draw-pile tab on draw cards that would reshuffle (replaces `SHUFFLE`; `KEPT` removed).
  - **Relic Reminders:** relic tabs, crossed for Art of War / Ripple Basin / Pael's Tears (replaces the separate relic icons).
  - **Pen Nib ready:** a Pen Nib tab. The violet border is removed; the tray row still flashes.
  - **Blocked cards:** still dimmed. The reason is now a crossed tab with the blocking power's or relic's icon instead of text.
  - **Galvanized:** a Galvanic power tab with the damage.
  - **Added Exhaust:** a Goopy tab. **Innate / Eternal:** Royally Approved / Tezcatara's Ember tabs.
- If a card has many tabs, the row shrinks to fit the card's width.

#### Removed
- Text badges, the separate relic-icon row, the Pen Nib border, the `KEPT` marker, and the `ShuffleHandMarkers` setting.

### Spire QoL 0.11.0
#### Changed
- **Keyword tabs** (Retain, Sly, double play) moved from the card's left edge to a centered row along its **top edge**, overlapping slightly, so they're visible in a fanned hand without hovering. Text badges and relic icons now sit just above the tabs.

### Spire QoL 0.10.0
#### Changed
- **Double play** now shows as a native-style tab with the Echo Form power icon, with the play count in the tab's number slot. It replaces the `×N` badge and the echo ripples.

#### Removed
- Echo ripples (0.8.0). They didn't render in game; the card's glow node likely has no plain texture to copy.

#### Verified
- 0.8.0 session log: no errors.
- The HP bar node reports a 0×0 box at the bar's center, which confirms why the tray drew over the bar before 0.7.0. The measured container is 250px wide.

### Spire QoL 0.9.0
#### Changed
- **Retain and Sly** now show as native-style tabs on the card's left edge, replacing the `RET` / `SLY` text badges. Each tab is a copy of the card's own enchantment tab.
  - Retain uses the Steady enchantment icon; Sly uses the Master Planner power icon.
  - A `1` in the tab means this turn only.

### Spire QoL 0.8.0
#### Added
- **Double-play indicator:** cards that will play more than once get echo ripples, copies of the card's own glow outline expanding outward and fading, inspired by Echo Form's afterimages, plus a `×2` / `×3` badge.
  - The count comes from the game's own play-count calculation, so Echo Form, Burst, Duplication, One-Two Punch, Signal Boost, Throwing Axe, Tag Team (best case across enemies) and enchantment replays are all covered.
  - Setting: `ShowDoublePlay`.

### Spire QoL 0.7.0 (feedback from fifth test)
#### Added
- **Pin picker and Attach button** on every widget while the layout is unlocked (F8):
  - A 3×3 grid chooses which corner or edge of the widget stays fixed when it grows or shrinks.
  - The button switches between following your character and staying fixed on screen.
  - Ctrl+1–9 and middle-click remain as shortcuts.

#### Changed
- Dragging a widget keeps the pin you chose. It no longer silently re-anchors to the nearest screen point, which is why picking an anchor didn't seem to work.
- Relic Counters measures the HP bar's visible container (`%HpBarContainer`). The bar node's own box stopped short, so the tray drew over the bar. It also gets 12px of padding. The measured rects are logged once per combat, for checking.

#### Fixed
- Pale Blue Dot's label said "attacks"; it counts every card played (the game's property is misleadingly named `AttacksPlayedThisTurn`).

### Spire QoL 0.6.0 (feedback from fourth test)
#### Changed
- **Floating widgets draw behind your cards in hand.** During combat they're placed inside the game's combat UI, just before the hand. They move back on top while the layout is unlocked, so you can drag them.
- **Relic Counters tray** now attaches just right of your HP bar, with its bottom edge level with the bar's bottom, so it stays clear of enemies. Your saved position for it is reset once.
- **Ethereal translucency** is skipped for cards that are only Ethereal because of Hex (the act 3 elite's Hexed affliction). Default opacity raised from 70% to 80%; a saved 0.7 is raised to 0.8 once.

#### Added
- **Ctrl+1–9** over a widget while the layout is unlocked pins it to a chosen screen anchor (numpad layout), keeping its position.

#### Verified in game
- Pael's Tears tray row and card warnings. Floating widgets hide correctly under the pause menu and deck/pile screens. No errors in the 0.5.0 session log.

### Spire QoL 0.5.0 (feedback from third test)
#### Added
- **Countdown powers in the counter tray:** Orbit, Automation, Cacophony (across turns); Panache, Pale Blue Dot (per turn); and the enemy power Withering Presence (every 6 of your cards adds a Wither), in red.
- **Pael's Tears:**
  - The counter tray shows whether you still have energy to carry into next turn.
  - Cards that would spend your last energy (including X-cost cards) get a crossed-out Pael's Tears icon.

#### Changed
- Counters longer than 12 steps (Cacophony's 33) show only the countdown number, no bar.
- Incoming Damage's attack icon now matches the incoming total's tier, using the same thresholds as enemy intents.

#### Fixed
- Incoming Damage's attack icon showed the game's missing-texture placeholder. The plain `intent_attack.tres` isn't shipped; the game uses tiered `attack/intent_attack_N` icons.
- The counter tray and other floating widgets drew over the pause menu, pile views, deck view, rewards and the map. They now hide while any of those are open.

### Spire QoL 0.4.0 (feedback from second test)
#### Changed
- **Incoming Damage** is now a stacked sum with the game's attack/defend icons: incoming, minus end-of-turn block, equals damage taken.
- **Incoming Damage placement:** now just left of the block indicator on your HP bar. If that doesn't fit on screen, it goes below the HP bar and every row of power icons (measured from the icons, so wrapped rows count). It no longer covers your powers. Your saved position for it is reset once.
- **Incoming Damage accuracy:** the enemy turn is now simulated hit by hit.
  - Enemies that Poison will kill before they act are left out.
  - Retaliation (Thorns, Flame Barrier, Reflect) that kills an attacker mid multi-hit cancels its remaining hits.
  - Exoskeleton's Hard To Kill (per-hit cap) and Skulking Colony's Hardened Shell (per-turn total cap) are both handled.
  - Doom does not stop attacks; doomed enemies still attack before dying.
- **Block Forecast** no longer lists Horn Cleat / Captain's Wheel. Their block arrives at the start of your turn, after the enemy attacks.

#### Added
- Widgets can try several attach points in order, falling back when one doesn't fit on screen.

### Spire QoL 0.3.0 (feedback from first test)
#### Changed
- **Incoming Damage** is now one line, `Incoming N · Block N · Take N`, centered under the player's HP and power icons. It follows your character. "Take" includes unblockable HP loss. Your saved position for it is reset once to the new default.
- **Block Forecast** hides while Incoming Damage is showing, since that line already includes end-of-turn block.
- **Relic Counters:** one-shot relics no longer get a tray row; their icon on the card that uses them is enough. Pael's Eye keeps its row (it has no card to mark).
- **Card Indicators:** removed the `ETH` badge; translucency alone marks Ethereal cards.

#### Removed
- **Shuffle Warning** summary box. The `SHUFFLE` / `KEPT` card badges remain.

#### Added
- Widgets can attach to a game element. Middle-click (while the layout is unlocked) toggles attached vs. pinned to the screen.

#### Verified in game
- Permafrost reminder on powers; Infection damage (Phrog Parasite) counted in Incoming Damage. Mod loads cleanly, with no errors in the log.

### Spire QoL 0.2.0 (first in-game test build, game v0.111.0)
#### Added
- **Layout system:** F8 unlocks the layout. Then drag widgets to move them, scroll to scale them, right-click to reset; they snap to the nearest of nine screen anchors. F9 hides/shows all overlays. Positions are saved to `SpireQoL.settings.cfg`.
- **Settings file** `mods/SpireQoL/SpireQoL.settings.cfg`: feature toggles, per-relic toggles, hotkeys, Ethereal opacity, flash speed.
- **Relic Counters:** a tray of tick-mark bars for counter relics (Pen Nib, Nunchaku, Tuning Fork, Iron Club, Kunai, Shuriken, Stone Calendar and more), turn-trigger relics and one-shot relics. A row flashes when the relic is about to fire. Counters that carry across turns sort first.
- **Relic Reminders:** relic icons on cards in hand. Crossed out for Art of War / Ripple Basin on attacks. Plain for Vambrace, Throwing Axe, Burning Sticks, Unsettling Lamp, Permafrost and Music Box while unused. Pulsing violet border on attacks when Pen Nib is ready.
- **Block Forecast:** end-of-turn block from Plating, Frost orbs, Orichalcum, Cloak Clasp, Ripple Basin; Horn Cleat / Captain's Wheel shown the turn before.
- **Incoming Damage:** total blockable damage (enemy intents + Burn, Decay, Infection, Toxic, Wither, Constrict, Magic Bomb, Disintegration) vs. your block plus forecast block; unblockable HP loss (Regret, Bad Luck, Beckon, Demise) listed separately.
- **Card Indicators:** Ethereal cards are translucent; badges for ETH, RET / RET·1, SLY / SLY·1, +EXH (and optional INN / ETR); cards blocked by Bound, Ringing, Smog or Velvet Choker are dimmed with the reason; Galvanized cards show their self-damage.
- **Shuffle Warning:** draws left before a reshuffle; SHUFFLE badge on draw cards that would trigger it; KEPT badge on the other hand cards while a mid-turn shuffle is possible; draw potions that would shuffle are listed.

#### Changed
- Split the planned "Combat Math" into **Block Forecast** and **Incoming Damage**. "Keyword Badges" grew into **Card Indicators**.

#### Known gaps
- No in-game settings menu yet (edit the `.cfg` file). Intent Forecast not built. No health-bar damage preview. See "Not yet" under each feature in `docs/FEATURES.md`.

### Repo
#### Changed
- Release check: a push to `main` waits up to 20 minutes for the release instead of failing when the release is published a few seconds after the push.

#### Added
- `ResetModSettingsOnInstall` (set in `Sts2.local.props`): each dev install deletes the installed settings file, so a new build starts with everything shown.
- `scripts/release.ps1`: builds each mod, zips `dist/<ModId>-<version>.zip` and publishes it as a GitHub release, with the version's changelog section as notes.
- "Release check" GitHub workflow: fails when a mod's versions disagree, the changelog lacks the version, mod code changed since the last release without a version bump, or (on main) the current version has no release with its zip.
- README starts with player install instructions.
- Shared `core/`: settings file, overlay host (frame loop, hotkeys, per-feature error isolation), movable widgets, tick bar, game-state helpers.
- Monorepo build setup that references the game's assemblies from your install and copies built mods into `<game>/mods/`.
- Docs: feature reference (`docs/FEATURES.md`), work tracker (`docs/TRACKER.md`), roadmap, and notes on how the mod loader works.
