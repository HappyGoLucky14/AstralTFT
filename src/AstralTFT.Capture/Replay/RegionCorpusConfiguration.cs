namespace AstralTFT.Capture.Replay;

/// <summary>
/// Parses the opt-in local replay-corpus directory without touching the file system.
/// </summary>
public sealed record RegionCorpusConfiguration(
    bool Enabled,
    string? DirectoryPath,
    string? Diagnostic)
{
    public static RegionCorpusConfiguration FromEnvironmentValue(string? value)
    {
        var directory = value?.Trim();
        if (string.IsNullOrEmpty(directory))
            return new RegionCorpusConfiguration(Enabled: false, DirectoryPath: null, Diagnostic: null);

        try
        {
            if (!Path.IsPathFullyQualified(directory))
                return Disabled("Corpus directory must be an absolute path.");

            return new RegionCorpusConfiguration(
                Enabled: true,
                DirectoryPath: Path.GetFullPath(directory),
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

}
