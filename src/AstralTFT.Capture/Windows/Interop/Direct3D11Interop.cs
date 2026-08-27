using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using WinRtDirect3DDevice = Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice;

namespace AstralTFT.Capture.Windows.Interop;

internal sealed class Direct3D11CaptureDevice : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0
    ];

    public Direct3D11CaptureDevice(bool allowWarpFallback)
    {
        ID3D11Device? nativeDevice = null;
        ID3D11DeviceContext? nativeContext = null;
        WinRtDirect3DDevice? winRtDevice = null;

        try
        {
            var result = D3D11.D3D11CreateDevice(
                adapter: null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                FeatureLevels,
                out ID3D11Device hardwareDevice,
                out ID3D11DeviceContext hardwareContext);

            if (result.Success)
            {
                nativeDevice = hardwareDevice;
                nativeContext = hardwareContext;
            }
            else
            {
                hardwareContext?.Dispose();
                hardwareDevice?.Dispose();

                if (!allowWarpFallback)
                    result.CheckError();

                result = D3D11.D3D11CreateDevice(
                    adapter: nint.Zero,
                    DriverType.Warp,
                    DeviceCreationFlags.BgraSupport,
                    FeatureLevels,
                    out ID3D11Device warpDevice,
                    out ID3D11DeviceContext warpContext);
                result.CheckError();
                nativeDevice = warpDevice;
                nativeContext = warpContext;
            }

            winRtDevice = CreateWinRtDevice(nativeDevice);
            NativeDevice = nativeDevice;
            NativeContext = nativeContext;
            WinRtDevice = winRtDevice;
        }
        catch
        {
            try { winRtDevice?.Dispose(); } catch { }
            try { nativeContext?.Dispose(); } catch { }
            try { nativeDevice?.Dispose(); } catch { }
            throw;
        }
    }

    public ID3D11Device NativeDevice { get; }
    public ID3D11DeviceContext NativeContext { get; }
    public WinRtDirect3DDevice WinRtDevice { get; }

    public void Dispose()
    {
        try { WinRtDevice.Dispose(); } catch { }
        try { NativeContext.Dispose(); } catch { }
        try { NativeDevice.Dispose(); } catch { }
    }

    private static WinRtDirect3DDevice CreateWinRtDevice(ID3D11Device device)
    {
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable);
        Marshal.ThrowExceptionForHR(hr);
        if (inspectable == 0)
            throw new InvalidOperationException("CreateDirect3D11DeviceFromDXGIDevice returned a null object.");

        try
        {
            return MarshalInterface<WinRtDirect3DDevice>.FromAbi(inspectable)
                ?? throw new InvalidOperationException("Failed to project WinRT IDirect3DDevice.");
        }
        finally
        {
            // FromAbi creates the managed projection's own reference. Release the
            // native reference returned by CreateDirect3D11DeviceFromDXGIDevice.
            Marshal.Release(inspectable);
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice,
        out nint graphicsDevice);
}

internal static class Direct3DSurfaceInterop
{
    private static readonly Guid ID3D11Texture2DGuid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    // This managed COM shape intentionally follows Microsoft's C# WGC helper:
    // COM interop translates the HRESULT and exposes the native void** result as
    // the managed return value. Do not add PreserveSig to this signature.
    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        nint GetInterface(in Guid iid);
    }

    public static ID3D11Texture2D GetTexture2D(IDirect3DSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var pointer = access.GetInterface(ID3D11Texture2DGuid);
        if (pointer == 0)
            throw new InvalidOperationException("IDirect3DDxgiInterfaceAccess returned a null ID3D11Texture2D pointer.");

        // GetInterface/QI returns an owned COM reference. Vortice does not AddRef
        // when wrapping an nint, so disposing this wrapper releases exactly that
        // returned reference.
        return new ID3D11Texture2D(pointer);
    }
}

internal static class D3D11DeviceLoss
{
    private const int DxgiErrorDeviceRemoved = unchecked((int)0x887A0005);
    private const int DxgiErrorDeviceHung = unchecked((int)0x887A0006);
    private const int DxgiErrorDeviceReset = unchecked((int)0x887A0007);
    private const int DxgiErrorDriverInternalError = unchecked((int)0x887A0020);

    public static bool IsDeviceLoss(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.HResult is DxgiErrorDeviceRemoved or DxgiErrorDeviceHung or DxgiErrorDeviceReset or DxgiErrorDriverInternalError)
                return true;
        }
        return false;
    }
}
