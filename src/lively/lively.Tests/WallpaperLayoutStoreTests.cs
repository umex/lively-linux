using lively.Core.Wallpapers;
using lively.Models;

namespace lively.Tests;

public class WallpaperLayoutStoreTests
{
    [Fact]
    public void Missing_GivesEmpty()
    {
        using var tmp = new TempDirectory();

        Assert.Empty(new JsonWallpaperLayoutStore(tmp.Combine("WallpaperLayout.json")).Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        using var tmp = new TempDirectory();
        var path = tmp.Combine("config", "WallpaperLayout.json");
        var store = new JsonWallpaperLayoutStore(path);

        store.Save([new WallpaperLayoutModel("DP-1", "/wp/rain"), new WallpaperLayoutModel("HDMI-A-1", "/wp/waves")]);
        var loaded = new JsonWallpaperLayoutStore(path).Load();

        Assert.Equal([("DP-1", "/wp/rain"), ("HDMI-A-1", "/wp/waves")], loaded.Select(x => (x.DisplayId, x.LivelyInfoPath)));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_OverwritesExisting()
    {
        using var tmp = new TempDirectory();
        var store = new JsonWallpaperLayoutStore(tmp.Combine("WallpaperLayout.json"));

        store.Save([new WallpaperLayoutModel("DP-1", "/wp/rain")]);
        store.Save([]);

        Assert.Empty(store.Load());
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("")]
    [InlineData("{\"DisplayId\":\"DP-1\"}")]
    public void Corrupt_GivesEmpty(string content)
    {
        using var tmp = new TempDirectory();

        Assert.Empty(new JsonWallpaperLayoutStore(tmp.WriteFile(content, "WallpaperLayout.json")).Load());
    }

    [Fact]
    public void IncompleteEntries_AreDropped()
    {
        using var tmp = new TempDirectory();
        var path = tmp.WriteFile("""
            [
              { "DisplayId": "DP-1", "LivelyInfoPath": "/wp/rain" },
              { "DisplayId": "", "LivelyInfoPath": "/wp/x" },
              { "DisplayId": "DP-2" },
              null
            ]
            """, "WallpaperLayout.json");

        var loaded = new JsonWallpaperLayoutStore(path).Load();

        Assert.Equal(["DP-1"], loaded.Select(x => x.DisplayId));
    }
}
