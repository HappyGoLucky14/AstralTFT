namespace AstralTFT.Capture.Replay;

/// <summary>
/// Classifies a drive root without creating corpus directories.
/// </summary>
public interface IRegionCorpusDriveClassifier
{
    RegionCorpusDriveInfo Classify(string rootDirectory);
}

/// <summary>
/// Drive state used to enforce the replay corpus's local-only boundary.
/// </summary>
public readonly record struct RegionCorpusDriveInfo(DriveType DriveType, bool IsReady);

/// <summary>
/// Inspects the already-existing components of a prospective corpus path.
/// </summary>
public interface IRegionCorpusReparsePointInspector
{
    bool HasExistingReparsePointComponent(string normalizedDirectory);
}

/// <summary>
/// Parses and safety-inspects the opt-in local replay-corpus directory without
/// creating corpus paths or files.
/// </summary>
public sealed record RegionCorpusConfiguration(
    bool Enabled,
    string? DirectoryPath,
    string? Diagnostic)
{
    private const string DirectLocalPathDiagnostic = "Corpus directory must be a direct local path.";
    private static readonly IRegionCorpusDriveClassifier SystemDriveClassifier = new SystemRegionCorpusDriveClassifier();
    private static readonly IRegionCorpusReparsePointInspector SystemReparsePointInspector = new SystemRegionCorpusReparsePointInspector();

    public static RegionCorpusConfiguration FromEnvironmentValue(string? value) =>
        FromEnvironmentValue(value, SystemDriveClassifier, SystemReparsePointInspector);

    /// <summary>
    /// Parses the opt-in directory using an injectable drive classifier for
    /// deterministic local-only validation.
    /// </summary>
    public static RegionCorpusConfiguration FromEnvironmentValue(
        string? value,
        IRegionCorpusDriveClassifier driveClassifier)
        => FromEnvironmentValue(value, driveClassifier, SystemReparsePointInspector);

    /// <summary>
    /// Parses the opt-in directory using injectable safety inspectors for
    /// deterministic validation without creating a corpus directory.
    /// </summary>
    public static RegionCorpusConfiguration FromEnvironmentValue(
        string? value,
        IRegionCorpusDriveClassifier driveClassifier,
        IRegionCorpusReparsePointInspector reparsePointInspector)
    {
        ArgumentNullException.ThrowIfNull(driveClassifier);
        ArgumentNullException.ThrowIfNull(reparsePointInspector);

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

            var rootDirectory = Path.GetPathRoot(normalizedDirectory);
            if (rootDirectory is null || !IsDirectDriveRootedPath(rootDirectory))
                return Disabled(DirectLocalPathDiagnostic);

            var drive = driveClassifier.Classify(rootDirectory);
            if (!IsReadyLocalDrive(drive))
                return Disabled(DirectLocalPathDiagnostic);

            if (reparsePointInspector.HasExistingReparsePointComponent(normalizedDirectory))
                return Disabled(DirectLocalPathDiagnostic);

            return new RegionCorpusConfiguration(
                Enabled: true,
                DirectoryPath: normalizedDirectory,
                Diagnostic: null);
        }
        catch (Exception)
        {
            // Drive inspection is security-relevant. An unavailable root or an
            // inspection error must never silently opt in to corpus storage.
            return Disabled(DirectLocalPathDiagnostic);
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

    private static bool IsReadyLocalDrive(RegionCorpusDriveInfo drive) =>
        drive.IsReady && drive.DriveType is not (
            DriveType.Network or
            DriveType.Unknown or
            DriveType.NoRootDirectory);

    private sealed class SystemRegionCorpusDriveClassifier : IRegionCorpusDriveClassifier
    {
        public RegionCorpusDriveInfo Classify(string rootDirectory)
        {
            var drive = new DriveInfo(rootDirectory);
            return new RegionCorpusDriveInfo(drive.DriveType, drive.IsReady);
        }
    }

    private sealed class SystemRegionCorpusReparsePointInspector : IRegionCorpusReparsePointInspector
    {
        public bool HasExistingReparsePointComponent(string normalizedDirectory)
        {
            var rootDirectory = Path.GetPathRoot(normalizedDirectory)
                ?? throw new ArgumentException("A drive-rooted path must have a root.", nameof(normalizedDirectory));
            if (IsExistingReparsePoint(rootDirectory))
                return true;

            var current = rootDirectory;
            var remainingComponents = normalizedDirectory[rootDirectory.Length..]
                .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var component in remainingComponents)
            {
                current = Path.Combine(current, component);
                if (IsExistingReparsePoint(current))
                    return true;
            }

            return false;
        }

        private static bool IsExistingReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
        }
    }
}
