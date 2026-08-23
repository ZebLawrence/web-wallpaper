using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace WebWallpaper;

/// <summary>Owns the tray icon, settings, and one wallpaper window per monitor.</summary>
internal sealed class WallpaperAppContext : ApplicationContext
{
    readonly Settings _settings = Settings.Load(Settings.DefaultPath);
    readonly TrayIcon _tray;
    readonly List<WallpaperForm> _forms = new();

    static readonly string UserDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WebWallpaper", "WebView2");

    public WallpaperAppContext()
    {
        if (!IsWebView2Available())
        {
            MessageBox.Show(
                "The Microsoft Edge WebView2 Runtime is required but was not found.\n\n" +
                "Download it from:\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
                "WebWallpaper", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
        }

        _tray = new TrayIcon(_settings.Paused);
        _tray.SetUrlClicked += OnSetUrl;
        _tray.ReloadClicked += () => _forms.ForEach(f => f.Reload());
        _tray.PauseToggled += OnPauseToggled;
        _tray.ExitClicked += ExitApp;

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        if (!File.Exists(Settings.DefaultPath)) OnSetUrl(); // first run: ask for a URL
        else RebuildWindows();
    }

    static bool IsWebView2Available()
    {
        try { return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); }
        catch { return false; }
    }

    void OnDisplayChanged(object? sender, EventArgs e) => RebuildWindows();

    async void RebuildWindows()
    {
        try
        {
            TearDownWindows();
            if (_settings.Paused) return;

            IntPtr target = WallpaperInterop.GetWallpaperTarget();
            Log.Write($"RebuildWindows: target=0x{target:X}");
            foreach (var screen in Screen.AllScreens)
            {
                Log.Write($"RebuildWindows: screen {screen.Bounds}");
                var form = new WallpaperForm(screen.Bounds, _settings.Url);
                _forms.Add(form);
                form.AttachTo(target);
                await form.InitWebViewAsync(UserDataFolder);
                Log.Write($"RebuildWindows: webview ready, handle=0x{form.Handle:X}");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"RebuildWindows FAILED: {ex}");
        }
    }

    void TearDownWindows()
    {
        foreach (var f in _forms)
        {
            try { f.Close(); f.Dispose(); } catch { }
        }
        _forms.Clear();
        WallpaperInterop.RefreshDesktop();
    }

    void OnSetUrl()
    {
        using var dialog = new SetUrlDialog(_settings.Url);
        if (dialog.ShowDialog() != DialogResult.OK) return;
        _settings.Url = dialog.Url;
        _settings.Save(Settings.DefaultPath);
        RebuildWindows();
    }

    async void OnPauseToggled(bool paused)
    {
        _settings.Paused = paused;
        _settings.Save(Settings.DefaultPath);
        if (paused)
        {
            foreach (var f in _forms) await f.PauseAsync();
            WallpaperInterop.RefreshDesktop();
        }
        else if (_forms.Count == 0)
        {
            RebuildWindows();
        }
        else
        {
            _forms.ForEach(f => f.Resume());
        }
    }

    void ExitApp()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        TearDownWindows();
        _tray.Dispose();
        ExitThread();
    }
}
