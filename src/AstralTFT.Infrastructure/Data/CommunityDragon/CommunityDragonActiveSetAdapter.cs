using System.Text.Json;
using AstralTFT.Infrastructure.Networking;
using AstralTFT.Meta.Sources;

namespace AstralTFT.Infrastructure.Data.CommunityDragon;

public sealed class CommunityDragonActiveSetAdapter : IDataSourceAdapter<ActiveSetRequest, ActiveSetInfo>
{
    private static readonly Uri ActiveSetsUri = new(
        "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/tftsets.json");

    private readonly ConditionalHttpCache _cache;

    public CommunityDragonActiveSetAdapter(ConditionalHttpCache cache)
    {
        _cache = cache;
    }

    public DataSourceDescriptor Descriptor { get; } = new(
        "communitydragon-active-set",
        "CommunityDragon TFT Sets",
        IsOfficial: false,
        SupportedDatasets: new HashSet<DatasetKind> { DatasetKind.StaticGameData },
        ExpectedRefreshCadence: TimeSpan.FromHours(1));

    public async ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await _cache.GetStringAsync(
                ActiveSetsUri,
                "communitydragon-tftsets",
                TimeSpan.FromDays(2),
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<SourceSnapshot<ActiveSetInfo>> FetchAsync(
        ActiveSetRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _cache.GetStringAsync(
            ActiveSetsUri,
            "communitydragon-tftsets",
            TimeSpan.FromDays(2),
            cancellationToken).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result.Content);
        var root = doc.RootElement.GetProperty("LCTFTModeData");
        var set = root.GetProperty("mDefaultSet");

        var info = new ActiveSetInfo(
            set.GetProperty("SetName").GetString() ?? throw new InvalidDataException("Missing SetName."),
            set.GetProperty("SetCoreName").GetString() ?? throw new InvalidDataException("Missing SetCoreName."),
            set.GetProperty("SetDisplayName").GetString() ?? throw new InvalidDataException("Missing SetDisplayName."),
            set.TryGetProperty("SetAugmentName", out var augment) ? augment.GetString() ?? string.Empty : string.Empty,
            result.FetchedAt);

        var quality = result.IsStaleFallback ? 0.55 : result.FromCache ? 0.88 : 0.92;
        return new SourceSnapshot<ActiveSetInfo>(
            Descriptor.Id,
            DatasetKind.StaticGameData,
            new DataScope(request.PatchHint),
            result.FetchedAt,
            SampleSize: null,
            Quality: quality,
            Payload: info);
    }
}
