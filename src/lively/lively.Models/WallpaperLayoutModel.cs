using System;

namespace lively.Models
{
    /// <summary>
    /// One entry of WallpaperLayout.json: which wallpaper the user wants on which display.
    /// </summary>
    [Serializable]
    public class WallpaperLayoutModel
    {
        /// <summary><see cref="DisplayMonitor.Id"/> of the display.</summary>
        public string DisplayId { get; set; }
        /// <summary>Wallpaper folder, the one that contains LivelyInfo.json.</summary>
        public string LivelyInfoPath { get; set; }

        public WallpaperLayoutModel()
        {
        }

        public WallpaperLayoutModel(string displayId, string livelyInfoPath)
        {
            DisplayId = displayId;
            LivelyInfoPath = livelyInfoPath;
        }
    }
}
