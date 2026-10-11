using Microsoft.Web.WebView2.Core;

namespace WebWallpaper;

/// <summary>
/// One borderless window per monitor, re-parented behind the desktop icons.
/// Hosts WebView2 in visual-hosting mode (CompositionController + DirectComposition),
/// so the page renders into a composition visual and receives input only through
/// <see cref="SendMouseEvent"/> — never from the system directly.
/// </summary>
internal sealed class WallpaperForm : Form
{
    readonly Rectangle _screenBounds;   // physical pixels, virtual-screen coordinates
    readonly string _url;

    CoreWebView2CompositionController? _controller;
    CoreWebView2? _core;
    DCompInterop.IDCompositionDesktopDevice? _dcompDevice;
    DCompInterop.IDCompositionTarget? _dcompTarget;
    DCompInterop.IDCompositionVisual2? _dcompVisual;

    const int WS_EX_TOOLWINDOW = 0x80;
    const int WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>The monitor this window covers, in physical virtual-screen pixels.</summary>
    public Rectangle ScreenBounds => _screenBounds;

    public WallpaperForm(Rectangle screenBounds, string url)
    {
        _screenBounds = screenBounds;
        _url = url;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    /// <summary>Re-parent into the wallpaper layer and cover this monitor.</summary>
    public void AttachTo(IntPtr wallpaperTarget)
    {
        var handle = Handle; // forces handle creation before SetParent
        var result = WallpaperInterop.SetParent(handle, wallpaperTarget);
        Log.Write($"AttachTo: handle=0x{handle:X} target=0x{wallpaperTarget:X} SetParent={result} err={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        // Child coordinates are relative to the wallpaper layer, whose origin is the virtual screen's top-left.
        var v = SystemInformation.VirtualScreen;
        WallpaperInterop.SetWindowPos(handle, IntPtr.Zero,
            _screenBounds.X - v.X, _screenBounds.Y - v.Y,
            _screenBounds.Width, _screenBounds.Height,
            WallpaperInterop.SWP_NOZORDER | WallpaperInterop.SWP_NOACTIVATE);
        Show();
    }

    public async Task InitWebViewAsync(string userDataFolder)
    {
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        _controller = await env.CreateCoreWebView2CompositionControllerAsync(Handle);

        // Build the DirectComposition chain WebView2 renders into:
        // device -> target (bound to this hwnd) -> root visual -> WebView2.
        _dcompDevice = DCompInterop.CreateDevice();
        _dcompDevice.CreateTargetForHwnd(Handle, topmost: true, out _dcompTarget);
        _dcompDevice.CreateVisual(out _dcompVisual);
        _dcompTarget.SetRoot(_dcompVisual);
        _controller.RootVisualTarget = _dcompVisual;
        // Commit AFTER handing the visual to WebView2 — its content visuals join our
        // tree as part of that assignment and only show up once committed.
        _dcompDevice.Commit();

        // The window lives in the shell's hierarchy, so WebView2 cannot infer the right
        // monitor or scale on its own; size and scale it explicitly in physical pixels.
        _controller.ShouldDetectMonitorScaleChanges = false;
        _controller.RasterizationScale = WallpaperInterop.GetScaleForMonitor(_screenBounds);
        _controller.Bounds = new Rectangle(0, 0, _screenBounds.Width, _screenBounds.Height);
        _controller.IsVisible = true;

        _core = _controller.CoreWebView2;
        _core.Settings.AreDefaultContextMenusEnabled = false;
        _core.Settings.AreDevToolsEnabled = false;
        _core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        _core.Settings.IsStatusBarEnabled = false;
        _core.IsMuted = true;
        _core.ProcessFailed += (_, _) => BeginInvoke(Reload);
        _core.Navigate(_url);
        Log.Write($"InitWebView: visual hosting ready on {_screenBounds}, scale={_controller.RasterizationScale:F2}");

        await AdoptBrowserHelperWindowAsync(_core.BrowserProcessId);
    }

    /// <summary>
    /// In visual-hosting mode the browser process parks a top-level input-helper window
    /// over the WebView's screen area. Left top-level it floats above the shell's icon
    /// layer, so we pull it into this form, placing it inside the wallpaper band with
    /// the rest of the page.
    /// </summary>
    async Task AdoptBrowserHelperWindowAsync(uint browserPid)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            IntPtr helper = WallpaperInterop.FindBrowserHelperWindow(browserPid, _screenBounds);
            if (helper != IntPtr.Zero)
            {
                WallpaperInterop.SetParent(helper, Handle);
                WallpaperInterop.SetWindowPos(helper, IntPtr.Zero, 0, 0, _screenBounds.Width, _screenBounds.Height,
                    WallpaperInterop.SWP_NOZORDER | WallpaperInterop.SWP_NOACTIVATE);
                Log.Write($"AdoptHelper: 0x{helper:X} re-parented into form 0x{Handle:X}");
                return;
            }
            await Task.Delay(100);
        }
        Log.Write($"AdoptHelper: no helper window found for {_screenBounds}");
    }

    /// <summary>Delivers a mouse event to the page. <paramref name="client"/> is in
    /// physical pixels relative to this monitor's top-left corner.</summary>
    public void SendMouseEvent(uint msg, uint mouseData, int virtualKeys, Point client)
    {
        try
        {
            // CoreWebView2MouseEventKind values equal the WM_* codes, and
            // CoreWebView2MouseEventVirtualKeys values equal the MK_* flags.
            _controller?.SendMouseInput(
                (CoreWebView2MouseEventKind)msg,
                (CoreWebView2MouseEventVirtualKeys)virtualKeys,
                mouseData,
                client);
        }
        catch (Exception ex)
        {
            Log.Write($"SendMouseEvent failed: {ex.Message}");
        }
    }

    public void Reload()
    {
        try { _core?.Reload(); } catch { /* webview may be gone during teardown */ }
    }

    public async Task PauseAsync()
    {
        Hide();
        try
        {
            if (_controller != null) _controller.IsVisible = false;
            if (_core != null) await _core.TrySuspendAsync();
        }
        catch { }
    }

    public void Resume()
    {
        try
        {
            _core?.Resume();
            if (_controller != null) _controller.IsVisible = true;
        }
        catch { }
        Show();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { _controller?.Close(); } catch { }
        _controller = null;
        _core = null;
        base.OnFormClosed(e);
    }
}
