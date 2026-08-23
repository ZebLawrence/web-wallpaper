# WebWallpaper Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows tray app that renders a webpage behind the desktop icons on every monitor, non-interactive, distributed via a public GitHub repo with a prebuilt-exe release.

**Architecture:** C# WinForms (.NET 10) app. `WallpaperInterop` performs the WorkerW injection (with Windows 11 24H2 fallback); one `WallpaperForm` per monitor hosts a muted WebView2; `WallpaperAppContext` wires the tray icon, settings, and monitor-change handling.

**Tech Stack:** .NET 10 SDK, WinForms, Microsoft.Web.WebView2 (NuGet), xUnit for `Settings` tests, gh CLI for repo + release.

---

### Task 1: Solution scaffold

**Files:**
- Create: `WebWallpaper.sln`, `src/WebWallpaper/WebWallpaper.csproj`, `tests/WebWallpaper.Tests/WebWallpaper.Tests.csproj`, `.gitignore`

- [ ] **Step 1: Scaffold projects**

```bash
dotnet new sln -n WebWallpaper
dotnet new winforms -n WebWallpaper -o src/WebWallpaper -f net10.0
dotnet new xunit -n WebWallpaper.Tests -o tests/WebWallpaper.Tests -f net10.0
dotnet sln add src/WebWallpaper tests/WebWallpaper.Tests
dotnet add tests/WebWallpaper.Tests reference src/WebWallpaper
dotnet add src/WebWallpaper package Microsoft.Web.WebView2
dotnet new gitignore
```

- [ ] **Step 2: Edit `src/WebWallpaper/WebWallpaper.csproj`** — high-DPI, nullable, name:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>WebWallpaper</AssemblyName>
    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2592.51" />
  </ItemGroup>
</Project>
```

Set the test project's `TargetFramework` to `net10.0-windows` and add `<UseWindowsForms>true</UseWindowsForms>` so it can reference the app project. Delete the template's `Form1*.cs` and `Class1.cs`/`UnitTest1.cs`.

- [ ] **Step 3: Verify build** — `dotnet build` → succeeds.
- [ ] **Step 4: Commit** — `git add -A && git commit -m "chore: scaffold solution"`

### Task 2: Settings (TDD)

**Files:**
- Create: `src/WebWallpaper/Settings.cs`
- Test: `tests/WebWallpaper.Tests/SettingsTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using WebWallpaper;

public class SettingsTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = Settings.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.Equal("about:blank", s.Url);
        Assert.False(s.Paused);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "settings.json");
        new Settings { Url = "https://example.com/", Paused = true }.Save(path);
        var s = Settings.Load(path);
        Assert.Equal("https://example.com/", s.Url);
        Assert.True(s.Paused);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{not json");
        Assert.Equal("about:blank", Settings.Load(path).Url);
    }

    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("  https://a.b/c ", "https://a.b/c")]
    public void TryNormalizeUrl_ValidInput_Normalizes(string input, string expected)
    {
        Assert.True(Settings.TryNormalizeUrl(input, out var url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://example.com")]
    [InlineData("not a url at all!")]
    public void TryNormalizeUrl_InvalidInput_ReturnsFalse(string input)
        => Assert.False(Settings.TryNormalizeUrl(input, out _));
}
```

- [ ] **Step 2: Run** — `dotnet test` → FAIL (Settings not defined).
- [ ] **Step 3: Implement `Settings.cs`**

```csharp
using System.Text.Json;

namespace WebWallpaper;

public sealed class Settings
{
    public string Url { get; set; } = "about:blank";
    public bool Paused { get; set; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WebWallpaper", "settings.json");

    public static Settings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Settings();
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
        }
        catch { return new Settings(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool TryNormalizeUrl(string input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        var candidate = input.Trim();
        if (!candidate.Contains("://")) candidate = "https://" + candidate;
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            normalized = uri.ToString();
            return true;
        }
        return false;
    }
}
```

- [ ] **Step 4: Run** — `dotnet test` → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: settings persistence and URL validation"`

### Task 3: WallpaperInterop

**Files:**
- Create: `src/WebWallpaper/WallpaperInterop.cs`

- [ ] **Step 1: Implement** (verified by build here; behavior verified in Task 7 smoke test)

```csharp
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
        SendMessageTimeout(progman, WM_SPAWN_WORKER, new IntPtr(0xD), new IntPtr(0x1), 0, 1000, out _);

        // Pre-24H2: the icon list lives in a WorkerW sibling; ours is the WorkerW right after it.
        IntPtr workerw = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (top != progman && FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                workerw = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);

        // 24H2+: icons live under Progman and the spawned WorkerW is Progman's child.
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
```

- [ ] **Step 2: Build** — `dotnet build` → succeeds.
- [ ] **Step 3: Commit** — `git commit -m "feat: WorkerW interop with 24H2 fallback"`

### Task 4: WallpaperForm

**Files:**
- Create: `src/WebWallpaper/WallpaperForm.cs`

- [ ] **Step 1: Implement**

```csharp
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
        WallpaperInterop.SetParent(handle, wallpaperTarget);
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
```

- [ ] **Step 2: Build** — `dotnet build` → succeeds.
- [ ] **Step 3: Commit** — `git commit -m "feat: per-monitor wallpaper window hosting muted WebView2"`

### Task 5: TrayIcon and SetUrlDialog

**Files:**
- Create: `src/WebWallpaper/TrayIcon.cs`, `src/WebWallpaper/SetUrlDialog.cs`

- [ ] **Step 1: Implement `TrayIcon.cs`**

```csharp
namespace WebWallpaper;

/// <summary>The app's only visible UI: a tray icon with the control menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _pauseItem;

    public event Action? SetUrlClicked;
    public event Action? ReloadClicked;
    public event Action<bool>? PauseToggled;
    public event Action? ExitClicked;

    public TrayIcon(bool paused)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Set URL…", null, (_, _) => SetUrlClicked?.Invoke());
        menu.Items.Add("Reload", null, (_, _) => ReloadClicked?.Invoke());
        _pauseItem = new ToolStripMenuItem("Pause") { Checked = paused, CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) => PauseToggled?.Invoke(_pauseItem.Checked);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitClicked?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "WebWallpaper",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => SetUrlClicked?.Invoke();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
```

- [ ] **Step 2: Implement `SetUrlDialog.cs`**

```csharp
namespace WebWallpaper;

internal sealed class SetUrlDialog : Form
{
    readonly TextBox _urlBox = new() { Width = 376 };

    public string Url { get; private set; } = "";

    public SetUrlDialog(string currentUrl)
    {
        Text = "WebWallpaper — Set URL";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        ClientSize = new Size(400, 100);

        var label = new Label { Text = "Webpage to show as wallpaper:", AutoSize = true, Left = 12, Top = 12 };
        _urlBox.Left = 12;
        _urlBox.Top = 34;
        _urlBox.Text = currentUrl;
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 220, Top = 66, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 308, Top = 66, Width = 80 };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { label, _urlBox, ok, cancel });

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            if (Settings.TryNormalizeUrl(_urlBox.Text, out var normalized))
            {
                Url = normalized;
            }
            else
            {
                e.Cancel = true;
                MessageBox.Show(this, "Please enter a valid http(s) URL.", "WebWallpaper",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
    }
}
```

- [ ] **Step 3: Build** — `dotnet build` → succeeds.
- [ ] **Step 4: Commit** — `git commit -m "feat: tray icon menu and URL dialog"`

### Task 6: App wiring

**Files:**
- Create: `src/WebWallpaper/WallpaperAppContext.cs`
- Modify: `src/WebWallpaper/Program.cs`

- [ ] **Step 1: Implement `WallpaperAppContext.cs`**

```csharp
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
        TearDownWindows();
        if (_settings.Paused) return;

        IntPtr target = WallpaperInterop.GetWallpaperTarget();
        foreach (var screen in Screen.AllScreens)
        {
            var form = new WallpaperForm(screen.Bounds, _settings.Url);
            _forms.Add(form);
            form.AttachTo(target);
            await form.InitWebViewAsync(UserDataFolder);
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
```

- [ ] **Step 2: Replace `Program.cs`**

```csharp
namespace WebWallpaper;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\WebWallpaper_SingleInstance", out bool createdNew);
        if (!createdNew) return; // already running

        ApplicationConfiguration.Initialize();
        Application.Run(new WallpaperAppContext());
    }
}
```

- [ ] **Step 3: Build + tests** — `dotnet build && dotnet test` → succeed.
- [ ] **Step 4: Commit** — `git commit -m "feat: app context wiring tray, settings, and monitors"`

### Task 7: Smoke test

- [ ] **Step 1: Run** — `dotnet run --project src/WebWallpaper`, settings pre-seeded with a URL.
- [ ] **Step 2: Verify** — page renders behind icons on all monitors; icons clickable; windows layer above; process alive; then Exit via tray (or kill + `RefreshDesktop`) restores the static wallpaper.
- [ ] **Step 3: Fix anything found; commit fixes.**

### Task 8: README, publish, GitHub repo + release

**Files:**
- Create: `README.md`, `LICENSE` (MIT)

- [ ] **Step 1: Write README** — what it is, how it works (one paragraph), Download section pointing at Releases, Build-from-source section (`git clone`, `dotnet build`, `dotnet publish` commands), usage (tray menu), uninstall (delete exe, `%APPDATA%\WebWallpaper`, `%LOCALAPPDATA%\WebWallpaper`).
- [ ] **Step 2: Publish exe**

```bash
dotnet publish src/WebWallpaper -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Zip the publish folder's `WebWallpaper.exe` as `WebWallpaper-v1.0.0-win-x64.zip`.

- [ ] **Step 3: Create repo and push**

```bash
gh repo create web-wallpaper --public --source . --push
```

- [ ] **Step 4: Create release**

```bash
gh release create v1.0.0 WebWallpaper-v1.0.0-win-x64.zip --title "WebWallpaper v1.0.0" --notes "<notes>"
```

- [ ] **Step 5: Verify** — repo page renders README; release asset downloads.
