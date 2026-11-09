# Spire QoL: feature reference

This is the complete list of every change Spire QoL makes to the game. If a change isn't here, the mod doesn't make it. Each feature lists what you see, its settings, which game UI it touches, and the game methods it patches.

**Status key:** 🟢 shipped · 🟡 built, being tested in game · ⚪ planned

Spire QoL is **display-only**. It never changes game state, damage, cards or saves. Its manifest sets `"affects_gameplay": false`, so you can join multiplayer lobbies whose players don't have it. See [MODDING_NOTES.md](MODDING_NOTES.md).

**Harmony patches: none.** Every feature works by reading live game state once per frame and drawing its own nodes. Nothing in the game's code is replaced or changed.

| Feature | Id | Status | Default |
| --- | --- | --- | --- |
| [Settings and layout](#settings-and-layout) | (core) | 🟡 | always on |
| [Card Indicators](#card-indicators) | `card-indicators` | 🟡 | on |
| [Relic Counters](#relic-counters) | `relic-counters` | 🟡 | on |
| [Incoming Damage](#incoming-damage) | `incoming-damage` | 🟡 | on |
| [Proc Indicators](#proc-indicators) | `proc-indicators` | 🟡 | on |
| [Shuffle Warning](#shuffle-warning) | `shuffle-warning` | 🟡 | on |
| [Intent Forecast](#intent-forecast) | `intent-forecast` | ⚪ | on (does nothing yet) |

## What the mod adds to the game, technically

- **One CanvasLayer** (`SpireQoL_Overlay`, layer 10) under the scene tree root, holding a toast message and a `SpireQoL_Widgets` container for the floating widgets. **During live combat with no screen open, that container is moved inside the game's `NCombatUi`, directly before the `%Hand` node, so your cards draw over the widgets.** It moves back to our layer when combat ends, when any screen opens over combat, and while the layout is unlocked (so the hand can't swallow drag clicks). Widgets attached to a game element read that element's screen position each frame; they never modify it.
- **Floating widgets hide while a game screen covers combat:** the pause menu, draw/discard/exhaust pile and deck views (`NCapstoneContainer.InUse`), card rewards and card-choice screens (`NOverlayStack.ScreenCount > 0`), and the map (`NMapScreen.IsOpen`). Card decorations live on the card nodes in your hand, so they already sit underneath those screens.
- **A second CanvasLayer** (`SpireQoL_Settings`, layer 100) for the settings menu, hidden until opened. While the menu is open it registers itself with the game's `NHotkeyManager` as a blocking screen (`AddBlockingScreen` / `RemoveBlockingScreen`, the game's own mechanism for popups), so game hotkeys do nothing, and pushes its own Esc / back binding (`ui_cancel`, `mega_pause_and_back`, `mega_back`) to close it; both are removed when it closes.
- **Two "Spire QoL settings" buttons** (plain Godot `Button`s) added as children of game nodes while those screens are open: the Mods screen's info panel (`NModdingScreen` → `%ModInfoContainer`, only while Spire QoL is the selected mod) and the pause menu (`NPauseMenu`, bottom-left). Found each frame from `NGame.Instance.MainMenu.SubmenuStack.Peek()` and `NCapstoneContainer.Instance.CurrentCapstoneScreen`.
- **One `SceneTree.ProcessFrame` callback.** It runs each feature, then draws card decorations.
- **A child node `SpireQoL_CardOverlay`** under `%CardContainer` of each card node in your hand that has something to show. While a decorated card is in your hand, Spire QoL changes that container's `Modulate` alpha (Ethereal translucency). The alpha is restored as soon as the node stops showing that card. The game reuses card nodes, so this matters.
- **A subscription to `CombatManager.CombatSetUp`,** to know the current combat.
- **One file**, `mods/SpireQoL/SpireQoL.settings.cfg` (JSON). Never written to game saves. New on-card tab kinds are added to it as they first appear (saved at most every 3 seconds).
- **Two audits write to the game log** (`godot.log`, lines start with `[SpireQoL] [audit]`) when a prediction doesn't match what happened, to surface bugs:
  - **Forecast audit:** the Incoming Damage forecast from the last moment of your turn, against the HP you and Osty actually lost by your next turn (from the game's combat history). A mismatch logs the forecast breakdown and every damage and block entry.
  - **Forecast audit** also checks the end-of-turn block it predicted, and splits the history by whose turn each entry happened on, so Poison ticking at the start of your next turn isn't counted.
  - **Proc audit:** the tabs on a card just before you played it, against what happened until your next card play. It logs: a relic or power that fired with no tab; a relic tab whose relic didn't fire; proc block that differs (exact); proc damage outside the tabs' predicted range (to enemies and to you); and a **budget** check: draws, exhausts, discards, created cards, orbs, summons and powers applied beyond what the card itself does plus what its tabs can add are logged as "unexplained" (a missing tag). Less than the card's own is a `[model]` line (our model of the card is off, or a pile ran out).
  - **Summaries:** at the end of each combat, one line per audit: `card plays checked, N matched (issue counts)` and `enemy turns forecast, N matched`.
  - Both only read the combat history and subscribe to relics' and powers' `Flashed` events. They're the features `forecast-audit` and `proc-audit`, on by default.
- **Error reporting** (setting `ReportErrors`, on): crashes caught anywhere in the mod (feature errors, frame errors, tag layout, play facts) and every audit mismatch are sent to the mod's own Sentry project. Each report carries the game state from `Diagnostics/Snapshot.cs`: combat, player, powers, relics, orbs, enemies with intents, the hand with each card's tabs and play facts, the last 40 combat history entries, the audit window, and the last 80 mod log lines. It uses the game's own Sentry .NET (`Sentry.dll`) through a separate `SentryClient` with our DSN. `SentrySdk.Init` is never called, so the game's own reporting is untouched and no global handlers are installed. There's no machine name, user or personal data. Each fingerprint is sent once per session, at most 40 events. Mismatch fingerprints leave out amounts, so the same bug groups into one issue. An hourly GitHub Action mirrors new Sentry issues to GitHub issues (`auto-report`).
- **Error logging is capped:** a failure that repeats every frame is logged in full once, then as a count every ~600 repeats.
- **No Harmony patches.** Everything is read from live game state each frame; the game's hook queries it calls (`Hook.ModifyCardPlayCount`, `ModifyBlock`, `ModifyDamage`, `ShouldEtherealTrigger`, `ModifyOrbPassiveTriggerCount`) are pure queries with no side effects.

---

## Settings and layout

**What you see:** a settings menu, floating indicators you can place yourself, and hotkeys.

### Settings menu

Opens with **F10** (setting `SettingsKey`), the **Spire QoL settings** button on the Mods screen (Settings → Mod Settings → select Spire QoL) or the same button on the pause menu. Every change applies right away and is saved. Esc or Close closes it; game hotkeys are blocked while it's open.

- **Pages:** General (features and options), Card tabs, Relics, Your powers, Enemy powers.
- **General:** each feature on/off, live (a feature switched off is skipped each frame and its widgets hide; no restart): Card Indicators, Proc Indicators, Relic Counters, Incoming Damage, Shuffle Warning, and the two prediction checks. Options: amounts on proc tabs (`ProcNumbers`), indirect procs (`ProcIndirect`), tracker flashes early (`PulseOneStepEarly`), send error reports (`ReportErrors`, live), and sliders for Ethereal opacity and tracker flash speed.
- **Card tabs:** Retain, Sly, Replay, Can't be played, Reshuffle, each with its icon, plus the family switches (all proc tabs, all double-play tabs). Tab kinds seen in play that the catalog doesn't describe yet are listed under **Other**, so everything stays switchable.
- **Relics / Your powers / Enemy powers:** a table, one row per relic or power the mod knows: the game's icon (with the red slash if its tab is crossed) and name (hover for the game's own description), then two checkboxes: **On cards** (its tab, with what it marks and shows) and **Tracker** (its Relic Counters row, with what it counts). Each column has All on / All off for the rows listed. Rows come from `Menu/SettingsCatalog.cs`, which covers every Proc Indicators rule, every double-play source and every tracker entry.
- **Search** filters every page by name or description; **Only what's in this fight** (in combat) lists only relics and powers you or the enemies have right now.

### Layout and hotkeys

- **F8: unlock/lock layout.** When unlocked, every floating indicator shows with an orange outline, even if it has nothing to show right now, plus a header with:
  - **A 3×3 pin picker.** It chooses which corner or edge of the widget stays fixed when the widget grows or shrinks. With the bottom-left pin, for example, extra counter rows grow the widget upward and to the right. Picking a pin never moves the widget. The orange square is the current pin.
  - **An Attach button** (widgets that can follow a game element: Incoming Damage, Relic Counters). When attached, the widget follows your character. Otherwise it stays fixed on screen.
- While unlocked:
  - **Drag** to move a widget. It keeps your chosen pin; there's no automatic re-anchoring.
  - **Mouse wheel** over it to scale it (0.4×–3×).
  - **Right-click** to reset it to its default place, pin and size.
  - **Middle-click** is the same as the Attach button.
  - **Ctrl+1–9** while hovering a widget is a shortcut for the pin picker, laid out like a numpad (7 8 9 top, 4 5 6 middle, 1 2 3 bottom). Ctrl avoids the game's number-key card shortcuts.
- **How positions are stored:**
  - **On screen:** the pin is measured from the matching point of the screen. A bottom-left pin is measured from the screen's bottom-left corner.
  - **Attached:** the pin is measured from a point beside the game element. Relic Counters uses just right of the HP bar's bottom-right corner, with a natural bottom-left pin. Incoming Damage uses just left of the block icon, with a natural right pin.
  - Either way, positions survive resolution changes, and widgets can't go off screen.
- Locking makes the widgets click-through again and saves the layout.
- **F9: hide/show all overlays.**
- A short message in the top-left confirms each hotkey.

**Settings file** (`SpireQoL.settings.cfg`; everything here except key names and positions is also in the settings menu; edit while the game is closed):

| Key | Default | Meaning |
| --- | --- | --- |
| `Features` | all `true` | turn features on/off by id |
| `Relics` | (empty) | Relic Counters tray: relic or power class name → shown, e.g. `"PenNib": false`. Missing = shown, except turn timers (off). Since 0.23.0 this is the tray only; relic tabs are `Tabs` `proc:<Relic>` (a saved `false` was copied there once) |
| `SettingsVersion` | (managed) | migrations of older settings files run once |
| `Widgets` | defaults | saved positions (`Anchor` = pin, `OffsetX/Y`, `Scale`, `Opacity`, `FollowGame`, `CustomPin`) |
| `LayoutVersion` | (managed) | when a release changes a default position, older saved positions for that widget are reset once |
| `ReportErrors` | `true` | send crashes and audit mismatches, with the game state at the time, to the mod's Sentry project (see "Error reporting") |
| `UnlockLayoutKey` / `HideAllKey` / `SettingsKey` | `F8` / `F9` / `F10` | Godot key names |
| `EtherealOpacity` | `0.8` | 0.25–1 (a saved 0.7, the old default, is raised to 0.8 once) |
| `Tabs` | every tab shown | one entry per on-card tab kind, added (as `true`) the first time that kind appears, plus a group switch per family: `"proc:*"`, `"double-play:*"`. Set any to `false` to hide it; a group set to `false` hides its whole family. Keys: `retain`, `sly`, `replay`, `blocked` (also turns off dimming), `shuffle`, `double-play:<Source>`, `proc:<Source>` (class names, e.g. `proc:FeelNoPainPower`). Replaces `ShowDoublePlay` / `DimBlockedCards` (migrated once) and `ShowInnateEternalBadges` (removed). |
| `ProcNumbers` | `true` | amounts on block / damage / summon proc tabs; `false` = icon only |
| `ProcIndirect` | `true` | count procs one step removed: cards that exhaust or discard other cards, draw, or create cards |
| `ShowTurnTimers` | (removed) | replaced in 0.23.0 by per-relic `Relics` entries (turn timers default off); a saved `true` turned them all on once |
| `PulseOneStepEarly` | `false` | Relic Counters also flash two steps before firing |
| `FlashHz` | `2.5` | flash speed |

**Developer builds:** with `ResetModSettingsOnInstall` set in `Sts2.local.props`, every install deletes the settings file, so a new build starts from defaults with everything shown. Release zips don't include a settings file.

**Not yet:** an in-game settings menu (planned; for now, use the file), attaching to game elements other than the player's HP (hand, draw pile), grid snap, per-widget opacity control in game.

---

## On-card tabs: one language for everything on a card

**The question every tab answers: does something happen the moment I play this card, right now?** Exceptions: the Retain tab, and Ethereal translucency.

Every indicator about a specific card in your hand uses the **same form**: a **tag** hanging up off the card's **top edge**, centered in a row, one tag per effect. In a fanned hand the tops of cards stay visible while their sides are covered, so you can read the tags at a glance without hovering. A tag means something out of the ordinary will happen when you play that card.

- **Look:** each tag is a copy of the card's own enchantment tab (the game's banner art), rotated a quarter turn so it hangs off the top edge the way an enchantment tab hangs off the side. **It draws beneath the card art but above the card's glow**, tucked about 17% under the top edge, so the card itself covers the tucked part. The icon is drawn slightly smaller (85%), centered as if the tag were tucked 25% (the backdrop sits a little lower than the icon, evening out the margins above and below it). Each tag carries the icon of whatever is affecting the card: a relic, power or enchantment, or the draw pile.
- **Text instead of an icon only for replay:** replay on the card itself has no power or relic to show, so its tab reads `2×` (total plays).
- **Numbers only for amounts:** a tab shows a number only when the proc gains you block, deals damage or summons, and the amount is the point (e.g. Feel No Pain on True Grit: `3`). It sits at the icon's lower-right corner, like a power amount. Everything else is icon only.
- **Red slash:** the icon crossed out means playing this card breaks or is blocked by that thing, hurts you, or helps the enemy (Enrage, Galvanized...).
- **One per source:** each source shows at most one tab per card. Tabs keep their size and spacing; if a row would be wider than the card, only the gaps shrink (tabs overlap, later ones on top), so a row never spills past the card's edges.
- **Not tabs:** the only on-card effects that aren't tabs are **dimming** (the card can't be played right now; the reason is a crossed tab) and **Ethereal translucency**.

| Tab icon | Meaning | Feature |
| --- | --- | --- |
| Steady (enchantment) | Retain (permanent or this turn only) | Card Indicators |
| Master Planner (power) | Sly (permanent or this turn only) | Card Indicators |
| The double-play source (Echo Form, Burst, Duplication, One-Two Punch, Signal Boost, Tag Team, Throwing Axe) | plays more than once because of it | Card Indicators |
| Text `2×` (no icon) | Replay on the card itself (Hidden Gem, Transfigure, Sword Sage, enchantments): total plays | Card Indicators |
| Bound / Ringing / Smog power, or Velvet Choker, **crossed** + card dimmed | can't be played right now | Card Indicators |
| Discard pile | its draw would reshuffle your discard pile into the draw pile | Shuffle Warning |
| A power or relic (+ amount for block / damage / summon) | playing this sets it off: see [Proc Indicators](#proc-indicators) | Proc Indicators |
| A power or relic, **crossed** (+ amount for damage to you) | playing this helps the enemy, hurts you, or gives up a relic (Enrage, Thorns, Curl Up, Galvanized, Art of War, Pael's Tears, Pael's Eye...) | Proc Indicators |

**Game UI touched:** card nodes in the hand. Tag copies live in a `SpireQoL_TagLayer` node inserted directly after the card's glow (`NCard.CardHighlight`) in the glow's parent, so they draw above the glow and beneath the card art. It mirrors the card frame's transform each frame, and is hidden when the node stops showing a decorated hand card. The game's own enchantment tab is only read as a template.

---

## Card Indicators

**What you see:** mid-combat changes to a card in your hand that the game otherwise shows only as text, as on-card tabs (see above).

- **Ethereal cards are translucent** (80% opacity by default) when Ethereal is on the card itself: cards that are always Ethereal, and Music Box copies. **Not** when Ethereal comes from Hex (the act 3 elite's Hexed affliction): Hex makes every card Ethereal through a power (a "global" keyword source), and the game's affliction overlay already marks those cards.
- **Retain:** a Steady tab (Steady is the game's Retain enchantment). **Sly:** a Master Planner tab (the de-facto Sly power). Granted-for-this-turn (Expertise, Hand Trick) looks the same.
- **Replay** on the card itself (Hidden Gem, Transfigure, Sword Sage on Sovereign Blade, enchantments): a text tab `2×`, the card's total plays (`CardModel.GetEnchantedReplayCount` + 1). The card text says "Replay", but it's easy to miss without hovering.
- **Added keywords get no tab:** the game prints every keyword a card currently has (including added Exhaust) on the card itself. (The Goopy "added Exhaust" tab was removed in 0.21.0.)
- **Blocked cards** (Bound, Ringing, Smog, Velvet Choker): the card is dimmed, with a crossed tab showing the blocking power's or relic's icon. Detected with the game's own `CardModel.CanPlay`.
- **Galvanized** moved to [Proc Indicators](#proc-indicators) in 0.19.0 (crossed Galvanic tab with the damage you take).
- **Double play:** one tab per source, with that source's own icon: the power (Echo Form, Burst, Duplication, One-Two Punch, Signal Boost, Tag Team) or the relic (Throwing Axe). Replay on the card itself is the `2×` tab instead. The game's query reports which models changed the count, so new sources get their own icon automatically.
  - The count comes from the game's own play-count query, the one it runs when you play the card: enchanted replays + 1, then `Hook.ModifyCardPlayCount`. So it covers Echo Form (first N cards each turn), Burst (skills), Duplication, One-Two Punch (attacks), Signal Boost (powers), Throwing Axe (first card each combat), Tag Team, enchantments, and anything added later.
  - Tag Team only doubles attacks aimed at the enemy that has it. Single-target cards are checked against each enemy, and the best case is shown.

**Not yet:** free-this-turn / free-this-combat / temporary-cost duration markers, decorations outside the hand (draw/discard views).

---
## Relic Counters

**What you see:** a compact tray, by default attached just right of your HP bar with its bottom edge level with the bar's bottom (so it stays clear of enemies), with one row per counter relic or countdown power you have: its icon, a bar split into one segment per step, and a short label. Counters longer than 12 steps (Cacophony's 33) show only the countdown number, no bar.

**What belongs here:** counters you push forward by playing (cards, attacks, skills, energy, draws, orbs, stars, exhausts) and per-turn card limits. On/off relic states are on-card tabs instead (Pael's Tears, Pael's Eye, one-shot relics), and relics that just count turns are opt-in (Tracker checkbox per relic in the settings menu, off by default).

- **Counters that carry across turns and combats sort first:** Pen Nib, Nunchaku, Tuning Fork, Iron Club, Galactic Dust, Joss Paper, Metronome.
- **Then per-turn counters:** Kunai, Shuriken, Ornamental Fan, Kusarigama, Letter Opener, Brilliant Scarf.
- **The row flashes when the next qualifying action fires the relic.** This uses the game's own "about to fire" state (`RelicModel.Status == Active`). Pen Nib flashes at 9/10. Stone Calendar flashes all of turn 7.
- **Limits:**
  - Velvet Choker shows plays left and turns red at 1.
  - Pocketwatch shows green while you're within its card limit and red ("missed") once you go over.
- **Turn timers** (off by default; switch each on in the settings menu, Relics page, Tracker column): Horn Cleat, Candelabra (turn 2); Captain's Wheel, Chandelier, Sparkling Rouge (turn 3), shown with progress and "next turn" the turn before; Stone Calendar, Happy Flower, Fake Happy Flower, Pendulum, Pollinous Core as counters.
- **Countdown powers** (sorted after relics; the game's `PowerModel.DisplayAmount` is "steps left"). The bar fills toward the next trigger and flashes when one step is left:
  - Across turns: Orbit (every 4 energy spent), Automation (every 10 cards drawn), Cacophony (every 33 cards drawn, number only).
  - Per turn: Panache (every 5 cards played), Pale Blue Dot (5 cards played, any type; shows `done` once it has fired this turn).
  - Enemy powers that count *your* plays, in red: Withering Presence (every 6 cards you play adds a Wither to your hand).
- **Not in the tray:** Pael's Tears and Pael's Eye (crossed tabs on the cards that would give them up), and one-shot relics (Vambrace, Throwing Axe, Burning Sticks, Unsettling Lamp, Permafrost, Music Box: tabs on the cards that would use them).

Thresholds are read live from each relic, so balance patches are picked up automatically.

**Game UI touched:** none; the tray is our own overlay. The relic bar's own counters are unchanged.

Checked against every relic the game gives a counter (v0.111.0): the rest are run-level counters (teas, Girya, Winged Boots, Toy Box...), plus Pael's Legion (block cooldown) and Pael's Flesh (shows the turn number).

---

## Relic Reminders (merged into Proc Indicators in 0.21.0)

The relic tabs (Pen Nib ready; crossed Art of War, Ripple Basin, Pael's Tears; one-shot Vambrace, Burning Sticks, Unsettling Lamp, Music Box) are now rules in [Proc Indicators](#proc-indicators), keyed `proc:<Relic>`, so every tab goes through one rule table and one audit.

---

## Incoming Damage

**What you see**, during your turn: a small stacked sum, read like a math problem. It uses the game's own intent icons: defend, and the attack icon tier that matches the incoming total (the same thresholds enemies use: under 5, 10, 20, 40, and 40+):

```
[attack]  14     incoming damage
[defend] − 5     your block at end of turn
[osty]   − 4     what Osty absorbs (Necrobinder, only while Osty is out; red if it dies)
──────────
      =   9     what you'll lose (red), or 0 in green
```

**Placement:** it sits just left of your block indicator, at the left end of your HP bar, and follows your character. If that doesn't fit on screen (your character stands near the left edge), it drops below the HP bar and every row of power icons instead; wrapped rows are measured. Move it with F8, or middle-click to pin it to the screen instead.

**How the numbers are worked out:** the enemy turn is simulated hit by hit, in turn order:
- **Osty** (Necrobinder): enemies never target Osty, but its Die For You power takes the part of an enemy attack that gets through *your* block (pets share your block), up to Osty's HP; the overkill comes back to you. Damage that isn't an attack (Burn, Constrict, HP loss) isn't redirected. The Osty row uses Die For You's icon.
- **Your end-of-turn self damage** lands first, against your end-of-turn block.
- **Your end-of-turn damage to enemies** lands before they act, so an enemy it kills doesn't attack: Hailstorm (enough Frost orbs), The Bomb (last tick), Screaming Flagon (empty hand), Stone Calendar (its turn), Charon's Ashes (per Ethereal card exhausting) hit every enemy; Lightning orb passives, Parrying Shield and Forgotten Soul hit a random enemy, so they're counted only when there's one enemy. Enemy block is still up at that point.
- **HP-loss effects**, in the game's order, on every hit that reaches your HP: Tungsten Rod (−1 each), Beating Remnant (caps HP lost per turn), then Buffer (cancels a hit, one charge each). Intangible is already in the enemy intent numbers.
- **Poison:** an enemy that Poison will kill doesn't attack. Poison ticks at the start of its turn, before it moves. The amount is the game's own poison preview (`PoisonPower.CalculateTotalDamageNextTurn`), which already applies per-hit damage caps such as Exoskeleton's **Hard To Kill** (each instance capped, e.g. at 9). Skulking Colony's **Hardened Shell** caps an enemy's *total* HP loss per turn; the game's preview ignores that, so we apply it.
- **Doom doesn't stop attacks.** Doomed enemies die at the end of their own turn, after attacking.
- **Retaliation** can kill an attacker partway through a multi-hit. Each later hit is then cancelled, because the game checks for a dead attacker before every hit.
  - Thorns hits before each blow. The blow during which Thorns kills still lands.
  - Flame Barrier hits after each blow.
  - Reflect, after each blow, deals the damage you blocked.
  - Retaliation damage goes through the game's damage hook, so the enemy's caps and Vulnerable apply. It hits the enemy's block first; enemy block is assumed cleared at its turn start unless it has Barricade.
- **Unblockable HP loss** (Regret, Bad Luck, Beckon, Demise) is added to both the incoming and the result.

**Sources:**
- **Blockable:** enemy attack intents (`AttackIntent.GetSingleDamage` × hits, the same numbers the game shows); Burn, Decay, Infection, Toxic, Wither in hand; the Constrict, Magic Bomb, Disintegration debuffs.
- **Unblockable:** Regret (HP loss = cards in hand), Bad Luck, Beckon, Demise.
- **End-of-turn block** (the "block" line = current block + these):
  - Plating
  - Frost orb passives (times the game's passive trigger count, so trigger-boosting effects are included)
  - Orichalcum / Fake Orichalcum (only if you currently have 0 block, like the game checks)
  - Cloak Clasp (block × cards in hand)
  - Ripple Basin (only if you haven't played an attack this turn)
  - Feel No Pain × Ethereal cards still in hand: they exhaust at end of turn (after Orichalcum checks your block), each giving Feel No Pain's block
  - Not counted: Horn Cleat and Captain's Wheel. They give block at the *start* of turn 2/3, after the enemy has attacked; Relic Counters shows their countdown.

**Game UI touched:** none; our own widget.

**Game UI read (not changed):** the player's `NCreature` → `%HealthBar` (`NCreatureStateDisplay`) → its `%HealthBar` (and that bar's `%BlockContainer`) and `%PowerContainer`'s icons, for placement only.

**Not yet:**
- Health-bar preview.
- Per-player targeting in multiplayer (all attack intents are counted).
- Breakdown on hover.
- Enemies that gain block mid-turn before retaliation lands.
- Other end-of-turn effects that could kill enemies first (e.g. Lightning orbs).

---

## Proc Indicators

**What you see:** on each card in your hand, a tab for every power or relic that playing it would set off right now, with that source's icon (see "On-card tabs").

- **Plain tab:** it triggers (draws, energy, Strength, channels, upgrades, creates cards...).
- **Tab with a number:** it gains you block, deals damage, or summons, and the number is how much. Block uses the game's own block math (`Hook.ModifyBlock`), so Dexterity and Frail count where they apply. Damage is per enemy hit, from the dealer's side (`Hook.ModifyDamage` with no target), so an enemy's Vulnerable isn't included.
- **Crossed tab:** it helps the enemy or hurts you; a number is the damage you take.
- **Multiplied by plays:** a card that plays twice (Echo Form, Burst...) sets off per-play procs twice. Procs that fire once per card (Black Hole, stars spent, exhausting itself) count once.
- **One step removed** (setting `ProcIndirect`): cards that exhaust or discard other cards, draw, or create cards count those too. For example, with Feel No Pain, True Grit shows the block from exhausting another card. Choices you make mid-play count as the most the card can do (Purity, Hidden Daggers).
- **Counter relics** (Kunai, Shuriken, Ornamental Fan, Nunchaku, Kusarigama, Letter Opener, Tuning Fork, Iron Club) show only on cards that complete the count; the tray shows progress.
- Several enemies with the same power share one tab, amounts added.
- Cards that can't be played (Unplayable, blocked, or their own condition unmet, via `CardModel.CanPlay`) get no proc tabs. Cards you can't afford yet still show them, since energy can come mid-turn.

**Uses or breaks a relic** (formerly Relic Reminders): Pen Nib ready (attacks); crossed Art of War / Ripple Basin (attacks, until you've played one this turn); crossed Pael's Tears (cards that would spend your last energy); crossed Pael's Eye (every card, while armed and nothing played this turn: playing one gives up the extra turn); one-shot Vambrace (block cards), Burning Sticks (skills that exhaust when played), Unsettling Lamp (debuffing cards), Music Box (attacks).

**Your attack against enemies** (from the card's damage to each enemy, through the game's damage math, vs that enemy's block and HP):
- Thorns on an enemy: **crossed**, damage to you per hit. Curl Up (it gains block), Personal Hive (Dazed into your deck), Asleep / Slumber (wakes it): **crossed**.
- Burrowed (breaking its block stuns it), Flutter, Plow, Shriek (stuns), The Gambit (it dies), Slippery (HP loss capped at 1, crossed).
- Killing an enemy: Gremlin Horn (energy and a draw); crossed for enemy death effects (Infested, Steam Eruption, Surprise, and Crab Rage / Ravenous on the others).
- Breaking block: Hand Drill (Vulnerable).

**Losing HP from the card** (Bloodletting-style): Centennial Puzzle (first time each combat), Self-Forming Clay, Demon Tongue (first time each turn), Emotion Chip, Red Skull (when it takes you to its threshold), plus Rupture.

**Sovereign Blade:** the powers buffing it show as tabs on the Blade: Seeking Edge (hits all enemies), Parry (gains block), Sword Sage (replays), Conqueror on an enemy (more Blade damage to it).

**Sources** (each mirrors the source's own hook in the game code, v0.111.0):

| Source | Cards marked | Shows |
| --- | --- | --- |
| Afterimage | every card | block per play |
| Rage | attacks | block |
| Spirit of Ash | Ethereal cards | block |
| Danse Macabre | cards costing ≥ 2 energy | block |
| Feel No Pain | cards that exhaust: Exhaust cards, skills under Corruption, and cards that exhaust others (Brand, Burning Pact, Cinder, Cleanse, Fiend Fire, Flak Cannon, Purity, Scavenge, Second Wind, Stoke, Thrash, True Grit) | block × cards exhausted |
| Child of the Stars | star-cost cards | block × stars |
| Shroud | cards that apply Doom | block per Doom that lands |
| Pillar of Creation | cards that create cards | block × cards created |
| Gravity, Serpent Form | every card | damage |
| Panache | the card that completes its count | damage |
| Black Hole | cards that spend stars or gain stars (Gather Light, Venerate...; every card under The Sealed Throne) | damage per trigger |
| Haunt | Soul | damage |
| Sleight of Flesh | cards that apply debuffs to enemies | damage × debuffs that land (Artifact cancels one each; single-target cards use the best target) |
| Juggernaut | cards that gain block, counting block from other procs (Afterimage, Feel No Pain...) | damage per block gain |
| Speedster | cards that draw | damage × cards drawn |
| Cacophony | cards whose draws finish its countdown | damage |
| Smokestack | cards that create statuses | damage × statuses |
| Strangle (on an enemy, yours) | every card | damage |
| Devour Life | Soul | summon |
| Storm, Subroutine | powers | trigger |
| Calamity, Envenom, Concoct, Reaper Form, Monarch's Gaze | attacks | trigger |
| Juggling | your 3rd attack this turn | trigger |
| Master Planner | skills | trigger (gains Sly) |
| Monologue, The Sealed Throne | every card | trigger |
| Pale Blue Dot | your 5th card this turn | trigger |
| Dark Embrace | cards that exhaust | trigger |
| Orbit | cards whose energy finishes its count | trigger |
| Automation | cards whose draws finish its count | trigger |
| Corrosive Wave | cards that draw | trigger |
| Stratagem | cards whose draw causes a shuffle | trigger |
| Vicious | cards that apply Vulnerable | trigger |
| Arsenal, Trash to Treasure | cards that create cards / statuses | trigger |
| Oblivion, Slow (on an enemy) | every card | trigger |
| Enrage, Sneaky, Skittish, Imitation Learning, Withering Presence (enemy) | skills / attacks / attacks (first unblocked hit) / powers / the card that completes its count | **crossed** trigger |
| Tender, Smoggy (on you) | every card / skills | **crossed** trigger |
| Galvanic (enemy) | Galvanized cards | **crossed**, damage to you |
| Vital Spark (enemy) | Tainted cards | **crossed** trigger |
| Rupture | cards that cost you HP (Bloodletting-style HP loss) | trigger |
| Metronome | cards whose channels reach its orb count (including Storm's lightning from a power) | damage |
| Bone Flute | cards that make Osty attack, while Osty is out | block |
| Intimidating Helmet | cards spending ≥ 2 energy (energy actually spent: X-cost counts your energy) | block |
| Daughter of the Wind | attacks | block |
| Permafrost | powers (first this combat) | block |
| Ornamental Fan, Tuning Fork | completing card | block |
| Tough Bandages | cards that discard | block × discards |
| Galactic Dust | star-cost cards that reach its count | block |
| Regalite | cards that create cards (first this turn) | block |
| The Abacus | cards whose draw causes a shuffle | block |
| Lost Wisp | powers | damage |
| Kusarigama, Letter Opener | completing card | damage |
| Charon's Ashes, Forgotten Soul | cards that exhaust | damage × exhausts |
| Tingsha | cards that discard | damage × discards |
| Kunai, Shuriken, Nunchaku, Iron Club | completing card | trigger |
| Game Piece, Mummified Hand | powers | trigger |
| Helical Dart | Shivs | trigger |
| Ivory Tile | cards spending ≥ its threshold | trigger |
| Chemical X | X-cost cards | trigger |
| Razor Tooth | upgradable attacks and skills | trigger |
| Rainbow Ring | the card that completes attack + skill + power this turn | trigger |
| Mini Regent | star-cost cards, first star spend this turn | trigger |
| Joss Paper | cards whose exhausts reach its count | trigger |
| Unceasing Top | your last card in hand, if it draws nothing | trigger |

**Game UI touched:** on-card tabs only (see above). **Game code read (not changed):** `Hook.ModifyCardPlayCount`, `Hook.ModifyBlock`, `Hook.ModifyDamage`, `CardEnergyCost.GetAmountToSpend`, `CardModel.GetStarCostWithModifiers`, and a few relics' and powers' private counters (read by reflection).

**Procs of procs:** a second pass runs the procs that other procs feed: block from any proc counts toward Juggernaut, Storm's lightning toward Metronome, and The Sealed Throne's stars toward Black Hole.

**Not shown on purpose:** procs that fire on *drawing* a card (Pagestorm, Iteration, Automation, Speedster on draw...) happen when the card is drawn, not when you play it, so there's no choice to inform.

**Audit data:** every damage tab also records the exact range of damage it predicts across its targets, with each target's own modifiers: the sum over every enemy for all-enemy procs, the least- to most-damaged enemy for random ones, or the exact amount for a known target. The proc audit checks real plays against it.

**Not yet:** deeper chains (a proc's block setting off Juggernaut whose damage sets off something else); enemy death effects that revive (Adaptable, Illusion, Reattach).

---

## Shuffle Warning

**What you see:** a **discard pile** tab (the game's own discard pile icon) on draw cards in your hand whose draw is more than your draw pile, so playing them reshuffles your discard pile mid-turn. Nothing else: you know what a reshuffle means for the cards in your hand.

Draw cards are every `CardPileCmd.Draw` caller in the decompile (TRACKER §6). Most draw their Cards amount; Dagger Throw, Escape Plan and Pillage draw one (Pillage at least one); Scrawl draws up to a full hand; Compile Driver draws one per distinct orb type (the card's own calculation); Restlessness draws only when it's the only card in hand; Mad Science draws only with the Wisdom rider. A card that plays twice draws twice. The discard pile icon is read from the live discard pile button (`NCombatUi.DiscardPile` → `Icon`).

**Not yet:** draw potions (potion slots have no tab yet).

---

## Intent Forecast

⚪ Planned. Needs research into monster AI first (TRACKER §7). Registered but does nothing.
