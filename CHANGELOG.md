# Changelog

All notable changes to the mods in this repo. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow [SemVer](https://semver.org/). Each mod is versioned separately; entries are grouped under the mod they affect.

Every pull request or commit that changes behavior adds a line under **Unreleased**. When a mod is released, its Unreleased entries move under a version heading with the date and the game version it was tested on.

## [Unreleased]

### Spire QoL 0.35.0
#### Added
- Vigor and Gigantification tabs on the attacks that will spend them (Akabeko's Vigor on turn 1, for example).

### Spire QoL 0.34.2
#### Added
- The mod's own slow frames (over 50 ms) are sent as error reports, grouped by which part of the mod took the time. Slow frames that aren't the mod's (a slower PC, loading) are only logged; every slow frame of the mod's is logged.
#### Fixed
- Error reports no longer count Violent Chord's damage short when Slippery (Vantom) took it down to 1 (logs and error reports only).
- Block and damage from cards Lost Lute plays at random are no longer reported as unpredicted (logs and error reports only).
- A played card's random cost going back to normal is no longer reported as an untracked cost change (logs and error reports only).
- Punch of Joy turning a Sadness into Joy counts as its own created card (logs and error reports only).

### Main Saves 1.0.4
#### Changed
- Rebuilt with the shared code update (no change in what it does).

### Spire QoL 0.34.1
#### Added
- A slow-frame line in the game log: when the mod's work in one frame takes over 30 ms, which part took the time; when a frame stalls with the mod's share small, that it was the game's own work (logs only).

### Main Saves 1.0.3
#### Changed
- Rebuilt with the shared code update (no change in what it does).

### Spire QoL 0.34.0
#### Added
- A Nostalgia tab on the Attacks and Skills it will put on top of your draw pile.
#### Fixed
- Judgement kills its target at or below its threshold in the predictions (lethal, Incoming Damage, the enemy turn).
- A replayed attack's later plays hit without the Vigor (and Gigantification) its first play used: Wah Touch played twice with 8 Vigor hits 12, then 6.
- Unsettling Lamp: its tab shows on cards whose debuff is temporary (Dark Shackles), and the Strength loss and poison it doubles count in the predictions.
- The Hermit's discard makes room before it draws, so a full hand draws as many as the game does; a Curse it exhausts counts as an exhaust.
- The Magician's new orb slots come before its channels, so it no longer predicts evoking your orbs.

### Spire QoL 0.33.2
#### Fixed
- The play check no longer reports Throwing Axe firing on the first card of each fight: its double-play tab comes from the game's own play count (logs and error reports only).

### Spire QoL 0.33.1
#### Fixed
- Double-play tabs (Throwing Axe, Echo Form...) and the replay tab no longer show on cards that can't be played, such as curses.

### Spire QoL 0.33.0
#### Added
- A Stretch tab (the Fool) on block cards that will spend your Stretch. Cho Ki keeps it (it puts back what it had), and Entrench's block doesn't use it.

### Spire QoL 0.32.0
#### Added
- Incoming Damage shows a skull beside what you'll lose when the enemy turn kills you.
- When Fairy in a Bottle or Lizard Tail would keep you alive, Incoming Damage adds a row with its icon and your HP after the enemy turn (the Fairy is used first, as the game does; each heals you from 0).

### Spire QoL 0.31.40
#### Fixed
- Motif's draws (your first Wah, Cho and Ki each turn) count for the Shuffle Warning and draw procs, so a melody card that draws past your draw pile is marked.
- Ki Chant played several times shuffles when a later play finds your draw pile short; that shuffle counts for shuffle procs.
- A replayed shot at an enemy that isn't Impaled hits harder on its later plays with Serrated Arrows (its first play impales it): Rapid Shot played 4 times now counts 2, then 3 a hit (lethal tab, kills, Incoming Damage).
- The forecast check counts the full hit when an enemy's attack kills you and Fairy in a Bottle brings you back (logs and error reports only).

### Spire QoL 0.31.39
#### Fixed
- Repeat Shot hits once per energy (X): with no energy it deals nothing, so it no longer shows lethal (its Nunchaku, Kunai and Shuriken tabs still count it as an attack played).
- Slumbering Beetle: each hit through its block takes a Slumber stack, and the last wakes it stunned, so the prediction after the play and the forecast know it won't act.
- Adroit (enchantment): its block on every play of the card counts (block after the play, Incoming Damage, Juggernaut).
- The play check: Blossom Shot's Stretch, and Slumber counting down on a hit, are no longer reported (logs and error reports only).

### Spire QoL 0.31.38
#### Changed
- The Death Skull sits a little further from the HP numbers.
#### Fixed
- Swift's draws count (the Shuffle Warning, draw procs) the first time its card is played.
- Orbs you channel that get pushed out by later channels in the same play count their evoke (Voltaic filling your slots with Lightning).
- The play check: a Plasma orb evoked by a channel gives energy; an enemy power your end of turn sets off anyway (a Lightning orb waking the Lagavulin Matriarch or stunning a Shriek enemy) firing on a play with no tab is expected (logs and error reports only).

### Spire QoL 0.31.37
#### Added
- Mismatch and coverage reports are tagged with the card played, the check, the relic or power involved and the encounter, so related reports can be found together.
#### Fixed
- Surrounded (Kaiser Crab): a card aimed at the crab behind you turns you to face it, so after the play the other crab's attack is the one 50% stronger (intents and Incoming Damage after the play).
- Cho Ki keeps your Stretch for every play of a replay (it puts back the Stretch it uses).
- The Moon counts the Shivs it creates, The Magician the orbs it channels and the slots it adds, and Wheel of Fortune's draws count your hand after it's discarded; Ki Chant's discards count on every play of a replay.
- The play check: Sown's energy, Wrest's draws at an enemy it un-Impales, a new Axebot coming in with Stock, and procs set off by The Fool's random plays are no longer reported (logs and error reports only).
- The forecast check skips a turn followed by an extra turn instead of the enemy turn (logs and error reports only).
- A tab could show greyed out (its text and backdrop dimmed, like a disabled enchantment) when the card it hangs on had shown a disabled enchantment; tabs now always show at full color.

### Spire QoL 0.31.36
#### Fixed
- A replayed attack that makes its target Vulnerable (Break played twice) now counts the extra damage of its later plays in the lethal tab, kills and Incoming Damage.
- The play check no longer reports Razor Tooth as not firing: it never flashes, so the check now looks at whether the played card was upgraded (logs and error reports only).

### Spire QoL 0.31.35
#### Added
- Inferno: cards that cost you HP get a tab for its damage to every enemy, and its damage from losing HP at the end of your turn (Regret, Burn, Decay) or during a play (Galvanic, enemy Thorns) counts in Incoming Damage, kills before enemies act and the lethal tab.
#### Changed
- Error reports aren't sent while another mod that changes the game is loaded (its manifest says it affects gameplay, or it adds cards, relics, powers or monsters); the log says so once. Every report lists the other mods loaded.
#### Fixed
- The forecast check skips a turn where Stampede auto-played an attack at the end of your turn (a random card at a random target) (logs and error reports only).
- The play check: One-Two Punch being used up by the attack it replays, and Plow taking the Ceremonial Beast's Strength when it stuns her, are no longer reported (logs and error reports only).

### Main Saves 1.0.2
#### Changed
- Rebuilt with the shared code update (no change in what it does).

### Main Saves 1.0.1
#### Changed
- Rebuilt with the shared code update (no change in what it does).

### Spire QoL 0.31.34
#### Fixed
- The forecast check no longer reports an enemy your Thorns kills during its own attack as dying before it acted (its hit still lands) (logs and error reports only).
- The coverage check: the card scan read the owner of the game's template cards, which fails, so Wrest, Cho Firm, Ki Chant and Florescence were reported as unchecked; Ki Hop's and Wrest's draws at an Impaled target, and exhausts that pick cards by kind (Florescence's Shoot cards, Growth's Sadness), are now recognized; Yummy Cookie (it only changes its icon when copied) is no longer reported as a new relic with fight hooks (logs and error reports only).

### Spire QoL 0.31.33
#### Fixed
- Wuppy: while your pet lives it takes the attacks aimed at you, so Incoming Damage, the Death Skull and the forecast leave them out (your Thorns doesn't react to them).
- Stretch boosts only the first play of a replayed block card.
- Nail does nothing to an enemy that isn't Impaled, and a shot with no arrow applies nothing: their Weak no longer lowers the intents in the prediction.
- The play check: Vigor and Gigantification used up by your attack, and Tender taking Strength and Dexterity per card, are no longer reported.

### Spire QoL 0.31.32
#### Fixed
- Cards that gain block more than once (Cho Limbs, Cho Firm, Second Wind) had their block split across the gains in the simulator instead of each gain counting in full.
- Cho Barbs' Thorns counts (a temporary Thorns power gives real Thorns until the end of the enemy turn).
- Serrated Arrows and Wah Hammer: hits after the one that knocks your last arrow out of an enemy no longer get their bonus (Impaled is gone).
- Rain Shot's damage to every other enemy counts toward lethal.
- Vital Spark: playing a Tainted card raises the enemies' attacks on you (Incoming Damage, intents after the play).
- Galvanic's damage to you comes off your block in the prediction after the play.
- Lagavulin Matriarch: any damage through her block wakes her stunned (a proc's too), so she doesn't attack that turn.
- Damage procs after a single-target card (Violent Chord after Wah Ki) still hit the other enemies, even ones the card could have killed had it been aimed at them.
- The play check: Free Card being used up and the costs it lowered going back up, Mummified Hand's free card (it never flashes), Ki Hop's and Wrest's draws at the enemy actually targeted, a proc that fires as the play ends the fight, and Hardened Shell capping proc damage are no longer reported.

### Spire QoL 0.31.31
#### Fixed
- The Fool: the play check no longer reports a shot Impaling its target or spending Aim, an Impaled enemy losing its arrow to your hits, or a block card spending Stretch.
- Cards created, exhausted and discarded by the Fool's, tarot and event cards are counted (Mourning Blow turning into a Sadness, Jest, Sorrowful Strike, Brighten Up, Vitriol, Rummage, Self Deprecate, Blot Out the Sky, Florescence and The Shot, Growth, Ki Chant's discards and its shuffle when your draw pile is short, The Devil, The Hierophant, The Lovers, The Sun, Mighty Shield, Mighty Weapon).

### Spire QoL 0.31.30
#### Added
- The Fool (game v0.113.0 beta): tabs for Violent Chord (damage to every enemy) and Song Path (block) on your first Wah / Cho this turn, Lullaby (block) on the card that completes Wah, Cho and Ki, Motif and Lost Lute on your first melodies, Conductor's Baton on your third melody, Explosive Arrows on shots with an arrow to shoot, and Twine (block) and Yah (energy) on cards that knock your arrows loose. Ki Verse's replays show as a double-play tab.
- Obsidian Vice's end-of-turn damage counts toward lethal and the Death Skull.
#### Fixed
- The Fool: a Shoot card with no arrow to shoot was counted as dealing its damage (it does nothing); Nail was counted against enemies that aren't Impaled; Wah Blast's second hit, Cho Limbs' and Cho Firm's second block, and Aim on arrow attacks weren't counted.
- The Fool and tarot cards: draws and discards counted for the shuffle warning (Deep Stretch, Encore, Joy, Rummage, Steady Cadence, Wah Cho Ki, Ki Hop and Wrest on Impaled enemies, The High Priestess, The Sun, The Hermit, Wheel of Fortune).

### Main Saves 1.0.0
#### Added
- New mod: keeps using your normal profiles while mods are installed, instead of the game's separate modded profiles.

### Spire QoL 0.31.29
#### Fixed
- Game v0.113.0 (beta): multi-hit attacks whose hit count moved to a new card value (Fight Me, Gunk Up, Sovereign Blade, Spite, Celestial Might, Conflagration, Exterminate, Peck, Ricochet, Seven Stars, Sword Boomerang) counted one hit in lethal and proc tabs. Hits are read from the card's hit count on both game versions.
- Thumbscrew (v0.113.0): its extra hit on multi-hit attacks counts toward lethal and proc tabs.
#### Changed
- Coverage reviewed for game v0.113.0: every new relic, power, card and enchantment with combat hooks has a reviewed entry; arrow, extra-turn and damage-redirect hooks are checked.

### Spire QoL 0.31.28
#### Fixed
- The game could freeze with Shatter in hand and many orbs against several enemies (every orb's two evokes were explored at once; ten Lightning orbs against four enemies never finished).
- Multiplayer: Beacon of Hope with Juggernaut on several players could stall the game or run out of memory (each shared block gain's Juggernaut hit multiplied the outcomes). Every simulation step now has a fixed work limit; past it, nothing is claimed as guaranteed and damage to you is the worst case, as with too many random outcomes.

### Spire QoL 0.31.27
#### Added
- Teammate Incoming Damage (multiplayer): hovering a teammate shows what they'll take at end of turn, after their block, the same sum as yours, from what you can see (not the cards in their hand).
- Parrying Shield tab: on a card that makes it fire at end of turn, crossed on a card that stops it (Orichalcum no longer arming, Burns, Thorns).
#### Fixed
- Multiplayer: a teammate's Cloak Clasp, Ripple Basin and Parrying Shield count for them (their block, and Parrying Shield's damage toward lethal and the Death Skull).

### Spire QoL 0.31.26
#### Changed
- Multiplayer checks (logs and error reports): the state after your play now includes each teammate's block and HP; a play whose choice let a teammate's action run has its before/after checks logged instead of reported; cards aimed at a teammate or every player are flagged by the coverage check until the simulator models them.

### Spire QoL 0.31.25
#### Changed
- Cards played quickly, before the previous card finished: tabs, Incoming Damage and the Death Skull show the board as it is again, instead of running your queued plays first. The proc check now checks every play against a prediction made as it begins to resolve, so quick plays are fully checked (logs and error reports).

### Spire QoL 0.31.24
#### Fixed
- A card played while others were still resolving: its tabs ran only the plays ahead of it (it counted itself), and the proc check now compares a play with the tabs it had when you played it (a queued card is recomputed in your hand until it starts, so the check used to compare later tabs).

### Spire QoL 0.31.23
#### Changed
- Cards played quickly, before the previous card finished: tabs, Incoming Damage and the Death Skull now describe the state once everything already played has resolved (your queued plays run first in the prediction), instead of the half-finished board. A queued card with random targets is treated like any random outcome. Those plays are now checked by the proc audit too.
#### Fixed
- Sacrifice and Bone Shards kill your Osty in the simulator, so the attacks he'd have taken count against you (Incoming Damage, lethal and wake tabs).
- The proc check no longer reports Osty's HP from a summon (Bodyguard) as unexplained, treats a play as stale when a potion or a teammate's play resolved after its tabs were shown, and its combat summary counts the plays checked after plays still resolving (logs and error reports only).

### Spire QoL 0.31.22
#### Added
- Setting: Multiplayer predictions (experimental, on). Off: teammates are left out of every prediction.
#### Changed
- Error reports from multiplayer fights have their own per-session budget, so a co-op session can't use up the reports a single-player fight would send (logs and error reports only).

### Spire QoL 0.31.21
#### Changed
- Multiplayer: teammates are simulated as full players: their block and HP, each enemy hit on every player with that player's own amount, their Thorns and Flame Barrier (which can kill an attacker before it finishes its hits on you), and Beacon of Hope sharing block with you at end of turn. Incoming Damage and the Death Skull account for them. Single-player results are unchanged (checked by a test of 600 single-player fights).
- The forecast check compares each teammate's HP lost too (logs and error reports only).

### Spire QoL 0.31.20
#### Changed
- Error reports say how many players were in the fight and their characters, so multiplayer reports can be told apart (logs and error reports only).
- Coverage: reviewed for multiplayer. Covered and Intercept are read correctly through the game's damage math; a teammate's Beacon of Hope giving you block at end of turn is a known gap (Incoming Damage reads high).

### Spire QoL 0.31.19
#### Changed
- Faster outcome simulations: branches share what they don't change, and a fight with too many random outcomes stops exploring them sooner (a branch-heavy single-player hand is about 2.5 times faster; four Defects with 40 Lightning orbs went from 47 ms to 2.4 ms per card).

### Spire QoL 0.31.18
#### Added
- Death Skull: during your turn, a skull beside an enemy's HP when it dies before acting if you end your turn now, with the chance when a roll decides it (Death chance setting).
- Decisions, Decisions: while you choose the Skill it plays three times, each Skill's tabs show those three free plays (procs, Shuffle Warning, kills, a 3× replay tab), and the proc check compares the plays against them.
- Multiplayer: the simulator counts teammates' end-of-turn effects on enemies from what's on screen (their orbs, Hailstorm, The Bomb, Stone Calendar, Consuming Shadow), so an enemy a teammate's orbs finish no longer counts as attacking you.
#### Fixed
- Multiplayer: hovering a teammate no longer moves Incoming Damage and Relic Counters off your character.
- Orbit and Pael's Tears tabs don't show for auto-played cards (no energy is spent); Pael's Eye's doesn't either (auto-plays don't count for it).

### Spire QoL 0.31.17
#### Fixed
- The simulator counts Feel No Pain and Flame Barrier played this turn (their block on exhaust and damage back at enemies), and Colossus halving Vulnerable enemies' attacks (when you play it, or apply Vulnerable while you have it). This changes Incoming Damage and the lethal, Imbalanced and wake predictions after those cards.
- The proc check no longer reports an X-cost card's cost changing when you spend energy (Whirlwind) (logs and error reports only).

### Spire QoL 0.31.16
#### Added
- Update Notice: a test build (a version that isn't released, or built with uncommitted changes) shows its version, commit, build time and the newest release on the main menu instead.

### Spire QoL 0.31.15
#### Changed
- Update Notice: shows "Spire QoL up to date!" (muted grey) when GitHub confirms you're on the newest version.

### Spire QoL 0.31.14
#### Added
- Update Notice: on the main menu, a note above "MODDED (n)" says how many Spire QoL versions behind you are, when a newer one is on GitHub; click it to open the releases page. Checked once each time you reach the main menu (switch: `update-notice`).

### Spire QoL 0.31.13
#### Added
- Thunder: a tab with its damage on cards that evoke a Lightning orb, and the simulator counts it (kills, the enemy turn).
#### Fixed
- Iron Club's and Game Piece's draws account for cards Calamity adds to your hand first (a full hand stops them).
- The proc check no longer reports Artifact blocking Dark Shackles (logs and error reports only).

### Spire QoL 0.31.12
#### Fixed
- The simulator channels orbs for characters without orb slots (the game gives them a slot on their first channel); it dropped those orbs, so a channeled Lightning or Frost didn't count toward kills, block or Incoming Damage.
- Draw counts stop when you run out of cards in both piles (Shuffle Warning, draw procs).
#### Changed
- The proc check reports plays that drew or channeled fewer cards or orbs than predicted (logs and error reports only).

### Spire QoL 0.31.11
#### Fixed
- FTL only counts its draw while you've played fewer than 3 cards this turn (Shuffle Warning, draw procs).
- Reboot's shuffle counts for shuffle procs (Biiig Hug's Soot).
- The simulator models orb slots gained and lost: Bulk Up drops your back orb (it isn't evoked), and Capacitor and Modded make room so a later channel doesn't evoke. This changes the lethal, Incoming Damage, Imbalanced and wake predictions after those cards.
- The proc check no longer reports Reboot's shuffle, Dampen coming off when the Magi Knight dies (and your cards upgrading back), or keyword changes that are part of a card's upgrade (logs and error reports only). The card scan reports cards that shuffle your discard pile or change your orb slots and aren't counted.

### Spire QoL 0.31.10
#### Changed
- Relic Counters: Metronome's row goes away once it has fired this fight (and Brilliant Scarf's once it has fired this turn), as the game's own counter does.
#### Fixed
- The proc check no longer reports Dark Shackles' (and other temporary Strength, Dexterity and Focus cards') stat change as unexplained (logs and error reports only).

### Spire QoL 0.31.9
#### Changed
- The coverage check's card scan also checks cards that forge or transform cards, and checks each card's upgraded version too (logs and error reports only).

### Spire QoL 0.31.8
#### Fixed
- Incoming Damage and the lethal, Imbalanced and wake tabs count the block an enemy's Thorns takes from you when your card's attack hits it (before each hit).

### Spire QoL 0.31.7
#### Fixed
- The proc check no longer reports a card made free for the turn (Splash) going back to its cost after you play it (logs and error reports only).
- The outcome audit counts an enemy's hits through block only up to the hit that kills it, so the Waterfall Giant coming back with Steam Eruption no longer reports an extra hit (logs and error reports only).

### Spire QoL 0.31.6
#### Fixed
- The proc check no longer reports less damage or block than predicted when the play ends the fight: the game stops logging from the hit that ends it (Dualcast's second evoke killing the last enemy) (logs and error reports only).
- The proc check no longer reports Flutter going down when your attack gets through as unexplained; the state after each play compares it with the simulator's prediction instead (logs and error reports only).

### Spire QoL 0.31.5
#### Fixed
- An un-upgraded Spinner no longer counts as channelling a Glass orb when played (only Spinner+ does; its power channels at the start of your turn), so its predicted front-orb evoke, kills and orb tabs are gone.
- Chill counts one Frost per enemy it can hit, not one, for orb evokes, kills and Metronome.

### Spire QoL 0.31.4
#### Fixed
- Cards that Forge (Big Bang, Bulwark, Conqueror, Refine Blade, Seeking Edge, The Smith...) count the Sovereign Blade they create when you don't have one, for the Pillar of Creation, Arsenal and Regalite tabs.
- The proc check no longer reports the cards that Begone, Charge, Guards, Seance, Compact and Primal Force transform as unexplained created cards (logs and error reports only).

### Spire QoL 0.31.3
#### Fixed
- Tabs and predictions for Hyperbeam and other cards that change your Focus now account for your orbs' new values.
- Uppercut, Putrefy, Shockwave and Malaise count as applying Weak and Vulnerable (for Unsettling Lamp, Vicious, Sleight of Flesh and the simulator's intents).
- Modded's draw counts for the Shuffle Warning; Scrape's discards, Voltaic's Lightning, and Fisticuffs' and Entrench's block count in the simulator and procs.
- The proc check compared cards that return to your hand (Feral) against the prediction made after the play; it now uses the one from when the play started. It no longer compares the state after a play that ends the fight, or reports false coverage problems (two classes sharing a name, rule names, relic properties) (logs and error reports only).

### Spire QoL 0.31.2
#### Changed
- When one of the checks itself fails (the proc check, coverage check), the error is reported instead of only logged.

### Spire QoL 0.31.1
#### Fixed
- The lethal and wake tabs count The Boot: your attack hits that get through always lose the enemy at least its minimum.

### Spire QoL 0.31.0
#### Changed
- The proc check accounts for everything a play changes: energy, stars, gold, HP, max HP, your cards (pile, cost, upgrades, keywords, afflictions, enchantments) and relic counters. A change nothing in the play explains is reported with the relics, powers or cards that could have caused it (logs and error reports only).
- The coverage check confirms every relic that can affect a fight has a tab, a tracker row or a reviewed reason, and reports one that doesn't (including relics new in a game update).
- Random draws and auto-played cards no longer exempt a play from these checks.

### Spire QoL 0.30.0
#### Added
- Coverage check (on by default, logs and error reports only): every game mechanic that can act during a fight is listed with how the mod handles it; a mechanic the mod hasn't reviewed, one that's new in a game update, or a card whose code does something the mod doesn't count is reported the first time it shows up in your fight. The settings are checked to cover every tab, tracker row and feature.
#### Changed
- The proc check also reports powers that went down or away during a play with nothing to explain it.
- The outcome simulator knows Concoct's poison, Monarch's Gaze lowering enemy attacks, and the orbs Storm and Trash to Treasure channel (including the front orb they evoke when your slots are full).
#### Removed
- The Intent Forecast feature, which did nothing yet (it's still planned).

### Spire QoL 0.29.0
#### Changed
- The outcome simulator knows cards that change enemy Strength (Piercing Wail, Dark Shackles, Enfeebling Touch, Crush Under, Mangle, Dying Star, Shared Fate, Resonance, Fight Me), Bouncing Flask's random poison, and enemies reacting to an ally's death (Ravenous is stunned, Crab Rage hits harder and blocks), so Incoming Damage and the lethal and Imbalanced tabs account for them.
- Calculated Gamble's draws count (Shuffle Warning and draw procs), including the shuffle its discards make possible; Biiig Hug's Soot counts as a created status.

### Spire QoL 0.28.0
#### Changed
- Incoming Damage, lethal and the other simulator tabs know what being hit does to enemies: Burrowed keeps its block and is stunned when it breaks, Flutter, Plow and Shriek stun, The Gambit dies to an attack getting through, Slippery loses at most 1 HP per hit. A stunned enemy no longer counts as attacking you.
- The Burrowed, Flutter, Plow, Shriek, The Gambit, Slippery, Hand Drill, Juggernaut, Sneaky and Enrage tabs come from the outcome simulator, so procs, orbs, random targets and your end of turn count, and each rule lives in one place.
- The state check after each play also compares whether enemies are stunned (logs and error reports only).
#### Fixed
- The lethal tab could promise a kill on a Slippery enemy (its HP loss per hit is capped at 1).

### Spire QoL 0.27.0
#### Changed
- Kill tabs (Gremlin Horn; crossed for Infested, Steam Eruption, Surprise, Crab Rage, Ravenous) and the Curl Up and Skittish tabs come from the outcome simulator: random hits, orb evokes and procs count, and a random hit that might miss shows the warning without promising the reward.
- The proc audit attributes everything to the game's own card-play action (history entries, relic and power flashes, shuffles), instead of splitting the history between plays by guesswork; potions and ending the turn no longer mix into a play. Shuffles are counted by observing the game's shuffle call (logs and error reports only).
- A game update that renames something a rule reads is logged and reported instead of silently falling back.

### Spire QoL 0.26.0
#### Added
- Biiig Hug tab (crossed) on cards whose draws cause a shuffle: it puts a Soot into your draw pile.
#### Changed
- Shuffle Warning and every draw-based tab count the draws a play sets off, not just the card's own: Dark Embrace per card exhausted (Burning Pact with Dark Embrace), Joss Paper, Game Piece, Iron Club, Vicious, Centennial Puzzle. Draws stop at a full hand, and No Draw or Fiddle blocks them.
- The lethal, Imbalanced and wake tabs account for Envenom's poison, Snecko Skull, extra poison ticks, Weak and Enrage changing an enemy's attack, and Curl Up, Sneaky and Skittish block.
- Card outcome simulations run in parallel across CPU cores (the frame still waits for them, so tabs appear on time), and the forecast reruns only when the game state changes.
- Proc and forecast checks are stricter: shuffles, the state each play leaves (checked against the simulator's prediction), and whether one set of random targets explains a whole enemy turn (logs and error reports only).
#### Fixed
- Card simulations could be reused after an enemy's attack changed (Weak, Strength) or a power or relic value changed, leaving lethal, Imbalanced and wake tabs computed from the old state.
- Newly channelled Plasma orbs had no values in the simulator.

### Spire QoL 0.25.1
#### Changed
- Forecast and outcome reports carry the simulator's exact start state, so a report can be replayed offline as a test. The game log gets one line per enemy turn comparing the simulator's prediction with what happened for each enemy (logs and error reports only).

### Spire QoL 0.25.0
#### Added
- Outcome audit (logs and error reports only): after each enemy turn, the simulator's predictions are checked enemy by enemy against what happened: deaths before acting (this checks the lethal tab), HP and hits through block at the end of your turn, and Imbalanced full blocks. Only outcomes no random branch allows are reported.
#### Fixed
- Simulator: poison ticks on a stunned enemy too, so one poisoned to death while stunned counts as dead before acting.

### Spire QoL 0.24.1
#### Fixed
- Glass orbs in the simulator: only the orb's base value decays (by 1 each time its passive fires, not below 0); Focus adds on top, so a decayed Glass orb still deals your Focus every turn. Before, the Focus-included value decayed, so a long-lived Glass orb's damage was underestimated with Focus.

### Spire QoL 0.24.0
#### Added
- **Lethal tab** (skull): on cards that kill an enemy for certain before it acts, counting the card's hits, orbs it evokes, procs it sets off, and everything your end of turn does (Juggernaut from Plating or Orichalcum block, orb passives, poison...). Stone Armor with Juggernaut and the Juggernaut card with Orichalcum show it. Switchable in the settings menu.
#### Changed
- New outcome simulator behind Incoming Damage and the lethal, Imbalanced and wake tabs: your end of turn and the enemy turn played out in the game's order, after each card. Random targets are explored as branches instead of being left out, so a result that's the same whatever the target counts (two Lightning orbs and two enemies they each kill: no damage incoming). Incoming Damage shows the worst branch for you; good outcomes (lethal, Imbalanced) need every branch, warnings (waking an enemy) need only one. Simulations are cached and get a small time budget per frame, so they never stall the game.
- Proc audit: a card played while the previous one was still resolving isn't checked (its tabs couldn't see that play's effects).

### Spire QoL 0.23.21
#### Fixed
- Proc audit false alarm (logs and error reports only): a potion's effects (the card a Power Potion adds, Lucky Tonic's Buffer) were blamed on the card played before or after it. The game records a potion after its effects, so the split between plays now ends before them.

### Spire QoL 0.23.20
#### Fixed
- Incoming Damage counts Juggernaut damage from your end-of-turn block (Plating, Frost orbs, Orichalcum, Cloak Clasp, Ripple Basin, Feel No Pain): with one enemy, a kill means it doesn't attack. Wake and kill tabs use it too.

### Spire QoL 0.23.19
#### Fixed
- Proc audit: tabs switched off in settings were invisible to the audit, so their procs would have been reported as missing tabs. It now checks against every prediction.
#### Changed
- Proc audit reports carry the full window (every history entry, numbered, with relic and power flashes at their positions and the window boundaries), what the card itself was expected to do, tabs hidden by settings, and how the split between plays was chosen. Orbs are listed with their values and slot capacity.

### Spire QoL 0.23.18
#### Fixed
- Kusarigama, Letter Opener and other random or all-enemy damage procs: no tab when the card kills every enemy (a reviving enemy keeps the fight going, but there's nothing to hit).
- Proc audit false alarms (logs and error reports only): two 0-cost plays in a row now split their effects by fitting both cards together, so one card's after-play effects (Feral returning it, Game Piece's draw, an exhaust) aren't blamed on the next. Intangible capping a hit isn't reported as a missing tab.

### Spire QoL 0.23.17
#### Fixed
- Juggernaut counts block from Frost orbs the card evokes (channelling into full slots, Dualcast...). The Imbalanced tab counts that block toward fully blocking a hit.
#### Changed
- Audits don't report an effect when the combat history shows a random roll caused it in a multi-enemy fight (a random-target proc or Lightning evoke hitting or killing an enemy). Everything else in those fights, including the rest of the same play, is still checked.

### Spire QoL 0.23.16
#### Fixed
- Incoming Damage counts your Glass orbs' end-of-turn damage to every enemy (an enemy it kills doesn't attack).
- Shriek tab counts damage from orbs the card evokes.
- Proc audit false alarms (logs and error reports only), found in a Defect run:
  - End-of-turn effects (orb passives, Plating, Ethereal exhausts) were blamed on the last card played; its check now ends when you end your turn.
  - Block from Frost orbs a card evokes (channelling into full slots, Dualcast) was reported as unexplained.
  - Hard to Kill and Hardened Shell capping a hit were reported as missing tabs.
  - Enemies applying powers to each other (Heist) were blamed on your card.
- Forecast audit: HP lost lower than forecast isn't reported when random-target end-of-turn damage was left out on purpose.

### Spire QoL 0.23.15
#### Added
- Feral: a tab on 0-cost Attacks while Feral has charges left this turn (the card returns to your hand).

### Spire QoL 0.23.14
#### Fixed
- Unsettling Lamp: no tab when Artifact would block the debuff (the lamp isn't used up then). It shows when some enemy you could debuff has fewer Artifact charges than the debuffs the card applies.

### Spire QoL 0.23.13
#### Fixed
- Asleep / Slumber tabs (waking an enemy) now count everything that reaches the enemy by the end of your turn: the card's hits, orbs it evokes, damage procs it sets off (Kusarigama...) and your end-of-turn damage (Lightning orbs...). A Strike that strips enough block for your end-of-turn Lightning to get through now shows it wakes the enemy.

### Spire QoL 0.23.12
#### Added
- Imbalanced (Bowlbug): a tab on the card whose block takes you to fully blocking the enemy's first hit, which stuns it (or knocks the Bowlbug off balance). Only the card that crosses the line gets it.

### Spire QoL 0.23.11
#### Fixed
- Kill tabs (an enemy's death effect, Gremlin Horn...) now count damage from orbs the card evokes: channelling into full orb slots, Dualcast, Quadcast, Multicast, Shatter. Before, only the card's attack counted, so a Zap that evokes a lethal Glass orb had no tab.

### Spire QoL 0.23.10
#### Fixed
- Proc audit false alarms (logs and error reports only; nothing on screen changes):
  - Relics and powers that fired in an earlier fight (Horn Cleat, Happy Flower, Lantern...) were reported as firing with no tab (a 0.23.9 regression).
  - The first card of a turn costing 0 (Snecko Eye) picked up start-of-turn effects, or missed its own Spirit of Ash block.
  - A potion used between plays (Lucky Tonic's Buffer) was blamed on the card before it.
- Error reports are grouped by cause, so a new cause no longer reopens an unrelated report.

### Spire QoL 0.23.9
#### Fixed
- Proc audit false alarms (logs and error reports only; nothing on screen changes):
  - A play that started a few frames after you released the card (the first card of a turn, while start-of-turn effects run) was checked with no tabs, so a correct Tuning Fork tab was reported as missing.
  - Effects that fire just before a play (Spirit of Ash on an Ethereal card, Intimidating Helmet, Pen Nib...) were blamed on the card played before it.
#### Changed
- Proc audit reports include every play this turn so far (tabs and result), the game state just before the play (relic counters, powers, hand with tabs, enemies), the timing between releasing the card and the play starting, and the history range checked.

### Spire QoL 0.23.8
#### Changed
- Relic Counters: with Osty out (Necrobinder), the tray sits right of Osty's HP bar instead of yours, so it doesn't cover Osty. Only at the default placement; a tray you've moved stays where you put it.
- Card tabs: the replay tab's `2×` is bigger and fills the tab.

### Spire QoL 0.23.7
#### Fixed
- Card tabs stay on a card while you hold it to play it (they used to disappear as soon as you grabbed it).
#### Changed
- Card tabs: the icon sits a little lower again.

### Spire QoL 0.23.6
#### Fixed
- Proc audit false alarms (logs and error reports only; nothing on screen changes):
  - Cards played in quick succession: the next card's block was blamed on the previous card.
  - Cards that apply their own powers without a power value (Borrowed Time, Spirit of Ash, Blight Strike's Doom, Invoke) were reported as "unexplained powers".
  - Thorns expected damage even when you attacked a different enemy without Thorns.
  - Catastrophe's auto-played (random) cards were checked as if they were predictable.

### Spire QoL 0.23.5
#### Changed
- Card tabs: the icon (and its number) sits a little lower, centered in the visible part of the tab instead of hugging its top edge.

### Spire QoL 0.23.4
#### Added
- Pael's Legion: a tab on cards that gain block while it's off cooldown (that card gets double block), like Vambrace.
- Incoming Damage counts Consuming Shadow: evoked Frost orbs add block, evoked Dark and Glass orbs hit enemies before they act (so a kill stops their attack).
#### Fixed
- Attacks that hit several times without a Repeat value (Twin Strike, Whirlwind and other X-cost attacks, Finisher, Flechettes, Fiend Fire, Dismantle on a Vulnerable enemy...) were counted as one hit, so kill, block-break and per-hit tabs (Thorns, Slumber) could be missing or low. Spite now counts its extra hit only after you lost HP this turn.

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
