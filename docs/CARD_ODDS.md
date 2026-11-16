# Card odds for relics outside fights (plan)

Status: phases 1 and 2 done (1.6.2 to 1.7.1, decisions #58, #59, #61); phases 3 and 4 and Inspect planned.

**Purpose (owner, decision #60):** a relic's icon beside an event option is a reminder that you hold a relic that makes this choice unusually good (or, crossed, unusually bad); the number under it says how likely. The Inspect dialog is how a player understands the odds of a favorable outcome.

## The problem

Run Reminders puts a relic's icon beside an event option when the option's code adds a card to your deck (`ChoiceEffect.CardToDeck`). For relics that change only *some* cards, that's often wrong:

- **Fresnel Lens** (Nimble on cards `Nimble.CanEnchant` accepts) shows beside an option whose only card is a curse. Same for the eggs (Molten: Attacks, Frozen: Powers, Toxic: Skills) beside a "choose a Power" reward when you hold Molten Egg.
- It never says **how likely** the relic is to matter, though the game's rules decide that exactly: an event offering a Power from your pool, a transform, Room Full of Cheese's commons.
- Relics that make **certain cards stronger in fights** (Strike Dummy: Strikes; Miniature Cannon: upgraded cards; Mystic Lighter: enchanted cards; Vitruvian Minion: Minions; Helical Dart: Shivs) show nowhere outside fights. A card reward, shop or event is exactly when they should change your pick.

## What the game gives us (decompile v0.113.1)

Every card an event, reward or shop hands you comes from a small set of game calls with a known candidate set:

| Source | Game code | Candidates | Odds |
| --- | --- | --- | --- |
| Card reward (fights, events) | `CardFactory.CreateForReward(player, count, CardCreationOptions)` | `options.GetPossibleCards(player)` through `FilterForRun`, minus cards already offered (no duplicates) | per slot: a rarity roll (`RollForRarity`, default or uniform odds, with the rarity offset you can count yourself; `NoRarityModification`), then uniform within the rarity; then the upgrade roll unless `NoUpgradeRoll` |
| Transform to random | `CardCmd.TransformToRandom` → `CardFactory.GetDefaultTransformationOptions(original)` | the original's pool (colorless for event, ancient, token and quest cards), only Common/Uncommon/Rare unless the original is a Status or a Curse, not the original itself, `FilterForRun` | uniform |
| A fixed card | `CardCmd.TransformTo<T>`, `CardPileCmd.Add` of `ModelDb.Card<T>()`, a curse from the curse pool | that one card | 100% |
| Shop | `CardFactory.CreateForMerchant` | already on screen | n/a: tag the cards you see |

An event's options build their `CardCreationOptions` inline, with the pool and a filter lambda: Infested Automaton `c => c.Type == CardType.Power` and an energy-cost filter, Room Full of Cheese `c => c.Rarity == CardRarity.Common` with uniform odds. The option walk we already do (`RunRemindersFeature.EffectsOf`, IL of `OnChosen` and the methods it calls) can read these: the factory call, the pool expression (`Owner.Character.CardPool`, `ModelDb.CardPool<ColorlessCardPool>()`), the flags, and the filter (the lambda's method, found by its `ldftn`). The lambda is then run on each canonical card of the pool. Static lambdas (all the ones seen so far) run as they are; a closure over the event (`<>4__this`) gets the live event instance; anything else is "unknown".

The relics' own checks aren't safe to dry-run (their hooks clone and upgrade or enchant cards), so each relic gets a **card predicate** written from its decompiled hook, using the game's own tests where there is one: Fresnel Lens `Nimble.CanEnchant(card)`, Molten Egg `Type == Attack && IsUpgradable && CurrentUpgradeLevel < 1`, Strike Dummy `Tags.Contains(Strike)`, Miniature Cannon `IsUpgraded`, Mystic Lighter `Enchantment != null`.

Nothing here reads the RNG or the seed: candidate sets, rarity odds and the rarity offset are the game's rules and counts a player can track; the result is a probability, never the roll.

## Design

### 1. Card predicates (one table, scan-checked)

`Data/CardAffinity.cs`: relic → predicate on a card as you'd get it, and its kind:

- **Changes the card you get** (`Modifies`): the eggs, Fresnel Lens, and any relic overriding `TryModifyCardBeingAddedToDeck`, `TryModifyCardRewardOptions[Late]` or `ModifyMerchantCardCreationResults` per card.
- **Boosts the card in fights** (`Boosts`): Strike Dummy, Fake Strike Dummy, Miniature Cannon, Mystic Lighter, Vitruvian Minion, Helical Dart, and the like.

Order matters and the table says so: a boost predicate sees the card after the modifiers (Molten Egg upgrades an Attack, then Miniature Cannon boosts it).

**Scan check** (same pattern as `Diagnostics/CardCoverage.cs`): every relic whose hook bodies read a card property (`CardModel.Tags`, `Type`, `IsUpgraded`, `Enchantment`, `Keywords`, `Rarity`, `EnergyCost`) or override the card-creation hooks above must have a predicate or a `Reviewed` reason (too broad, like Burning Sticks on any Skill; or combat-only state). New relics in a game update are caught before anyone holds one. `scripts/coverage-ledger.mjs` lists missing ones and RELICS.md shows the predicate.

### 2. Card sources per choice (generic, read from the code)

Extend the option walk from "which effects" to "which cards": `ChoiceCards` per option, a list of:

- `Reward(pools, filter, odds, flags, count)`,
- `Transform(which: chosen / random deck card / all of a kind, count)`,
- `Fixed(card)`,
- `Unknown` (code we can't read: keep today's plain icon, so nothing is lost).

Readers are per game call, not per event: one for `CardCreationOptions.ForNonCombatWith*Odds` and its `With*` builders, one for each transform command, one for fixed cards. An event only needs code of its own when it builds cards some other way, and the coverage check reports any `Unknown` it meets in a player's game.

### 3. The odds (pure, tested)

`Sim/CardOdds.cs`, no game types, with tests (`tests/SpireQoL.Sim.Tests`): the candidate cards as (rarity, matches) pairs plus the odds and flags, giving **the chance at least one card you're offered (or get) matches**. Exact, not sampled: per slot a rarity roll, uniform without replacement within a rarity (the no-duplicates rule), and the upgrade roll where a predicate depends on upgrades. Transforms are matches / candidates. For "transform a card you pick", the odds per deck card differ only by the original being excluded and its pool; show the best and worst (one number when they're equal).

### 4. What you see

- **Event options:** a relic's icon only when some outcome can match (the Fresnel Lens bug goes away: a curse never matches). Under the icon, the chance as a small number (`43%`), left out at 100%. A crossed icon keeps its meaning (Bing Bong on a curse). `Unknown` keeps the plain icon, no number.
- **Card reward screen and shops:** a plain tab on each offered card a **Boosts** relic affects, with the relic's icon (decision needed: tabs so far answer "does playing this do something now"; this is "this relic changes this card", in the same visual language). **Modifies** relics get no tab: the game already shows the upgrade or enchantment on the card.
- **Card reward panel:** unchanged.
- **Guide page:** an event option with a percentage, and a reward card with a Strike Dummy tab.

### 5. Checks

- After an event option or a pick: a relic that changed a card (it flashed, or the added card differs from the offered one) while its predicate said no, or didn't while it said yes, is reported (`reminders`, `card-affinity`). The probability can't be checked per choice; the log keeps the predicted chance and whether it happened, for a look across many runs.
- The option walk reports `Unknown` card sources it meets, as a coverage task.

## Phases

1. **Predicates and the scan check; icons only when possible.** Fixes the Fresnel Lens report on its own: curses, fixed cards and filtered rewards that can't match drop the icon. Small, ships alone.
2. **Card sources from the code, and the odds.** The IL readers, `CardOdds` with tests, the percentage under event icons.
3. **Boost relics on rewards, shops and events.** Card tabs for Strike Dummy and the like; their icons beside event options with odds.
4. **Checks, coverage, Guide, docs.**

## Inspect: what an option can give, and your odds (plan)

For an option whose cards come from a known pool at random (Colorful Philosophers, Room Full of Cheese, Infested Automaton, a transform, a reward from an event), a small inspect button (the game's magnifying glass if it has one, else an eye) beside the option opens a panel listing every card it can give, without going to the compendium.

- **The list** comes from the same reading as the relic odds (phase 2's card sources): the pool, the option's filter, `FilterForRun`, grouped the way the option draws them. Colorful Philosophers shows three groups for the chosen color: its commons, its uncommons and its rares, each offering 3 (the event's `Cards` value). A transform shows the pool a card of yours turns into. Each card is the game's own card, as in the Guide page (`NCard` from the card scene).
- **Marking:** click cards to mark them as ones you'd be happy with; the panel shows the chance that what you're offered includes at least one of them, per group and for the option as a whole. For a pick of `k` distinct cards from a group of `n` with `g` marked, that's `1 − C(n−g, k) / C(n, k)`; rarity-rolled rewards weight it per rarity, as in `CardOdds`. "At least two" and the full distribution are a later step (the owner's idea for the relic odds too).
- **Relics:** the cards a relic applies to carry its tab in the list (an egg's type, Fresnel Lens, Strike Dummy), and a button marks them all at once.
- **Remembered:** marks are kept per card for the run (a player's own notes, never shown elsewhere), so the next event with the same pool starts with them.
- **Multiplayer:** each player inspects their own options; nothing about teammates.

It never reads the RNG: the list is every card the rules allow, and the odds are counts.

**Build after phase 2** (it needs the card sources and `CardOdds`): the panel (a new screen of ours, not a widget, opened from the button and closed with Esc like the settings menu), marking and the odds, then remembering marks. It's added to the Guide page and gets its own setting.

## Interactions reviewed (1.7.0)

- **Mystic Lighter + Tezcatara's Nutritious Soup** (and any option that enchants your cards): Lighter's icon beside the option. Same for Miniature Cannon and options that upgrade a card.
- **Relic + relic**: an egg upgrades an Attack you get, so Miniature Cannon applies to it; Fresnel Lens enchants an Attack that gains block, so Mystic Lighter applies. A boost's rule names the change it looks for, a modifier's the change it makes, so a new pair is covered by its rules.
- **Cards that make cards**: Begone! makes a Minion Strike (a Strike and a Minion), so Strike Dummy and Vitruvian Minion apply to it. Read from the card's code (`CreateCard<T>`, `ModelDb.Card<T>`).
- **Attack only**: Miniature Cannon and Mystic Lighter add to powered attack damage; read as the card being an Attack.
- **Enchantment eligibility** (1.7.1): Mystic Lighter beside an enchanting option only when an Attack of yours can take one of its enchantments (some can't go on Attacks; Nimble only on cards that gain block, such as Iron Wave). Miniature Cannon beside an upgrade only when an Attack of yours is upgradable.
- **Pools** (1.7.1): the reward's options go through the game's `Hook.ModifyCardRewardCreationOptions`, so Dingy Rug (colorless cards in card rewards) and Prismatic Gem (every character's cards) are counted.
- **Cards that arrive upgraded** (1.7.1): Astrolabe upgrades the cards it transforms into, so no egg icon; Miniature Cannon counts them.
- **Glass Eye** (1.7.1): read as five rewards, 2 commons, 2 uncommons, a rare, 3 cards each.
- **Not yet**: Colorful Philosophers' rewards keep the game's upgrade roll (the others turn it off); it isn't counted, so Miniature Cannon there is a little more likely than shown.

## Ancients (owner, 2026-11-16)

Ancients' card options are read through the relic's `AfterObtained`: Sea Glass (three rewards from its own character's pool), Glass Eye (a reward per rarity from yours), Pandora's Box (your Strikes and Defends transformed). Both done in 1.7.1: an option that upgrades the cards it gives counts them as upgraded, and Glass Eye's per-rarity filter is read per value.

## Answers (owner, 2026-11-16)

- **The number:** the chance at least one card you're offered matches. A distribution (the chance of 0, 1, 2... matching) is possible later work.
- **Broad relics:** the eggs stay in, whole card types and all: they're rare, and it's easy to forget the type you'll see upgraded.
- **Tabs outside fights:** yes, on card rewards and on a group of event cards to choose from, for a relic that applies and that you might not think of.
- **Which relics tag cards:** those whose effect the card doesn't show. Strike Dummy (its damage isn't in the card's numbers outside fights), Miniature Cannon, Mystic Lighter. Not Fresnel Lens or the eggs (the card shows the enchantment or the upgrade), and not relics that react to playing a card (Pen Nib, Ornamental Fan: maybe later, as synergy hints). Others by judgement as the scan lists them.
- **Cards a card makes:** Begone! creates a Minion Strike, so Strike Dummy applies to it though Begone! isn't a Strike. A predicate looks at the card and the cards it creates (its hover-tip cards / the cards its code makes), and the scan check covers that case.
