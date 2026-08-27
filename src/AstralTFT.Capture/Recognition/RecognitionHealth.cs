namespace AstralTFT.Capture.Recognition;

public enum DetectorHealthState
{
    Healthy,
    Degraded,
    CoolingDown,
    Disabled
}

public sealed record DetectorHealthSnapshot(
    string DetectorId,
    DetectorHealthState State,
    int ConsecutiveFailures,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    DateTimeOffset? RetryAfter,
    string? LastError);

/// <summary>
/// Small circuit breaker around each detector. A bad model/provider should degrade
/// independently instead of taking down capture or every other recogniser.
/// </summary>
public sealed class DetectorHealthTracker
{
    private sealed class MutableState
    {
        public DetectorHealthState State = DetectorHealthState.Healthy;
        public int ConsecutiveFailures;
        public DateTimeOffset? LastSuccessAt;
        public DateTimeOffset? LastFailureAt;
        public DateTimeOffset? RetryAfter;
        public string? LastError;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, MutableState> _states = new(StringComparer.OrdinalIgnoreCase);

    public bool CanRun(RecognitionDetectorDescriptor descriptor, DateTimeOffset now)
    {
        lock (_gate)
        {
            var state = GetOrCreate(descriptor.Id);
            if (state.State == DetectorHealthState.Disabled) return false;

            if (state.State == DetectorHealthState.CoolingDown && state.RetryAfter is { } retryAfter)
            {
                if (now < retryAfter) return false;
                state.State = DetectorHealthState.Degraded;
                state.RetryAfter = null;
            }

            return true;
        }
    }

    public void RecordSuccess(string detectorId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var state = GetOrCreate(detectorId);
            state.State = DetectorHealthState.Healthy;
            state.ConsecutiveFailures = 0;
            state.LastSuccessAt = now;
            state.RetryAfter = null;
            state.LastError = null;
        }
    }

    public void RecordFailure(
        RecognitionDetectorDescriptor descriptor,
        DateTimeOffset now,
        string? error)
    {
        lock (_gate)
        {
            var state = GetOrCreate(descriptor.Id);
            state.ConsecutiveFailures++;
            state.LastFailureAt = now;
            state.LastError = error;

            if (state.ConsecutiveFailures >= Math.Max(1, descriptor.MaxConsecutiveFailures))
            {
                state.State = DetectorHealthState.CoolingDown;
                state.RetryAfter = now + descriptor.EffectiveFailureCooldown;
            }
            else
            {
                state.State = DetectorHealthState.Degraded;
            }
        }
    }

    public void Disable(string detectorId, string? reason = null)
    {
        lock (_gate)
        {
            var state = GetOrCreate(detectorId);
            state.State = DetectorHealthState.Disabled;
            state.LastError = reason;
            state.RetryAfter = null;
        }
    }

    public void Enable(string detectorId)
    {
        lock (_gate)
        {
            var state = GetOrCreate(detectorId);
            state.State = DetectorHealthState.Healthy;
            state.ConsecutiveFailures = 0;
            state.RetryAfter = null;
            state.LastError = null;
        }
    }

    public DetectorHealthSnapshot Snapshot(string detectorId)
    {
        lock (_gate)
        {
            var state = GetOrCreate(detectorId);
            return new DetectorHealthSnapshot(
                detectorId,
                state.State,
                state.ConsecutiveFailures,
                state.LastSuccessAt,
                state.LastFailureAt,
                state.RetryAfter,
                state.LastError);
        }
    }

    private MutableState GetOrCreate(string detectorId)
    {
        if (!_states.TryGetValue(detectorId, out var state))
        {
            state = new MutableState();
            _states[detectorId] = state;
        }

        return state;
    }
}
