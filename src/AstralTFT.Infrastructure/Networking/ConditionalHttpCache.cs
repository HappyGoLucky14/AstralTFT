using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AstralTFT.Infrastructure.Networking;

public sealed record CachedHttpResult(
    string Content,
    DateTimeOffset FetchedAt,
    bool FromCache,
    bool IsStaleFallback,
    string? ETag,
    DateTimeOffset? LastModified);

/// <summary>
/// Small disk-backed conditional HTTP cache. It uses ETag/Last-Modified when providers support
/// them and can fall back to a bounded-age cached payload during temporary provider failures.
/// </summary>
public sealed class ConditionalHttpCache
{
    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public ConditionalHttpCache(HttpClient http, string cacheDirectory)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _cacheDirectory = cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory));
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async ValueTask<CachedHttpResult> GetStringAsync(
        Uri uri,
        string cacheKey,
        TimeSpan maxFallbackAge,
        CancellationToken cancellationToken = default)
    {
        var safeKey = Sanitize(cacheKey);
        var payloadPath = Path.Combine(_cacheDirectory, safeKey + ".json");
        var metadataPath = Path.Combine(_cacheDirectory, safeKey + ".meta.json");
        var metadata = await ReadMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrWhiteSpace(metadata?.ETag) &&
            EntityTagHeaderValue.TryParse(metadata.ETag, out var etag))
            request.Headers.IfNoneMatch.Add(etag);
        if (metadata?.LastModified is { } modified)
            request.Headers.IfModifiedSince = modified;

        try
        {
            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified && File.Exists(payloadPath) && metadata is not null)
            {
                var cached = await File.ReadAllTextAsync(payloadPath, cancellationToken).ConfigureAwait(false);
                return new CachedHttpResult(cached, metadata.FetchedAt, true, false, metadata.ETag, metadata.LastModified);
            }

            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var newMetadata = new CacheMetadata(
                now,
                response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified ?? response.Headers.Date);

            await AtomicWriteAsync(payloadPath, content, cancellationToken).ConfigureAwait(false);
            await AtomicWriteAsync(metadataPath, JsonSerializer.Serialize(newMetadata, _json), cancellationToken).ConfigureAwait(false);

            return new CachedHttpResult(content, now, false, false, newMetadata.ETag, newMetadata.LastModified);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            if (metadata is not null &&
                File.Exists(payloadPath) &&
                DateTimeOffset.UtcNow - metadata.FetchedAt <= maxFallbackAge)
            {
                var cached = await File.ReadAllTextAsync(payloadPath, cancellationToken).ConfigureAwait(false);
                return new CachedHttpResult(cached, metadata.FetchedAt, true, true, metadata.ETag, metadata.LastModified);
            }

            throw;
        }
    }

    private async ValueTask<CacheMetadata?> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<CacheMetadata>(json, _json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async ValueTask AtomicWriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temp, content, cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); }
                catch { }
            }
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        if (string.IsNullOrWhiteSpace(result)) throw new ArgumentException("Cache key cannot be empty.", nameof(value));
        return result;
    }

    private sealed record CacheMetadata(
        DateTimeOffset FetchedAt,
        string? ETag,
        DateTimeOffset? LastModified);
}
