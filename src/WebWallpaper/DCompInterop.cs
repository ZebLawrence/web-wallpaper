using System.Runtime.InteropServices;

namespace WebWallpaper;

/// <summary>
/// Minimal DirectComposition interop: enough to hand WebView2's visual-hosting mode a
/// root visual on our window. Vtable layouts transcribed from the Windows SDK dcomp.h
/// (10.0.19041.0); unused slots are declared only to keep the vtable offsets correct.
/// </summary>
internal static class DCompInterop
{
    [DllImport("dcomp.dll")]
    static extern int DCompositionCreateDevice2(IntPtr renderingDevice, ref Guid iid, out IntPtr device);

    [ComImport, Guid("5F4633FE-1E08-4CB8-8C75-CE24333F5602"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDCompositionDesktopDevice
    {
        // IDCompositionDevice2
        void Commit();
        void WaitForCommitCompletion();
        void GetFrameStatistics(IntPtr statistics);
        void CreateVisual(out IDCompositionVisual2 visual);
        void CreateSurfaceFactory(IntPtr renderingDevice, out IntPtr surfaceFactory);
        void CreateSurface(uint width, uint height, uint pixelFormat, uint alphaMode, out IntPtr surface);
        void CreateVirtualSurface(uint initialWidth, uint initialHeight, uint pixelFormat, uint alphaMode, out IntPtr virtualSurface);
        void CreateTranslateTransform(out IntPtr transform);
        void CreateScaleTransform(out IntPtr transform);
        void CreateRotateTransform(out IntPtr transform);
        void CreateSkewTransform(out IntPtr transform);
        void CreateMatrixTransform(out IntPtr transform);
        void CreateTransformGroup(IntPtr transforms, uint elements, out IntPtr transformGroup);
        void CreateTranslateTransform3D(out IntPtr transform);
        void CreateScaleTransform3D(out IntPtr transform);
        void CreateRotateTransform3D(out IntPtr transform);
        void CreateMatrixTransform3D(out IntPtr transform);
        void CreateTransform3DGroup(IntPtr transforms, uint elements, out IntPtr transformGroup);
        void CreateEffectGroup(out IntPtr effectGroup);
        void CreateRectangleClip(out IntPtr clip);
        void CreateAnimation(out IntPtr animation);
        // IDCompositionDesktopDevice
        void CreateTargetForHwnd(IntPtr hwnd, bool topmost, out IDCompositionTarget target);
        void CreateSurfaceFromHandle(IntPtr handle, out IntPtr surface);
        void CreateSurfaceFromHwnd(IntPtr hwnd, out IntPtr surface);
    }

    [ComImport, Guid("eacdd04c-117e-4e17-88f4-d1b12b0e3d89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDCompositionTarget
    {
        void SetRoot(IDCompositionVisual2? visual);
    }

    /// <summary>Opaque root visual; WebView2 draws into it, we never call its methods.</summary>
    [ComImport, Guid("E8DE1639-4331-4B26-BC5F-6A321D347A85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDCompositionVisual2
    {
    }

    internal static IDCompositionDesktopDevice CreateDevice()
    {
        var iid = typeof(IDCompositionDesktopDevice).GUID;
        int hr = DCompositionCreateDevice2(IntPtr.Zero, ref iid, out IntPtr ptr);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        var device = (IDCompositionDesktopDevice)Marshal.GetObjectForIUnknown(ptr);
        Marshal.Release(ptr); // RCW holds its own reference
        return device;
    }
}
