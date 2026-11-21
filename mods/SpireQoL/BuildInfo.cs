using System.Reflection;

namespace SpireQoL;

/// <summary>
/// Which build of the mod this is (decision #128): its version, the commit it was built from (SourceLink stamps it after a +
/// in AssemblyInformationalVersion), whether it had uncommitted changes (Directory.Build.targets stamps GitDirty), and when the
/// DLL was built. The dev stream, the dashboard and notes carry <see cref="Label"/>, so two builds of one version can be told apart.
/// </summary>
internal static class BuildInfo
{
    private static readonly Assembly Me = typeof(ModEntry).Assembly;

    private static readonly string Info = Me.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

    /// <summary>The version (1.24.6).</summary>
    public static string Version => Info.IndexOf('+') is var plus and >= 0 ? Info[..plus] : Info;

    /// <summary>The commit, 7 characters, or "" when none was stamped.</summary>
    public static string Commit => Info.IndexOf('+') is var plus and >= 0 && Info[(plus + 1)..] is var c ? (c.Length > 7 ? c[..7] : c) : "";

    /// <summary>
    /// A development build: anything but the release script's Release build (a plain <c>dotnet build</c> is Debug). Its version is
    /// the one the next release will carry, so it's labelled "-dev".
    /// </summary>
#if DEBUG
    public static bool Dev => true;
#else
    public static bool Dev => false;
#endif

    /// <summary>Built with uncommitted changes.</summary>
    public static bool Dirty => Me.GetCustomAttributes<AssemblyMetadataAttribute>().Any(a => a.Key == "GitDirty" && a.Value == "true");

    /// <summary>When the DLL was built (its file's time).</summary>
    public static DateTime BuiltAt
    {
        get
        {
            try { return File.GetLastWriteTime(Me.Location); }
            catch { return DateTime.MinValue; }
        }
    }

    /// <summary>
    /// "1.25.0+abc1234" for a release; "1.25.0-dev+abc1234" for a development build (semantic versioning's pre-release and build
    /// parts), with " uncommitted, built Nov 20 19:40" when it had uncommitted changes (the commit alone wouldn't tell two such
    /// builds apart).
    /// </summary>
    public static string Label => $"{Version}{(Dev ? "-dev" : "")}{(Commit == "" ? "" : "+" + Commit)}{(Dirty ? $" uncommitted, built {BuiltAt:MMM d HH:mm}" : "")}";
}
