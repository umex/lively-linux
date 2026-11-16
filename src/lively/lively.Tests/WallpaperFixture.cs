using lively.Common;
using lively.Common.Helpers.Storage;
using lively.Models;

namespace lively.Tests;

public static class WallpaperFixture
{
    /// <summary>
    /// Writes a wallpaper folder (LivelyInfo.json, wallpaper file, thumbnail) and returns its path.
    /// </summary>
    public static string Create(TempDirectory root, string folderName, string title, WallpaperType type = WallpaperType.video)
    {
        var dir = root.CreateDirectory(folderName);
        File.WriteAllText(Path.Combine(dir, "wallpaper.mp4"), "video");
        File.WriteAllText(Path.Combine(dir, "thumb.jpg"), "image");
        JsonStorage<LivelyInfoModel>.StoreData(Path.Combine(dir, "LivelyInfo.json"), new LivelyInfoModel
        {
            Title = title,
            Desc = "description of " + title,
            Type = type,
            FileName = "wallpaper.mp4",
            Thumbnail = "thumb.jpg",
        });
        return dir;
    }
}
