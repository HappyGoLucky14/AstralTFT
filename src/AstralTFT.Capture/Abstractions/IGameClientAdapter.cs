namespace AstralTFT.Capture.Abstractions;

public sealed record GameWindow(
    nint Hwnd,
    string ProcessName,
    string WindowTitle,
    int Width,
    int Height,
    string? ClientFamily,
    string? ClientVersion,
    bool IsMinimized = false);

public interface IGameClientAdapter
{
    string Id { get; }
    ValueTask<GameWindow?> TryLocateAsync(CancellationToken cancellationToken = default);
}
