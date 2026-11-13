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
    private static IReadOnlyList<string>? _gameplay;

    /// <summary>
    /// "id-version" of each loaded mod (not ours) that changes the game, empty when there are none. Read once the
    /// mods are loaded (not from a mod initializer: later mods aren't loaded yet), then kept.
    /// </summary>
    public static IReadOnlyList<string> Gameplay(Assembly ours)
    {
        if (_gameplay != null) return _gameplay;
        var found = new List<string>();
        foreach (var m in ModManager.GetLoadedMods())
        {
            if (m.assemblies.Contains(ours) || m.manifest?.id == ours.GetName().Name) continue;
            var name = $"{m.manifest?.id ?? "?"}-{m.manifest?.version ?? "?"}";
            if ((m.manifest?.affectsGameplay ?? true) || m.assemblies.Any(AddsContent)) found.Add(name);
        }
        _gameplay = found;
        return found;
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
