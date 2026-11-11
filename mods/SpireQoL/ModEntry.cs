using Godot;
using MegaCrit.Sts2.Core.Modding;
using SpireQoL.Cards;
using SpireQoL.Features;
using Sts2Mods.Core;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Settings;
using Sts2Mods.Core.Ui;

namespace SpireQoL;

/// <summary>
/// Entry point. The game calls the static method named in [ModInitializer] once at load.
/// (Without this attribute the game would just call Harmony.PatchAll on the assembly,
/// which would apply every patch with no per-feature isolation.)
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Id = "SpireQoL";

    public static ModLog Log { get; } = new(Id);
    public static FeatureHost Features { get; } = new(Id, Log);

    public static void Initialize()
    {
        Log.Info($"loading v{typeof(ModEntry).Assembly.GetName().Version}");

        var settingsFile = new JsonSettingsFile<QolSettings>(JsonSettingsFile<QolSettings>.DefaultPathFor(typeof(ModEntry), Id), Log);
        var settings = settingsFile.Load();
        MigrateLayout(settings);
        MigrateTabs(settings);
        MigrateSettings(settings);

        var host = new OverlayHost(Id, Log)
        {
            UnlockLayoutKey = ParseKey(settings.UnlockLayoutKey, Key.F8),
            HideAllKey = ParseKey(settings.HideAllKey, Key.F9),
        };
        host.LayoutChanged += settingsFile.Save;
        host.SuppressWhen = Sts2State.IsScreenOpenOverCombat;
        host.WidgetUnderlay = Sts2State.WidgetUnderlay; // widgets draw under the cards in hand

        var ctx = new QolContext(settingsFile, host);

        // Error reporting: crashes and audit mismatches go to Sentry with the game state attached.
        var version = typeof(ModEntry).Assembly.GetName().Version?.ToString(3) ?? "?";
        var reporter = Sts2Mods.Core.Diagnostics.ErrorReporter.Init(Diagnostics.ReportingConfig.Dsn, settings.ReportErrors, $"spireqol@{version}", Log);
        reporter.Tags["mod_version"] = version;
        try { reporter.Tags["game_version"] = MegaCrit.Sts2.Core.Nodes.NGame.GetGameVersion(); } catch { }
        reporter.ContextProvider = () => Diagnostics.Snapshot.Capture(ctx);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => reporter.Flush();
        Sts2State.Init();
        // A private member a rule reads is gone (a game update renamed it): the rule silently uses its
        // fallback, so say so once, in the log and as a report.
        Sts2State.MissingMember += (type, member) =>
        {
            Log.Warn($"game member not found: {type}.{member} (a rule reading it uses its fallback)");
            reporter.Mismatch("api", $"Game member not found: {type}.{member}", new[] { "missing-member", type, member },
                $"Sts2State.Private couldn't find {type}.{member}. A game update probably renamed or removed it; the rule reading it is using its fallback value.");
        };

        // Order matters for card decorations: later features add badges after earlier ones.
        IFeature[] features =
        {
            new CardIndicatorsFeature(ctx),
            new RelicCountersFeature(ctx),
            new ProcIndicatorsFeature(ctx),
            new IncomingDamageFeature(ctx),
            new ShuffleWarningFeature(ctx),
            new UpdateNoticeFeature(),
            new Diagnostics.ForecastAudit(),
            new Diagnostics.ProcAudit(ctx),
            new Diagnostics.CoverageWatch(ctx),
        };
        foreach (var f in features) Features.Add(f);

        Features.ApplyAll(f => settings.Features.TryGetValue(f.Id, out var on) ? on : f.EnabledByDefault);

        // Write every feature id into settings so players can see what can be toggled.
        foreach (var f in features)
        {
            settings.Features.TryAdd(f.Id, f.EnabledByDefault);
        }
        settingsFile.Save();

        // Features switched off are still attached, just skipped each frame, so the settings menu
        // can turn them on and off while the game runs (host.FeatureEnabled).
        var defaults = features.ToDictionary(f => f.Id, f => f.EnabledByDefault);
        host.FeatureEnabled = id => settings.Features.TryGetValue(id, out var on) ? on : !defaults.TryGetValue(id, out var d) || d;
        foreach (var f in features)
        {
            if (f is IFrameFeature frame && Features.States[f.Id] != FeatureState.Failed)
            {
                host.AddFeature(f.Id, frame);
            }
        }

        var menu = new Menu.SettingsMenu(ctx, Features, host);
        host.AddHotkey(ParseKey(settings.SettingsKey, Key.F10), menu.Toggle);
        host.AddLateFrame(menu.OnFrame);
        Menu.SettingsCatalog.Check(Log);

        var renderer = new CardOverlayRenderer(host, ctx.Decor);
        host.AddLateFrame(renderer.Render);

        // New tab kinds are written into settings as they first appear (so the file lists every
        // tab that can be turned off). Save at most every few seconds.
        ulong lastSave = 0;
        var warnedKeys = new HashSet<string>();
        host.AddLateFrame(() =>
        {
            if (!settings.TabsDirty) return;
            var now = Time.GetTicksMsec();
            if (now - lastSave < 3000) return;
            lastSave = now;
            settings.TabsDirty = false;
            settingsFile.Save();
            foreach (var key in settings.Tabs.Keys)
            {
                if (!key.EndsWith(":*") && !Menu.SettingsCatalog.IsKnownTabKey(key) && warnedKeys.Add(key))
                {
                    Log.Warn($"tab kind {key} isn't in the settings menu catalog (Menu/SettingsCatalog.cs); it's listed under Card tabs > Other");
                }
            }
        });
        host.Start();
    }

    /// <summary>Resets saved positions whose defaults changed, so players get the new defaults.</summary>
    private static void MigrateLayout(QolSettings settings)
    {
        if (settings.LayoutVersion < 2)
        {
            // 0.3.0: Incoming Damage moved under the player's HP; Shuffle Warning lost its widget.
            settings.Widgets.Remove("incoming-damage");
            settings.Widgets.Remove("shuffle-warning");
        }
        if (settings.LayoutVersion < 3)
        {
            // 0.4.0: Incoming Damage moved to the left of the block indicator.
            settings.Widgets.Remove("incoming-damage");
        }
        if (settings.LayoutVersion < 4)
        {
            // 0.6.0: the counter tray attaches to the right of the HP bar; Ethereal default 0.7 → 0.8.
            settings.Widgets.Remove("relic-counters");
            if (Math.Abs(settings.EtherealOpacity - 0.7f) < 0.001f) settings.EtherealOpacity = 0.8f;
        }
        settings.LayoutVersion = QolSettings.CurrentLayoutVersion;
    }

    /// <summary>0.19.0: the one-off tab switches became entries in Tabs.</summary>
    private static void MigrateTabs(QolSettings settings)
    {
        if (settings.ShowDoublePlay == false) settings.Tabs["double-play:*"] = false;
        settings.ShowDoublePlay = null;
        settings.DimBlockedCards = null;
        settings.ShowInnateEternalBadges = null; // Innate/Eternal tabs were removed
        settings.Tabs.Remove("blocked"); // 0.23.1: the game's crossed-out cost already marks these cards
    }

    /// <summary>
    /// 0.23.0 (settings menu): Relics now only controls the tracker tray (relic tabs are
    /// Tabs "proc:<Relic>"), and turn timers became per-relic tracker entries (off by default).
    /// </summary>
    private static void MigrateSettings(QolSettings settings)
    {
        if (settings.SettingsVersion < 1)
        {
            foreach (var (relic, on) in settings.Relics.ToList())
            {
                // Relics also held countdown powers (tray only); only relic tabs were hidden by it.
                if (!on && !relic.EndsWith("Power")) settings.Tabs["proc:" + relic] = false;
            }
            if (settings.ShowTurnTimers == true)
            {
                foreach (var spec in Data.RelicSpecs.All.Where(s => s.TurnTimer)) settings.Relics.TryAdd(spec.TypeName, true);
            }
        }
        settings.ShowTurnTimers = null;
        settings.SettingsVersion = QolSettings.CurrentSettingsVersion;
    }

    private static Key ParseKey(string? name, Key fallback)
        => Enum.TryParse<Key>(name, ignoreCase: true, out var key) ? key : fallback;
}
