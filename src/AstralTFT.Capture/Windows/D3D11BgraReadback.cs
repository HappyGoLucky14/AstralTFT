using System.Buffers;
using System.Runtime.InteropServices;
using AstralTFT.Capture.Abstractions;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AstralTFT.Capture.Windows;

internal sealed record CpuReadbackFrame(Bgra32FrameBuffer Buffer, IDisposable Lease);

/// <summary>
/// First benchmark path: one reusable staging texture plus pooled CPU byte arrays.
/// Avoiding an ~8 MB managed allocation for every 1080p sample is essential; the
/// later ROI-only/GPU fingerprint path can replace the full-frame copy entirely.
/// </summary>
internal sealed class D3D11BgraReadback : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private ID3D11Texture2D? _staging;
    private int _width;
    private int _height;

    public D3D11BgraReadback(ID3D11Device device, ID3D11DeviceContext context)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public CpuReadbackFrame Read(ID3D11Texture2D source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        EnsureStaging(width, height);
        _context.CopyResource(_staging!, source);

        var mapped = _context.Map(_staging!, 0, MapMode.Read);
        byte[]? pixels = null;
        try
        {
            var packedStride = checked(width * 4);
            var required = checked(packedStride * height);
            pixels = ArrayPool<byte>.Shared.Rent(required);

            if (mapped.RowPitch == packedStride)
            {
                Marshal.Copy(mapped.DataPointer, pixels, 0, required);
            }
            else
            {
                for (var row = 0; row < height; row++)
                {
                    var sourceRow = mapped.DataPointer + checked((nint)(row * mapped.RowPitch));
                    Marshal.Copy(sourceRow, pixels, checked(row * packedStride), packedStride);
                }
            }

            var lease = new PooledByteArrayLease(pixels);
            var buffer = new Bgra32FrameBuffer(
                width,
                height,
                packedStride,
                new ReadOnlyMemory<byte>(pixels, 0, required));
            pixels = null; // lease owns it now
            return new CpuReadbackFrame(buffer, lease);
        }
        finally
        {
            _context.Unmap(_staging!, 0);
            if (pixels is not null)
                ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    public void Dispose()
    {
        _staging?.Dispose();
        _staging = null;
        _width = 0;
        _height = 0;
    }

    private void EnsureStaging(int width, int height)
    {
        if (_staging is not null && _width == width && _height == height)
            return;

        _staging?.Dispose();
        _staging = _device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            (uint)width,
            (uint)height,
            arraySize: 1,
            mipLevels: 1,
            bindFlags: BindFlags.None,
            usage: ResourceUsage.Staging,
            cpuAccessFlags: CpuAccessFlags.Read,
            sampleCount: 1,
            sampleQuality: 0,
            miscFlags: ResourceOptionFlags.None));
        _width = width;
        _height = height;
    }

    private sealed class PooledByteArrayLease : IDisposable
    {
        private byte[]? _array;

        public PooledByteArrayLease(byte[] array) => _array = array;

        public void Dispose()
        {
            var array = Interlocked.Exchange(ref _array, null);
            if (array is not null)
                ArrayPool<byte>.Shared.Return(array);
        }
    }
}
