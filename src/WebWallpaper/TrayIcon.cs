namespace WebWallpaper;

/// <summary>The app's only visible UI: a tray icon with the control menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _pauseItem;
    readonly ToolStripMenuItem _mouseItem;

    public event Action? SetUrlClicked;
    public event Action? ReloadClicked;
    public event Action<bool>? MouseInteractionToggled;
    public event Action<bool>? PauseToggled;
    public event Action? ExitClicked;

    public TrayIcon(bool paused, bool mouseInteraction)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Set URL…", null, (_, _) => SetUrlClicked?.Invoke());
        menu.Items.Add("Reload", null, (_, _) => ReloadClicked?.Invoke());
        _mouseItem = new ToolStripMenuItem("Mouse interaction") { Checked = mouseInteraction, CheckOnClick = true };
        _mouseItem.CheckedChanged += (_, _) => MouseInteractionToggled?.Invoke(_mouseItem.Checked);
        menu.Items.Add(_mouseItem);
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
