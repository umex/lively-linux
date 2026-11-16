using lively.Common;
using lively.Common.Helpers.Storage;
using lively.Models;

namespace lively.Tests;

public class JsonStorageTests
{
    [Fact]
    public void StoreThenLoad_RoundTripsLivelyInfo()
    {
        using var tmp = new TempDirectory();
        var path = tmp.Combine("LivelyInfo.json");
        var info = new LivelyInfoModel
        {
            Title = "Rain",
            Author = "someone",
            Type = WallpaperType.video,
            FileName = "rain.mp4",
            IsAbsolutePath = true,
        };

        JsonStorage<LivelyInfoModel>.StoreData(path, info);
        var loaded = JsonStorage<LivelyInfoModel>.LoadData(path);

        Assert.Equal("Rain", loaded.Title);
        Assert.Equal("someone", loaded.Author);
        Assert.Equal(WallpaperType.video, loaded.Type);
        Assert.Equal("rain.mp4", loaded.FileName);
        Assert.True(loaded.IsAbsolutePath);
    }

    [Fact]
    public void LoadData_EmptyFile_Throws()
    {
        using var tmp = new TempDirectory();
        var path = tmp.WriteFile("", "empty.json");

        Assert.Throws<ArgumentNullException>(() => JsonStorage<LivelyInfoModel>.LoadData(path));
    }
}
