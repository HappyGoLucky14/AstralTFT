namespace AstralTFT.Infrastructure.Runtime;

public enum RuntimeFeature
{
    WindowDiscovery = 0,
    Capture = 1,
    ShopRecognition = 2,
    BoardRecognition = 3,
    ItemRecognition = 4,
    AugmentRecognition = 5,
    EconomyRecognition = 6,
    CosmeticRecognition = 7,
    Overlay = 8,
    CompanionLiveView = 9,
    MetaUpdates = 10,
    History = 11,
    PostGameAnalysis = 12
}

public enum ModuleHealth
{
    Healthy = 0,
    Degraded = 1,
    Disabled = 2,
    IncompatibleLayout = 3
}

public sealed record FeatureStatus(
    RuntimeFeature Feature,
    bool Enabled,
    ModuleHealth Health,
    string? Reason = null);

/// <summary>
/// Central feature gate used by Safe Mode and patch-compatibility fallbacks. Keeping this policy
/// outside individual detectors means a broken module can be disabled without branching through
/// unrelated capture/state/analysis code.
/// </summary>
public sealed class FeaturePolicy
{
    private readonly object _gate = new();
    private readonly Dictionary<RuntimeFeature, FeatureStatus> _status = Enum
        .GetValues<RuntimeFeature>()
        .ToDictionary(x => x, x => new FeatureStatus(x, true, ModuleHealth.Healthy));

    public bool IsSafeMode { get; private set; }

    public IReadOnlyList<FeatureStatus> Snapshot()
    {
        lock (_gate) return _status.Values.OrderBy(x => x.Feature).ToArray();
    }

    public bool IsEnabled(RuntimeFeature feature)
    {
        lock (_gate) return _status[feature].Enabled;
    }

    public void SetHealth(RuntimeFeature feature, ModuleHealth health, string? reason = null)
    {
        lock (_gate)
        {
            var current = _status[feature];
            _status[feature] = current with
            {
                Health = health,
                Enabled = health is not ModuleHealth.Disabled and not ModuleHealth.IncompatibleLayout,
                Reason = reason
            };
        }
    }

    public void EnterSafeMode(string reason)
    {
        lock (_gate)
        {
            IsSafeMode = true;
            Disable(RuntimeFeature.Capture, reason);
            Disable(RuntimeFeature.ShopRecognition, reason);
            Disable(RuntimeFeature.BoardRecognition, reason);
            Disable(RuntimeFeature.ItemRecognition, reason);
            Disable(RuntimeFeature.AugmentRecognition, reason);
            Disable(RuntimeFeature.EconomyRecognition, reason);
            Disable(RuntimeFeature.CosmeticRecognition, reason);
            Disable(RuntimeFeature.Overlay, reason);
            Disable(RuntimeFeature.CompanionLiveView, reason);

            // Meta/history/post-game remain usable so Safe Mode is not equivalent to a dead app.
            Enable(RuntimeFeature.MetaUpdates);
            Enable(RuntimeFeature.History);
            Enable(RuntimeFeature.PostGameAnalysis);
            Enable(RuntimeFeature.WindowDiscovery);
        }
    }

    public void ExitSafeMode()
    {
        lock (_gate)
        {
            IsSafeMode = false;
            foreach (var feature in Enum.GetValues<RuntimeFeature>())
                _status[feature] = new FeatureStatus(feature, true, ModuleHealth.Healthy);
        }
    }

    private void Disable(RuntimeFeature feature, string reason) =>
        _status[feature] = new FeatureStatus(feature, false, ModuleHealth.Disabled, reason);

    private void Enable(RuntimeFeature feature) =>
        _status[feature] = new FeatureStatus(feature, true, ModuleHealth.Healthy);
}
