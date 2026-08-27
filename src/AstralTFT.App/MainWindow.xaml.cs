using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using AstralTFT.App.Diagnostics;
using AstralTFT.Capture.Abstractions;
using AstralTFT.Capture.Regions;
using AstralTFT.Capture.Recognition;
using AstralTFT.Capture.Windows;

namespace AstralTFT.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int ShopChangeGridColumns = 24;
    private const int ShopChangeGridRows = 6;
    private const double ShopMeaningfulChangeThreshold = 0.025;

    private static readonly WgcCaptureOptions DiagnosticCaptureOptions = new(
        MaxCpuReadbacksPerSecond: 10,
        FramePoolBufferCount: 2,
        CaptureCursor: false,
        AllowWarpFallback: false,
        CpuReadbackRegion: new WgcNormalizedRegion(
            Id: "shop-band-benchmark",
            X: 0.20,
            Y: 0.77,
            Width: 0.60,
            Height: 0.22));

    private readonly TftWindowLocator _locator = new();
    private readonly ShopSlotRecognizer _shopRecognizer = new();
    private readonly CancellationTokenSource _shutdown = new();
    private const int ShopHudConfirmFrames = 3;
    private const int ShopHudDropFrames = 20;

    private int _shopHudConfirmationFrames;
    private int _shopHudMissingFrames;
    private bool _shopHudConfirmed;
    private string _lastConfirmedShopSummary = "No confirmed shop yet.";
    private WindowsGraphicsCaptureFrameSource? _captureSource;
    private Task? _captureTask;
    private CancellationTokenSource? _captureSamplerCts;
    private Task? _captureSamplerTask;
    private CaptureBenchmarkRecorder? _benchmark;
    private nint _capturedHwnd;
    private bool _everAttached;
    private int _missingAfterAttachCount;
    private int _shutdownStarted;
    private bool _allowClose;
    private DateTimeOffset _captureStartedAt;

    private string _statusText = "WAITING";
    private Brush _statusBrush = new SolidColorBrush(Color.FromRgb(170, 161, 189));
    private string _windowTitle = "Waiting for TFT…";
    private string _windowDetails = "Window discovery is active";
    private string _resolutionText = "—";
    private string _discoveryState = "SEARCHING";
    private string _captureState = "Waiting for TFT";
    private string _captureRateText = "—";
    private string _readbackText = "—";
    private string _dropText = "—";
    private string _captureDetails = "Capture starts automatically after the TFT match window is detected.";
    private string _recognitionState = "WAITING";
    private string _recognitionDetails = "Waiting for the first meaningful shop change.";
    private string _footerText = "Structural shop recognition is enabled without OCR or game-memory access.";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => _ = RunDiscoveryLoopAsync(_shutdown.Token);
        Closing += OnClosing;
    }

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }
    public string WindowTitle { get => _windowTitle; private set => Set(ref _windowTitle, value); }
    public string WindowDetails { get => _windowDetails; private set => Set(ref _windowDetails, value); }
    public string ResolutionText { get => _resolutionText; private set => Set(ref _resolutionText, value); }
    public string DiscoveryState { get => _discoveryState; private set => Set(ref _discoveryState, value); }
    public string CaptureState { get => _captureState; private set => Set(ref _captureState, value); }
    public string CaptureRateText { get => _captureRateText; private set => Set(ref _captureRateText, value); }
    public string ReadbackText { get => _readbackText; private set => Set(ref _readbackText, value); }
    public string DropText { get => _dropText; private set => Set(ref _dropText, value); }
    public string CaptureDetails { get => _captureDetails; private set => Set(ref _captureDetails, value); }
    public string RecognitionState { get => _recognitionState; private set => Set(ref _recognitionState, value); }
    public string RecognitionDetails { get => _recognitionDetails; private set => Set(ref _recognitionDetails, value); }
    public string FooterText { get => _footerText; private set => Set(ref _footerText, value); }

    private async Task RunDiscoveryLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var game = await _locator.TryLocateAsync(cancellationToken);
                if (game is not null)
                {
                    _missingAfterAttachCount = 0;
                    _everAttached = true;
                    StatusText = game.IsMinimized ? "TFT PAUSED" : "TFT DETECTED";
                    StatusBrush = new SolidColorBrush(game.IsMinimized
                        ? Color.FromRgb(242, 183, 91)
                        : Color.FromRgb(114, 217, 170));
                    WindowTitle = game.WindowTitle;
                    WindowDetails = $"{game.ProcessName}  •  HWND 0x{game.Hwnd:X}" + (game.IsMinimized ? "  •  minimized" : string.Empty);
                    ResolutionText = $"{game.Width}×{game.Height}";
                    DiscoveryState = game.IsMinimized ? "PAUSED" : "READY";

                    if (game.IsMinimized)
                    {
                        if (_captureSource is not null)
                            await StopCaptureAsync(WgcCaptureEndReason.Requested, null);
                        CaptureState = "Paused — TFT minimized";
                        CaptureDetails = "Capture and recognition work are stopped while TFT is minimized.";
                    }
                    else if (_captureSource is null || _capturedHwnd != game.Hwnd)
                    {
                        await AttachCaptureAsync(game, cancellationToken);
                    }
                }
                else
                {
                    StatusText = "WAITING";
                    StatusBrush = new SolidColorBrush(Color.FromRgb(170, 161, 189));
                    WindowTitle = "Waiting for TFT…";
                    WindowDetails = _everAttached ? "Confirming that the TFT match window closed…" : "Window discovery is active";
                    ResolutionText = "—";
                    DiscoveryState = "SEARCHING";

                    if (_everAttached)
                    {
                        _missingAfterAttachCount++;
                        // A short grace period survives transient window recreation or a
                        // client handoff without leaving AstralTFT running after TFT closes.
                        if (_missingAfterAttachCount >= 3)
                        {
                            await StopCaptureAsync(WgcCaptureEndReason.Requested, null);
                            Close();
                            return;
                        }
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                StatusText = "DEGRADED";
                DiscoveryState = "ERROR";
                FooterText = $"Window/capture discovery error: {ex.GetType().Name}: {ex.Message}";
                await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
            }
        }
    }

    private async Task AttachCaptureAsync(GameWindow game, CancellationToken cancellationToken)
    {
        await StopCaptureAsync(WgcCaptureEndReason.Requested, null);

        var source = new WindowsGraphicsCaptureFrameSource(game, DiagnosticCaptureOptions);
        var benchmark = new CaptureBenchmarkRecorder(
            game,
            DiagnosticCaptureOptions,
            new RegionChangeBenchmarkConfiguration(
                RegionId: "shop-band-change-benchmark",
                GridColumns: ShopChangeGridColumns,
                GridRows: ShopChangeGridRows,
                MeaningfulThreshold: ShopMeaningfulChangeThreshold));
        var changeDetector = new GridLumaRegionChangeDetector(
            ShopChangeGridColumns,
            ShopChangeGridRows,
            ShopMeaningfulChangeThreshold);
        source.TelemetryUpdated += OnCaptureTelemetry;
        source.CaptureFaulted += OnCaptureFaulted;
        source.CaptureEnded += OnCaptureEnded;
        _captureSource = source;
        _benchmark = benchmark;
        _capturedHwnd = game.Hwnd;
        _captureStartedAt = DateTimeOffset.UtcNow;
        CaptureState = "Starting…";
        CaptureDetails = "Attaching Windows.Graphics.Capture to the TFT HWND. CPU readback is capped at 10 FPS for the first overhead benchmark.";

        try
        {
            await source.StartAsync(cancellationToken);
            CaptureState = "Capturing TFT window";
            FooterText = "Capture-only benchmark active. Metrics will be written automatically to LocalAppData\\AstralTFT\\Diagnostics.";
            _captureTask = ConsumeFramesAsync(source, benchmark, changeDetector, _shutdown.Token);

            _captureSamplerCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _captureSamplerTask = SampleBenchmarkAsync(benchmark, _captureSamplerCts.Token);
        }
        catch
        {
            source.TelemetryUpdated -= OnCaptureTelemetry;
            source.CaptureFaulted -= OnCaptureFaulted;
            source.CaptureEnded -= OnCaptureEnded;
            _captureSource = null;
            _benchmark = null;
            _capturedHwnd = 0;
            try { await benchmark.CompleteAsync(WgcCaptureEndReason.StartupFailed); } catch { }
            // StartAsync owns failed-start cleanup; avoid a redundant disposal wait.
            throw;
        }
    }

    private async Task ConsumeFramesAsync(
        WindowsGraphicsCaptureFrameSource source,
        CaptureBenchmarkRecorder benchmark,
        GridLumaRegionChangeDetector changeDetector,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in source.ReadFramesAsync(cancellationToken))
            {
                using (frame)
                {
                    // This is deliberately still recognition-free. We only fingerprint
                    // the CPU-visible shop ROI to measure how many expensive downstream
                    // recognition passes could be suppressed by a tiny sampled luma grid.
                    var region = new RegionOfInterest(
                        "shop-band-change-benchmark",
                        0,
                        0,
                        frame.Width,
                        frame.Height);

                    var started = Stopwatch.GetTimestamp();
                    var change = changeDetector.Compare(frame, region);
                    var elapsed = Stopwatch.GetElapsedTime(started);
                    benchmark.RecordRegionChange(change, elapsed);

                    if (frame.NativeFrameHandle is Bgra32FrameBuffer pixels)
                    {
                        // Screen-state detection is separate from slot classification.
                        // Probe only the fixed/repeated TFT shop-frame anchors every
                        // accepted ROI frame. Full slot recognition still wakes only
                        // when the ROI meaningfully changes.
                        var hud = _shopRecognizer.CheckHud(pixels);
                        var justConfirmed = false;
                        var justLost = false;

                        if (hud.IsVisible)
                        {
                            _shopHudMissingFrames = 0;

                            if (!_shopHudConfirmed)
                            {
                                _shopHudConfirmationFrames++;
                                if (_shopHudConfirmationFrames >= ShopHudConfirmFrames)
                                {
                                    _shopHudConfirmed = true;
                                    justConfirmed = true;
                                }
                            }
                        }
                        else if (_shopHudConfirmed && hud.SupportsHold)
                        {
                            // Greyed unaffordable units and short HUD animation states
                            // may weaken the exact frame colors without removing the
                            // underlying shop. Keep the last confirmed result, but do
                            // not run a fresh classification from the muted frame.
                            _shopHudConfirmationFrames = 0;
                            _shopHudMissingFrames = 0;
                        }
                        else
                        {
                            _shopHudConfirmationFrames = 0;

                            if (_shopHudConfirmed)
                            {
                                _shopHudMissingFrames++;
                                if (_shopHudMissingFrames >= ShopHudDropFrames)
                                {
                                    _shopHudConfirmed = false;
                                    _shopHudMissingFrames = 0;
                                    justLost = true;
                                }
                            }
                        }

                        if (justLost)
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                RecognitionState = "INACTIVE";
                                RecognitionDetails =
                                    $"Shop HUD not present • anchors {hud.TopBorderMatches}/5 top, {hud.SeparatorMatches}/4 separators. No shop guesses are shown.";
                                FooterText = "Non-shop scene detected; shop recognition is sleeping.";
                            });
                        }
                        else if (_shopHudConfirmed &&
                                 hud.IsVisible &&
                                 (justConfirmed || change.IsMeaningful))
                        {
                            var shop = _shopRecognizer.Recognize(pixels);
                            var summary = string.Join(
                                "  •  ",
                                shop.Slots.Select(FormatShopSlot));
                            _lastConfirmedShopSummary = summary;

                            await Dispatcher.InvokeAsync(() =>
                            {
                                RecognitionState = "ACTIVE";
                                RecognitionDetails = summary;
                                FooterText =
                                    $"Shop HUD confirmed ({shop.Hud.TopBorderMatches}/5 top, {shop.Hud.SeparatorMatches}/4 separators) • structural recognition {shop.ProcessingTime.TotalMicroseconds:F1} µs.";
                            });
                        }
                        else if (_shopHudConfirmed && !hud.IsVisible)
                        {
                            var remaining = Math.Max(0, ShopHudDropFrames - _shopHudMissingFrames);
                            await Dispatcher.InvokeAsync(() =>
                            {
                                RecognitionState = "HOLD";
                                RecognitionDetails =
                                    $"{_lastConfirmedShopSummary}  •  temporarily holding ({remaining} grace frames)";
                                FooterText =
                                    "Shop chrome is muted/transitioning • hold-only frame evidence detected • no new shop guess is being made.";
                            });
                        }
                        else if (!_shopHudConfirmed)
                        {
                            var progress = Math.Clamp(_shopHudConfirmationFrames, 0, ShopHudConfirmFrames);
                            await Dispatcher.InvokeAsync(() =>
                            {
                                RecognitionState = "WAITING";
                                RecognitionDetails =
                                    $"Shop HUD not confirmed ({progress}/3) • anchors {hud.TopBorderMatches}/5 top, {hud.SeparatorMatches}/4 separators. No slot guesses are shown.";
                                FooterText = "AstralTFT is waiting for the real in-match shop frame before surfacing recognition.";
                            });
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                CaptureState = "Capture consumer fault";
                FooterText = $"Capture consumer error: {ex.GetType().Name}: {ex.Message}";
            });
        }
    }

    private static async Task SampleBenchmarkAsync(CaptureBenchmarkRecorder recorder, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                recorder.SampleProcess();
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static string FormatShopSlot(ShopSlotReading slot)
    {
        return slot.Occupancy switch
        {
            ShopSlotOccupancy.Empty => $"S{slot.SlotIndex}: empty",
            ShopSlotOccupancy.Unit when slot.CostTier is >= 1 and <= 5
                => $"S{slot.SlotIndex}: {slot.CostTier}-cost",
            ShopSlotOccupancy.Unit => $"S{slot.SlotIndex}: unit",
            _ => $"S{slot.SlotIndex}: unknown"
        };
    }

    private void OnCaptureTelemetry(object? sender, WgcCaptureTelemetry telemetry)
    {
        _benchmark?.RecordTelemetry(telemetry);
        _ = Dispatcher.BeginInvoke(() =>
        {
            var elapsed = DateTimeOffset.UtcNow - _captureStartedAt;
            var readbackRate = elapsed.TotalSeconds > 0.25
                ? telemetry.FramesReadBack / elapsed.TotalSeconds
                : 0;

            var megabytesPerSecond = elapsed.TotalSeconds > 0.25
                ? telemetry.BytesReadBack / elapsed.TotalSeconds / 1024d / 1024d
                : 0;

            CaptureRateText = $"{readbackRate:F1} readbacks/s";
            ReadbackText = telemetry.LastReadbackDuration == TimeSpan.Zero
                ? "warming up"
                : $"{telemetry.LastReadbackDuration.TotalMilliseconds:F2} ms • {telemetry.ReadbackWidth}×{telemetry.ReadbackHeight}";
            DropText = $"{telemetry.FramesDroppedWhileBusy} busy • {telemetry.FramesDroppedByBackPressure} stale • {telemetry.FramesDroppedByThrottle} throttled";
            CaptureDetails = $"ROI {telemetry.ReadbackRegionId} • {megabytesPerSecond:F1} MB/s CPU copy • Arrived {telemetry.FramesArrived:N0} • Read back {telemetry.FramesReadBack:N0} • telemetry {telemetry.TelemetryEventsEmitted:N0} • Errors {telemetry.CaptureErrors} • source {telemetry.Width}×{telemetry.Height}";

            if (telemetry.CaptureErrors > 0)
            {
                StatusText = "CAPTURE DEGRADED";
                StatusBrush = new SolidColorBrush(Color.FromRgb(242, 183, 91));
            }
        });
    }

    private void OnCaptureFaulted(object? sender, Exception ex)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            CaptureState = "Capture degraded";
            FooterText = $"WGC error isolated: {ex.GetType().Name}: {ex.Message}";
        });
    }

    private void OnCaptureEnded(object? sender, WgcCaptureEndedEventArgs args)
    {
        if (args.Reason == WgcCaptureEndReason.Requested) return;
        _ = Dispatcher.BeginInvoke(() => _ = HandleUnexpectedCaptureEndAsync(sender as WindowsGraphicsCaptureFrameSource, args));
    }

    private async Task HandleUnexpectedCaptureEndAsync(WindowsGraphicsCaptureFrameSource? source, WgcCaptureEndedEventArgs args)
    {
        if (source is null || !ReferenceEquals(source, _captureSource)) return;
        CaptureState = args.Reason == WgcCaptureEndReason.DeviceLost ? "GPU capture restarting…" : "Capture ended";
        FooterText = args.Error is null
            ? $"Capture ended: {args.Reason}. Window discovery will reattach if TFT is still running."
            : $"Capture ended: {args.Reason} — {args.Error.GetType().Name}: {args.Error.Message}";
        await StopCaptureAsync(args.Reason, args.Error);
    }

    private async Task StopCaptureAsync(WgcCaptureEndReason reason, Exception? error)
    {
        var source = _captureSource;
        var benchmark = _benchmark;
        var samplerCts = _captureSamplerCts;
        var samplerTask = _captureSamplerTask;
        var captureTask = _captureTask;

        _captureSource = null;
        _benchmark = null;
        _captureSamplerCts = null;
        _captureSamplerTask = null;
        _captureTask = null;
        _capturedHwnd = 0;

        if (source is null && benchmark is null) return;

        if (source is not null)
        {
            source.TelemetryUpdated -= OnCaptureTelemetry;
            source.CaptureFaulted -= OnCaptureFaulted;
            source.CaptureEnded -= OnCaptureEnded;
        }

        try { samplerCts?.Cancel(); } catch { }
        if (source is not null)
        {
            // StopAsync requests shutdown immediately, then waits for any in-flight
            // D3D readback to relinquish its resources. Bound the UI wait without
            // forcing unsafe disposal underneath a potentially stuck GPU callback;
            // the source continues deferred cleanup in the background if necessary.
            using var stopWait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await source.StopAsync(stopWait.Token);
            }
            catch (OperationCanceledException) when (stopWait.IsCancellationRequested)
            {
                FooterText = "Capture stop is waiting on the graphics driver; safe deferred cleanup is continuing in the background.";
            }
            catch
            {
                // Capture source reports faults through its own telemetry/events.
            }
        }

        if (captureTask is not null)
        {
            try { await captureTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
        if (samplerTask is not null)
        {
            try { await samplerTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
        samplerCts?.Dispose();

        if (benchmark is not null)
        {
            try
            {
                var path = await benchmark.CompleteAsync(reason, error);
                if (!string.IsNullOrWhiteSpace(path))
                    FooterText = $"Capture benchmark saved: {path}";
            }
            catch (Exception ex)
            {
                FooterText = $"Capture stopped; benchmark export failed: {ex.GetType().Name}: {ex.Message}";
            }
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;

        // WPF normally tears the process down as soon as the last window closes.
        // Defer that final close until capture cleanup and atomic benchmark export
        // have had a chance to finish.
        e.Cancel = true;
        IsEnabled = false;
        try
        {
            await ShutdownAsync();
        }
        finally
        {
            _allowClose = true;
            Close();
        }
    }

    private async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;
        _shutdown.Cancel();
        await StopCaptureAsync(WgcCaptureEndReason.Requested, null);
        _shutdown.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
