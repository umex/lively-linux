using lively.Common;
using lively.Models;

namespace lively.Tests;

public class LibraryModelTests
{
    [Fact]
    public void RelativePaths_ResolveAgainstWallpaperFolder()
    {
        using var tmp = new TempDirectory();
        var dir = WallpaperFixture.Create(tmp, "rain", "Rain");
        var info = new LivelyInfoModel { Title = "Rain", Type = WallpaperType.video, FileName = "wallpaper.mp4", Thumbnail = "thumb.jpg" };

        var model = new LibraryModel(info, dir);

        Assert.Equal(Path.Combine(dir, "wallpaper.mp4"), model.FilePath);
        Assert.Equal(Path.Combine(dir, "thumb.jpg"), model.ThumbnailPath);
        Assert.Equal(model.ThumbnailPath, model.ImagePath);
        Assert.Equal(dir, model.LivelyInfoFolderPath);
        Assert.Equal(LibraryTileType.ready, model.DataType);
    }

    [Fact]
    public void MissingFiles_BecomeNull()
    {
        using var tmp = new TempDirectory();
        var dir = tmp.CreateDirectory("empty");
        var info = new LivelyInfoModel { Title = "Gone", Type = WallpaperType.video, FileName = "missing.mp4", Thumbnail = "missing.jpg" };

        var model = new LibraryModel(info, dir);

        Assert.Null(model.FilePath);
        Assert.Null(model.ThumbnailPath);
    }

    [Fact]
    public void UrlWallpaper_KeepsAddressAsFilePath()
    {
        using var tmp = new TempDirectory();
        var info = new LivelyInfoModel { Title = "Site", Type = WallpaperType.url, FileName = "https://example.com" };

        var model = new LibraryModel(info, tmp.Path);

        Assert.Equal("https://example.com", model.FilePath);
    }

    [Fact]
    public void BlankTitleAndAuthor_GetPlaceholder()
    {
        using var tmp = new TempDirectory();
        var info = new LivelyInfoModel { Title = " ", Author = null, Type = WallpaperType.url, FileName = "https://example.com" };

        var model = new LibraryModel(info, tmp.Path);

        Assert.Equal("---", model.Title);
        Assert.Equal("---", model.Author);
    }

    [Fact]
    public void LongTitle_IsTruncated()
    {
        using var tmp = new TempDirectory();
        var info = new LivelyInfoModel { Title = new string('a', 150), Type = WallpaperType.url, FileName = "https://example.com" };

        var model = new LibraryModel(info, tmp.Path);

        Assert.Equal(100, model.Title.Length);
    }

    [Fact]
    public void PropertyChange_IsNotified()
    {
        using var tmp = new TempDirectory();
        var model = new LibraryModel(new LivelyInfoModel { Title = "A", Type = WallpaperType.url, FileName = "https://example.com" }, tmp.Path);
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.Title = "B";

        Assert.Equal([nameof(LibraryModel.Title)], changed);
    }
}
