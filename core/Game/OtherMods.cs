using System.Reflection;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;

namespace Sts2Mods.Core.Game;

/// <summary>
/// Other loaded mods that change the game: their manifest says <c>affects_gameplay</c> (the game's default when
/// a manifest leaves it out), or their code adds game content (a card, relic, power, monster: any AbstractModel).
/// Our predictions model the unmodded game, so with one of these loaded a mismatch or crash may be theirs.
/// Multiplayer needs the same gameplay mods on every player, so checking our own game is enough.
/// </summary>
public static class OtherMods
{
    private static IReadOnlyList<(string Id, string Name, bool Gameplay)>? _others;

    /// <summary>
    /// "id-version" of each loaded mod (not ours) that changes the game, empty when there are none: its manifest or code says
    /// so, or its id is in <paramref name="listed"/> (reporting.json's gameplayMods: mods that say they don't but do, decision
    /// #97). The mods are read once they're loaded (not from a mod initializer: later mods aren't loaded yet), then kept.
    /// </summary>
    public static IReadOnlyList<string> Gameplay(Assembly ours, IReadOnlyCollection<string>? listed = null)
    {
        if (_others == null)
        {
            var found = new List<(string, string, bool)>();
            foreach (var m in ModManager.GetLoadedMods())
            {
                if (m.assemblies.Contains(ours) || m.manifest?.id == ours.GetName().Name) continue;
                var id = m.manifest?.id ?? "?";
                found.Add((id, $"{id}-{m.manifest?.version ?? "?"}", (m.manifest?.affectsGameplay ?? true) || m.assemblies.Any(AddsContent)));
            }
            _others = found;
        }
        return _others.Where(o => o.Gameplay || (listed != null && listed.Contains(o.Id, StringComparer.OrdinalIgnoreCase))).Select(o => o.Name).ToList();
    }

    /// <summary>
    /// Another mod's code in an exception's stack, inner exceptions included: a frame in one of its assemblies, or a patched game
    /// method (Harmony runs it as a dynamic replacement) that another mod also patches. Its id; null when only our code, the
    /// game's and the runtime's is there. A crash of ours on another mod's content (its card reaching our code) is still ours.
    /// </summary>
    /// <param name="ourHarmony">Our Harmony ids' prefix ("SpireQoL."): our own patches don't count.</param>
    public static string? InStack(Exception ex, Assembly ours, string ourHarmony)
    {
        try
        {
            var others = new Dictionary<Assembly, string>();
            foreach (var m in ModManager.GetLoadedMods())
            {
                if (m.assemblies.Contains(ours) || m.manifest?.id == ours.GetName().Name) continue;
                foreach (var a in m.assemblies) others[a] = m.manifest?.id ?? "?";
            }
            return InStack(ex, others, ourHarmony);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The same, against a given map of other mods' assemblies (tests).</summary>
    internal static string? InStack(Exception ex, IReadOnlyDictionary<Assembly, string> others, string ourHarmony) => Walk(ex, others, ourHarmony, 0);

    private static string? Walk(Exception? ex, IReadOnlyDictionary<Assembly, string> others, string ourHarmony, int depth)
    {
        if (ex == null || depth > 8) return null;
        foreach (var f in new System.Diagnostics.StackTrace(ex, false).GetFrames())
        {
            var mb = f.GetMethod();
            if (mb == null) continue;
            var asm = mb.DeclaringType?.Assembly ?? mb.Module.Assembly;
            if (others.TryGetValue(asm, out var id)) return id;
            if (mb.DeclaringType == null && PatchOwner(mb.Name, ourHarmony) is { } owner) return owner;
        }
        if (ex is AggregateException agg)
            foreach (var inner in agg.InnerExceptions)
                if (Walk(inner, others, ourHarmony, depth + 1) is { } r) return r;
        return Walk(ex.InnerException, others, ourHarmony, depth + 1);
    }

    /// <summary>The first patch owner other than us on the game method a dynamic replacement stands for ("DMD&lt;Type::Method&gt;").</summary>
    private static string? PatchOwner(string dynamicName, string ourHarmony)
    {
        var i = dynamicName.IndexOf('<');
        var j = dynamicName.LastIndexOf('>');
        if (i < 0 || j <= i) return null;
        var inner = dynamicName[(i + 1)..j];
        foreach (var m in HarmonyLib.Harmony.GetAllPatchedMethods())
        {
            if (m.DeclaringType == null || !inner.Contains(m.DeclaringType.Name) || !inner.Contains("::" + m.Name)) continue;
            if (HarmonyLib.Harmony.GetPatchInfo(m)?.Owners.FirstOrDefault(o => !o.StartsWith(ourHarmony, StringComparison.Ordinal)) is { } owner) return owner;
        }
        return null;
    }

    /// <summary>"id-version" of every other loaded mod, gameplay or not (for reports).</summary>
    public static IReadOnlyList<string> Loaded(Assembly ours)
    {
        try
        {
            return ModManager.GetLoadedMods()
                .Where(m => !m.assemblies.Contains(ours) && m.manifest?.id != ours.GetName().Name)
                .Select(m => $"{m.manifest?.id ?? "?"}-{m.manifest?.version ?? "?"}")
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool AddsContent(Assembly a)
    {
        try
        {
            return a.GetTypes().Any(t => !t.IsAbstract && typeof(AbstractModel).IsAssignableFrom(t));
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Any(t => t is { IsAbstract: false } && typeof(AbstractModel).IsAssignableFrom(t));
        }
        catch
        {
            return false;
        }
    }
}
