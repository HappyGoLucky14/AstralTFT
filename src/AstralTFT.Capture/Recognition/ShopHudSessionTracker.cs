namespace AstralTFT.Capture.Recognition;

/// <summary>
/// Per-capture-session hysteresis for the structural shop HUD gate.
/// </summary>
public sealed class ShopHudSessionTracker
{
    private readonly int _confirmationFrames;
    private readonly int _dropFrames;
    private int _visibleFrames;
    private int _missingFrames;
    private bool _confirmed;

    public ShopHudSessionTracker(int confirmationFrames, int dropFrames)
    {
        if (confirmationFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(confirmationFrames));
        if (dropFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(dropFrames));

        _confirmationFrames = confirmationFrames;
        _dropFrames = dropFrames;
    }

    /// <summary>
    /// Records one HUD observation and returns the actions safe for that frame.
    /// </summary>
    public ShopHudSessionDecision Observe(ShopHudObservation hud, bool isMeaningfulChange)
    {
        ArgumentNullException.ThrowIfNull(hud);

        var justConfirmed = false;
        var justLost = false;

        if (hud.IsVisible)
        {
            _missingFrames = 0;

            if (!_confirmed)
            {
                _visibleFrames++;
                if (_visibleFrames >= _confirmationFrames)
                {
                    _confirmed = true;
                    justConfirmed = true;
                }
            }
        }
        else if (_confirmed && hud.SupportsHold)
        {
            // Muted unaffordable cards and brief animation states retain the
            // previous confirmation without authorizing new recognition work.
            _visibleFrames = 0;
            _missingFrames = 0;
        }
        else
        {
            _visibleFrames = 0;

            if (_confirmed)
            {
                _missingFrames++;
                if (_missingFrames >= _dropFrames)
                {
                    _confirmed = false;
                    _missingFrames = 0;
                    justLost = true;
                }
            }
        }

        return new ShopHudSessionDecision(
            IsConfirmed: _confirmed,
            IsVisible: hud.IsVisible,
            JustConfirmed: justConfirmed,
            JustLost: justLost,
            VisibleFrames: _visibleFrames,
            MissingFrames: _missingFrames,
            ShouldRecordChangedShop: _confirmed && hud.IsVisible && isMeaningfulChange,
            ShouldRecognize: _confirmed && hud.IsVisible && (justConfirmed || isMeaningfulChange));
    }
}

/// <summary>
/// The per-frame result of <see cref="ShopHudSessionTracker"/>.
/// </summary>
public sealed record ShopHudSessionDecision(
    bool IsConfirmed,
    bool IsVisible,
    bool JustConfirmed,
    bool JustLost,
    int VisibleFrames,
    int MissingFrames,
    bool ShouldRecordChangedShop,
    bool ShouldRecognize);
