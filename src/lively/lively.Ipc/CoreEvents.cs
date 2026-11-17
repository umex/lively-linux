namespace lively.Ipc
{
    /// <summary>A wallpaper running on a display, as reported by the core.</summary>
    public sealed record RunningWallpaper(string DisplayId, string FolderPath);

    /// <summary>The WallpaperChanged signal.</summary>
    public sealed class CoreWallpaperChangedEventArgs : EventArgs
    {
        public CoreWallpaperChangedEventArgs(string displayId, string? folderPath)
        {
            DisplayId = displayId;
            FolderPath = folderPath;
        }

        public string DisplayId { get; }

        /// <summary>Wallpaper folder now running on the display, null when it was closed.</summary>
        public string? FolderPath { get; }
    }

    /// <summary>The WallpaperUpdated signal.</summary>
    public sealed class CoreWallpaperUpdatedEventArgs : EventArgs
    {
        public CoreWallpaperUpdatedEventArgs(string folderPath)
        {
            FolderPath = folderPath;
        }

        /// <summary>Wallpaper folder whose LivelyInfo.json changed.</summary>
        public string FolderPath { get; }
    }

    /// <summary>The Error signal.</summary>
    public sealed class CoreErrorEventArgs : EventArgs
    {
        public CoreErrorEventArgs(string displayId, string message)
        {
            DisplayId = displayId;
            Message = message;
        }

        public string DisplayId { get; }

        public string Message { get; }
    }
}
