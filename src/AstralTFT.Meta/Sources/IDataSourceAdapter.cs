namespace AstralTFT.Meta.Sources;

public enum DatasetKind
{
    StaticGameData = 0,
    Compositions = 1,
    Augments = 2,
    Items = 3,
    Units = 4,
    Trends = 5,
    PlayerRank = 6,
    PlayerMatches = 7,
    Cosmetics = 8
}

public sealed record DataScope(
    string Patch,
    string? RankBucket = null,
    string? Region = null,
    string? QueueType = null);

public sealed record DataSourceDescriptor(
    string Id,
    string DisplayName,
    bool IsOfficial,
    IReadOnlySet<DatasetKind> SupportedDatasets,
    TimeSpan ExpectedRefreshCadence,
    bool RequiresAuthentication = false);

public sealed record SourceSnapshot<T>(
    string SourceId,
    DatasetKind Kind,
    DataScope Scope,
    DateTimeOffset CapturedAt,
    long? SampleSize,
    double Quality,
    T Payload);

public interface IDataSourceAdapter
{
    DataSourceDescriptor Descriptor { get; }

    ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

public interface IDataSourceAdapter<TRequest, TPayload> : IDataSourceAdapter
{
    ValueTask<SourceSnapshot<TPayload>> FetchAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}
