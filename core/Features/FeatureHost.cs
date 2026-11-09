using HarmonyLib;

namespace Sts2Mods.Core.Features;

public enum FeatureState
{
    NotApplied,
    Disabled,
    Active,
    Failed,
}

/// <summary>
/// Applies a mod's features one at a time, each under its own Harmony id, so a game
/// patch that breaks one feature only turns that feature off.
/// </summary>
public sealed class FeatureHost
{
    private readonly string _modId;
    private readonly ModLog _log;
    private readonly List<IFeature> _features = new();
    private readonly Dictionary<string, FeatureState> _states = new();

    public FeatureHost(string modId, ModLog log)
    {
        _modId = modId;
        _log = log;
    }

    public IReadOnlyDictionary<string, FeatureState> States => _states;

    public FeatureHost Add(IFeature feature)
    {
        _features.Add(feature);
        _states[feature.Id] = FeatureState.NotApplied;
        return this;
    }

    /// <summary>
    /// Applies a feature that was off at startup (turned on from the settings menu). Returns
    /// false if it failed. Frame-only features have nothing to patch, so this is instant.
    /// </summary>
    public bool EnableLate(string id)
    {
        var feature = _features.FirstOrDefault(f => f.Id == id);
        if (feature == null) return false;
        if (_states[id] == FeatureState.Active) return true;
        if (_states[id] == FeatureState.Failed) return false;
        var harmony = new Harmony($"{_modId}.{feature.Id}");
        try
        {
            feature.Apply(harmony);
            _states[id] = FeatureState.Active;
            _log.Info($"[{id}] turned on");
            return true;
        }
        catch (Exception ex)
        {
            _states[id] = FeatureState.Failed;
            _log.Error($"[{id}] failed to apply; feature turned off. {ex}");
            Diagnostics.ErrorReporter.Instance?.Exception(ex, id + ".Apply");
            try { harmony.UnpatchAll(harmony.Id); } catch { /* best effort */ }
            return false;
        }
    }

    /// <param name="isEnabled">Settings lookup; defaults to each feature's EnabledByDefault.</param>
    public void ApplyAll(Func<IFeature, bool>? isEnabled = null)
    {
        foreach (var feature in _features)
        {
            if (!(isEnabled?.Invoke(feature) ?? feature.EnabledByDefault))
            {
                _states[feature.Id] = FeatureState.Disabled;
                _log.Info($"[{feature.Id}] disabled");
                continue;
            }

            var harmony = new Harmony($"{_modId}.{feature.Id}");
            try
            {
                feature.Apply(harmony);
                _states[feature.Id] = FeatureState.Active;
                _log.Info($"[{feature.Id}] active ({harmony.GetPatchedMethods().Count()} methods patched)");
            }
            catch (Exception ex)
            {
                _states[feature.Id] = FeatureState.Failed;
                _log.Error($"[{feature.Id}] failed to apply; feature turned off. {ex}");
                Diagnostics.ErrorReporter.Instance?.Exception(ex, feature.Id + ".Apply");
                try { harmony.UnpatchAll(harmony.Id); } catch { /* best effort */ }
            }
        }
    }
}
