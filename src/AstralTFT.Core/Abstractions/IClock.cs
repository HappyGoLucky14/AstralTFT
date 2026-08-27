namespace AstralTFT.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
