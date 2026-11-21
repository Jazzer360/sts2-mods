using SpireQoL.Cards;
using Sts2Mods.Core.Settings;
using Sts2Mods.Core.Ui;

namespace SpireQoL;

/// <summary>Shared services handed to every feature.</summary>
public sealed class QolContext
{
    public QolContext(JsonSettingsFile<QolSettings> settingsFile, OverlayHost host)
    {
        SettingsFile = settingsFile;
        Host = host;
        Decor.Filter = key => Settings.TabEnabled(key);
        Decor.Shape = spec => Settings.Shown(spec);
        Predictions = new Combat.CardPredictions(this);
    }

    public JsonSettingsFile<QolSettings> SettingsFile { get; }
    public QolSettings Settings => SettingsFile.Value;
    public OverlayHost Host { get; }
    public CardDecorFrame Decor { get; } = new();

    /// <summary>The predictions for the cards in your hand (worked out once a frame by Proc Indicators; read by the audits too).</summary>
    public Combat.CardPredictions Predictions { get; }


    public MovableWidget Widget(string id, string title, WidgetLayout defaults)
        => new(Host, id, title, Settings.Widget(id, defaults), defaults);
}
