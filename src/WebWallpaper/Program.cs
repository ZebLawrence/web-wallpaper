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
