namespace AstralTFT.Capture.Abstractions;

/// <summary>
/// One owned capture-frame lease. The frame source must ensure NativeFrameHandle
/// remains valid until Dispose is called. A Windows Graphics Capture implementation
/// should therefore copy/lease the underlying texture before releasing the WinRT
/// capture-frame object.
/// </summary>
public sealed record CapturedFrame(
    long Sequence,
    DateTimeOffset CapturedAt,
    int Width,
    int Height,
    object NativeFrameHandle,
    IDisposable? ResourceLease = null) : IDisposable
{
    public void Dispose() => ResourceLease?.Dispose();
}

/// <summary>
/// Single-consumer asynchronous frame source. Async enumeration gives frame lifetime
/// explicit ownership semantics and avoids ambiguous EventHandler ownership when
/// native GPU resources are involved.
/// </summary>
public interface IFrameSource : IAsyncDisposable
{
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
        CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
