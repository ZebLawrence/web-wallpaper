# WebWallpaper

Display any webpage as your Windows desktop background. The page renders **behind your
desktop icons and all windows**, and it is completely non-interactive — clicks land on
your desktop like normal, never on the page.

Works on Windows 10 and Windows 11 (including 24H2+), with multi-monitor support:
every display shows its own copy of the page.

## How it works

WebWallpaper uses the same trick as Wallpaper Engine and Lively Wallpaper: it asks the
shell's `Progman` window (message `0x052C`) to spawn a `WorkerW` window that sits between
the static wallpaper and the desktop icon layer, then re-parents a borderless
[WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) window into it —
one per monitor. Because the page lives below the icon layer, input never reaches it.

## Download

Grab `WebWallpaper-vX.Y.Z-win-x64.zip` from the
[Releases page](https://github.com/ZebLawrence/web-wallpaper/releases), unzip it
anywhere, and run `WebWallpaper.exe`. No installer needed.

Requires the Microsoft Edge WebView2 Runtime, which ships with Windows 11 and most
updated Windows 10 machines. If it's missing, the app shows a download link on startup.

## Usage

On first run you'll be asked for a URL. After that, everything is in the **system tray
icon**:

| Menu item | What it does |
|---|---|
| Set URL… | Change the webpage (also on double-click of the tray icon) |
| Reload | Refresh the page on all monitors |
| Pause | Hide the wallpaper and suspend the browser to free CPU/GPU |
| Exit | Quit and restore your normal wallpaper |

The page's audio is always muted. Settings are stored in
`%APPDATA%\WebWallpaper\settings.json`.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows.

```bash
git clone https://github.com/ZebLawrence/web-wallpaper.git
cd web-wallpaper
dotnet build
dotnet test
dotnet run --project src/WebWallpaper
```

To produce a standalone exe (what the release ships):

```bash
dotnet publish src/WebWallpaper -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The exe lands in `src/WebWallpaper/bin/Release/net10.0-windows/win-x64/publish/`.

## Uninstall

Exit the app from the tray, then delete:

- `WebWallpaper.exe` (wherever you put it)
- `%APPDATA%\WebWallpaper` (settings)
- `%LOCALAPPDATA%\WebWallpaper` (browser cache and log)

## License

[MIT](LICENSE)
