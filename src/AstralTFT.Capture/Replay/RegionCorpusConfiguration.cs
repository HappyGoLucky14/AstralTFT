namespace AstralTFT.Capture.Replay;

/// <summary>
/// Parses the opt-in local replay-corpus directory without touching the file system.
/// </summary>
public sealed record RegionCorpusConfiguration(
    bool Enabled,
    string? DirectoryPath,
    string? Diagnostic)
{
    private const string DirectLocalPathDiagnostic = "Corpus directory must be a direct local path.";

    public static RegionCorpusConfiguration FromEnvironmentValue(string? value)
    {
        var directory = value?.Trim();
        if (string.IsNullOrEmpty(directory))
            return new RegionCorpusConfiguration(Enabled: false, DirectoryPath: null, Diagnostic: null);

        try
        {
            // Reject before Path.GetFullPath so device-UNC paths receive the same
            // fixed footer-safe diagnostic even if the runtime refuses to normalize
            // one of their alternate separator forms.
            if (IsNetworkPath(directory))
                return Disabled(DirectLocalPathDiagnostic);

            if (!Path.IsPathFullyQualified(directory))
                return Disabled("Corpus directory must be an absolute path.");

            var normalizedDirectory = Path.GetFullPath(directory);
            if (IsNetworkPath(normalizedDirectory))
                return Disabled(DirectLocalPathDiagnostic);

            return new RegionCorpusConfiguration(
                Enabled: true,
                DirectoryPath: normalizedDirectory,
                Diagnostic: null);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            IOException)
        {
            return Disabled("Corpus directory must be a valid absolute path.");
        }
    }

    private static RegionCorpusConfiguration Disabled(string diagnostic) => new(
        Enabled: false,
        DirectoryPath: null,
        Diagnostic: diagnostic);

    private static bool IsNetworkPath(string path)
    {
        var normalizedSeparators = path.Replace('/', '\\');
        if (normalizedSeparators.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ||
            normalizedSeparators.StartsWith(@"\\.\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Conventional UNC paths begin with two separators. Local device paths
        // such as \\?\C:\corpus remain direct local paths and are not rejected.
        return normalizedSeparators.StartsWith(@"\\", StringComparison.Ordinal) &&
               !normalizedSeparators.StartsWith(@"\\?\", StringComparison.Ordinal) &&
               !normalizedSeparators.StartsWith(@"\\.\", StringComparison.Ordinal);
    }
}
