namespace WebWallpaper;

/// <summary>Minimal diagnostic log at %LOCALAPPDATA%\WebWallpaper\webwallpaper.log.</summary>
internal static class Log
{
    static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WebWallpaper", "webwallpaper.log");

    static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
                File.AppendAllText(PathName, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never take the app down */ }
    }
}
