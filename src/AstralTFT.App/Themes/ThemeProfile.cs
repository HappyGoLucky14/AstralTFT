namespace AstralTFT.App.Themes;

public enum ThemeMode
{
    Personal,
    Adaptive,
    CosmeticMatch,
    Manual
}

public sealed record ThemeProfile(
    ThemeMode Mode,
    string BaseIdentity,
    string PrimaryHex,
    string SecondaryHex,
    double PanelOpacity,
    bool GlassEffectsEnabled);
