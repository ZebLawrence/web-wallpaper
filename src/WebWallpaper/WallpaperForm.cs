using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WebWallpaper;

/// <summary>One borderless WebView2 window per monitor, re-parented behind the desktop icons.</summary>
internal sealed class WallpaperForm : Form
{
    readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    readonly Rectangle _screenBounds;   // physical pixels, virtual-screen coordinates
    readonly string _url;

    const int WS_EX_TOOLWINDOW = 0x80;
    const int WS_EX_NOACTIVATE = 0x08000000;

    public WallpaperForm(Rectangle screenBounds, string url)
    {
        _screenBounds = screenBounds;
        _url = url;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Controls.Add(_webView);
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
        Log.Write($"AttachTo: shown, visible={Visible} handleNow=0x{Handle:X}");
    }

    public async Task InitWebViewAsync(string userDataFolder)
    {
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        await _webView.EnsureCoreWebView2Async(env);

        var core = _webView.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.IsMuted = true;
        core.ProcessFailed += (_, _) => BeginInvoke(Reload);
        core.Navigate(_url);
    }

    public void Reload()
    {
        try { _webView.CoreWebView2?.Reload(); } catch { /* webview may be gone during teardown */ }
    }

    public async Task PauseAsync()
    {
        Hide();
        try { if (_webView.CoreWebView2 is { } core) await core.TrySuspendAsync(); } catch { }
    }

    public void Resume()
    {
        try { _webView.CoreWebView2?.Resume(); } catch { }
        Show();
    }
}
