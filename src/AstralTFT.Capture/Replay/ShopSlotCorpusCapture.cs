using AstralTFT.Capture.Abstractions;
using AstralTFT.Capture.Recognition;

namespace AstralTFT.Capture.Replay;

/// <summary>
/// Copies the five visible shop-card regions into the optional local replay corpus.
/// </summary>
public static class ShopSlotCorpusCapture
{
    public static int TryRecordChangedShop(CapturedFrame frame, BoundedRegionCorpusRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(recorder);

        var slots = ShopSlotRecognizer.ProjectSlots(frame.Width, frame.Height);
        var factory = new CpuBgraRegionSnapshotFactory();
        var accepted = 0;

        foreach (var slot in slots)
        {
            IRegionSnapshot? snapshot = null;
            try
            {
                snapshot = factory.Create(frame, slot);
                if (snapshot is Bgra32RegionSnapshot bgraSnapshot &&
                    recorder.TryRecord(bgraSnapshot, RegionCorpusSourceKind.LiveCapture))
                {
                    accepted++;
                }
            }
            catch (Exception)
            {
                // Local replay capture is diagnostic-only; a single failed slot must
                // never interrupt live HUD detection or recognition.
            }
            finally
            {
                snapshot?.Dispose();
            }
        }

        return accepted;
    }
}
