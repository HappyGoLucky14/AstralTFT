using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AstralTFT.Capture.Abstractions;

namespace AstralTFT.Capture.Windows;

/// <summary>
/// Finds the live TFT match window without mistaking the Riot/League launcher for
/// gameplay. Minimized match windows are deliberately returned (with IsMinimized)
/// so the caller can pause capture instead of treating minimize as "TFT closed".
/// </summary>
public sealed class TftWindowLocator : IGameClientAdapter
{
    public string Id => "windows-tft-window-v2";

    public ValueTask<GameWindow?> TryLocateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = new List<TftWindowCandidateSelector.Candidate>();
        EnumWindows((hwnd, _) =>
        {
            if (cancellationToken.IsCancellationRequested) return false;
            if (!IsWindowVisible(hwnd)) return true;

            var title = GetWindowText(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return true;

            Process? process = null;
            try { process = Process.GetProcessById((int)pid); }
            catch { return true; }

            if (!GetClientRect(hwnd, out var rect)) return true;
            var width = Math.Max(0, rect.Right - rect.Left);
            var height = Math.Max(0, rect.Bottom - rect.Top);
            var minimized = IsIconic(hwnd);

            // A minimized HWND still retains its client dimensions. Ignore tiny utility
            // windows but do not reject a valid minimized TFT match window.
            if (width < 800 || height < 600) return true;

            candidates.Add(new TftWindowCandidateSelector.Candidate(
                hwnd, process.ProcessName, title, width, height, minimized));

            return true;
        }, IntPtr.Zero);

        var best = TftWindowCandidateSelector.ChooseBest(candidates);
        if (best is null) return ValueTask.FromResult<GameWindow?>(null);

        var window = best.Window;
        return ValueTask.FromResult<GameWindow?>(new GameWindow(
            window.Hwnd,
            window.ProcessName,
            window.Title,
            window.Width,
            window.Height,
            ClientFamily: "TFT-PC",
            ClientVersion: null,
            IsMinimized: window.IsMinimized));
    }

    private static string GetWindowText(nint hwnd)
    {
        var length = GetWindowTextLengthW(hwnd);
        if (length <= 0) return string.Empty;
        var sb = new StringBuilder(length + 1);
        _ = GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hWnd, out RECT lpRect);
}
