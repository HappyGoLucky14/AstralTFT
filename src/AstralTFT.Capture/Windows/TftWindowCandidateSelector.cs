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

    /// <summary>
    /// Window-title matching is intentionally NOT enough. Browsers, TFT guide apps,
    /// and companion tools routinely contain "TFT" in their titles/process names.
    /// Restrict discovery to known Riot gameplay process families, then use the title
    /// only as a secondary ranking signal.
    /// </summary>
    public static bool IsSupportedGameProcess(string processName)
    {
        ArgumentNullException.ThrowIfNull(processName);
        var p = processName.Trim();

        if (p.StartsWith("TFTClient", StringComparison.OrdinalIgnoreCase))
            return true;

        if (p.Equals("TFT", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("TeamfightTactics", StringComparison.OrdinalIgnoreCase))
            return true;

        if (p.Equals("League of Legends", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("LeagueOfLegends", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static int Score(string processName, string title)
    {
        ArgumentNullException.ThrowIfNull(processName);
        ArgumentNullException.ThrowIfNull(title);

        if (!IsSupportedGameProcess(processName))
            return 0;

        var p = processName.Trim();
        var t = title.Trim();
        var score = 100;

        if (p.StartsWith("TFTClient", StringComparison.OrdinalIgnoreCase))
            score += 180;
        else if (p.Equals("TFT", StringComparison.OrdinalIgnoreCase) ||
                 p.StartsWith("TeamfightTactics", StringComparison.OrdinalIgnoreCase))
            score += 160;
        else
            score += 100;

        if (t.Contains("teamfight tactics", StringComparison.OrdinalIgnoreCase))
            score += 50;
        if (t.Equals("TFT", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("TFT ", StringComparison.OrdinalIgnoreCase))
            score += 35;
        if (t.Contains("league of legends", StringComparison.OrdinalIgnoreCase))
            score += 20;

        return score;
    }

    public static RankedCandidate? ChooseBest(IEnumerable<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        RankedCandidate? best = null;
        foreach (var candidate in candidates)
        {
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
