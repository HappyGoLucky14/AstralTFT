using System.Diagnostics;
using System.Threading.Channels;
using AstralTFT.Capture.Abstractions;
using AstralTFT.Capture.Windows.Interop;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace AstralTFT.Capture.Windows;

public sealed record WgcCaptureTelemetry(
    long FramesArrived,
    long FramesReadBack,
    long FramesDroppedByThrottle,
    long FramesDroppedWhileBusy,
    long FramesDroppedByBackPressure,
    long ResizeEvents,
    long CaptureErrors,
    long TelemetryEventsEmitted,
    long BytesReadBack,
    int Width,
    int Height,
    string ReadbackRegionId,
    int ReadbackWidth,
    int ReadbackHeight,
    TimeSpan LastReadbackDuration);

public enum WgcCaptureEndReason
{
    Requested,
    CaptureItemClosed,
    DeviceLost,
    StartupFailed
}

public sealed record WgcCaptureEndedEventArgs(WgcCaptureEndReason Reason, Exception? Error = null);

/// <summary>
/// Window-specific Windows.Graphics.Capture source. This first real-machine gate
/// emits CPU-visible BGRA frames because the existing change-detector stack can
/// consume them immediately. WGC/D3D lifetime is intentionally isolated here so
/// ROI-only GPU readback can later replace this implementation without touching
/// state, analysis, or recogniser contracts.
/// </summary>
public sealed class WindowsGraphicsCaptureFrameSource : IFrameSource
{
    private readonly GameWindow _window;
    private readonly WgcCaptureOptions _options;
    private readonly Channel<CapturedFrame> _frames;
    private readonly object _lifecycleGate = new();

    private Direct3D11CaptureDevice? _device;
    private D3D11BgraReadback? _readback;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private SizeInt32 _poolSize;
    private long _sequence;
    private long _lastAcceptedTimestamp;
    private int _processingFrame;
    private int _started;
    private int _stopping;
    private int _endedRaised;
    private int _terminal;
    private readonly TaskCompletionSource<bool> _stopCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private long _framesArrived;
    private long _framesReadBack;
    private long _framesDroppedByThrottle;
    private long _framesDroppedWhileBusy;
    private long _framesDroppedByBackPressure;
    private long _resizeEvents;
    private long _captureErrors;
    private long _telemetryEventsEmitted;
    private long _bytesReadBack;
    private int _lastReadbackWidth;
    private int _lastReadbackHeight;
    private long _lastReadbackTicks;

    public WindowsGraphicsCaptureFrameSource(GameWindow window, WgcCaptureOptions? options = null)
    {
        if (window.Hwnd == 0) throw new ArgumentException("Game window has no HWND.", nameof(window));
        _window = window;
        _options = (options ?? new WgcCaptureOptions()).Validate();
        _frames = Channel.CreateBounded<CapturedFrame>(new BoundedChannelOptions(2)
        {
            SingleReader = false, // writer also drains stale queued frames before replacement
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public event EventHandler<WgcCaptureTelemetry>? TelemetryUpdated;
    public event EventHandler<Exception>? CaptureFaulted;
    public event EventHandler<WgcCaptureEndedEventArgs>? CaptureEnded;

    public WgcCaptureTelemetry Telemetry => new(
        Interlocked.Read(ref _framesArrived),
        Interlocked.Read(ref _framesReadBack),
        Interlocked.Read(ref _framesDroppedByThrottle),
        Interlocked.Read(ref _framesDroppedWhileBusy),
        Interlocked.Read(ref _framesDroppedByBackPressure),
        Interlocked.Read(ref _resizeEvents),
        Interlocked.Read(ref _captureErrors),
        Interlocked.Read(ref _telemetryEventsEmitted),
        Interlocked.Read(ref _bytesReadBack),
        _poolSize.Width,
        _poolSize.Height,
        _options.CpuReadbackRegion?.Id ?? "full-frame",
        Volatile.Read(ref _lastReadbackWidth),
        Volatile.Read(ref _lastReadbackHeight),
        TimeSpan.FromTicks(Interlocked.Read(ref _lastReadbackTicks)));

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _terminal) != 0)
                throw new InvalidOperationException("A WindowsGraphicsCaptureFrameSource is one-shot. Create a new source after it stops.");
            if (Volatile.Read(ref _started) != 0)
                return ValueTask.CompletedTask;

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                throw new PlatformNotSupportedException("AstralTFT capture requires Windows 10 2004 or newer.");
            if (!GraphicsCaptureSession.IsSupported())
                throw new PlatformNotSupportedException("Windows Graphics Capture is not supported on this system.");

            try
            {
                _device = new Direct3D11CaptureDevice(_options.AllowWarpFallback);
                _readback = new D3D11BgraReadback(_device.NativeDevice, _device.NativeContext);
                _item = GraphicsCaptureInterop.CreateForWindow(_window.Hwnd);
                _poolSize = _item.Size;

                if (_poolSize.Width <= 0 || _poolSize.Height <= 0)
                    throw new InvalidOperationException("TFT capture item reported an invalid size.");

                _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _device.WinRtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    _options.FramePoolBufferCount,
                    _poolSize);

                _session = _framePool.CreateCaptureSession(_item);
                TrySetCursorCapture(_session, _options.CaptureCursor);

                _framePool.FrameArrived += OnFrameArrived;
                _item.Closed += OnCaptureItemClosed;

                // Mark started before StartCapture so an immediately delivered frame
                // cannot be discarded merely because the callback beat this thread.
                Volatile.Write(ref _started, 1);
                _session.StartCapture();
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _started, 0);
                Volatile.Write(ref _terminal, 1);
                if (_framePool is not null)
                    _framePool.FrameArrived -= OnFrameArrived;
                if (_item is not null)
                    _item.Closed -= OnCaptureItemClosed;
                CleanupResourcesUnsafe();
                _frames.Writer.TryComplete(ex);
                RaiseEndedOnce(WgcCaptureEndReason.StartupFailed, ex);
                _stopCompletion.TrySetResult(true);
                throw;
            }
        }

        EmitTelemetry();
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await _frames.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_frames.Reader.TryRead(out var frame))
                yield return frame;
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestStop(WgcCaptureEndReason.Requested, null);
        await _stopCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        RequestStop(WgcCaptureEndReason.Requested, null);
        await _stopCompletion.Task.ConfigureAwait(false);
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        Interlocked.Increment(ref _framesArrived);

        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _stopping) != 0)
                return;

            // Never let WGC callbacks pile up behind a slow GPU->CPU map.
            if (Interlocked.Exchange(ref _processingFrame, 1) != 0)
            {
                Interlocked.Increment(ref _framesDroppedWhileBusy);
                DropOnePendingFrame(sender);
                return;
            }

        }

        var shouldEmitTelemetry = false;
        try
        {
            SizeInt32? recreate = null;
            var frame = sender.TryGetNextFrame();
            if (frame is null) return;

            try
            {
                var size = frame.ContentSize;
                if (size.Width <= 0 || size.Height <= 0) return;

                // Dispose the checked-out frame before Recreate. WGC returns its
                // backing surface to the pool when the frame is disposed.
                if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
                {
                    recreate = size;
                    return;
                }

                if (!ThrottleAllowsReadback())
                {
                    Interlocked.Increment(ref _framesDroppedByThrottle);
                    return;
                }

                var sw = Stopwatch.StartNew();
                using var texture = Direct3DSurfaceInterop.GetTexture2D(frame.Surface);
                var sourceRegion = _options.CpuReadbackRegion?.Project(size.Width, size.Height);
                var cpu = sourceRegion is { } region
                    ? _readback!.ReadRegion(texture, region)
                    : _readback!.Read(texture, size.Width, size.Height);
                sw.Stop();

                Interlocked.Exchange(ref _lastReadbackTicks, sw.Elapsed.Ticks);
                Interlocked.Increment(ref _framesReadBack);
                Interlocked.Add(ref _bytesReadBack, cpu.Buffer.RequiredByteLength);
                Volatile.Write(ref _lastReadbackWidth, cpu.Buffer.Width);
                Volatile.Write(ref _lastReadbackHeight, cpu.Buffer.Height);
                shouldEmitTelemetry = true;

                var captured = new CapturedFrame(
                    Interlocked.Increment(ref _sequence),
                    DateTimeOffset.UtcNow,
                    cpu.Buffer.Width,
                    cpu.Buffer.Height,
                    cpu.Buffer,
                    cpu.Lease,
                    sourceRegion);

                PublishLatest(captured);
            }
            finally
            {
                frame.Dispose();
                if (recreate is { } requestedSize && Volatile.Read(ref _stopping) == 0)
                {
                    RecreateFramePool(requestedSize);
                    shouldEmitTelemetry = true;
                }
            }
        }
        catch (Exception ex)
        {
            shouldEmitTelemetry = true;
            Interlocked.Increment(ref _captureErrors);
            try { CaptureFaulted?.Invoke(this, ex); } catch { }

            if (D3D11DeviceLoss.IsDeviceLoss(ex))
                QueueTerminalStop(WgcCaptureEndReason.DeviceLost, ex);
        }
        finally
        {
            Volatile.Write(ref _processingFrame, 0);
            if (shouldEmitTelemetry)
                EmitTelemetry();
        }
    }

    private bool ThrottleAllowsReadback()
    {
        var now = Stopwatch.GetTimestamp();
        var minimumTicks = Math.Max(1L, Stopwatch.Frequency / _options.MaxCpuReadbacksPerSecond);
        var prior = Interlocked.Read(ref _lastAcceptedTimestamp);
        if (prior != 0 && now - prior < minimumTicks)
            return false;

        Interlocked.Exchange(ref _lastAcceptedTimestamp, now);
        return true;
    }

    private void PublishLatest(CapturedFrame captured)
    {
        // Explicit newest-frame semantics. Dispose stale queued frames before
        // replacement so a future GPU-backed lease cannot linger behind the reader.
        while (_frames.Reader.TryRead(out var stale))
        {
            stale.Dispose();
            Interlocked.Increment(ref _framesDroppedByBackPressure);
        }

        if (!_frames.Writer.TryWrite(captured))
        {
            captured.Dispose();
            Interlocked.Increment(ref _framesDroppedByBackPressure);
        }
    }

    private static void DropOnePendingFrame(Direct3D11CaptureFramePool sender)
    {
        try
        {
            using var frame = sender.TryGetNextFrame();
        }
        catch
        {
            // The active callback owns fault reporting; this path only keeps the
            // frame pool from accumulating work while a readback is in progress.
        }
    }

    private void RecreateFramePool(SizeInt32 size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;

        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _stopping) != 0) return;
            var pool = _framePool;
            var device = _device;
            if (pool is null || device is null) return;

            _poolSize = size;
            _readback?.Dispose();
            _readback = new D3D11BgraReadback(device.NativeDevice, device.NativeContext);
            pool.Recreate(
                device.WinRtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                _options.FramePoolBufferCount,
                size);
            Interlocked.Increment(ref _resizeEvents);
        }
    }

    private void OnCaptureItemClosed(GraphicsCaptureItem sender, object args)
    {
        // Never tear down the frame pool synchronously from a WinRT callback. Queue
        // cleanup so active FrameArrived work can leave its callback first.
        QueueTerminalStop(WgcCaptureEndReason.CaptureItemClosed, null);
    }

    private void QueueTerminalStop(WgcCaptureEndReason reason, Exception? error)
    {
        RequestStop(reason, error);
    }

    /// <summary>
    /// Begins one-way shutdown without ever freeing D3D resources underneath an
    /// in-flight FrameArrived callback. The final cleanup is deliberately deferred
    /// until the callback releases its ownership, even if that takes longer than an
    /// arbitrary UI timeout. A hung driver can therefore delay disposal, but cannot
    /// turn shutdown into a use-after-free crash.
    /// </summary>
    private void RequestStop(WgcCaptureEndReason reason, Exception? error)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
            return;

        lock (_lifecycleGate)
        {
            if (_framePool is not null)
                _framePool.FrameArrived -= OnFrameArrived;
            if (_item is not null)
                _item.Closed -= OnCaptureItemClosed;

            Volatile.Write(ref _started, 0);
            Volatile.Write(ref _terminal, 1);
        }

        _ = CompleteStopWhenQuiescentAsync(reason, error);
    }

    private async Task CompleteStopWhenQuiescentAsync(WgcCaptureEndReason reason, Exception? error)
    {
        try
        {
            // A callback that entered before _stopping was set owns the D3D device,
            // immediate context, frame, and staging resources until it clears this
            // flag. Waiting asynchronously avoids blocking WPF/WinRT callback threads.
            while (Volatile.Read(ref _processingFrame) != 0)
                await Task.Delay(1).ConfigureAwait(false);

            lock (_lifecycleGate)
            {
                CleanupResourcesUnsafe();
                _poolSize = default;
            }

            while (_frames.Reader.TryRead(out var stale))
                stale.Dispose();

            _frames.Writer.TryComplete(error);
            RaiseEndedOnce(reason, error);
            _stopCompletion.TrySetResult(true);
        }
        catch (Exception cleanupError)
        {
            // Cleanup failure must be observable and must not strand callers waiting
            // forever. Preserve the original terminal error as channel cause when one
            // already exists; otherwise surface the cleanup failure.
            Interlocked.Increment(ref _captureErrors);
            try { CaptureFaulted?.Invoke(this, cleanupError); } catch { }
            _frames.Writer.TryComplete(error ?? cleanupError);
            RaiseEndedOnce(reason, error ?? cleanupError);
            _stopCompletion.TrySetException(cleanupError);
        }
    }

    /// <summary>Caller must hold _lifecycleGate or be in failed startup before publication.</summary>
    private void CleanupResourcesUnsafe()
    {
        try { _session?.Dispose(); } catch { }
        try { _framePool?.Dispose(); } catch { }
        try { _readback?.Dispose(); } catch { }
        try { _device?.Dispose(); } catch { }

        _session = null;
        _framePool = null;
        _readback = null;
        _device = null;
        _item = null;
    }

    private void RaiseEndedOnce(WgcCaptureEndReason reason, Exception? error)
    {
        if (Interlocked.Exchange(ref _endedRaised, 1) != 0) return;
        try { CaptureEnded?.Invoke(this, new WgcCaptureEndedEventArgs(reason, error)); } catch { }
    }

    private static void TrySetCursorCapture(GraphicsCaptureSession session, bool enabled)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        try
        {
            session.IsCursorCaptureEnabled = enabled;
        }
        catch
        {
            // Older supported builds may not expose the optional cursor property.
        }
    }

    private void EmitTelemetry()
    {
        Interlocked.Increment(ref _telemetryEventsEmitted);
        try { TelemetryUpdated?.Invoke(this, Telemetry); } catch { }
    }
}
