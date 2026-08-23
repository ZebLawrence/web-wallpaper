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
