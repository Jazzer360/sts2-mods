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
Every simulator rule gets a scenario test from the decompile; bugs get a failing test first. One simulation stays under 4 ms on a dev machine and 30 ms on CI runners (replaced by counted work and a per-step copy budget, decision #31).

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

### 26. The simulator is the one model: every prediction comes from it, every check compares the game with it — active (2026-11-11)
Development builds the outcome simulator as the single model of the game, for single-player and multiplayer alike. Every prediction shown is the simulator's output, and every audit compares what the game did with the simulator's prediction; a mismatch is a simulator bug (or an input the adapter read wrong) and is fixed there, with a test. The adapter only reads the game's state into the simulator: counters, flags, and amounts from the game's own damage and block math (inputs, not a copy of the math). Rules live in the simulator.
- **Revisits #12** ("proc rules decide sources and amounts, the simulator decides consequences"): about 100 trigger rules still live in ProcIndicatorsFeature, checked against the game directly. They move into the simulator family by family (each with its own tests, single-player tabs added to the golden test first so a move can't change them unnoticed); new rules go straight into the simulator.
*Why:* two models drift apart, and multiplayer needs triggers across players and through chains (a teammate's Juggernaut set off by your block card through Beacon of Hope), which per-player rules reading one play can't express. One model, checked everywhere, is what makes every prediction trustworthy.
*Replaced:* #12's split by kind of knowledge (rules for which sources fire, simulator for consequences).

### 27. Tabs describe the state after the play queue resolves — replaced by #28 (2026-11-11)
Plays resolve one at a time, in single-player as in multiplayer: you can play a card before the previous one has resolved, and a teammate's play can be waiting in the queue too. A card's tabs and predictions describe what happens when it resolves, after everything queued before it: the simulator runs the queued plays first (a lead-in), then the card. A queued play with random outcomes (Bouncing Flask's targets) branches like any other random outcome, never rolled, so a tab that needs certainty (lethal) shows only if it holds in every branch. What the lead-in can't model is logged by the audit, not reported (as quick plays are today).
- **Proc rules move into the simulator (#26) with a shadow mode:** the old rule and the new one both run in game while a family moves; any card where they disagree is logged and reported, and the old rule is deleted once real fights show none. (The proc rules only run in game, so there's no offline baseline of today's tabs to test against.)
*Why:* the queue is one concept for both modes; branching is already how the simulator handles what can't be known, and it keeps the RNG line intact.
*Replaced:* quick plays left unaudited (a play made before the previous one resolved: its tabs came from the state before it).

### 28. Tabs show the board as it is; each play is checked against a prediction made as it begins to resolve — active (2026-11-11)
Tabs, Incoming Damage and the Death Skull come from the board on screen, including while a play resolves. They don't run plays still resolving or queued. The proc audit rebuilds a play's prediction the moment its action starts (`ActionExecutor.BeforeActionExecuted`), from the board at that moment, through the same code as the tabs. Nothing is pending ahead of it, so the check is exact, and every play is checked, quick ones included. When the prediction can't be rebuilt, the play is checked against the tabs it last showed in hand, as before (logged only if a play finished in between).
- **Tabs update as a play resolves:** every hit, block or power the resolving card applies changes the board, and every prediction is recomputed from it on that frame. What hasn't happened yet isn't counted, so a good outcome (lethal, a kill, the Death Skull) appears when the hit that causes it lands, never early. Example: Bouncing Flask on an enemy at 15 HP with Strike (6) queued: Strike's lethal tab waits until the Flask's bounces bring it into range. If two bounces already have, it shows then, before the third. Warnings and Incoming Damage can read slightly high for that moment (before the hit that kills an attacker lands).
- The shadow-mode plan for moving proc rules into the simulator (from #27) stays.
*Why:* a prediction is reliable when nothing is resolving, and that is exactly the moment a play begins. Running the queue first in the tabs cost a play-queue reader, a snapshot, per-card queue prefixes and an extra audit capture. In multiplayer the queue isn't fully visible (a teammate's queued card, a client's unconfirmed card), so tabs built on it would rest on what the player can't see. The tabs are only off while a play resolves, under a second, and nobody decides a play in that window.
*Replaced:* #27 (the lead-in: queued plays run first in every prediction, and the audit compared the tabs from when you played the card).

### 29. Another action inside your play: its snapshot checks are logged, not reported — active (2026-11-11)
While your play waits for your choice (Burning Pact's pick, Decisions, Decisions), the game runs other players' queued actions (`ActionQueueSet.GetReadyAction` skips a paused action). Their history entries and flashes are already kept out of your play, but the before/after snapshots (state after the play, every power, everything else) span the whole action and would include them. For such a play, those three checks are logged with the action that ran, and counted in the combat summary; the history-based checks still report. Single-player can't hit it: the paused play blocks your only queue.
*Why:* nothing can separate a teammate's effects from yours inside one snapshot without a second snapshot model; a visible, counted skip is better than false reports or a check that could hide a mismatch.
*Replaced:* nothing (the case wasn't handled).

### 30. Teammate Incoming Damage is shown from what you can see, and checked on every game — active (2026-11-12)
Hovering a teammate shows their Incoming Damage, from the same simulation as yours run once more without the cards in their hand (decision #25): their hand size still counts (Cloak Clasp, Screaming Flagon, Regret's amount), the cards in it don't (Burn, Decay, Regret). The enemy-turn check compares every teammate's HP lost, and their Osty's, on your own game, including a teammate who runs Spire QoL too: a teammate is simulated through its own path (not the one their game checks for themselves), and your screen shows it. A teammate's end-of-turn effects are the same as yours: a parity test runs each end-of-turn effect once on you and once on a teammate and requires the same block and damage.
*Why:* multiplayer is supported fully; a number on screen needs a check on the same game, and an effect applied only to you (Cloak Clasp, Ripple Basin and Parrying Shield were) has to fail a test, not wait for a report.
*Replaced:* the teammate HP check skipped teammates known to run Spire QoL (host only).

### 31. Simulator performance is limited by counted work, and every step has a copy budget — active (2026-11-12)
The performance tests count what a simulation does (state copies, branches stepped, steps, bytes allocated), which is the same on every machine, and fail when a heavy scenario does more than today's work plus headroom. A further test checks the bound itself over a grid of fights (one to five enemies, one to four players, no orbs to full Lightning, Beacon of Hope and Juggernaut, Shatter): no step makes more than `SimEngine.StepCopyBudget` (2048) state copies, and none steps more than `MaxBranches` branches. Past a step's budget, its random choices follow their first target and the run is out of branches, the same fallback as the branch cap (nothing guaranteed; damage to you from the safe run). Effects chained inside one step merge identical results as steps do. One timing check stays, at 50 ms per simulation, for a slowdown that neither copies nor allocates.
*Why:* timing depends on the CPU and its caches, so the 4 ms limit failed on a cold run and needed a separate CI limit; counts don't. Counting also found what timing never covered: Shatter branched inside one step (4^20 copies with ten Lightning orbs and four enemies, the frame never finished), and Beacon of Hope with Juggernaut multiplied its branches inside a step until it ran out of memory with four players.
*Replaced:* decision #5's limits (one simulation under 4 ms on a dev machine, 30 ms on CI runners).

### 32. One build for the stable and beta game versions — active (2026-11-12)
The mod is built against the newest game (v0.113.0, beta) and keeps working on the previous one (v0.111.0, stable): game values whose names changed are read by name with the old name as the fallback (a card's HitCount, then Repeat, then the fixed count its old code used), and hooks are called through the game's own static `Hook` methods that exist in both. The coverage ledger is generated from the newest decompile; on the older game the coverage check reports the version difference once.
*Why:* players on Steam's beta branch get the new characters and cards first, while most stay on stable; one release serves both without a second build.
*Replaced:* nothing (first game update with a beta branch).

### 33. Main Saves is its own mod — active (2026-11-12)
Using the normal profiles while modded is a separate mod (`mods/MainSaves`) with one prefix on `UserDataPathProvider.GetAccountDir`, not a Spire QoL setting.
*Why:* it changes what the game does (which saves it reads and writes), and Spire QoL only observes (decision #10). As its own mod, only players who install it get it, and Spire QoL stays safe to install without touching anyone's saves. It patches only the current-mode answer, so the game's own first-time copy and its "running modded" state are unchanged.
*Replaced:* nothing (before, a third-party mod did this as a side effect).

### 34. No error reports while a mod that changes the game is loaded — active (2026-11-12)
Spire QoL sends nothing (crashes or mismatches) while another loaded mod changes the game: its manifest's `affects_gameplay` is true or missing (the game's default), or its assemblies define game models (cards, relics, powers, monsters). The audits still run and log. Every report sent lists the other loaded mods (`other_mods` tag).
*Why:* the predictions model the unmodded game, so another mod's mechanics would turn up as our mismatches, and its content as our crashes; those reports can't be fixed here. A crash of ours that only happens alongside another mod is lost, which is the price of not chasing other mods' content. Display-only mods (affects_gameplay false, no models) don't stop reports, and the tag shows them in case one interferes.
*Replaced:* nothing (reports were sent whatever else was loaded).

### 35. Your HP in the simulator, only when it's given — active (2026-11-13)
The simulator tracks your HP when the adapter sets it (SimPlayer.Hp), with what prevents death in the game's order (Fairy in a Bottle, then Lizard Tail, each healing you from 0). Taken stays the full damage, overkill included (what Incoming Damage shows, and what the forecast check compares: it adds each hit's OverkillDamage). States that don't set your HP (the generated single-player fights, older replay cases) never die, so their results are unchanged.
*Why:* a lethal enemy turn and a revive are what matter most on that screen, and the simulator already has every hit in order; doing it in the display instead would miss revives partway through the enemy turn.
*Replaced:* nothing (death wasn't predicted; Lizard Tail and Fairy in a Bottle were reviewed as "ignore").

### 36. Slow frames: report the mod's own, log everything — active (2026-11-14)
The frame loop times the mod's own work each frame, per feature. Over 30 ms it's logged every time; over 50 ms it's also an error report (`perf`, grouped by the slowest feature), from that feature's second such frame in a session. A slow frame where the mod's share stayed small is the game's own work and is only logged (rate-limited), never reported.
*Why:* a stutter from our work is a bug we can fix, and the report carries the game state that caused it. A slow PC or the game's loading would otherwise send reports from every player on weaker hardware, none of them ours to fix. The first slow frame per feature is often its code warming up in the first fight, so it isn't sent.
*Replaced:* nothing (slow frames weren't measured).

### 37. Out-of-combat reminders read each event option's code — active (2026-11-14)
Run Reminders shows, beside an event option, the relics that change what it does. What an option does comes from its own compiled code: the game commands its OnChosen runs (adding a card or curse, losing HP, gaining gold, getting a potion, resting), following calls into the event's own methods but not delegates (those are the options of the next page). Relics map to those effects in one table (`Data/ChoiceReminders.cs`); card rewards get a panel of the relics that react to the card you take, with their counters. A check compares, for each option you choose, the reminder relics that flashed with the icons shown, and reports a relic that reacted without one. Coverage: every relic overriding a choice hook (`Diagnostics/ChoiceHooks.txt`) has a rule or a reviewed reason, and `docs/RELICS.md` is generated with every relic and what the mod shows for it, in and out of fights.
*Why:* option text varies by event and language, and hand-tagging some 70 events would drift with every game update; the code is the rule the game follows, the same reading the play checks already use (`CodeIndex`), so new events are covered as they appear. Following delegates would put the next page's effects (a fight, another choice) on the current option. A generated relic list keeps the coverage table from going stale.
*Replaced:* nothing (goal 3 was planned in ROADMAP: reminders on the choice's own button). Icons sit beside the option rather than inside it, so they never cover its text.

### 38. Teammates' hands in what's shown: a setting, off by default — active (2026-11-14)
Revisits #25, which left open whether to also show what a player could know only by being told: the cards in teammates' hands. A setting, **Count teammates' hands** (`TeammateHands`, off by default), runs everything shown (your Incoming Damage, the Death Skull, card tabs, Teammate Incoming Damage) on the whole state. Off, as before, what's shown runs on `SimState.WithoutTeammateHands`, which also drops their Ethereal cards now that the simulator exhausts them (Feel No Pain, Charon's Ashes, Forgotten Soul). The enemy-turn check always uses the whole state (`TurnEndModel.Full`).
*Why:* teammates' Ethereal exhausts can kill enemies before they act, so their hand now changes your own forecast, not only their HP. Players who tell each other their hands can see that; the hand is information a player can have, not a roll, so it's within the knowable-information rule. Off by default keeps #25's reason: working the game out together is part of co-op.
*Replaced:* #25's display rule, which is now the default instead of the only option. Before this, your forecast came from the whole state, which was the same thing while a teammate's hand only changed their own HP.

### 39. End-of-turn auto-plays are simulated, a branch per random pick — active (2026-11-14)
The simulator plays your end-of-turn auto-plays (the game's auto-play phase, before every other end-of-turn effect): Stampede's random Attack and Simpleton's random cheapest card are a branch per card they could pick (and per target, for a card aimed at one enemy, as `CardCmd.AutoPlay` picks), from the cards in your hand; Howl from Beyond plays from your exhaust pile; I Am Invincible plays from the top of your draw pile only when every card there is a copy of it (otherwise its being on top is hidden draw order). A pick that changes your hand (draws, made cards) ends what's predicted for later picks in that branch, and the forecast check skips such a turn. Auto-played cards carry no procs from the proc rules.
*Why:* the picks are random choices among cards you can see, the same as random targets, which the simulator already branches (good outcomes need every branch, damage to you is the worst). Leaving them out made Incoming Damage and the lethal tab wrong whenever you had them, and the forecast check skipped every such turn.
*Replaced:* the forecast check skipping any turn with an auto-play of yours (0.31.35), and the coverage gaps for these cards and powers.

### 40. Extra turns: the forecast stays the enemies' next turn, with an icon — active (2026-11-14)
When ending your turn now gives you another turn first (Pael's Eye with nothing played, Extra Turn), Incoming Damage keeps forecasting the enemies' next turn as if it came next, and shows the source's icon with "+1 turn" (the game's own `Hook.ShouldTakeExtraTurn`, a pure query).
*Why:* the owner's choice among three: this one, the enemy hits without your end-of-turn block (it decays when the extra turn starts), or only what your end of turn does. Keeping the forecast leaves every other check and tab unchanged; the icon says the enemies aren't next, and that your end-of-turn block won't be there when they act.
*Replaced:* the coverage gap for Extra Turn and Pael's Eye's extra turn (no sign of it at all).

### 41. Potion tabs: the card tab's art, hanging point down under each potion — active (2026-11-14)
Relics that react to drinking a potion get a tab under that potion in the top bar: a copy of a hand card's enchantment tab (the card tabs' art), turned the other way so its point hangs down like a banner, with the relic's icon. Same language as the card tabs (one per source, plain or crossed), keys `potion:<Relic>`. First sources: Reptile Trinket, Belt Buckle (your last potion), White Beast Statue (while your slots are full: its reward would be lost).
*Why:* the owner's call: these reminders belong on the potions, and a banner reads better hanging under a potion than a tab pointing up into it.
*Replaced:* the coverage gaps for Reptile Trinket and Belt Buckle ("potions have no tabs yet").

### 42. A card as an icon: the game's tiny card, for cards that come back to your hand — active (2026-11-14)
Cards have no relic or power icon, so where the mod needs one it shows the game's tiny card from the run history's deck list (`NTinyCard`: the card frame in its character's color, its type's portrait shape, its rarity's banner), built from the game's own deck entry. First use: cards that come back to your hand when you do something. Make It So (every 3rd Skill this turn, from any pile) gets a relic tracker row with the count; Make It So, Right Hand Hand (cards costing 2+) and Whistling Arrows (shooting an arrow), while in your discard pile, get a plain tab on the play that brings them back. Bolas, Bounce and Thrumming Hatchet come back next turn by their own text: nothing shown. Shared rules in `Procs/ReturnToHand.cs`; a scan check flags any other card whose hooks move a card.
*Why:* the owner asked for Make It So's count (hard to track by hand) and for the other returns as tabs; the tiny card is game art that reads as "a card", the same for the tracker and the tabs. The count lives in the tracker because it's progress you push by playing, like the relic counters.
*Replaced:* Make It So counted only in your hand size after a play (0.39.0), and Right Hand Hand and Whistling Arrows reviewed as "hand read live".

### 43. Run Reminders get their own settings section, with a switch per place — active (2026-11-15)
The Relics page splits into **In fights** (On cards and Tracker columns, as before) and **Outside fights**, whose columns are the places a reminder shows: Card rewards (the reward panel's row), Events (icons beside options) and Shops (icons beside prices). Each relic gets a switch in every place it shows there (`reminder-reward:`, `reminder-event:`, `reminder-shop:<Relic>`), with All on / All off per column. Where a rule shows is defined once (`ReminderRule.In`), used by the feature, and the catalog check holds the menu's columns to it. Older settings migrate: a relic switched off stays off everywhere; the old group switch maps to the Run Reminders feature switch.
*Why:* the owner's call: out-of-fight reminders don't fit the On cards / Tracker columns, and one relic can show in very different places (Lucky Fysh's gold on rewards against its icon on shop prices), so a player may want one without the other.
*Replaced:* one `reminder:<Relic>` switch per relic, listed in the On cards column next to the fight tabs.

### 44. Free-card tabs: the energy saved, "?" under Snecko Eye, crossed when wasted — active (2026-11-15)
Free Card (Tune), Free Attack (Unrelenting) and Free Skill (Pounce) get a tab on every card in your hand that would use one up: the energy it saves as the tab's number (an exception to "numbers only for block, damage, summons"), crossed on 0-cost and X-cost cards (the game takes the charge from any card played from hand, so they waste it). Under Snecko Eye the free card shows a re-rolled cost as 0, so the cost is hidden: the tab says `?` and is never crossed (crossing it would give away a 0). X-cost cards stay crossed. Rules in `Procs/FreeCards.cs`; the play check compares each charge spent with the tabs.
*Why:* the owner asked for the energy saved, and chose the crossed tab for wasted charges (the design language's "gives up a bonus"); the `?` keeps the tab to what a player can know.
*Replaced:* nothing shown (the cost was read live).

### 45. Rest Preview: HP after resting, and the boss fight's start before the boss — active (2026-11-15)
A new feature: under Rest, the HP you'll have and the heal wasted over max; at the last rest site before the boss with Pantograph or an unused Bath Robes, the HP the boss fight starts with under every option (rested or not), and how much of the rest heal doesn't carry into it. From the game's own heal amount and the relics' own "active before the boss" status; checked after resting and at the boss fight's start.
*Why:* the owner asked for an overview of what resting does, and a reminder of Pantograph when it makes resting partly wasted.
*Replaced:* nothing (the game's rest text shows the heal amount only).
