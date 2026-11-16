using lively.Common;
using lively.Common.Helpers.Storage;
using lively.Core.Library;
using lively.Models;

namespace lively.Tests;

public class LibraryScannerTests
{
    [Fact]
    public void Scan_LoadsWallpaperFoldersSortedByTitle()
    {
        using var tmp = new TempDirectory();
        WallpaperFixture.Create(tmp, "1", "Waves");
        WallpaperFixture.Create(tmp, "2", "Aurora");
        WallpaperFixture.Create(tmp, "3", "Rain");

        var items = LibraryScanner.ScanFolders([tmp.Path]);

        Assert.Equal(["Aurora", "Rain", "Waves"], items.Select(x => x.Title));
    }

    [Fact]
    public void Scan_SkipsFoldersWithoutValidLivelyInfo()
    {
        using var tmp = new TempDirectory();
        WallpaperFixture.Create(tmp, "valid", "Rain");
        tmp.CreateDirectory("not-a-wallpaper");
        tmp.WriteFile("{ not json", "corrupt", "LivelyInfo.json");

        var items = LibraryScanner.ScanFolders([tmp.Path]);

        Assert.Equal(["Rain"], items.Select(x => x.Title));
    }

    [Fact]
    public void Scan_MissingFolder_GivesEmptyLibrary()
    {
        using var tmp = new TempDirectory();

        Assert.Empty(LibraryScanner.ScanFolders([tmp.Combine("does-not-exist")]));
    }

    [Fact]
    public void Scan_CombinesSeveralParentFolders()
    {
        using var a = new TempDirectory();
        using var b = new TempDirectory();
        WallpaperFixture.Create(a, "1", "Waves");
        WallpaperFixture.Create(b, "1", "Aurora");

        var items = LibraryScanner.ScanFolders([a.Path, b.Combine("missing"), b.Path]);

        Assert.Equal(["Aurora", "Waves"], items.Select(x => x.Title));
    }

    [Fact]
    public void ScanFolder_NotAWallpaper_ReturnsNull()
    {
        using var tmp = new TempDirectory();

        Assert.Null(LibraryScanner.ScanFolder(tmp.CreateDirectory("plain")));
        Assert.Null(LibraryScanner.ScanFolder(tmp.Combine("missing")));
    }

    [Fact]
    public void ScanFolder_Wallpaper_ResolvesFolderAndFile()
    {
        using var tmp = new TempDirectory();
        var dir = WallpaperFixture.Create(tmp, "rain", "Rain");

        var item = LibraryScanner.ScanFolder(dir);

        Assert.NotNull(item);
        Assert.Equal(dir, item.LivelyInfoFolderPath);
        Assert.Equal(Path.Combine(dir, "wallpaper.mp4"), item.FilePath);
        Assert.Equal(WallpaperType.video, item.LivelyInfo.Type);
    }

    [Fact]
    public void ScanFolder_UrlWallpaper_KeepsAddress()
    {
        using var tmp = new TempDirectory();
        var dir = tmp.CreateDirectory("site");
        JsonStorage<LivelyInfoModel>.StoreData(Path.Combine(dir, "LivelyInfo.json"), new LivelyInfoModel
        {
            Title = "Site",
            Type = WallpaperType.url,
            FileName = "https://example.com",
        });

        var item = LibraryScanner.ScanFolder(dir);

        Assert.NotNull(item);
        Assert.Equal("https://example.com", item.FilePath);
        Assert.Equal(WallpaperType.url, item.LivelyInfo.Type);
    }

    [Theory]
    [InlineData("Aurora", 0)]
    [InlineData("Banana", 1)]
    [InlineData("Rain", 1)]
    [InlineData("Snow", 2)]
    [InlineData("Zebra", 3)]
    public void FindInsertIndex_KeepsTitleOrder(string title, int expected)
    {
        using var tmp = new TempDirectory();
        WallpaperFixture.Create(tmp, "1", "Rain");
        WallpaperFixture.Create(tmp, "2", "Aurora");
        WallpaperFixture.Create(tmp, "3", "Waves");
        var items = LibraryScanner.ScanFolders([tmp.Path]);

        Assert.Equal(expected, LibraryScanner.FindInsertIndex(items, title));
    }

    [Fact]
    public void FindInsertIndex_EmptyList_IsZero()
    {
        Assert.Equal(0, LibraryScanner.FindInsertIndex([], "Anything"));
    }
}
