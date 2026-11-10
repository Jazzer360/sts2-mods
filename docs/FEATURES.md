# Spire QoL: feature reference

This is the complete list of every change Spire QoL makes to the game. If a change isn't here, the mod doesn't make it. Each feature lists what you see, its settings, which game UI it touches, and the game methods it patches.

**Status key:** 🟢 shipped · 🟡 built, being tested in game · ⚪ planned

Spire QoL is **display-only**. It never changes game state, damage, cards or saves. Its manifest sets `"affects_gameplay": false`, so you can join multiplayer lobbies whose players don't have it. See [MODDING_NOTES.md](MODDING_NOTES.md).

**Only knowable information.** Spire QoL shows only what a player with perfect memory and full knowledge of the game's rules could work out. It never reads the RNG or the seed, the draw pile's order, or an enemy's move beyond the intent on screen. Random effects show their amount, never their target or roll, and predictions that would depend on a roll leave it out.

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
  - **Forecast audit:** the Incoming Damage forecast from the last moment of your turn, against the HP you and Osty actually lost by your next turn (from the game's combat history). A mismatch logs the forecast breakdown and every damage and block entry. The forecast is the simulator's worst branch; HP lost (yours or Osty's) anywhere between the best and worst branch isn't a mismatch, since a random target decides which happens.
  - **Outcome audit** (part of the forecast audit): the outcome simulator's prediction from that same moment, enemy by enemy, against the history: dead before it acted (predicted in every branch but it acted, or in no branch but it died first; this is what checks the lethal tab), its HP when your end of turn was over and the damage instances that got through its block then (what Slumber and Asleep count), against the branches' range, and for Imbalanced enemies whether its first hit was fully blocked when every branch agreed. Only what no branch allows is reported, so a random target never causes a report; if the simulation was truncated, only the guarantees it still makes are checked. Reports carry the simulator's per-enemy prediction and the full numbered history window, enemy turn included.
  - **Forecast audit** also checks the end-of-turn block it predicted, and splits the history by whose turn each entry happened on, so Poison ticking at the start of your next turn isn't counted.
  - **Proc audit:** the tabs a card had on its last frame in your hand (every tab the mod predicted, including ones hidden in settings) (a play can start several frames after the card leaves your hand, while the game's action queue is busy), against what happened until your next card play. Plays are split the way the game runs them: energy/stars spent → "before play" procs → play started → the card → play finished → "after play" procs. The split between two plays is the next card's energy or star spend; for a 0-cost card after another play it's decided once that card is checked too, as the point that best fits both cards' tabs together (the previous play's report waits for it), and for a 0-cost first play of the turn (start-of-turn effects, then its before-play procs, nothing marking the split) the point that best fits its own tabs. Relic and power flashes are split the same way (by their position in the fight's combat history). A potion used between plays belongs to neither: the game writes its history entry after its effects (a Power Potion's card, Lucky Tonic's Buffer), so the next card's window starts after that entry, and the previous card's ends at the point between its last play finishing and the entry that best fits its tabs. Cards auto-played by it, such as Catastrophe's, stay in its window. **Quick plays aren't checked:** if another of your plays finished after a card's tabs were last computed (you played it while the previous card was still resolving), its prediction didn't see that play's effects, so its mismatches are logged as `[model]` lines, not reported. Simulator tabs (lethal, Imbalanced, wake) of a card the simulator hadn't reached yet (frame budget) aren't reported either. The last play's window ends when you end your turn (`CombatManager.PlayerEndedTurn`), before end-of-turn effects (orb passives, Plating, Ethereal exhausts). Block from Frost orbs the card evokes counts as its own. Damage caps (Hard to Kill, Hardened Shell, Intangible) flash when they cap a hit; they aren't procs, so they're not reported as missing tabs. Powers enemies apply to each other (Heist) aren't the play's doing. **Random outcomes aren't reported:** when a play's result depends on a roll with more than one possible target (a random-target proc such as Juggernaut, Kusarigama, Tingsha, Forgotten Soul; a Lightning evoke; a random-target attack; all with several enemies alive), only effects the combat history shows a random hit caused are logged as a `[model]` line instead of a mismatch: an enemy power firing after that enemy took random damage (no card source, or this card's random-target hit) or after a random kill; Gremlin Horn, extra summons, draws, cards or powers after a random kill. Everything else in that play, and every play without randomness, is still checked, so multi-enemy fights still verify their tabs. Reports are grouped by cause: which powers for "unexplained powers", and more or less than predicted for amounts. It logs: a relic or power that fired with no tab; a relic tab whose relic didn't fire; proc block that differs (exact; block from any card's own play, including the next one and auto-played ones, isn't proc block); proc damage outside the tabs' predicted range (to enemies and to you); and a **budget** check: draws, exhausts, discards, created cards, orbs, summons and powers applied beyond what the card itself does plus what its tabs can add are logged as "unexplained" (a missing tag). A card's own powers are its power values plus every power its own code applies (`PowerCmd.Apply<T>` calls, read from the compiled card class). Windows with auto-played cards skip the budget: those cards are random, so nothing predicts them. Less than the card's own is a `[model]` line (our model of the card is off, or a pile ran out).
  - **Summaries:** at the end of each combat, one line per audit: `card plays checked, N matched (issue counts)` and `enemy turns forecast, N matched`.
  - Both only read the combat history and subscribe to relics' and powers' `Flashed` events. They're the features `forecast-audit` and `proc-audit`, on by default.
- **Error reporting** (setting `ReportErrors`, on): crashes caught anywhere in the mod (feature errors, frame errors, tag layout, play facts) and every audit mismatch are sent to the mod's own Sentry project. Proc audit reports also carry the full window: every history entry from just before to just after it, numbered by position and described in our own words (the game's text for a draw says "discarded"), with relic and power flashes at their positions and markers for the window and the play's start; what the card itself was expected to do (draws, exhausts, orbs, hits, orb evokes, random outcome); tabs hidden by settings; how the split between plays was chosen, with each candidate's issue counts; every play this turn so far (card, its tabs, matched or what didn't), the state just before the play started (player, powers, relics with their counters, orbs, enemies, hand with tabs), how many frames passed between the card's last frame in hand and its play starting, and the history range checked and how its start was chosen. Each report carries the game state from `Diagnostics/Snapshot.cs`: combat, player, powers, relics, orbs (front first, with passive and evoke values and slot capacity), enemies with intents, the hand with each card's tabs and play facts, the last 40 combat history entries, the audit window, and the last 80 mod log lines. It uses the game's own Sentry .NET (`Sentry.dll`) through a separate `SentryClient` with our DSN. `SentrySdk.Init` is never called, so the game's own reporting is untouched and no global handlers are installed. There's no machine name, user or personal data. Each fingerprint is sent once per session, at most 40 events. Mismatch fingerprints leave out amounts, so the same bug groups into one issue. An hourly GitHub Action mirrors new Sentry issues to GitHub issues (`auto-report`).
- **Error logging is capped:** a failure that repeats every frame is logged in full once, then as a count every ~600 repeats.
- **No Harmony patches.** Everything is read from live game state each frame; the game's hook queries it calls (`Hook.ModifyCardPlayCount`, `ModifyBlock`, `ModifyDamage`, `ShouldEtherealTrigger`, `ModifyOrbPassiveTriggerCount`) are pure queries with no side effects.

---

## Settings and layout

**What you see:** a settings menu, floating indicators you can place yourself, and hotkeys.

### Settings menu

Opens with **F10** (setting `SettingsKey`), the **Spire QoL settings** button on the Mods screen (Settings → Mod Settings → select Spire QoL) or the same button on the pause menu. Every change applies right away and is saved. Esc or Close closes it; game hotkeys are blocked while it's open.

- **Pages:** General (features and options), Card tabs, Relics, Your powers, Enemy powers.
- **General:** each feature on/off, live (a feature switched off is skipped each frame and its widgets hide; no restart): Card Indicators, Proc Indicators, Relic Counters, Incoming Damage, Shuffle Warning (with the discard pile icon), and the two prediction checks. Options: amounts on proc tabs (`ProcNumbers`), indirect procs (`ProcIndirect`), tracker flashes early (`PulseOneStepEarly`), send error reports (`ReportErrors`, live), and sliders for Ethereal opacity and tracker flash speed.
- **Card tabs:** Retain, Sly, Replay, Reshuffle, each with its icon, plus the family switches (all proc tabs, all double-play tabs). Tab kinds seen in play that the catalog doesn't describe yet are listed under **Other**, so everything stays switchable.
- **Relics / Your powers / Enemy powers:** a table, one row per relic or power the mod knows: the game's icon (with the red slash if its tab is crossed) and name (hover for the game's own description), then two checkboxes: **On cards** (its tab, with what it marks and shows) and **Tracker** (its Relic Counters row, with what it counts). Each column has All on / All off for the rows listed. Rows come from `Menu/SettingsCatalog.cs`, which covers every Proc Indicators rule, every double-play source and every tracker entry.
- **Search** filters every page by name or description; **Only what's in this fight** (in combat) lists only relics and powers you or the enemies have right now.
- **Reset defaults** (top right): click twice (the first click arms it for 4 s) to put every feature, option, card tab and tracker row back to its default. Hotkeys, widget positions and the error-reporting choice are kept.

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
| `Tabs` | every tab shown | one entry per on-card tab kind, added (as `true`) the first time that kind appears, plus a group switch per family: `"proc:*"`, `"double-play:*"`. Set any to `false` to hide it; a group set to `false` hides its whole family. Keys: `retain`, `sly`, `replay`, `shuffle`, `double-play:<Source>`, `proc:<Source>` (class names, e.g. `proc:FeelNoPainPower`). Replaces `ShowDoublePlay` / `DimBlockedCards` (migrated once) and `ShowInnateEternalBadges` (removed). |
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

- **Look:** each tag is a copy of the card's own enchantment tab (the game's banner art), rotated a quarter turn so it hangs off the top edge the way an enchantment tab hangs off the side. **It draws beneath the card art but above the card's glow**, tucked about 17% under the top edge, so the card itself covers the tucked part. The icon is drawn slightly smaller (85%) and sits just below the middle of the part of the tag that shows above the card. Tags stay on a card while you hold it to play it (dragging or aiming): the game moves a held card out of the hand's holder container (`NPlayerHand.StartCardPlay`), so the held card is found through the hand's current card play (`NPlayerHand._currentCardPlay`, read only). Each tag carries the icon of whatever is affecting the card: a relic, power or enchantment, or the draw pile.
- **Text instead of an icon only for replay:** replay on the card itself has no power or relic to show, so its tab reads `2×` (total plays). The text fills the icon's slot (the tab's own auto-sizing label gets the icon's box and a larger maximum font size).
- **Numbers only for amounts:** a tab shows a number only when the proc gains you block, deals damage or summons, and the amount is the point (e.g. Feel No Pain on True Grit: `3`). It sits at the icon's lower-right corner, like a power amount. Everything else is icon only.
- **Plain vs crossed:** a plain tab means playing this card does something extra. A crossed tab (red slash) is a warning: playing it might be bad for you, because it helps the enemy (Enrage, Curl Up), hurts you (Thorns, Galvanized) or gives up a relic's bonus (Art of War, Pael's Tears).
- **One per source:** each source shows at most one tab per card. Tabs keep their size and spacing; if a row would be wider than the card, only the gaps shrink (tabs overlap, later ones on top), so a row never spills past the card's edges.
- **Not tabs:** the only on-card effect that isn't a tab is **Ethereal translucency**.
- **Not marked: cards that can't be played.** The game already crosses out the cost of a card that can't be played for any reason other than cost (`NCard` unplayable energy/star icon) and drops its glow, so there's no tab for it (removed in 0.23.1).

| Tab icon | Meaning | Feature |
| --- | --- | --- |
| Steady (enchantment) | Retain (permanent or this turn only) | Card Indicators |
| Master Planner (power) | Sly (permanent or this turn only) | Card Indicators |
| The double-play source (Echo Form, Burst, Duplication, One-Two Punch, Signal Boost, Tag Team, Throwing Axe) | plays more than once because of it | Card Indicators |
| Text `2×` (no icon) | Replay on the card itself (Hidden Gem, Transfigure, Sword Sage, enchantments): total plays | Card Indicators |
| Discard pile | its draw would reshuffle your discard pile into the draw pile | Shuffle Warning |
| Skull (the game's emote wheel skull) | playing this kills an enemy for certain before it acts (see [Outcome simulator](#outcome-simulator)) | Proc Indicators |
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
- **Blocked cards** (Bound, Ringing, Smog, Velvet Choker): no tab since 0.23.1. The game's own crossed-out cost shows it whenever `CardModel.CanPlay` fails for a reason other than cost, which covers every case the old tab did.
- **Galvanized** moved to [Proc Indicators](#proc-indicators) in 0.19.0 (crossed Galvanic tab with the damage you take).
- **Double play:** one tab per source, with that source's own icon: the power (Echo Form, Burst, Duplication, One-Two Punch, Signal Boost, Tag Team) or the relic (Throwing Axe). Replay on the card itself is the `2×` tab instead. The game's query reports which models changed the count, so new sources get their own icon automatically.
  - The count comes from the game's own play-count query, the one it runs when you play the card: enchanted replays + 1, then `Hook.ModifyCardPlayCount`. So it covers Echo Form (first N cards each turn), Burst (skills), Duplication, One-Two Punch (attacks), Signal Boost (powers), Throwing Axe (first card each combat), Tag Team, enchantments, and anything added later.
  - Tag Team only doubles attacks aimed at the enemy that has it. Single-target cards are checked against each enemy, and the best case is shown.

**Not yet:** free-this-turn / free-this-combat / temporary-cost duration markers, decorations outside the hand (draw/discard views).

---
## Relic Counters

**What you see:** a compact tray, by default attached just right of your HP bar with its bottom edge level with the bar's bottom (so it stays clear of enemies); when you have Osty (Necrobinder), it sits just right of Osty's HP bar instead, since Osty stands to your right (only at the default placement: once you move or re-pin the tray it's always measured from your own HP bar, and Osty's bar stays up reading "Dead" when Osty dies, so the tray doesn't jump), with one row per counter relic or countdown power you have: its icon, a bar split into one segment per step, and a short label. Counters longer than 12 steps (Cacophony's 33) show only the countdown number, no bar.

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
- **Not in the tray:** Pael's Tears and Pael's Eye (crossed tabs on the cards that would give them up), and one-shot relics (Vambrace, Pael's Legion, Throwing Axe, Burning Sticks, Unsettling Lamp, Permafrost, Music Box: tabs on the cards that would use them; the game shows Pael's Legion's cooldown on the relic).

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

**How the numbers are worked out:** by the [outcome simulator](#outcome-simulator): your end of turn, then the enemy turn hit by hit, in turn order. Random targets (Lightning, Juggernaut, Forgotten Soul, Parrying Shield) are explored as branches, and the numbers shown are the **worst branch for you**. With one enemy, or when every branch ends the same (two Lightning orbs and two enemies they each kill), the result is exact.
- **Osty** (Necrobinder): enemies never target Osty, but its Die For You power takes the part of an enemy attack that gets through *your* block (pets share your block), up to Osty's HP; the overkill comes back to you. Damage that isn't an attack (Burn, Constrict, HP loss) isn't redirected. The Osty row uses Die For You's icon.
- **Your end-of-turn self damage** lands first, against your end-of-turn block.
- **Your end-of-turn damage to enemies** lands before they act, so an enemy it kills doesn't attack: Hailstorm (enough Frost orbs), The Bomb (last tick), Screaming Flagon (empty hand), Stone Calendar (its turn), Charon's Ashes (per Ethereal card exhausting) and Glass orb passives (base value + Focus; the base drops by 1 each time it fires, never below 0, so a decayed Glass orb still deals your Focus) hit every enemy; Consuming Shadow's evoked orbs (one per stack, last orb first, after orb passives) hit the lowest-HP enemy (Dark, including this turn's growth) or every enemy (Glass); Lightning orb passives and evokes, Parrying Shield, Forgotten Soul and Juggernaut (once per end-of-turn block gain: Plating, each Frost passive trigger, Orichalcum, Cloak Clasp, Ripple Basin, each Feel No Pain exhaust, Consuming Shadow Frost evokes) hit a random enemy, so they're counted only when there's one enemy (with several, a kill we can't be sure of would hide an attack). Enemy block is still up at that point.
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
  - Frost orbs evoked by Consuming Shadow (their evoke block)
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

**Uses or breaks a relic** (formerly Relic Reminders): Pen Nib ready (attacks); crossed Art of War / Ripple Basin (attacks, until you've played one this turn); crossed Pael's Tears (cards that would spend your last energy); crossed Pael's Eye (every card, while armed and nothing played this turn: playing one gives up the extra turn); one-shot Vambrace (block cards), Pael's Legion (block cards while it's off cooldown: double block), Burning Sticks (skills that exhaust when played), Unsettling Lamp (debuffing cards, when some enemy you could debuff has fewer Artifact charges than the debuffs this play applies: the lamp ignores debuffs that Artifact blocks), Music Box (attacks).

**Your attack against enemies** (from the card's damage to each enemy, through the game's damage math, times its hits, vs that enemy's block and HP). Hits match each card's own code: its Repeat value, or a fixed count (Twin Strike, Whirlwind's X...), a calculated count (Finisher, Flechettes...), or a condition (Dismantle on a Vulnerable enemy, Spite after you lost HP this turn, Fiend Fire per other card in hand):
- Thorns on an enemy: **crossed**, damage to you per hit. Curl Up (it gains block), Personal Hive (Dazed into your deck): **crossed**.
- Asleep / Slumber (wakes it): **crossed**. Each damage instance that gets through its block counts (Asleep wakes on the first, Slumber after its Amount). Judged by the [outcome simulator](#outcome-simulator) through the end of your turn: the card's hits, orbs it evokes, procs it sets off, then your end-of-turn damage (the enemy's block stays up until its own turn). A warning, so it shows when waking is **possible** in some branch after this card and isn't without it.
- Burrowed (breaking its block stuns it), Flutter, Plow, Shriek (stuns), The Gambit (it dies), Slippery (HP loss capped at 1, crossed).
- **Imbalanced** (Bowlbug and others; fully blocking one of its hits stuns it or knocks it off balance): a plain tab when, after this card, its first hit is fully blocked in **every** branch of the [outcome simulator](#outcome-simulator) and it isn't when you end your turn now. The card's block, Frost orbs it evokes, block procs and your end-of-turn block all count.
- Random and all-enemy proc damage (Kusarigama, Letter Opener...) lands after the card, so enemies the card kills aren't targets; a play that kills every enemy (one that revives keeps the fight going) gets no such tab.
- Killing an enemy: Gremlin Horn (energy and a draw); crossed for enemy death effects (Infested, Steam Eruption, Surprise, and Crab Rage / Ravenous on the others). Kills count damage from orbs the card evokes, not just its attack: channelling into full slots evokes your front orb first, Dualcast / Quadcast / Multicast evoke it several times, Shatter evokes every orb twice. Glass hits every enemy, Dark the lowest-HP one, Lightning a random one (counted only when there's one enemy). Orbs a play channels and then evokes itself aren't counted.
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
| Feral | Attacks that spend 0 energy (not copies), while it has charges left this turn | trigger (returns to your hand; `FeralPower.ModifyCardPlayResultLocation`) |
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
| Game Piece | powers | trigger |
| Mummified Hand | powers, when another card in hand still costs energy or stars (one of them becomes free this turn) | trigger |
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

**Audit data:** every damage tab also records the exact range of damage it predicts across its targets, with each target's own modifiers: the sum over every enemy for all-enemy procs, the least- to most-damaged enemy for random ones, or the exact amount for a known target. Thorns on an enemy expects 0 to its full damage when a single-target or random attack could hit an enemy without Thorns instead. The proc audit checks real plays against it.

**Not yet:** deeper chains (a proc's block setting off Juggernaut whose damage sets off something else); enemy death effects that revive (Adaptable, Illusion, Reattach).

---

## Outcome simulator

One simulator behind Incoming Damage, the lethal tab, the Imbalanced and wake tabs, and the forecast audit (`mods/SpireQoL/Sim/`). It has no game types, so it's tested without the game (`tests/SpireQoL.Sim.Tests`, run on every push by the "Simulator tests" workflow).

- **State** (`SimAdapter.Build`): each enemy's HP, block, Hardened Shell budget, Hard to Kill / Intangible cap per hit, poison, Barricade, stun, Imbalanced, and its attack (the intent's numbers per hit); your block, the powers and relics that act at end of turn (Juggernaut, Plating, Feel No Pain, Hailstorm, The Bomb, Consuming Shadow, Thorns, Flame Barrier, Reflect, Buffer; Orichalcum, Cloak Clasp, Ripple Basin, Screaming Flagon, Stone Calendar, Charon's Ashes, Forgotten Soul, Parrying Shield, Tungsten Rod, Beating Remnant), cards in hand that act at end of turn, Ethereal cards that will exhaust, orbs in order with their values and passive trigger counts, and Osty.
- **A card play** (`SimAdapter.Play`): its hits (per target, through the game's damage math), its own block, orbs it evokes and channels (channelling into full slots evokes the front orb first; new orbs get your Focus), powers it gives you (Plating, Juggernaut, Feel No Pain, Thorns, Flame Barrier), poison it applies, the cards it draws or creates, and the damage and block procs the proc rules predict for it.
- **Order** (`SimEngine`, from `CombatManager.DoTurnEnd` and the hook order): the play, then your end of turn: Orichalcum's check (you had no block), Plating, your powers (Hailstorm, The Bomb) before your relics (Orichalcum, Cloak Clasp, Ripple Basin, Screaming Flagon, Stone Calendar), orb passives front first (Gold-Plated Cables' extra trigger goes to whichever orb is in front; a Glass orb's base value drops by 1 each time its passive fires, Focus adds on top, and its evoke is twice its current passive), Ethereal cards exhausting (Feel No Pain, Charon's Ashes, Forgotten Soul), then cards in hand (Burn, Regret...), debuffs on you, Consuming Shadow evokes, Parrying Shield, then Disintegration. Then the enemy turn: poison tick, block cleared unless Barricade, each hit with your Thorns, block, Osty, HP-loss effects (Tungsten Rod, Beating Remnant, whose budget carries over from your turn and isn't spent by hits Buffer stops, Buffer), Flame Barrier, Reflect. Every block gain sets off Juggernaut. "Dead before it acts" (lethal) means dead by the end of your turn or by its own poison tick, not killed by retaliation during its attack.
- **Random targets** are explored: one branch per possible target, with enemies killed earlier no longer targets. Branches that end up identical are merged (at most 512 kept; past that, nothing is claimed as guaranteed).
- **Good outcomes need every branch; bad ones need only one.** Lethal and Imbalanced show only when they happen in every branch; wake warnings show when they happen in any branch; Incoming Damage shows the worst branch.
- **Lethal tab** (`lethal`, the skull): after playing this card (aimed at any one enemy, for single-target cards), some enemy is dead before it acts in every branch, and ending your turn now wouldn't kill it. That counts the card's hits, orbs, procs, and everything your end of turn does: Stone Armor with Juggernaut (Plating's block at end of turn sets off Juggernaut), the Juggernaut card with Orichalcum and no block, poison that kills it at the start of its turn.
- **Knowable information only:** it reads what the game shows and the game's rules; random targets are branched, never read from the RNG.
- **Speed:** simulations run on the game's frame, only when the state changes (results are cached). A typical hand takes well under a millisecond. Card simulations get 4 ms per frame (`SimBudgetMs`); in branch-heavy fights the rest finish over the next frames, so tabs can appear a few frames late but the game never stalls. Branches are capped at 128; past that nothing is claimed as guaranteed, and damage to you comes from a run where random hits land on nobody (never an underestimate).
- Approximations: orb values of newly channelled orbs use base values plus Focus and Infused Core; a card's procs all land after its hits; poison a card applies is one tick; a card's block split across several gains drops the remainder; random card draws and random orb types (Chaos) aren't simulated beyond their count.

## Shuffle Warning

**What you see:** a **discard pile** tab (the game's own discard pile icon) on draw cards in your hand whose draw is more than your draw pile, so playing them reshuffles your discard pile mid-turn. Nothing else: you know what a reshuffle means for the cards in your hand.

Draw cards are every `CardPileCmd.Draw` caller in the decompile (TRACKER §6). Most draw their Cards amount; Dagger Throw, Escape Plan and Pillage draw one (Pillage at least one); Scrawl draws up to a full hand; Compile Driver draws one per distinct orb type (the card's own calculation); Restlessness draws only when it's the only card in hand; Mad Science draws only with the Wisdom rider. A card that plays twice draws twice. The discard pile icon is read from the live discard pile button (`NCombatUi.DiscardPile` → `Icon`).

**Not yet:** draw potions (potion slots have no tab yet).

---

## Intent Forecast

⚪ Planned. Needs research into monster AI first (TRACKER §7). Registered but does nothing.
