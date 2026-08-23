# WebWallpaper — Design Spec

**Date:** 2026-08-23
**Status:** Approved by user

## Purpose

A Windows program that displays a webpage as the desktop background. The page renders
behind all desktop icons and application windows, and is completely non-interactive —
clicks go to the desktop, never to the page.

## Requirements

- Windows 10/11, x64.
- Renders any URL as a live desktop background.
- Page sits **behind** desktop icons and all windows.
- Page receives **no input** (mouse/keyboard) — the desktop behaves normally.
- **All monitors** show the wallpaper, one independent copy of the **same URL** per monitor.
- Audio from the page is **always muted**.
- Configured via a **system tray icon**: Set URL…, Reload, Pause/Resume, Exit.
- **No autostart** — manual launch only.
- On exit (or crash), the normal static wallpaper is restored (desktop refreshed).
- Distributed via a **public GitHub repo** with a README (clone + build instructions)
  and a **GitHub Release** carrying a pre-built, self-contained exe.

## Approach

**WorkerW injection** (the Wallpaper Engine / Lively Wallpaper technique):

1. Find the `Progman` window and send it message `0x052C`. Windows responds by creating
   a `WorkerW` window layered behind the desktop icons but in front of the static wallpaper.
2. Re-parent our borderless windows into that `WorkerW` via `SetParent`.
3. Because the window lives behind the icon layer, it never receives input — the
   non-interactivity requirement comes for free.

**Windows 11 24H2+ fallback:** Microsoft changed the desktop window hierarchy — `WorkerW`
becomes a child of `Progman` (or is absent) and windows are parented to `Progman` directly.
At runtime the app detects which structure exists and parents accordingly.

Rejected alternatives: a bottom-most (`HWND_BOTTOM`) window covers the desktop icons;
screensaver/DWM tricks are fragile.

## Architecture

Single C# WPF app, `net10.0-windows`, published self-contained for win-x64.

| Component | Responsibility |
|---|---|
| `WallpaperInterop` | Win32 layer: find `Progman`, spawn/locate `WorkerW` (with 24H2 fallback), `SetParent`, refresh desktop on exit. |
| `WallpaperWindow` | One borderless, non-activating WPF window per monitor, sized to the monitor's pixel bounds, hosting a WebView2 (`IsMuted = true`, context menus / dev tools / accelerator keys disabled). |
| `TrayIcon` | The only visible UI. Menu: Set URL… (small dialog), Reload, Pause/Resume, Exit. |
| `Settings` | JSON persistence at `%APPDATA%\WebWallpaper\settings.json`: URL and paused flag. |
| `App` | Wires everything: startup, monitor-change handling, single-instance guard, clean shutdown. |

## Behavior details

- **Monitor changes** (plug/unplug, resolution): tear down all wallpaper windows and
  recreate them for the new layout (`SystemEvents.DisplaySettingsChanged`).
- **Pause**: hides windows and suspends WebView2 (`TrySuspendAsync`) to free CPU/GPU.
- **Exit / restart**: broadcast a desktop refresh (`SystemParametersInfo(SPI_SETDESKWALLPAPER)`)
  so the static wallpaper repaints and no dead region remains.
- **WebView2 runtime missing**: message box linking to Microsoft's Evergreen installer, then exit.
- **Page crash/hang**: WebView2 `ProcessFailed` → automatic reload.
- **Single instance**: a named mutex; second launch activates nothing and exits quietly.

## Error handling

- Invalid URL entered → validated in the Set URL dialog (must parse as http/https URI).
- `WorkerW` not found after `0x052C` → retry once, then fall back to `Progman` parenting.
- Settings file corrupt/missing → fall back to defaults (about:blank + not paused).

## Testing

Interop and rendering are verified manually: page appears behind icons on every monitor,
icons remain clickable, app windows layer above, Pause/Resume/Reload work, clean exit
restores the wallpaper. `Settings` load/save/defaults logic is covered by unit tests.

## Distribution

- Public GitHub repo under the user's account (`ZebLawrence`).
- README: what it is, screenshot-free quick start, clone + `dotnet build` / `dotnet publish`
  instructions, tray usage, uninstall note (delete exe + `%APPDATA%\WebWallpaper`).
- GitHub Release `v1.0.0` with a zipped self-contained single-file exe built via
  `dotnet publish -c Release -r win-x64 --self-contained`.
