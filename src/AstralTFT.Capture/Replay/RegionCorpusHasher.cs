using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AstralTFT.Capture.Replay;

public static class RegionCorpusHasher
{
    public const int MaxDimension = 4096;
    public const int MaxCanonicalBytes = 64 * 1024 * 1024;

    public static string ComputeHash(int width, int height, int stride, ReadOnlySpan<byte> pixels)
    {
        if (width <= 0 || width > MaxDimension)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0 || height > MaxDimension)
            throw new ArgumentOutOfRangeException(nameof(height));

        var expectedStride = checked(width * 4);
        if (stride != expectedStride)
            throw new ArgumentOutOfRangeException(nameof(stride));

        var canonicalByteLength = checked(stride * height);
        if (canonicalByteLength > MaxCanonicalBytes)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (pixels.Length != canonicalByteLength)
            throw new ArgumentException("Pixel buffer must contain exactly stride × height bytes.", nameof(pixels));

        var marker = Encoding.ASCII.GetBytes("AstralTFT-BGRA32-v1\0");
        Span<byte> geometry = stackalloc byte[sizeof(int) * 3];
        BinaryPrimitives.WriteInt32LittleEndian(geometry[0..sizeof(int)], width);
        BinaryPrimitives.WriteInt32LittleEndian(geometry[sizeof(int)..(sizeof(int) * 2)], height);
        BinaryPrimitives.WriteInt32LittleEndian(geometry[(sizeof(int) * 2)..], stride);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(marker);
        hash.AppendData(geometry);
        hash.AppendData(pixels);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
