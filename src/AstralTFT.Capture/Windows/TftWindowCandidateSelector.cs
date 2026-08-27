namespace AstralTFT.Capture.Windows;

/// <summary>
/// Pure scoring/selection logic for TFT window discovery. Keeping this separate from
/// Win32 enumeration lets us regression-test client naming changes without needing a
/// live Riot process or user32 calls in the test itself.
/// </summary>
public static class TftWindowCandidateSelector
{
    public sealed record Candidate(
        nint Hwnd,
        string ProcessName,
        string Title,
        int Width,
        int Height,
        bool IsMinimized);

    public sealed record RankedCandidate(Candidate Window, int Score);

    public static int Score(string processName, string title)
    {
        ArgumentNullException.ThrowIfNull(processName);
        ArgumentNullException.ThrowIfNull(title);

        var p = processName.ToLowerInvariant();
        var t = title.ToLowerInvariant();
        var score = 0;

        if (t.Contains("teamfight tactics")) score += 100;
        if (t.Contains("league of legends")) score += 35;
        if (t.Contains("tft")) score += 30;

        // Current League-hosted client and future dedicated TFT clients should both
        // score positively, while launcher/UX helper processes are aggressively
        // suppressed even when their title contains League/TFT terms.
        if (p.Contains("tft")) score += 90;
        if (p.Equals("league of legends", StringComparison.OrdinalIgnoreCase)) score += 50;
        if (p.Contains("leagueclientux")) score -= 145;
        if (p.Contains("riotclient")) score -= 160;

        return score;
    }

    public static RankedCandidate? ChooseBest(IEnumerable<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        RankedCandidate? best = null;
        foreach (var candidate in candidates)
        {
            // Minimized windows retain their client size and remain valid targets; a
            // caller can pause expensive capture while keeping match identity alive.
            if (candidate.Hwnd == 0 || candidate.Width < 800 || candidate.Height < 600)
                continue;

            var score = Score(candidate.ProcessName, candidate.Title);
            if (score <= 0)
                continue;

            var ranked = new RankedCandidate(candidate, score);
            if (best is null || ranked.Score > best.Score)
                best = ranked;
        }

        return best;
    }
}
