# Decisions

Design and engineering decisions for these mods, newest last. Each says what was decided, why, and what it replaced. **A decision stays until there's a good reason to change it**: changing one means adding a new entry that names the old one and the reason, not quietly drifting back. Going back to an earlier path needs a reason the earlier entry didn't already weigh.

Status: **active**, **superseded by #N**, or **in progress**.

---

### 1. Only knowable information (hard rule) — active
Spire QoL shows nothing a player with perfect memory and full knowledge of the rules couldn't know. Never read the RNG or seed, the draw order, moves beyond the intent on screen, or anything about rewards or the map ahead. Random outcomes show the amount or range, never the target or the roll. Diagnostics may read anything, as long as none of it reaches the screen. Details in CLAUDE.md.
*Why:* the mod is a memory and arithmetic aid, not a lookahead. Reverse-engineering the seed is possible for a player, but looking ahead is the line.

### 2. Plain vs crossed tabs — active
A plain tab means "playing this does something extra". A crossed tab means "this might be bad for you" (helps the enemy, hurts you, gives up a relic's bonus): a warning, not a verdict. One tab per source, the game's own icons, no text except replay counts.
*Why:* one visual language that reads at a glance; everything else is noise on the card.

### 3. Guaranteed good, possible bad — active
Good outcomes (lethal, Imbalanced) show only when they happen in every branch of random targets. Warnings (waking an enemy) show when they happen in any branch. Damage to you shows the worst branch. Targeting you choose is yours: lethal shows if some aim is lethal in every random branch.
*Why:* a tab promising a kill that doesn't happen costs a run; a warning that might not happen costs nothing.

### 4. An outcome simulator inside the mod — active (0.24.0)
Incoming Damage, lethal, Imbalanced and wake tabs come from one simulator (`mods/SpireQoL/Sim`): plain C#, no game types, fed by an adapter that reads the game. Random targets are branches, never rolls. It replaced per-feature hand calculations that disagreed with each other.
*Why:* chained effects (Plating → block → Juggernaut → a kill) need one model of the turn; a pure model can be tested without the game.
*Alternative weighed:* running the game's own code on a copy of the combat. Rejected: the game's commands are async, tied to Godot nodes and the RNG, and copying combat state isn't supported.

### 5. Tests with every simulator change; performance limits — active
Every simulator rule gets a scenario test from the decompile; bugs get a failing test first. One simulation stays under 4 ms on a dev machine and 30 ms on CI runners.

### 6. Audits verify every prediction; random outcomes excused only with proof — active
Every prediction has an audit comparing it with what the game did, reported through Sentry into GitHub issues. An effect is excused as random only when the history shows a random hit or kill caused it; multi-enemy fights stay audited. When a closed report comes back, the original fix is revisited and reverted if it didn't help.
*Why:* broad excuses hide real bugs; speculative fixes pile up.

### 7. Every change ships as a release — active
Version bump in the manifest and project, changelog section, push to main, `scripts/release.ps1`. A CI check fails if mod code changed without a release.

### 8. Steam Workshop only through CI — active
Not published to the Workshop unless the upload can run in CI. Releases stay on GitHub.

### 9. Card simulations run in parallel and the frame waits — active (0.26.0)
Each frame builds every card's play on the main thread (it reads the game), runs all the simulations in parallel on worker threads, and waits for them (capped at 250 ms), so tabs appear on the frame they belong to. Results are cached by a fingerprint of the whole state.
*Replaced:* a 4 ms per-frame budget that spread simulations over later frames (tabs could appear late), and an earlier non-blocking draft (tabs appeared when ready).
*Why:* correctness on the frame matters more than the frame's length; the work is independent per card and target, so it parallelizes well.

### 10. Observe-only Harmony patches are allowed — active (2026-11-10)
Spire QoL may patch game methods only to observe (a prefix that records a call and changes nothing). First use: `CardPileCmd.Shuffle`, which the game doesn't record in its history.
*Replaced:* "no Harmony patches; everything is read from game state each frame".
*Why:* some events leave no trace in state or history; reconstructing them from side effects (pile events) was fragile. Patches that change game behavior are still out for this mod.

### 11. Audits attribute effects to the game's play action — active (2026-11-10)
The proc audit attributes every history entry, relic/power flash and shuffle to the `PlayCardAction` running when it happened (`ActionExecutor.BeforeActionExecuted`, `CombatHistory.Changed`, `GameAction.AfterFinished`), and checks the play when its action finishes, capturing the state it left at that moment.
*Replaced:* windows between plays split by energy spends, best-fit splits for 0-cost cards and potions, and an "action queue idle" capture of the after-play state.
*Why:* most audit mismatches were the window split's own mistakes; the game already knows which action caused what.

### 12. One model of a card play — in progress (2026-11-10)
What a play does (draws, exhausts, block gains, shuffles, procs) is moving from two models (the proc rules in `ProcIndicatorsFeature`/`PlayFacts`, and the simulator's play step) toward one: pure, tested code computing the play's effects, which both the tabs and the simulator read. Started with draws (`Sim/DrawMath.cs`).
*Why:* chained effects had to be written in both places, and a miss in one (Dark Embrace draws) went unnoticed.
*Progress:* draws and shuffles (`Sim/PlayChain.cs`); kill-reactive tabs (Gremlin Horn, Infested, Steam Eruption, Surprise, Crab Rage, Ravenous) and Curl Up / Skittish tabs now read the simulator's branches instead of their own checks. Next: Juggernaut's tab from the simulator (the proc rules' second pass duplicates it), the remaining enemy reactions (Sneaky, Enrage), and moving proc damage/block amounts into pure rules the simulator applies itself.

### 13. Game types referenced by type, not by name — active (2026-11-10)
Rules name game classes with `nameof(...)`/type patterns where the class is public, so a renamed or removed class in a game update is a build error instead of a silently dead rule. Settings keys stay strings (they're saved in players' settings).
