using AstralTFT.Core.Models;

namespace AstralTFT.State.Fusion;

public enum FusionDecisionKind
{
    Retained = 0,
    Deferred = 1,
    Accepted = 2,
    RejectedStale = 3
}

public sealed record FusionDecision<T>(
    FusionDecisionKind Kind,
    Observation<T>? Accepted,
    Observation<T>? Candidate,
    string Reason);

/// <summary>
/// Stabilizes noisy observations for one semantic value/region. Confirmed observations may be
/// accepted immediately; probable changes need temporal support. Low-confidence frames never
/// erase a stable accepted value.
/// </summary>
public sealed class TemporalObservationFusion<T>
{
    private readonly ObservationFusionPolicy _policy;
    private readonly IEqualityComparer<T> _comparer;
    private Observation<T>? _accepted;
    private Pending? _pending;

    public TemporalObservationFusion(
        ObservationFusionPolicy? policy = null,
        IEqualityComparer<T>? comparer = null)
    {
        _policy = policy ?? new ObservationFusionPolicy();
        _comparer = comparer ?? EqualityComparer<T>.Default;
    }

    public Observation<T>? Accepted => _accepted;

    public FusionDecision<T> Observe(Observation<T> observation)
    {
        if (_accepted is not null && observation.ObservedAt < _accepted.ObservedAt)
        {
            return new FusionDecision<T>(
                FusionDecisionKind.RejectedStale,
                _accepted,
                observation,
                "Observation predates the currently accepted value.");
        }

        if (_accepted is not null && _comparer.Equals(_accepted.Value, observation.Value))
        {
            // Same semantic value: refresh confidence/timestamp if the new evidence is at least as
            // trustworthy, but do not force a state transition.
            if (observation.Confidence.Clamped >= _accepted.Confidence.Clamped)
                _accepted = observation;

            _pending = null;
            return new FusionDecision<T>(
                FusionDecisionKind.Retained,
                _accepted,
                observation,
                "Observation agrees with accepted state.");
        }

        var classification = _policy.Classify(observation.Confidence);
        if (classification == ConfidenceState.Confirmed)
        {
            _accepted = observation;
            _pending = null;
            return new FusionDecision<T>(
                FusionDecisionKind.Accepted,
                _accepted,
                observation,
                "Confirmed observation accepted immediately.");
        }

        if (classification == ConfidenceState.Unknown)
        {
            return new FusionDecision<T>(
                FusionDecisionKind.Deferred,
                _accepted,
                observation,
                "Low-confidence observation cannot replace stable state.");
        }

        if (_pending is null ||
            !_comparer.Equals(_pending.Observation.Value, observation.Value) ||
            observation.ObservedAt - _pending.FirstSeenAt > _policy.ConfirmationWindow)
        {
            _pending = new Pending(observation, observation.ObservedAt, 1, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                observation.Source
            });

            return new FusionDecision<T>(
                FusionDecisionKind.Deferred,
                _accepted,
                observation,
                "Probable change awaiting supporting observation.");
        }

        var sources = new HashSet<string>(_pending.Sources, StringComparer.OrdinalIgnoreCase)
        {
            observation.Source
        };
        var confirmations = _pending.Confirmations + 1;
        _pending = _pending with
        {
            Observation = observation,
            Confirmations = confirmations,
            Sources = sources
        };

        // Two temporally consistent observations are enough for probable state. Independent source
        // agreement is accepted on the same rule but retained in diagnostics for future weighting.
        if (confirmations >= 2)
        {
            _accepted = observation;
            _pending = null;
            return new FusionDecision<T>(
                FusionDecisionKind.Accepted,
                _accepted,
                observation,
                sources.Count >= 2
                    ? "Probable change confirmed by repeated observations from multiple sources."
                    : "Probable change confirmed by repeated observations.");
        }

        return new FusionDecision<T>(
            FusionDecisionKind.Deferred,
            _accepted,
            observation,
            "Probable change still awaiting confirmation.");
    }

    private sealed record Pending(
        Observation<T> Observation,
        DateTimeOffset FirstSeenAt,
        int Confirmations,
        IReadOnlySet<string> Sources);
}
