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
            // The opt-in corpus supports only direct drive-rooted paths. Reject all
            // native/device namespaces lexically before normalization so no UNC,
            // MUP, GLOBALROOT, or root-relative spelling can reach storage setup.
            if (!IsDirectDriveRootedPath(directory))
                return Disabled(DirectLocalPathDiagnostic);

            var normalizedDirectory = Path.GetFullPath(directory);
            if (!IsDirectDriveRootedPath(normalizedDirectory))
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
            return Disabled("Corpus directory must be a valid direct local path.");
        }
    }

    private static RegionCorpusConfiguration Disabled(string diagnostic) => new(
        Enabled: false,
        DirectoryPath: null,
        Diagnostic: diagnostic);

    private static bool IsDirectDriveRootedPath(string path)
    {
        if (path.Length < 3 || path[1] != ':' || (path[2] is not ('\\' or '/')))
            return false;

        var drive = path[0];
        return (drive is >= 'A' and <= 'Z') || (drive is >= 'a' and <= 'z');
    }
}
