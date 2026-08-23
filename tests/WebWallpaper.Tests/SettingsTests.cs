using WebWallpaper;

namespace WebWallpaper.Tests;

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
        try
        {
            new Settings { Url = "https://example.com/", Paused = true }.Save(path);
            var s = Settings.Load(path);
            Assert.Equal("https://example.com/", s.Url);
            Assert.True(s.Paused);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{not json");
            Assert.Equal("about:blank", Settings.Load(path).Url);
        }
        finally
        {
            File.Delete(path);
        }
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
