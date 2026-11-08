namespace Sts2Mods.Core.Features;

/// <summary>
/// A feature that updates once per rendered frame. Most of our UI works this way: it reads
/// live game state and refreshes plain Godot nodes, instead of patching game methods.
///
/// Mod DLLs can't register Godot scripts (the loader doesn't call ScriptManagerBridge),
/// so we can't subclass Node and override _Process. One frame callback drives everything.
/// </summary>
public interface IFrameFeature
{
    /// <summary>Called once, on the main thread, after the overlay layer exists.</summary>
    void Attach(Ui.OverlayHost host);

    /// <summary>Called every frame while the feature is active. Throwing is contained.</summary>
    void OnFrame(Ui.OverlayHost host);
}
