namespace AstralTFT.Capture.Replay;

public enum RegionCorpusSourceKind
{
    LiveCapture,
    ImportedFrame
}

public sealed record RegionCorpusHeader(
    int SchemaVersion,
    string PixelFormat,
    DateTimeOffset CreatedAtUtc,
    string CreatedByVersion);

public sealed record RegionCorpusObservation(
    int SchemaVersion,
    string ContentHash,
    string RegionId,
    long FrameSequence,
    DateTimeOffset CapturedAtUtc,
    int Width,
    int Height,
    int Stride,
    RegionCorpusSourceKind SourceKind);

public sealed record RegionCorpusWriteRequest(
    string RegionId,
    long FrameSequence,
    DateTimeOffset CapturedAtUtc,
    int Width,
    int Height,
    int Stride,
    byte[] Pixels,
    RegionCorpusSourceKind SourceKind);
