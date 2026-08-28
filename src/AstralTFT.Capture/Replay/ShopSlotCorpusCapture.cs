using AstralTFT.Capture.Abstractions;
using AstralTFT.Capture.Recognition;

namespace AstralTFT.Capture.Replay;

/// <summary>
/// Copies the five visible shop-card regions into the optional local replay corpus.
/// </summary>
public static class ShopSlotCorpusCapture
{
    private const string PartialCaptureDiagnostic = "Developer replay corpus skipped one or more shop slots.";

    public static ShopSlotCorpusCaptureResult TryRecordChangedShop(
        CapturedFrame frame,
        BoundedRegionCorpusRecorder recorder,
        IRegionSnapshotFactory? snapshotFactory = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(recorder);

        var slots = ShopSlotRecognizer.ProjectSlots(frame.Width, frame.Height);
        var factory = snapshotFactory ?? new CpuBgraRegionSnapshotFactory();
        var accepted = 0;
        var failed = 0;

        foreach (var slot in slots)
        {
            IRegionSnapshot? snapshot = null;
            var slotFailed = false;
            try
            {
                snapshot = factory.Create(frame, slot);
                if (snapshot is Bgra32RegionSnapshot bgraSnapshot &&
                    recorder.TryRecord(bgraSnapshot, RegionCorpusSourceKind.LiveCapture))
                {
                    accepted++;
                }
                else
                {
                    slotFailed = true;
                }
            }
            catch (Exception)
            {
                // Local replay capture is diagnostic-only; a single failed slot must
                // never interrupt live HUD detection or recognition.
                slotFailed = true;
            }
            finally
            {
                try
                {
                    snapshot?.Dispose();
                }
                catch (Exception)
                {
                    // Snapshot disposal is equally isolated from the live pipeline.
                    slotFailed = true;
                }
            }

            if (slotFailed)
                failed++;
        }

        return new ShopSlotCorpusCaptureResult(
            AcceptedCount: accepted,
            FailedSlotCount: failed,
            Diagnostic: failed == 0 ? null : PartialCaptureDiagnostic);
    }
}

/// <summary>
/// Outcome of one changed-shop corpus submission attempt.
/// </summary>
public sealed record ShopSlotCorpusCaptureResult(
    int AcceptedCount,
    int FailedSlotCount,
    string? Diagnostic);
