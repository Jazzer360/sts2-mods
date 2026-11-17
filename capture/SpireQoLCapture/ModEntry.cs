using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;

namespace SpireQoLCapture;

/// <summary>
/// Entry point. Does nothing unless the game was started with --spireqol-capture=&lt;scenes folder&gt;
/// (capture.ps1 does this), so leaving the helper in the mods folder by mistake changes nothing.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static void Initialize()
    {
        var scenes = CommandLineHelper.GetValue("spireqol-capture");
        if (string.IsNullOrWhiteSpace(scenes))
        {
            GD.Print("[SpireQoLCapture] not started with --spireqol-capture; idle");
            return;
        }
        var output = CommandLineHelper.GetValue("spireqol-capture-out") ?? Path.Combine(scenes, "..", "out");
        var only = CommandLineHelper.GetValue("spireqol-capture-only");

        // Start on the main thread's first frame, so every await resumes on Godot's main thread.
        var tree = (SceneTree)Engine.GetMainLoop();
        void Start()
        {
            tree.ProcessFrame -= Start;
            var runner = new CaptureRunner(scenes, output, only);
            _ = runner.RunAsync();
        }
        tree.ProcessFrame += Start;
    }
}
