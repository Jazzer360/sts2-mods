using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Cards;
using SpireQoL.Features;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Menu;

/// <summary>
/// The Guide page (decision #56): how to read what the mod shows, with live examples built from the game's own pieces: real
/// cards with tags drawn by the same renderer as the hand, Relic Counters rows and Incoming Damage sums built by their own
/// features' code. Nothing on it is about the current fight; every example is made up.
/// </summary>
public sealed partial class SettingsMenu
{
    /// <summary>The example card added last (to adjust it: FreeCost).</summary>
    private ShownCard? _lastGuideCard;

    /// <summary>The last example card shows a free card's cost: the game's green 0.</summary>
    private void ShowLastCardFree()
    {
        if (_lastGuideCard != null) _lastGuideCard.FreeCost = true;
    }

    /// <summary>Per-frame redraws for the Guide page's examples (cards settling, flashing rows); cleared on every rebuild.</summary>
    private readonly List<Action<float, float>> _guideTicks = new();

    private const float GuideCardScale = 0.55f;
    private const float GuideCellWidth = 300;

    private void BuildGuide()
    {
        Section("How to read Spire QoL");
        Paragraph("Everything the mod shows is something you could work out yourself from what's on screen and the game's rules. "
            + "It never looks ahead: not the draw pile's order, not a random roll, not an enemy's move beyond its intent. "
            + "When a roll decides something, you see its chance, the most likely outcome or a range, never the result. "
            + "The other pages say exactly what each tag and row covers; this page shows how the pieces look.");

        // In the order of the pages and their sections (decision #186): each feature's entry where its switch is.
        BuildGuideSetup();
        BuildGuideTabs();
        BuildGuideEnemies();
        Section(SectionTitle(FeatureSection.BesideYou));
        BuildGuideIncoming();
        BuildGuideCounters();
        BuildGuidePotions();
        BuildGuideHolding();
        BuildGuideProgress();
        Section(SectionTitle(FeatureSection.BetweenFights));
        BuildGuideReminders();
        BuildGuideRest();
        BuildGuideGeneral();
    }

    // ------------------------------------------------------------------ card tabs

    private void BuildGuideTabs()
    {
        Section(SectionTitle(FeatureSection.OnYourCards));
        Paragraph("A tag hanging off the top of a card in your hand answers one question: does something happen the moment I play this card, right now? "
            + "Keyword Tags mark what the card picks up (Retain, Sly, playing more than once), Proc Tags what playing it sets off, and the Shuffle Tag a reshuffle. "
            + "The icon is what sets it off (a relic, your power, an enemy's power, an enchantment, a pile). One tag per source; a card can carry several. "
            + "In fights, On your cards: Tags on cards turns them all off, Plain tags and Crossed tags choose which kinds show (on potions too), and each feature lists its own kinds. "
            + "Each relic's, power's and card's tag is on the Tags by source page, under a switch for its whole family (All proc tags, All double-play tags...).");

        var flow = Flow();
        // A mix of every character's cards, each one a card the example could really happen on.

        GuideCardCell(flow, Card<DaggerThrow>(), "Plain: it sets something off",
            "Playing this sets off what the icon shows. Here Dagger Throw is your third Attack this turn, so Kunai gives you Dexterity.",
            Tab("proc:Kunai", Relic<Kunai>()));
        GuideCardCell(flow, Card<BallLightning>(), "A number: how much",
            "Block gained, damage dealt, the amount summoned, cards drawn or energy gained, after everything that changes it (a full hand draws fewer), or on a warning the cards put into your piles (Personal Hive's Dazed). Only those tags carry a number "
            + "(In fights, Proc Tags: Amounts on proc tags). Here Ornamental Fan gives 4 block.",
            Tab("proc:OrnamentalFan", Relic<OrnamentalFan>(), number: 4));
        GuideCardCell(flow, Card<KinglyPunch>(), "Crossed: a warning",
            "Playing this might be bad for you: it helps an enemy, hurts you, or gives up a relic's bonus (or makes it smaller). Often still the right play; "
            + "the slash makes sure you know. Here the enemy's Thorns hits you for 3.",
            Tab("proc:ThornsPower", Power<ThornsPower>(), crossed: true, number: 3));
        GuideCardCell(flow, Card<BallLightning>(), "A chance on a warning",
            "When only some outcomes set the warning off (here, the Lightning orb waking the sleeping enemy if it hits that one), the tag "
            + "carries the chance. No number: it's certain. Shown with Show chances on. A few plain tags carry a chance too, where only some outcomes "
            + "set the bonus up (Hailstorm when Chaos might channel your first Frost orb) or set it off (Iteration when your draw might be your first Status this turn).",
            Tab("proc:AsleepPower", Power<AsleepPower>(), crossed: true, numberText: "50%"));
        GuideCardCell(flow, Card<BallLightning>(), "A chance on the skull",
            "When a roll decides the kill (here, which enemy the Lightning orb hits), the lethal tag carries the chance. "
            + "Shown only when the card surely makes a death more likely than ending your turn now.",
            Tab("lethal", GameIcons.Skull(), numberText: "50%"));
        GuideCardCell(flow, Card<Bash>(), "The arrow: only at some targets",
            "The game's targeting arrow in the corner means it happens only at some of the targets you could pick. "
            + "Here Bash kills an enemy before it acts (the skull), but only aimed at the right one. On a crossed tag, aim elsewhere to avoid it.",
            Tab("lethal", GameIcons.Skull(), aimed: true));
        GuideCardCell(flow, Card<Claw>(), "Several at once",
            "Each source gets its own tag, in a row. If the row is wider than the card, the tags overlap. "
            + "Here: Pen Nib doubles it, Ornamental Fan gives 4 block, and the enemy curls up if you aim at it.",
            Tab("proc:PenNib", Relic<PenNib>()), Tab("proc:OrnamentalFan", Relic<OrnamentalFan>(), number: 4),
            Tab("proc:CurlUpPower", Power<CurlUpPower>(), crossed: true, aimed: true));
        GuideCardCell(flow, Card<SuckerPunch>(), "Free cards: the energy saved",
            "A free card (Free Attack here; also Free Skill and Free Power) is used up by this play. The number is the energy it saves. "
            + "Crossed on a card that costs 0 or X: the free card would be wasted.",
            Tab("free:FreeAttackPower", Power<FreeAttackPower>(), number: 1));
        ShowLastCardFree();
        GuideCardCell(flow, Card<CrescentSpear>(), "?: a cost you couldn't know",
            "A free card shows every cost as 0. When nothing on screen tells you this copy's real cost (Snecko Eye re-rolled it, "
            + "Frantic Escape raised copies like it), the tag says ? instead of telling you.",
            Tab("free:FreeAttackPower", Power<FreeAttackPower>(), numberText: "?"));
        ShowLastCardFree();
        GuideCardCell(flow, Card<Reave>(), "Text: played more than once",
            "The total plays, when the card replays itself: here Transfigure gave Reave Replay, so it's played twice. "
            + "Something else that doubles a play shows its own icon instead "
            + "(Echo Form, Burst, Duplication...).",
            Tab("replay", icon: null, text: "2×"));
        GuideCardCell(flow, Card<EscapePlan>(), "Text: a chance",
            "The chance the card's effect happens, when it depends on what it draws: here Escape Plan draws a Skill (and gains its block) "
            + "in 40% of the cards your draw pile could give. From the pile's contents, never its order.",
            Tab("drawn", icon: null, text: "40%"));
        GuideCardCell(flow, Card<Pillage>(), "Text: a chance, and how many",
            "The most likely number on top, its chance under it: here Pillage most likely draws 1 Attack (33%) before it stops. "
            + "Scrape shows how many of the cards it draws cost 0 (the ones it keeps). A tie goes to the higher number.",
            Tab("drawn", icon: null, text: "33%", numberText: "1"));
        GuideCardCell(flow, Card<Patter>(), "A card: it comes back",
            "Playing this brings that card back to your hand: here Patter is your third Skill this turn, so Make It So comes back from your discard pile. "
            + "Also Right Hand Hand and Whistling Arrows.",
            new TabSpec("return:MakeItSo", Icon: null, Card: Card<MakeItSo>()));
        GuideCardCell(flow, Card<Backflip>(CardKeyword.Retain, CardKeyword.Sly), "Retain and Sly",
            "The card stays in your hand at end of turn (Retain), or is Sly (Hand Trick, Master Planner). The card's text says so too; "
            + "the tags are there because they're about the card, not about playing it, and they show even when it's only for this turn.",
            Tab("retain", Enchantment<Steady>()), Tab("sly", Power<MasterPlannerPower>()));
        GuideCardCell(flow, Card<Acrobatics>(), "Discard pile: a reshuffle",
            "It draws more cards than your draw pile has, so playing it shuffles your discard pile back into your draw pile.",
            Tab("shuffle", GameIcons.DiscardPile()));
        GuideHandCell(flow, Card<StrikeIronclad>(), Card<AscendersBane>(), "See-through: Ethereal Fade",
            "Not a tag: an Ethereal card exhausts if it's still in your hand at the end of your turn, "
            + "so it shows the card behind it. How see-through: In fights, Ethereal Fade, Ethereal opacity.",
            Settings.EtherealOpacity);
        _rows!.AddChild(flow);

        Paragraph("Teammates' tags: in multiplayer, a teammate's power or relic that your card sets off gets a tag like your own, "
            + "with their icon (Rally's block setting off their Juggernaut). One you both have shares a tag, the amounts added.");
        Paragraph("Powers that act on every card you play and never run out (Afterimage, Serpent Form, Oblivion...) would tag every card, so their tags start off, "
            + "and so do Cloak Clasp's and Obsidian Vice's warnings, which would be on nearly every card: switch each on in the Tags by source page. Tender's and Slow's start on.");
    }

    // ------------------------------------------------------------------ beside you: potion tags

    private void BuildGuidePotions()
    {
        SubSection("Potion Tags");
        Paragraph("The same tag as on cards hangs under a potion in the top bar when drinking it sets off a relic "
            + "(Reptile Trinket, Belt Buckle, White Beast Statue). Each relic's potion tag, and All potion tags, is on the Tags by source page (Relics).");
    }

    // ------------------------------------------------------------------ relic counters

    private void BuildGuideCounters()
    {
        SubSection("Relic Counters");
        Paragraph("Beside your HP bar, one row per relic or power that counts what you do: its icon, a bar with one segment per step, and a label. "
            + "A row flashes when your next qualifying play sets it off. Counters that carry across turns come first.");

        CounterRow(RelicOf<PenNib>(), filled: 7, total: 10, RelicCountersFeature.Shade.Relic, "7/10", flash: false,
            "Gold: a relic's counter. Pen Nib has counted 7 of its 10 Attacks; it carries across turns and fights.");
        CounterRow(RelicOf<Kunai>(), filled: 2, total: 3, RelicCountersFeature.Shade.Relic, "2/3", flash: true,
            "Flashing: your next Attack sets Kunai off. (In fights, Relic Counters: Tracker flashes early starts it one step sooner.)");
        CounterRow(RelicOf<VelvetChoker>(), filled: 5, total: 6, RelicCountersFeature.Shade.Warning, "1 left", flash: true,
            "A limit: Velvet Choker's plays left this turn, red at the last one, then LIMIT.");
        CounterRow(RelicOf<Pocketwatch>(), filled: 2, total: 3, RelicCountersFeature.Shade.Good, "2/3 ok", flash: false,
            "Stay under: green while you're within Pocketwatch's limit, red (missed) once you go over.");
        CounterRow(PowerOf<PanachePower>(), filled: 3, total: 5, RelicCountersFeature.Shade.Power, "2 cards left", flash: false,
            "Blue: a power of yours counting down to its next trigger (Panache: 2 more cards this turn).");
        CounterRow(PowerOf<WitheringPresencePower>(), filled: 4, total: 6, RelicCountersFeature.Shade.Warning, "2 cards left", flash: false,
            "Red: an enemy's power counting your plays (Withering Presence adds a Wither to your hand).");
        CounterRow(PowerOf<CacophonyPower>(), filled: 12, total: 33, RelicCountersFeature.Shade.Power, "21 draws left", flash: false,
            "A long count shows just the number.");
        Paragraph("Relics that only count turns (Horn Cleat, Captain's Wheel...) are off by default: switch each on in the Tracker column of the Tags by source page (Relics).");
    }

    private void CounterRow(AbstractModel? model, int filled, int total, RelicCountersFeature.Shade shade, string label, bool flash, string text)
    {
        if (model == null) return;
        var row = RowBox();
        var example = RelicCountersFeature.ExampleRow(model, filled, total, shade, label, flash, out var paint);
        var holder = new MarginContainer { CustomMinimumSize = new Vector2(300, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        holder.AddChild(example);
        example.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(holder);
        row.AddChild(Wrapped(text));
        AddRow(row);
        _guideTicks.Add(paint);
    }

    // ------------------------------------------------------------------ incoming damage

    private void BuildGuideIncoming()
    {
        SubSection("Incoming Damage");
        Paragraph("Left of your block during your turn, a sum: what the enemies will deal on their turn, minus the block you'll have, "
            + "equals what you'll lose. Worked out hit by hit, in the game's order. It stays up while you pick one of a few cards mid-fight "
            + "(a discovery, a potion's choice).");

        var legend = new (string Text, string Detail)[]
        {
            ("Attack icon", "What lands on you: the enemies' intents (its tier matches the game's), plus Burn, Decay, Constrict and the like. "
                + "An enemy that dies first (Poison, your end-of-turn damage) doesn't attack, and retaliation can stop a multi-hit partway."),
            ("Block icon", "Your block when the turn ends, including what you gain then (Plating, Frost orbs, Orichalcum...)."),
            ("=", "What gets through your block: red, or 0 in green. "
                + "When a roll decides how the turn goes (a draw at the end of your turn, a random target), the sum is the most likely way, with its chance before the result."),
            ("Heart", "HP loss no block can stop (Regret, Beckon, Bad Luck), on its own line under the result: more block won't help with it."),
        };
        foreach (var (name, detail) in legend)
        {
            var row = RowBox();
            row.AddChild(Label(name, size: 20, Text, NameWidth));
            row.AddChild(Wrapped(detail));
            AddRow(row);
        }

        var flow = Flow();
        IncomingCell(flow, "You take 9", "", damageMathPanel => damageMathPanel.Show(incoming: 14, block: 5, osty: null, take: 9));
        IncomingCell(flow, "All blocked", "", damageMathPanel => damageMathPanel.Show(incoming: 8, block: 12, osty: null, take: 0));
        IncomingCell(flow, "This kills you", "The skull: the enemy turn takes you to 0 with nothing to save you.",
            damageMathPanel => damageMathPanel.Show(incoming: 46, block: 6, osty: null, take: 40, dies: true));
        IncomingCell(flow, "This might kill you", "When a roll decides it (which enemy a Lightning orb hits first), the skull carries the chance. "
            + "? means the numbers aren't certain: some outcome has something not modelled, or they're waiting on a newer result (a busy play).",
            damageMathPanel => damageMathPanel.Show(incoming: 46, block: 6, osty: null, take: 40, dies: true, deathChance: "40%", progress: "?"));
        IncomingCell(flow, "Most likely", "When a roll decides it (Bad Luck might be the card you draw at the end of your turn), the sum is the most likely way the turn goes, "
            + "with its chance. Hover the sum for every other way, each with its chance and what's different. Settings: Incoming Damage: most likely outcome (off: the worst case).",
            damageMathPanel => damageMathPanel.Show(incoming: 14, block: 5, osty: null, take: 9, outcomeChance: "72%"));
        IncomingCell(flow, "Too much to work out", "Rarely, a fight has too many outcomes to work out in time: a lone ?, with no number and no promise either way.",
            damageMathPanel => damageMathPanel.ShowUnknown());
        IncomingCell(flow, "Something saves you", "Fairy in a Bottle (or Lizard Tail) brings you back: your HP after the enemy turn.",
            damageMathPanel => damageMathPanel.Show(incoming: 46, block: 6, osty: null, take: 40, revive: (Potion<FairyInABottle>(), 23)));
        IncomingCell(flow, "Osty takes some", "Necrobinder: what Osty absorbs for you (red if it kills Osty).",
            damageMathPanel => damageMathPanel.Show(incoming: 18, block: 5, (6, false), take: 7));
        IncomingCell(flow, "Unblockable too", "Regret in hand: 3 HP lost whatever your block. The skull sits beside the last line.",
            damageMathPanel => damageMathPanel.Show(incoming: 14, block: 11, osty: null, take: 3, unblockable: 3));
        IncomingCell(flow, "Another turn first", "Ending your turn now gives you another one before the enemies act (Pael's Eye, Extra Turn from The Hanged Man "
            + "or Ambergris): no damage is shown, since your block and HP don't matter for ending this turn. Holding a card or a potion shows whether it would.",
            damageMathPanel => damageMathPanel.ShowExtraTurn(Relic<PaelsEye>(), progress: null));
        _rows!.AddChild(flow);
        Paragraph("Teammate Incoming Damage: in multiplayer, hover a teammate to see theirs beside their HP bar, worked out the same way "
            + "from what you can see of them (their block, powers, orbs and relics; not the cards in their hand, unless Count teammates' hands is on). "
            + "Yours steps aside while you hover them, unless you're holding a card.");
    }

    private static void IncomingCell(HFlowContainer flow, string title, string text, Action<DamageMathPanel> show)
    {
        var cell = Cell();
        cell.AddChild(Label(title, size: 20, Accent));
        cell.AddChild(ExampleBox(inner => show(new DamageMathPanel(inner))));
        if (text.Length > 0) cell.AddChild(CellText(text));
        flow.AddChild(cell);
    }

    // ------------------------------------------------------------------ rest sites

    private void BuildGuideRest()
    {
        SubSection("Rest Preview");
        Paragraph("At a rest site, beside your character, the same kind of sum: your HP, plus what resting heals, plus anything that changes it, "
            + "equals the HP you'll have. In multiplayer, hover a teammate while Mend is on offer to see the sum for them.");

        var heal = GameIcons.RestOption("heal");
        var flow = Flow();
        RestCell(flow, "Resting", "30% of your max HP, rounded down.",
            restMathPanel => restMathPanel.Show(currentHp: 30, new[] { (heal, 23) }, resultHp: 53, resultMax: 77, over: 0));
        RestCell(flow, "Some of it wasted", "Orange: the part of the heal past your max HP.",
            restMathPanel => restMathPanel.Show(currentHp: 70, new[] { (heal, 7) }, resultHp: 77, resultMax: 77, over: 16));
        RestCell(flow, "Relics change it", "A line each: Regal Pillow's +15, Stone Humidifier's max HP (the result shows the new max), Night Terrors.",
            restMathPanel => restMathPanel.Show(currentHp: 30, new[] { (heal, 23), (Relic<RegalPillow>(), 15), (Relic<StoneHumidifier>(), 5) }, resultHp: 73, resultMax: 82, over: 0));
        RestCell(flow, "Before the boss", "Pantograph and Bath Robes heal you when the boss fight starts: the result is the HP you start it with, "
            + "and what they'd heal anyway counts as wasted.",
            restMathPanel => restMathPanel.Show(currentHp: 40, new[] { (heal, 23), (Relic<Pantograph>(), 14) }, resultHp: 77, resultMax: 77, over: 11));
        RestCell(flow, "Mending a teammate", "Hover them while Mend is on offer: Mend heals them as their own Rest would.",
            restMathPanel => restMathPanel.Show(currentHp: 18, new[] { (GameIcons.RestOption("mend"), 21) }, resultHp: 39, resultMax: 70, over: 0));
        _rows!.AddChild(flow);
    }

    private static void RestCell(HFlowContainer flow, string title, string text, Action<RestMathPanel> show)
    {
        var cell = Cell();
        cell.AddChild(Label(title, size: 20, Accent));
        cell.AddChild(ExampleBox(inner => show(new RestMathPanel(inner))));
        cell.AddChild(CellText(text));
        flow.AddChild(cell);
    }

    /// <summary>A dark box like a widget's, holding an example.</summary>
    private static PanelContainer ExampleBox(Action<VBoxContainer> fill)
    {
        var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        box.AddThemeStyleboxOverride(GodotNames.Name("panel"), UiKit.PanelStyle(new Color(0, 0, 0, 0.35f), radius: 6, margin: 14));
        var inner = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddChild(inner);
        fill(inner);
        return box;
    }

    // ------------------------------------------------------------------ reminders outside fights

    private void BuildGuideReminders()
    {
        SubSection("Reminders");
        Paragraph("Between fights, your relics that change what a choice does show on the choice itself, "
            + "with the same plain and crossed icons as the tags on cards: crossed means the relic works against you there. "
            + "Each place is its own switch (Between fights page): Reward Tags, Event Reminders, Shop Reminders and Map Reminders.");

        var flow = Flow();

        // Card rewards: a tag on each offered card for every relic that taking it sets off.
        GuideCardCell(flow, Card<PommelStrike>(), "Reward Tags: card rewards",
            "On each card a reward offers: your relics that taking it sets off. Bing Bong copies the card, Lucky Fysh gives gold, and "
            + "every card counts toward Book of Five Rings' heal. With the green plus in its corner, taking this card heals you now; crossed, "
            + "part of that heal is wasted (you're missing less HP than it heals).",
            Tab("reminder-reward:BingBong", Relic<BingBong>()), Tab("reminder-reward:LuckyFysh", Relic<LuckyFysh>()),
            Tab("reminder-reward:BookOfFiveRings", Relic<BookOfFiveRings>(), marker: SpireQoL.Cards.GameIcons.Heal()));

        // Shops: icons left of the price, the net price under it.
        var shop = Cell();
        shop.AddChild(Label("Shop Reminders", size: 20, Accent));
        shop.AddChild(ExampleBox(inner =>
        {
            var pair = UiKit.Row(separation: 28);
            if (RelicOf<LuckyFysh>() is { } fysh)
                pair.AddChild(RunReminders.ExampleShopPrice(new[] { (fysh, false) }, price: 75, net: 60));
            if (RelicOf<Sozu>() is { } sozu)
                pair.AddChild(RunReminders.ExampleShopPrice(new[] { (sozu, true) }, price: 50, net: null));
            inner.AddChild(pair);
        }));
        shop.AddChild(CellText("Beside each price: the relics buying it sets off. On a card with Lucky Fysh, the green number is what it "
            + "really costs after the gold you get back. Crossed on a potion: with Sozu you can't get potions."));
        flow.AddChild(shop);

        // Events: icons left of the option.
        var vBoxContainer = Cell();
        vBoxContainer.AddChild(Label("Event Reminders", size: 20, Accent));
        vBoxContainer.AddChild(ExampleBox(inner =>
        {
            void Option(string text, params (RelicModel?, bool, string?)[] relics)
            {
                var row = UiKit.Row(separation: 10);
                row.AddChild(RunReminders.ExampleEventIcons(relics.Where(relic => relic.Item1 != null).Select(relic => (relic.Item1!, relic.Item2, relic.Item3))));
                var option = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                option.AddThemeStyleboxOverride(GodotNames.Name("panel"), UiKit.PanelStyle(new Color(0.25f, 0.2f, 0.15f, 0.9f), radius: 6, margin: 12));
                option.AddChild(UiKit.Label(text, size: 18, Colors.White));
                row.AddChild(option);
                inner.AddChild(row);
            }
            Option("Accept: gain a relic, add a curse", (RelicOf<BingBong>(), true, null), (RelicOf<DarkstonePeriapt>(), false, null));
            Option("Study: choose a card", (RelicOf<FresnelLens>(), false, "43%"));
        }));
        vBoxContainer.AddChild(CellText("Left of an option: the first adds a curse, so Bing Bong (crossed) copies the curse and Darkstone Periapt "
            + "gives you max HP. A relic that changes only some cards shows only when one of them can come up, with the chance it "
            + "does: here 43% that a card Fresnel Lens enchants is among the ones you're offered."));
        flow.AddChild(vBoxContainer);

        // Inspect: the magnifier beside an option with random cards (decision #64).
        var inspect = Cell();
        inspect.AddChild(Label("Event Reminders: Inspect", size: 20, Accent));
        inspect.AddChild(ExampleBox(inner =>
        {
            var row = UiKit.Row(separation: 10);
            var option = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            option.AddThemeStyleboxOverride(GodotNames.Name("panel"), UiKit.PanelStyle(new Color(0.25f, 0.2f, 0.15f, 0.9f), radius: 6, margin: 12));
            option.AddChild(UiKit.Label("Gorge: choose 2 of 8 commons", size: 18, Colors.White));
            row.AddChild(option);
            var icon = UiKit.Icon(RunReminders.InspectIcon(), size: 40);
            icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(icon);
            inner.AddChild(row);
        }));
        inspect.AddChild(CellText("Right of an option that gives cards at random from a known pool (a reward, a transform). Click it to see "
            + "every card it can give, grouped as it draws them; click the cards you'd be happy with to get your chance of being offered "
            + "one. For a transform, pick the card you'd transform to see what it can become. Marks are kept for the run."));
        flow.AddChild(inspect);

        // Map: icons left of the legend's rows (decision #65).
        var map = Cell();
        map.AddChild(Label("Map Reminders", size: 20, Accent));
        map.AddChild(ExampleBox(inner =>
        {
            void Legend(string room, params (RelicModel?, bool, string?)[] relics)
            {
                var row = UiKit.Row(separation: 10);
                row.AddChild(RunReminders.ExamplePathIcons(relics.Where(relic => relic.Item1 != null).Select(relic => (relic.Item1!, relic.Item2, relic.Item3))));
                var name = UiKit.Label(room, size: 20, Colors.White);
                name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                row.AddChild(name);
                row.Alignment = BoxContainer.AlignmentMode.End;
                inner.AddChild(row);
            }
            Legend("Elite", (RelicOf<BlackStar>(), false, null), (RelicOf<SlingOfCourage>(), false, null));
            Legend("Rest", (RelicOf<EternalFeather>(), false, "+9"), (RelicOf<Girya>(), false, null));
            Legend("Merchant", (RelicOf<MawBank>(), true, null), (RelicOf<MealTicket>(), false, "+15"));
        }));
        map.AddChild(CellText("Left of the map legend's rows: your relics (and quest cards, like Dowsing Rod's) that make that kind of room "
            + "unusually good or bad, with the heal or count they have now. Crossed: Maw Bank stops paying once you buy in a shop. Hover "
            + "one for its text and to light up those rooms on the map."));
        flow.AddChild(map);

        // Offered cards: a tag per relic that makes the card stronger in fights in a way it doesn't show (decision #62).
        GuideCardCell(flow, Card<Begone>(), "Reward Tags: cards you're offered",
            "On card rewards, an event's choice of cards and in the shop: a tag for each of your relics that makes the card stronger "
            + "in fights in a way the card doesn't show. Begone! makes a Minion Strike, so Strike Dummy and Vitruvian Minion both apply. "
            + "Also Miniature Cannon on upgraded Attacks and Mystic Lighter on enchanted ones.",
            Tab("reminder-reward:StrikeDummy", Relic<StrikeDummy>()), Tab("reminder-reward:VitruvianMinion", Relic<VitruvianMinion>()));
        _rows!.AddChild(flow);
    }

    // ------------------------------------------------------------------ in fights: on enemies, while you hold a card, under End Turn

    private void BuildGuideEnemies()
    {
        Section(SectionTitle(FeatureSection.OnEnemies));
        GuideRow(example: null, "Intent Details",
            "On each enemy's intent during your turn: the block it gains as a number on its defend intent (like an attack's damage), and each buff or debuff its move applies, "
            + "with its icon and amount (Strength 3, Weak 2), in the top-left corner of its buff or debuff intent, clear of the intent's number. Read from the move's own code; when the move only sometimes applies a power and what decides it can't be seen yet, a ? in place of the amount. Nothing while the intent is hidden.");
        GuideRow(example: null, "Intent Forecast",
            "Hover an enemy's intent: above it, its next moves after the one on screen (next turn just above it, later turns higher), with what each buff or debuff applies (Frail 2). Every turn its rules make certain, up to three turns ahead, "
            + "then the moves a random pick can make, side by side with their chances above. Worked out from its move rules and the moves it has made, never a roll. "
            + "Each later attack shows what it will hit you for then if you play nothing: Strength an enemy gains every turn, Weak and Vulnerable wearing off and a move's own buff count; "
            + "where another enemy's random move changes it, only what's certain counts, and where it can't be followed, its damage as things stand now.");
        var skull = UiKit.Icon(GameIcons.Skull(), IconSize * 0.7f);
        var example = UiKit.Row(separation: 4);
        example.AddChild(skull);
        example.AddChild(UiKit.Label("50%", size: 18, Colors.White));
        example.AddChild(UiKit.Label("  12/40", size: 20, Colors.White));
        GuideRow(example, "Death Skull",
            "Beside an enemy's HP: it dies before acting if you end your turn now (Poison, your end-of-turn damage, orbs), even if its own death brings it straight back (the Waterfall Giant). "
            + "With a chance when a roll decides it (In fights: Chances, Death chances). With a range and ? when the fight was too big to work out (from the kills already certain up).");
    }

    private void BuildGuideHolding()
    {
        Section(SectionTitle(FeatureSection.WhileHoldingACard));
        GuideRow(example: null, "Play Preview",
            "Hold a card (and aim it, for one that needs a target): Incoming Damage, Teammate Incoming Damage and the Death Skull show the turn as it would go after you play it, "
            + "tinted lavender, and an enemy intent the play changes gets a line through its damage and the new number above it (Weak lowering it, Strength raising it; the line alone for an enemy the play kills). "
            + "Every outcome of the play is already worked out for each card and target, so it shows at once. The card tags don't change. "
            + "Potions too: while you point at a potion's Drink, or aim it (at an enemy, or a teammate you throw it at), the same displays show the turn after using it, "
            + "once its outcome is worked out (a moment, in the background). Entropic Brew shows nothing: its random potions don't change the fight.");
    }

    private void BuildGuideProgress()
    {
        Section(SectionTitle(FeatureSection.UnderEndTurn));
        var progress = UiKit.Row(separation: 6);
        progress.AddChild(UiKit.Icon(GameIcons.Clock(), size: 22));
        progress.AddChild(new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Value = 0.6, ShowPercentage = false, CustomMinimumSize = new Vector2(90, 8),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        GuideRow(progress, "Prediction Progress",
            "Rarely, under the End Turn button: the mod hasn't finished working out its predictions for this board yet (it has too many possible "
            + "outcomes to finish within a frame), and keeps working in the background. The bar fills as it gets through them. Meanwhile you see "
            + "the last finished results: Incoming Damage and the Death Skull add a ? after a tenth of a second, and each card's tags change as soon as that card is done.");
    }

    // ------------------------------------------------------------------ quick setup, general

    private void BuildGuideSetup()
    {
        Section("Quick setup");
        GuideRow(example: null, "Quick setup",
            "The first page of this menu, which opens on it at your first launch: card tags as four levels shown on real cards (with Amounts on tags and Target marker under them), then a switch for each group of features "
            + "(On enemies, Beside you, Play Preview, Ethereal Fade, Between fights), Show chances, and Everything for every default.");
    }

    private void BuildGuideGeneral()
    {
        Section("General");
        GuideRow(example: null, "Update Notice",
            "On the main menu, above the game's MODDED note: how many Spire QoL versions behind you are, or that you're up to date.");
        GuideRow(example: null, "Moving things",
            $"{Settings.UnlockLayoutKey} unlocks the layout: drag a widget to move it, scroll over it to resize, right-click to reset. {Settings.HideAllKey} hides everything.");
    }

    /// <summary>A heading inside a section (one feature of several in it).</summary>
    private void SubSection(string title)
    {
        var label = Label(title, size: 21, Text);
        var marginContainer = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        marginContainer.AddThemeConstantOverride(GodotNames.Name("margin_top"), 10);
        marginContainer.AddThemeConstantOverride(GodotNames.Name("margin_bottom"), 4);
        marginContainer.AddChild(label);
        _rows!.AddChild(marginContainer);
    }

    private void GuideRow(Control? example, string name, string text)
    {
        var row = RowBox();
        var holder = new MarginContainer { CustomMinimumSize = new Vector2(120, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        if (example != null)
        {
            example.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            holder.AddChild(example);
        }
        row.AddChild(holder);
        row.AddChild(Label(name, size: 20, Text, NameWidth));
        row.AddChild(Wrapped(text));
        AddRow(row);
    }

    // ------------------------------------------------------------------ example cards

    private void GuideCardCell(HFlowContainer flow, CardModel? card, string title, string text, params TabSpec[] tabs) =>
        GuideCardCell(flow, card, title, text, opacity: null, tabs);

    private void GuideCardCell(HFlowContainer flow, CardModel? card, string title, string text, float? opacity, params TabSpec[] tabs)
    {
        var cell = Cell();
        if (card != null)
        {
            var example = new ShownCard(card, GuideCardScale, GuideCellWidth, tabs, opacity);
            _lastGuideCard = example;
            cell.AddChild(example.Root);
            _guideTicks.Add((_, _) => example.Tick());
        }
        cell.AddChild(Label(title, size: 20, Accent));
        cell.AddChild(CellText(text));
        flow.AddChild(cell);
    }

    /// <summary>
    /// Two cards held like a hand of two (fanned, each turned out a little about its bottom): <paramref name="back"/> plain
    /// behind, <paramref name="front"/> over it at <paramref name="opacity"/>, so the card behind shows through.
    /// </summary>
    private void GuideHandCell(HFlowContainer flow, CardModel? back, CardModel? front, string title, string text, float opacity)
    {
        var cell = Cell();
        if (back != null && front != null)
        {
            var cardWidth = MegaCrit.Sts2.Core.Nodes.Cards.NCard.defaultSize.X * GuideCardScale;
            var behind = new ShownCard(back, GuideCardScale, cardWidth, Array.Empty<TabSpec>());
            var over = new ShownCard(front, GuideCardScale, cardWidth, Array.Empty<TabSpec>(), opacity);
            var height = behind.Root.CustomMinimumSize.Y;
            // Room for the outer corners as they turn out.
            var hand = new Control { CustomMinimumSize = new Vector2(GuideCellWidth, height + 12), MouseFilter = Control.MouseFilterEnum.Ignore };
            var spread = cardWidth * 0.42f;
            foreach (var (card, dx, degrees) in new[] { (behind, -spread / 2, -9f), (over, spread / 2, 7f) })
            {
                card.Root.Position = new Vector2((GuideCellWidth - cardWidth) / 2 + dx, 6);
                card.Root.PivotOffset = new Vector2(cardWidth / 2, height);
                card.Root.RotationDegrees = degrees;
                hand.AddChild(card.Root);
                _guideTicks.Add((_, _) => card.Tick());
            }
            cell.AddChild(hand);
        }
        cell.AddChild(Label(title, size: 20, Accent));
        cell.AddChild(CellText(text));
        flow.AddChild(cell);
    }

    // ------------------------------------------------------------------ helpers

    private static TabSpec Tab(string key, Texture2D? icon, bool crossed = false, int? number = null, string? numberText = null,
        bool aimed = false, string? text = null, Texture2D? marker = null) =>
        new(key, icon, crossed, number, Text: text, NumberText: numberText, Aimed: aimed, Marker: marker);

    /// <param name="keywords">Keywords the example has picked up (Retain, Sly): a copy of the card carries them, so its text shows them.</param>
    private static CardModel? Card<T>(params CardKeyword[] keywords) where T : CardModel
    {
        CardModel canonical;
        try { canonical = ModelDb.Card<T>(); } catch { return null; }
        if (keywords.Length == 0) return canonical;
        try
        {
            var copy = canonical.ToMutable();
            foreach (var cardKeyword in keywords) copy.AddKeyword(cardKeyword);
            return copy;
        }
        catch
        {
            return canonical;
        }
    }

    private static Texture2D? Relic<T>() where T : RelicModel
    {
        try { return ModelDb.Relic<T>().Icon; } catch { return null; }
    }

    private static RelicModel? RelicOf<T>() where T : RelicModel
    {
        try { return ModelDb.Relic<T>(); } catch { return null; }
    }

    private static PowerModel? PowerOf<T>() where T : PowerModel
    {
        try { return ModelDb.Power<T>(); } catch { return null; }
    }

    private static Texture2D? Power<T>() where T : PowerModel => GameIcons.Power<T>();

    private static Texture2D? Enchantment<T>() where T : EnchantmentModel => GameIcons.Enchantment<T>();

    private static Texture2D? Potion<T>() where T : PotionModel
    {
        try { return ModelDb.Potion<T>().Image; } catch { return null; }
    }

    private void Paragraph(string text)
    {
        var label = Label(text, size: 18, Muted);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var marginContainer = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        marginContainer.AddThemeConstantOverride(GodotNames.Name("margin_left"), 8);
        marginContainer.AddThemeConstantOverride(GodotNames.Name("margin_right"), 8);
        marginContainer.AddThemeConstantOverride(GodotNames.Name("margin_bottom"), 8);
        marginContainer.AddChild(label);
        _rows!.AddChild(marginContainer);
    }

    private static Label Wrapped(string text)
    {
        var label = Label(text, size: 18, Muted);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    private static HFlowContainer Flow()
    {
        var flow = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride(GodotNames.Name("h_separation"), 24);
        flow.AddThemeConstantOverride(GodotNames.Name("v_separation"), 26);
        return flow;
    }

    private static VBoxContainer Cell()
    {
        var cell = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(GuideCellWidth, 0) };
        cell.AddThemeConstantOverride(GodotNames.Name("separation"), 6);
        return cell;
    }

    private static Label CellText(string text)
    {
        var label = Label(text, size: 17, Muted);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(GuideCellWidth, 0);
        return label;
    }
}
