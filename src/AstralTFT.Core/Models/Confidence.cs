namespace AstralTFT.Core.Models;

public enum ConfidenceState
{
    Unknown = 0,
    Probable = 1,
    Confirmed = 2
}

public readonly record struct Confidence(double Value)
{
    public double Clamped => Math.Clamp(Value, 0d, 1d);

    public ConfidenceState Classify(
        double probableThreshold = 0.85,
        double confirmedThreshold = 0.97)
    {
        var value = Clamped;
        if (value >= confirmedThreshold) return ConfidenceState.Confirmed;
        if (value >= probableThreshold) return ConfidenceState.Probable;
        return ConfidenceState.Unknown;
    }

    public override string ToString() => $"{Clamped:P1}";
}
