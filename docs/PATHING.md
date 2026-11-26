# Pathing reminders (idea phase)

Status: built in Spire QoL 1.11.0 (decision #65): icons beside the legend rows, hover highlights the room type, heals and counts under the icons, boss relics left out. The sections below are the plan as written; docs/FEATURES.md "Run Reminders" describes what shipped.

## The idea

Some relics and quest cards change what a kind of room is worth, so they change which path you'd pick: Black Star makes elites better, Meal Ticket makes shops heal, Planisphere makes `?` rooms heal, Dowsing Rod's quest card wants `?` rooms, anything that adds a rest site option makes campfires better. Players forget them when they look at the map. Run Reminders already reminds you at the choice itself (card rewards, events, shops). This extends it to the map, where you choose the room.

This is the same design as the event icons (decision #60, the owner's): **a reminder that a relic makes this kind of room unusually good (or unusually bad)**, not advice on which path to take.

## Where it shows: one central panel, not icons on nodes

The owner's suggestion: rather than hanging relic icons on every map node (dozens of nodes, the same icons repeated, noise over the map art), gather the relevant relics in one place.

Proposal: **a panel beside the map legend, one row per room type** (Elite, Rest, Unknown, Shop, Treasure, Monster), each row listing the icons of your relics and quest cards that change that room type. Rows with nothing are left out; with no relevant relics, the panel isn't shown.

- It reads like the legend: the room's icon, then what you hold that changes it.
- Hovering a relic gives its tooltip (the game's own), and could highlight the nodes of that room type on the map, the way the game's legend does on hover, if that can be reused.
- A crossed icon (red slash) when the relic makes that room type worse for you (Maw Bank on shops: buying something ends its gold).
- It's a movable widget, like every free-floating indicator.
- Options weighed:
  - Icons on every node of the type: rejected (noise; the owner's point).
  - A single unsorted strip: simpler, but you'd have to remember which room each relic is about. Grouping by room type answers that.

### Numbers

Some of these relics have a value a player can know right now:
- Eternal Feather's heal grows with deck size.
- Dowsing has rooms left.
- Winged Boots has uses left.
- Sword of Stone has elites left.
- Pumpkin Candle has a Kindle count.

The game already shows most of these as the relic's counter. The first version shows icons with the game's counters. A heal amount (Eternal Feather, Meal Ticket, Planisphere) next to the icon is a later choice for the owner. Out-of-combat panels aren't on-card tabs, so the tab rule against numbers doesn't bind it, but the same restraint applies.

## What's in scope (from the decompile, 2026-11-16)

Each entry is read from the relic's hook body, not its name. "Map shows it" entries are already visible on the map itself and are reviewed out.

| Room | Relic / card | What it does there | Hook |
|---|---|---|---|
| Elite | Black Star | extra relic reward | `TryModifyRewards` (RoomType.Elite) |
| Elite | White Star | extra card reward at boss rarity | `TryModifyRewards` (RoomType.Elite) |
| Elite | Sling of Courage | Strength on entering | `AfterRoomEntered` (RoomType.Elite) |
| Elite | Booming Conch | extra draw and energy | `ModifyHandDraw`, `AfterSideTurnStart` (Elite) |
| Elite | Tormentous Collar | Weak to enemies at turn start | `BeforeSideTurnStart` (Elite) |
| Elite | Sword of Stone | counts elites won toward its payoff | `AfterCombatVictory` (Elite) |
| Elite | War Hammer | upgrades cards after an elite win | `AfterCombatVictory` (Elite) |
| Monster | Prayer Wheel | extra card reward | `TryModifyRewards` (RoomType.Monster) |
| Monster | Fishing Rod | counts monster combats | `AfterCombatEnd` (Monster) |
| Rest | Eternal Feather | heals on entering, per 5 cards | `AfterRoomEntered` (RestSiteRoom) |
| Rest | Regal Pillow, Stone Humidifier, Dream Catcher, Tiny Mailbox | change what Rest gives | `ModifyRestSiteHealAmount`, `AfterRestSiteHeal`, `TryModifyRestSiteHealRewards` |
| Rest | Girya, Shovel, Meat Cleaver, Pael's Growth, Pumpkin Candle, Wuppy, Byrdpip | add a rest site option | `TryModifyRestSiteOptions` |
| Rest | Byrdonis Egg (card in deck) | adds Hatch | `TryModifyRestSiteOptions` |
| Rest | Miniature Tent | rest site options don't end the visit | `ShouldDisableRemainingRestSiteOptions` |
| Unknown | Planisphere | heals on entering a `?` | `AfterRoomEntered` (MapPointType.Unknown) |
| Unknown | Dowsing (Dowsing Rod's quest card) | 4 `?` rooms complete it (becomes Abundance) | `BeforeRoomEntered` (MapPointType.Unknown) |
| Unknown | Juzu Bracelet | `?` is never a combat | `ModifyUnknownMapPointRoomTypes` |
| Unknown | Golden Compass, Lantern Key (act 3) | `?` is always an event | `ModifyUnknownMapPointRoomTypes` |
| Shop | Meal Ticket | heals on entering | `AfterRoomEntered` (MerchantRoom) |
| Shop | Maw Bank (crossed) | gold per room until you buy | `AfterRoomEntered`, `AfterItemPurchased` |
| Shop | Lord's Parasol | buys everything on entering | `AfterRoomEntered` (MerchantRoom) |
| Shop | Membership Card | half price | `ModifyMerchantPrice` |
| Shop | The Courier | cheaper, and what you buy restocks | `ModifyMerchantPrice`, `ShouldRefillMerchantEntry` |
| Treasure | (Silver Crucible: no mark, reviewed) | the first treasure room you enter has no chest, whichever it is: no route avoids it | `ShouldGenerateTreasure` |
| Boss | Pantograph, Bath Robes | heal / effect at the boss | `AfterRoomEntered` (Boss) |
| (map shows it) | Fur Coat, Golden Compass's map, Winged Boots, Spoils Map | mark or change the map | `ModifyGeneratedMap*`, `ShouldAllowFreeTravel` |

Notes:
- **Boss rows** (Pantograph, Bath Robes, Amethyst Aubergine, Lava Rock) don't change a path: every path ends at the boss. Probably leave them out. Owner's call.
- **Every-combat relics** (Storyteller, Vajra, Bronze Scales...) treat every combat the same. They make fights in general easier, not a room type, so they stay out.
- **Rest site options** already show at the campfire (Rest Preview, the game's buttons). On the map, the point is "you have Girya: campfires are worth more to you".
- **Multiplayer:** your own relics only, as Run Reminders does.

## Coverage, written like the other reminder tables

- A `PathingRules` table (like `Data/ChoiceReminders.cs`): relic → room type(s), plain or crossed.
- A coverage check, so a game update can't add a pathing relic silently. Every relic or card that overrides a map hook needs a rule or a reviewed reason:
  - `ModifyGeneratedMap*`, `ModifyUnknownMapPointRoomTypes`, `ShouldAllowFreeTravel`, `ShouldGenerateTreasure`, `ModifyNextEvent`, rest site hooks, shop hooks (`ModifyMerchantPrice`, `ShouldRefillMerchantEntry`, the merchant card hooks, `AfterItemPurchased`; 1.15.3).
  - Or one whose `AfterRoomEntered` / `BeforeRoomEntered` / `AfterCombatVictory` / `AfterCombatEnd` / `TryModifyRewards` / `BeforeSideTurnStart` body compares `RoomType`, `MapPointType` or a room class (`RestSiteRoom`, `MerchantRoom`).
  - The room-type comparison can be read from the IL, as `OptionCards` reads event options. That gives the row for free and catches new relics like Black Star without naming them.
- Quest cards in the deck (Dowsing, Byrdonis Egg, Lantern Key) are covered the same way, from the card's hooks.
- Done (decision #374): the rows are read from each relic's and card's hooks by the program reader (`Data/RoomReadings.cs`): a room hook that acts gives its room, any other hook its acting steps' room tests; every room, every fight, the boss's and what the map shows give no row. The hand table is the catalog tests' check (`RoomReadingsTests`), `Snapshots/room-readings.txt` (and `-main`) each model's reading; only Silver Crucible is reviewed out (`ChoiceReminders.MapReviewed`).
- Settings: a Pathing section, with a switch per relic like the Outside fights section (decision #43), and a master switch.
- A Guide page example (a legend row with icons).

## Hard rule check

Only what a player knows:
- Your relics.
- Their rules.
- Their counters.
- The map as shown.

Nothing about what a `?` room will be beyond the rules (Juzu Bracelet narrows the possible types; it doesn't predict the roll).

A possible later idea: `?` room odds. They depend on how many of each type you've seen, which a player can count; `ModifyOddsIncreaseForUnrolledRoomType` adjusts them. It needs a review that the counters are knowable and not seed-derived before anything is shown.

## Phases (when this is picked up)

1. Rules table, IL coverage check and reviewed reasons; regenerate RELICS.md.
2. The legend panel on the map screen (find the game's legend node and map screen class in the decompile), icons only, movable widget.
3. Settings and Guide.
4. Owner's calls:
   - Boss rows.
   - Heal numbers.
   - Highlighting nodes on hover.
   - `?` odds.

## Open questions for the owner

- Beside the legend, or a free-floating widget elsewhere on the map screen?
- Should boss-only relics appear?
- Show heal amounts (Eternal Feather, Meal Ticket, Planisphere), or icons only?
- Highlight that room type's nodes when hovering a relic?
