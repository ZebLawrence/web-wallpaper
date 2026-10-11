using System.Runtime.InteropServices;
using System.Text;

namespace WebWallpaper;

/// <summary>
/// Global low-level mouse hook that forwards mouse events into the wallpaper's
/// WebView2 (via CompositionController.SendMouseInput) — but only while the cursor is
/// over the bare desktop (shell windows Progman/WorkerW), never over an app window or
/// the taskbar. Events are never swallowed; the desktop still receives them too.
/// </summary>
internal sealed class MouseForwarder : IDisposable
{
    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    const int WH_MOUSE_LL = 14;
    const uint GA_ROOT = 2;
    const uint WM_MOUSEMOVE = 0x0200;
    const uint WM_LBUTTONDOWN = 0x0201;
    const uint WM_LBUTTONUP = 0x0202;
    const uint WM_RBUTTONDOWN = 0x0204;
    const uint WM_RBUTTONUP = 0x0205;
    const uint WM_MOUSEWHEEL = 0x020A;
    const int MK_LBUTTON = 0x0001;
    const int MK_RBUTTON = 0x0002;

    /// <summary>Where to deliver a forwarded event: the wallpaper window under the
    /// cursor plus the cursor position in that monitor's physical client space.</summary>
    internal readonly record struct ForwardTarget(WallpaperForm? Form, Point Client);

    readonly Func<Point, ForwardTarget> _targetLookup; // screen point -> target, Form == null when none
    readonly HookProc _proc;                           // field keeps the delegate alive for the GC
    IntPtr _hook;
    bool _leftDown;                                    // tracked so drags carry MK_LBUTTON on moves
    static readonly bool _trace = Environment.GetEnvironmentVariable("WEBWALLPAPER_TRACE") == "1";

    public MouseForwarder(Func<Point, ForwardTarget> targetLookup)
    {
        _targetLookup = targetLookup;
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
        Log.Write($"MouseForwarder: hook installed={_hook != IntPtr.Zero} err={Marshal.GetLastWin32Error()}");
    }

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (uint)wParam;
            if (msg is WM_MOUSEMOVE or WM_LBUTTONDOWN or WM_LBUTTONUP or WM_RBUTTONDOWN or WM_RBUTTONUP or WM_MOUSEWHEEL)
            {
                try { Forward(msg, Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam)); }
                catch { /* the hook must never throw back into the system */ }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    void Forward(uint msg, MSLLHOOKSTRUCT info)
    {
        // Button state is tracked even off-desktop so a drag that leaves the
        // desktop and a release elsewhere can't wedge the forwarded state.
        if (msg == WM_LBUTTONDOWN) _leftDown = true;
        if (msg == WM_LBUTTONUP) _leftDown = false;

        if (_trace)
            Log.Write($"Forward: msg=0x{msg:X} pt=({info.pt.X},{info.pt.Y})");
        if (!IsOverDesktop(info.pt)) return;

        var target = _targetLookup(new Point(info.pt.X, info.pt.Y));
        if (target.Form is not { } form) return;

        uint mouseData = 0;
        if (msg == WM_MOUSEWHEEL)
            mouseData = (uint)(short)(info.mouseData >> 16); // wheel delta rides in the high word

        int virtualKeys = msg switch
        {
            WM_LBUTTONDOWN => MK_LBUTTON,
            WM_RBUTTONDOWN => MK_RBUTTON,
            WM_MOUSEMOVE when _leftDown => MK_LBUTTON, // lets pages see drags
            _ => 0,
        };
        form.SendMouseEvent(msg, mouseData, virtualKeys, target.Client);
    }

    static bool IsOverDesktop(POINT pt)
    {
        IntPtr hit = WindowFromPoint(pt);
        IntPtr root = GetAncestor(hit, GA_ROOT);
        if (root == IntPtr.Zero) return false;
        var cls = new StringBuilder(64);
        GetClassName(root, cls, cls.Capacity);
        var name = cls.ToString();
        bool over = name == "Progman" || name == "WorkerW";
        if (_trace && !over)
        {
            var hitCls = new StringBuilder(64);
            GetClassName(hit, hitCls, hitCls.Capacity);
            Log.Write($"IsOverDesktop: hit=0x{hit:X} ({hitCls}) root=0x{root:X} ({name})");
        }
        return over;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            Log.Write("MouseForwarder: hook removed");
        }
    }
}
