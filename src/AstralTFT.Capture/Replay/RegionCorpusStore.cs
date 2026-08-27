using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstralTFT.Capture.Replay;

public sealed class RegionCorpusStore : IRegionCorpusSink
{
    private const int SchemaVersion = 1;
    private const string PixelFormat = "Bgra32";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _rootDirectory;
    private readonly string _blobDirectory;
    private readonly string _observationsPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public RegionCorpusStore(string rootDirectory, string createdByVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByVersion);

        _rootDirectory = Path.GetFullPath(rootDirectory);
        _blobDirectory = Path.Combine(_rootDirectory, "blobs");
        _observationsPath = Path.Combine(_rootDirectory, "observations.jsonl");
        Directory.CreateDirectory(_rootDirectory);
        Directory.CreateDirectory(_blobDirectory);
        EnsureHeader(createdByVersion);
    }

    public async ValueTask<RegionCorpusWriteResult> WriteAsync(
        RegionCorpusWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();
        var contentHash = RegionCorpusHasher.ComputeHash(
            request.Width,
            request.Height,
            request.Stride,
            request.Pixels);
        var observation = new RegionCorpusObservation(
            SchemaVersion,
            contentHash,
            request.RegionId,
            request.FrameSequence,
            request.CapturedAtUtc,
            request.Width,
            request.Height,
            request.Stride,
            request.SourceKind);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var blobCreated = WriteBlob(contentHash, request.Pixels);
            AppendObservation(observation);
            return new RegionCorpusWriteResult(contentHash, blobCreated);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void EnsureHeader(string createdByVersion)
    {
        var headerPath = Path.Combine(_rootDirectory, "corpus.json");
        if (File.Exists(headerPath))
        {
            ValidateHeader(ReadHeader(headerPath));
            return;
        }

        var temporaryPath = Path.Combine(_rootDirectory, Path.GetRandomFileName());
        try
        {
            WriteFlushedFile(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(
                new RegionCorpusHeader(SchemaVersion, PixelFormat, DateTimeOffset.UtcNow, createdByVersion), JsonOptions));
            try
            {
                File.Move(temporaryPath, headerPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(headerPath))
            {
                ValidateHeader(ReadHeader(headerPath));
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private bool WriteBlob(string contentHash, byte[] pixels)
    {
        var blobPath = GetBlobPath(contentHash);
        if (File.Exists(blobPath))
        {
            ValidateExistingBlob(blobPath, pixels);
            return false;
        }

        var temporaryPath = Path.Combine(_rootDirectory, Path.GetRandomFileName());
        try
        {
            WriteFlushedFile(temporaryPath, pixels);
            try
            {
                File.Move(temporaryPath, blobPath, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(blobPath))
            {
                ValidateExistingBlob(blobPath, pixels);
                return false;
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private void AppendObservation(RegionCorpusObservation observation)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(observation, JsonOptions);
        using var stream = new FileStream(
            _observationsPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        stream.Write(payload);
        stream.Write(Encoding.UTF8.GetBytes(Environment.NewLine));
        stream.Flush(flushToDisk: true);
    }

    private static void WriteFlushedFile(string path, ReadOnlySpan<byte> contents)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        stream.Write(contents);
        stream.Flush(flushToDisk: true);
    }

    private static void ValidateExistingBlob(string blobPath, byte[] expectedPixels)
    {
        using var stream = new FileStream(blobPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != expectedPixels.Length)
            throw new InvalidDataException("Existing corpus blob has an unexpected length.");

        var pixels = new byte[expectedPixels.Length];
        stream.ReadExactly(pixels);
        if (!pixels.AsSpan().SequenceEqual(expectedPixels))
            throw new InvalidDataException("Existing corpus blob content does not match its hash.");
    }

    private static RegionCorpusHeader ReadHeader(string headerPath)
    {
        try
        {
            return JsonSerializer.Deserialize<RegionCorpusHeader>(File.ReadAllText(headerPath), JsonOptions)
                ?? throw new InvalidDataException("Corpus header is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Corpus header is malformed.", exception);
        }
    }

    private static void ValidateHeader(RegionCorpusHeader header)
    {
        if (header.SchemaVersion != SchemaVersion || !string.Equals(header.PixelFormat, PixelFormat, StringComparison.Ordinal))
            throw new InvalidDataException("Corpus header schema or pixel format is unsupported.");
        if (header.CreatedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(header.CreatedByVersion))
            throw new InvalidDataException("Corpus header is invalid.");
    }

    private static void ValidateRequest(RegionCorpusWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RegionId);
        ArgumentNullException.ThrowIfNull(request.Pixels);
        if (request.FrameSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.CapturedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (!Enum.IsDefined(request.SourceKind))
            throw new ArgumentOutOfRangeException(nameof(request));
    }

    private string GetBlobPath(string contentHash)
    {
        if (!IsValidContentHash(contentHash))
            throw new InvalidDataException("Corpus content hash is not a lowercase SHA-256 value.");
        return Path.Combine(_blobDirectory, contentHash + ".bgra");
    }

    private static bool IsValidContentHash(string? contentHash) =>
        contentHash is { Length: 64 } && contentHash.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
