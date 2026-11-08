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
    }

    public JsonSettingsFile<QolSettings> SettingsFile { get; }
    public QolSettings Settings => SettingsFile.Value;
    public OverlayHost Host { get; }
    public CardDecorFrame Decor { get; } = new();

    /// <summary>Set each frame by Incoming Damage; Block Forecast hides when it's shown (same info).</summary>
    public bool IncomingShown { get; set; }

    public MovableWidget Widget(string id, string title, WidgetLayout defaults)
        => new(Host, id, title, Settings.Widget(id, defaults), defaults);
}
