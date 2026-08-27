using System.Buffers;
using System.Runtime.InteropServices;
using AstralTFT.Capture.Abstractions;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace AstralTFT.Capture.Windows;

internal sealed record CpuReadbackFrame(Bgra32FrameBuffer Buffer, IDisposable Lease);

/// <summary>
/// Reusable staging-texture readback. Full-frame copy remains available as a baseline,
/// but ReadRegion uses CopySubresourceRegion so only the requested pixels cross the
/// GPU-to-CPU boundary. Pooled arrays avoid allocating a new pixel buffer per sample.
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        EnsureStaging(width, height);
        _context.CopyResource(_staging!, source);
        return MapStaging(width, height);
    }

    public CpuReadbackFrame ReadRegion(ID3D11Texture2D source, RegionOfInterest region)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(region.X);
        ArgumentOutOfRangeException.ThrowIfNegative(region.Y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(region.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(region.Height);

        EnsureStaging(region.Width, region.Height);
        var sourceBox = new Box(
            region.X,
            region.Y,
            0,
            region.X + region.Width,
            region.Y + region.Height,
            1);

        _context.CopySubresourceRegion(
            _staging!,
            0,
            0,
            0,
            0,
            source,
            0,
            sourceBox);

        return MapStaging(region.Width, region.Height);
    }

    public void Dispose()
    {
        _staging?.Dispose();
        _staging = null;
        _width = 0;
        _height = 0;
    }

    private CpuReadbackFrame MapStaging(int width, int height)
    {
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
            pixels = null;
            return new CpuReadbackFrame(buffer, lease);
        }
        finally
        {
            _context.Unmap(_staging!, 0);
            if (pixels is not null)
                ArrayPool<byte>.Shared.Return(pixels);
        }
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
