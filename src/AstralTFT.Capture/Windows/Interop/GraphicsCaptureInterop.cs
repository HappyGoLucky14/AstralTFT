using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace AstralTFT.Capture.Windows.Interop;

internal static class GraphicsCaptureInterop
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow([In] nint window, in Guid iid);
        nint CreateForMonitor([In] nint monitor, in Guid iid);
    }

    public static GraphicsCaptureItem CreateForWindow(nint hwnd)
    {
        if (hwnd == 0) throw new ArgumentOutOfRangeException(nameof(hwnd));

        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var abi = interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
        if (abi == 0)
            throw new InvalidOperationException("IGraphicsCaptureItemInterop.CreateForWindow returned a null capture item.");

        try
        {
            return GraphicsCaptureItem.FromAbi(abi)
                ?? throw new InvalidOperationException("Failed to project GraphicsCaptureItem from ABI pointer.");
        }
        finally
        {
            Marshal.Release(abi);
        }
    }
}
