using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AstralTFT.Capture.Recognition;

namespace AstralTFT.Capture.Replay;

public sealed class RegionCorpusReader
{
    private const int SchemaVersion = 1;
    private const string PixelFormat = "Bgra32";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _rootDirectory;
    private readonly string _blobDirectory;
    private readonly string _observationsPath;

    public RegionCorpusReader(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _blobDirectory = Path.Combine(_rootDirectory, "blobs");
        _observationsPath = Path.Combine(_rootDirectory, "observations.jsonl");
    }

    public int IgnoredIncompleteTailCount { get; private set; }

    public async IAsyncEnumerable<Bgra32RegionSnapshot> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IgnoredIncompleteTailCount = 0;
        ValidateHeader();
        EnsureExistingDirectoryIsSafe(_rootDirectory, "corpus root");
        RejectReparsePointIfPresent(_observationsPath, "corpus observation log");
        if (!File.Exists(_observationsPath))
            yield break;

        using var lineReader = new StreamReader(_observationsPath);
        var current = await lineReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        while (current is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = await lineReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(current))
            {
                if (next is null)
                    yield break;
                throw new InvalidDataException("Corpus observation log contains an empty non-final line.");
            }

            RegionCorpusObservation observation;
            try
            {
                observation = JsonSerializer.Deserialize<RegionCorpusObservation>(current, JsonOptions)
                    ?? throw new InvalidDataException("Corpus observation is empty.");
            }
            catch (JsonException exception) when (next is null && IsIncompleteFinalJson(current, exception))
            {
                IgnoredIncompleteTailCount++;
                yield break;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Corpus observation is malformed.", exception);
            }

            if (!IsValidObservation(observation, out var expectedLength))
                throw new InvalidDataException("Corpus observation is invalid.");

            var pixels = await ReadBlobAsync(observation.ContentHash, expectedLength, cancellationToken).ConfigureAwait(false);
            var actualHash = RegionCorpusHasher.ComputeHash(
                observation.Width,
                observation.Height,
                observation.Stride,
                pixels);
            if (!string.Equals(actualHash, observation.ContentHash, StringComparison.Ordinal))
                throw new InvalidDataException("Corpus blob content does not match its observation hash.");

            yield return new Bgra32RegionSnapshot(
                observation.RegionId,
                observation.FrameSequence,
                observation.CapturedAtUtc,
                observation.Width,
                observation.Height,
                observation.Stride,
                pixels);
            current = next;
        }
    }

    private void ValidateHeader()
    {
        EnsureExistingDirectoryIsSafe(_rootDirectory, "corpus root");
        var headerPath = Path.Combine(_rootDirectory, "corpus.json");
        RejectReparsePointIfPresent(headerPath, "corpus header");
        if (!File.Exists(headerPath))
            throw new InvalidDataException("Corpus header does not exist.");

        try
        {
            var header = JsonSerializer.Deserialize<RegionCorpusHeader>(File.ReadAllText(headerPath), JsonOptions)
                ?? throw new InvalidDataException("Corpus header is empty.");
            if (header.SchemaVersion != SchemaVersion ||
                !string.Equals(header.PixelFormat, PixelFormat, StringComparison.Ordinal) ||
                header.CreatedAtUtc.Offset != TimeSpan.Zero ||
                string.IsNullOrWhiteSpace(header.CreatedByVersion))
            {
                throw new InvalidDataException("Corpus header is invalid.");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Corpus header is malformed.", exception);
        }
    }

    private async Task<byte[]> ReadBlobAsync(string contentHash, int expectedLength, CancellationToken cancellationToken)
    {
        if (!IsValidContentHash(contentHash))
            throw new InvalidDataException("Corpus observation content hash is invalid.");

        EnsureExistingDirectoryIsSafe(_rootDirectory, "corpus root");
        EnsureExistingDirectoryIsSafe(_blobDirectory, "corpus blob directory");
        var blobPath = Path.Combine(_blobDirectory, contentHash + ".bgra");
        RejectReparsePointIfPresent(blobPath, "corpus blob");
        try
        {
            await using var stream = new FileStream(
                blobPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != expectedLength)
                throw new InvalidDataException("Corpus blob has an unexpected length.");

            var pixels = new byte[expectedLength];
            await stream.ReadExactlyAsync(pixels, cancellationToken).ConfigureAwait(false);
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Corpus blob has an unexpected length.");
            return pixels;
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidDataException("Corpus blob is missing.", exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new InvalidDataException("Corpus blob directory is missing.", exception);
        }
    }

    private static bool IsValidObservation(RegionCorpusObservation observation, out int expectedLength)
    {
        expectedLength = 0;
        if (observation.SchemaVersion != SchemaVersion ||
            !IsValidContentHash(observation.ContentHash) ||
            string.IsNullOrWhiteSpace(observation.RegionId) ||
            observation.FrameSequence < 0 ||
            observation.CapturedAtUtc.Offset != TimeSpan.Zero ||
            !Enum.IsDefined(observation.SourceKind) ||
            observation.Width <= 0 || observation.Width > RegionCorpusHasher.MaxDimension ||
            observation.Height <= 0 || observation.Height > RegionCorpusHasher.MaxDimension)
        {
            return false;
        }

        var expectedStride = (long)observation.Width * 4;
        if (observation.Stride != expectedStride)
            return false;

        var byteLength = expectedStride * observation.Height;
        if (byteLength > RegionCorpusHasher.MaxCanonicalBytes)
            return false;

        expectedLength = (int)byteLength;
        return true;
    }

    private static bool IsValidContentHash(string? contentHash) =>
        contentHash is { Length: 64 } && contentHash.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsIncompleteFinalJson(string line, JsonException exception) =>
        exception.LineNumber is 0 &&
        exception.BytePositionInLine is long bytePosition &&
        bytePosition >= Encoding.UTF8.GetByteCount(line);

    private static void EnsureExistingDirectoryIsSafe(string path, string description)
    {
        RejectReparsePointIfPresent(path, description);
        if (!Directory.Exists(path))
            throw new InvalidDataException($"{description} does not exist.");
    }

    private static void RejectReparsePointIfPresent(string path, string description)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"{description} must not be a reparse point.");
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidDataException($"{description} cannot be inspected safely.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidDataException($"{description} cannot be inspected safely.", exception);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
