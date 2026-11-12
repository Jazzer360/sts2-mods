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
*Known exception to #3:* if the cap is hit, that card's simulator tabs are missing for a frame or two, warnings included. Accepted because the cap is only reached in pathological states; the audit notes it (SimPending).

### 10. Observe-only Harmony patches are allowed — active (2026-11-10)
Spire QoL may patch game methods only to observe (a prefix that records a call and changes nothing). First use: `CardPileCmd.Shuffle`, which the game doesn't record in its history.
*Replaced:* "no Harmony patches; everything is read from game state each frame".
*Why:* some events leave no trace in state or history; reconstructing them from side effects (pile events) was fragile. Patches that change game behavior are still out for this mod.

### 11. Audits attribute effects to the game's play action — active (2026-11-10)
The proc audit attributes every history entry, relic/power flash and shuffle to the `PlayCardAction` running when it happened (`ActionExecutor.BeforeActionExecuted`, `CombatHistory.Changed`, `GameAction.AfterFinished`), and checks the play when its action finishes, capturing the state it left at that moment.
*Replaced:* windows between plays split by energy spends, best-fit splits for 0-cost cards and potions, and an "action queue idle" capture of the after-play state.
*Why:* most audit mismatches were the window split's own mistakes; the game already knows which action caused what.

### 12. One model of a card play — active (2026-11-10, finished in 0.28.0)
What a play does (draws, exhausts, block gains, shuffles, procs) was described by two models (the proc rules in `ProcIndicatorsFeature`/`PlayFacts`, and the simulator's play step); each rule now lives in exactly one of them, as below.
*Why:* chained effects had to be written in both places, and a miss in one (Dark Embrace draws) went unnoticed.
*Result:* the split is by kind of knowledge, with no rule in two places. **Proc rules** decide which sources fire and how much (they need game objects: counters, private flags, and the game's own damage and block math). **The simulator** decides every consequence: chains of draws and shuffles (`Sim/PlayChain.cs`), block gains and Juggernaut, kills (Gremlin Horn, enemy death effects), block breaks (Hand Drill, Burrowed), stuns (Flutter, Plow, Shriek), The Gambit, Slippery, enemy reactions (Curl Up, Sneaky, Skittish, Enrage), and the end of turn. Tabs for consequences read the simulator's branches.
*Not moved, on purpose:* proc amounts stay in the proc rules: they come from the game's damage and block hooks, and a pure copy of that math would be a second model. Black Hole (stars) and Metronome (channels) stay as the rules' second pass: the simulator doesn't model stars or channel counts, so nothing is duplicated. `PlayFacts.Kills` stays as a pre-simulation estimate of where random and all-enemy procs can land (procs feed the simulator, so they can't wait for it).

### 13. Game types referenced by type, not by name — active (2026-11-10)
Rules name game classes with `nameof(...)`/type patterns where the class is public, so a renamed or removed class in a game update is a build error instead of a silently dead rule. Settings keys stay strings (they're saved in players' settings).

### 14. Every combat mechanic is reviewed, and the unreviewed is reported — active (2026-11-10)
Every game class that overrides a combat hook has an explicit entry in the coverage ledger (covered, ignore or gap), written from its hook bodies. The running game is compared with the ledger at load; anything new, changed or unreviewed is reported when it shows up in a fight, and card code is compared with the per-card tables the predictions use.
*Why:* there are far more interactions than can be coded ahead; players' games must surface what the mod doesn't know, including after game updates, instead of it failing silently.
*Replaced:* a first version that counted a class as covered whenever the mod's code named it (Doom was "covered" because one rule checked for it) and classified the rest from the commands they call (Bing Bong, a deck-only relic, was called a combat gap). Both were wrong; entries now come from reading the hooks.

### 15. Settings stay complete, checked at load — active (2026-11-10)
Every proc rule, tracker row, double-play source and feature has a settings row or switch, and every row something behind it; checked from the compiled mod and game when the mod loads (CatalogCheck), plus tab kinds shown in play that the settings don't describe.

### 16. No dead code — active (2026-11-10)
Code that isn't part of what the mod does is removed, not kept for later: unused members (compiler analyzers IDE0051/IDE0052/IDE0060 and `scripts/dead-code.mjs` for public and internal ones) and stub features. Intent Forecast's empty feature was removed; its plan stays in docs/TRACKER.md §7 and ROADMAP until it's built.

### 17. Goals, and "everything is accounted for" — active (2026-11-10)
Spire QoL's goals are listed in docs/ROADMAP.md: a tab whenever a card does anything beyond its text, every fight-affecting relic tracked, reminders for out-of-combat decisions a relic changes, only knowable information, and every effect either predicted or reported. The default is complete coverage; what shows by default is tuned afterwards in settings.
*Added:* out-of-combat reminders (goal 3), as Relic Reminders did in StS1.

### 18. Every fight-affecting relic is tracked or reviewed; unexplained changes are reported, not waived — active (2026-11-10)
Every relic that can affect a fight (any hook outside the out-of-combat list) has a tab, a tracker row or a reviewed reason (RelicTracking, from its hook bodies), checked when you have it. The proc audit snapshots everything a play can change (energy, stars, gold, HP, max HP, every card's state and pile, relics) and reports any change no code that ran explains, naming the possible sources. Random draws and auto-plays don't waive a check: the drawn and auto-played cards and the sources reacting to draws become explanations instead. Only comparisons that are impossible stay skipped (the simulator can't predict an auto-played random card).
*Why:* aim as close to 100% as the limits of evaluation allow; a few possible causes are a reason to name them, not to skip.
*Safety:* predictions stay on the safe side for you: Incoming Damage is the worst branch (the safe fallback when branches run out never lets a random hit save you), good outcomes need every branch. Known gaps that could underestimate damage to you are listed first in the tracker.

### 19. Shortfalls in effects that aren't a choice are reported — active (2026-11-11)
The proc check reports a play that drew or channeled fewer than predicted, not only more. Excused only by what the history shows: the fight ended (nothing more runs), fewer exhausts than the upper bound (Dark Embrace and Joss Paper draw per exhaust), or no orb slots left (the channel fails). Exhausts, discards and created cards stay log-only below the prediction, since those are "up to" counts the player picks (Guards, Burning Pact's choice).
*Why:* FTL drew 0 of a predicted 1 (its play limit) and only showed in the log; players' games never reported it. The simulator is the base for later features, so a model gap in either direction should surface.
*Replaced:* shortfalls in every budgeted effect being log-only (never written down as a decision; part of #11's budget check).

### 20. The mod checks GitHub for new versions on the main menu — active (2026-11-11)
Spire QoL reads GitHub's public releases list once each time the main menu appears (never during a run) and shows how many versions behind you are above the game's "MODDED" note, linking to the releases page. One anonymous request (the mod's id as User-Agent), off the main thread; a switch turns it off. The game's own label is left alone (a label of ours is added above it).
*Why:* releases come often, and players otherwise don't know a fix is out; reports from old builds cost review time.
*Replaced:* no network access other than error reports (Sentry).

### 21. Fixed reports from older versions don't reopen issues — active (2026-11-11)
The Sentry sync reads the version that fixed a closed issue from its closing comment's release link. When the issue is seen again only from versions older than that (players who haven't updated), it stays closed and is resolved again in Sentry (in the fixing release); from the fixing version or newer, or with no fix version, it reopens with the newest occurrence as its report. New issues are created whatever the version, noting when the version is older than the newest release.
*Why:* releases are frequent and many players won't update; old builds re-reporting fixed problems would bury the real regressions.
*Replaced:* reopening on any occurrence after the close, with the original report left as the issue body.

### 22. Chances only for "dies if you end your turn now"; card tabs show certainties — active, open for review (2026-11-11)
The only chance the mod shows is beside an enemy's HP skull (Death Skull): the chance it dies before acting if you end your turn now, from the simulator's branches weighted by their share of the rolls (each random target equally likely). Card tabs stay certainties: the lethal tab means some aim of this card is needed for an enemy to be certainly dead before it acts; crossed tabs warn when something is possible in any branch. No chances on tabs, no enemy skulls following the hovered or dragged card, no kill-count spread.
*Why:* a chance can't be checked against what happened, only the simulation behind it can (the audits do that, branch by branch). Odds that depend on where a card is aimed and how rolls fall get hard to read and to remember, while certain outcomes are the common case and simple to read. "Dies if I end my turn now" is one easy question.
*Open:* the owner isn't settled on this; it's what's built for now, not a final answer (tracker §17).
*Replaced:* the open question of odds on random outcomes (tracker §17), and a proposal for chances on tabs, hover-following enemy skulls and a kill-count spread.

### 23. Running out of branches stops exploring; branches share what they don't change — active (2026-11-11)
When a simulation reaches 128 branches, it keeps a sample of 32 and each follows one outcome per random step from there (random hits land on the first possible target, in place), instead of keeping on splitting and throwing the extra branches away. Branches share their orbs and their teammates until one changes them (copy on change). Identical branches still merge, adding their shares of the rolls.
*Why:* multiplayer multiplies random hits (four Defects' Lightning: 4^40 roll sequences); the cap bounded the number of branches but not the work, which reached 47 ms for one simulation. Now 2.4 ms there, and single-player's worst case went from 2.8 to 1.0 ms.
*Trade-off:* past the cap nothing was guaranteed already (damage to you comes from the safe run where random hits land on nobody); the "can happen" warnings now come from a 32-branch sample instead of a partial 128.

### 24. Multiplayer is supported fully, checks first — active (2026-11-11)
Multiplayer gets the same treatment as single-player: everything a player can see about teammates (block, powers, orbs, relics, Osty) is modelled, and every prediction about them is checked by the audits, before anything new is shown on screen. Steps: reports say who was in the fight; the coverage ledger is reviewed for what a teammate's copy of each class does to you; then teammates become full players in the same simulation (their block, HP and incoming damage, Beacon of Hope's shared block, attacks changed by Tank, Intercept and Covered), checked per player; then their Incoming Damage on hover.
- **One simulation per card, not one per player.** Enemy attacks hit every player (monster attacks target all opponents unless a card like Intercept changes it), so each player's outcome comes from the same run: each hit's amount for that player (their own Vulnerable, Intangible, Tank...) and their own block, HP and Osty.
- **Teammates take no further actions this turn.** Predictions are for "if everyone ended their turn now"; a teammate's next play changes them. That's the only reading that can be checked, and the end-of-turn check compares the last frame of the players' turn (after everyone has ended) with what happened.
- **Their hand isn't known:** what depends on it doesn't count (the non-optimistic side), until it's known whether the game shows it.
- **Single-player stays first:** nothing here may slow it down or change its results (the simulator's performance tests include a four-Defect case).
*Why:* co-op works with the mod already (it only reads); showing teammates' outcomes is only useful if it's right, and it can only be shown right by checking it against what the game does.

### 25. Teammates' hands: used by the simulator and the audits, not shown — active (2026-11-11)
In multiplayer every client simulates the whole fight, so teammates' hands are in memory; the game shows only their hand size (and their deck). The simulator and the audits use everything that isn't a roll, teammates' hands included: with perfect play among perfect players they'd be revealed, and a simulation that knows them is the strongest check (a teammate's Stampede play is explained, not a mismatch). What's shown is what a player knows without communication: hand size, deck, powers, orbs, block, not the cards in hand. A shown prediction never depends on a teammate's hand; when an effect would make it (Stampede, cards that act in hand at end of turn), the shown prediction leaves their hand out (the non-optimistic side) and the audit's simulation uses it. Whether to also show what a player *could* know (their hand) is open. Unchanged: never the RNG or the seed.
*Why:* auditing only serves to harden the simulator, which is what makes the mod reliable; it shouldn't be held back by what's shown. Working out a multiplayer game together is part of its charm, so the display stays at what's known.
