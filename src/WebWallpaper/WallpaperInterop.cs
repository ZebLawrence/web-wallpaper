using System.Runtime.InteropServices;
using System.Text;

namespace WebWallpaper;

/// <summary>Win32 layer: locates the wallpaper WorkerW window and restores the desktop.</summary>
internal static class WallpaperInterop
{
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    /// <summary>
    /// Finds the top-level input-helper window the WebView2 browser process creates in
    /// visual-hosting mode: class Chrome_WidgetWin_1, owned by <paramref name="browserPid"/>,
    /// exactly covering <paramref name="screenRect"/>. Returns Zero when absent.
    /// </summary>
    internal static IntPtr FindBrowserHelperWindow(uint browserPid, Rectangle screenRect)
    {
        IntPtr found = IntPtr.Zero;
        var sb = new StringBuilder(64);
        EnumWindows((top, _) =>
        {
            GetWindowThreadProcessId(top, out uint pid);
            if (pid != browserPid) return true;
            sb.Clear();
            GetClassName(top, sb, sb.Capacity);
            if (sb.ToString() != "Chrome_WidgetWin_1") return true;
            GetWindowRect(top, out RECT r);
            if (r.Left == screenRect.Left && r.Top == screenRect.Top &&
                r.Right == screenRect.Right && r.Bottom == screenRect.Bottom)
            {
                found = top;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>Effective DPI scale (1.0 = 96 DPI) of the monitor covering the given bounds.</summary>
    internal static double GetScaleForMonitor(Rectangle monitorBounds)
    {
        var center = new POINT
        {
            X = monitorBounds.X + monitorBounds.Width / 2,
            Y = monitorBounds.Y + monitorBounds.Height / 2,
        };
        IntPtr monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out _) == 0
            ? dpiX / 96.0
            : 1.0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SystemParametersInfo(uint uiAction, uint uiParam, StringBuilder pvParam, uint fWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

    const uint WM_SPAWN_WORKER = 0x052C;
    const uint SPI_GETDESKWALLPAPER = 0x0073;
    const uint SPI_SETDESKWALLPAPER = 0x0014;
    const uint SPIF_UPDATEINIFILE = 0x01;
    const uint SPIF_SENDCHANGE = 0x02;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>Window to parent wallpaper forms into: the WorkerW behind the icons, or Progman on 24H2+.</summary>
    internal static IntPtr GetWallpaperTarget()
    {
        IntPtr progman = FindWindow("Progman", null);
        // Asks Progman to spawn the WorkerW that sits between the wallpaper and the icon layer.
        SendMessageTimeout(progman, WM_SPAWN_WORKER, new IntPtr(0xD), new IntPtr(0x1), 0, 1000, out _);

        // Pre-24H2: the icon list (SHELLDLL_DefView) lives in its own WorkerW; ours is the next WorkerW sibling.
        IntPtr workerw = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (top != progman && FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                workerw = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);

        // 24H2+: icons live directly under Progman and the spawned WorkerW is Progman's child.
        if (workerw == IntPtr.Zero)
            workerw = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);

        return workerw != IntPtr.Zero ? workerw : progman;
    }

    /// <summary>Re-applies the current static wallpaper so no dead region remains after exit.</summary>
    internal static void RefreshDesktop()
    {
        var current = new StringBuilder(512);
        SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)current.Capacity, current, 0);
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, current.ToString(), SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }
}
