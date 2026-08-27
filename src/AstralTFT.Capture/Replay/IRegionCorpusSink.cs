namespace AstralTFT.Capture.Replay;

public sealed record RegionCorpusWriteResult(string ContentHash, bool BlobCreated);

public interface IRegionCorpusSink
{
    ValueTask<RegionCorpusWriteResult> WriteAsync(
        RegionCorpusWriteRequest request,
        CancellationToken cancellationToken = default);
}
