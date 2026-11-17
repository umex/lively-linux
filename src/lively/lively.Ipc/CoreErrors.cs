using lively.Common;

namespace lively.Ipc
{
    /// <summary>D-Bus error names returned by the core (io.github.lively_linux.Core1).</summary>
    public static class CoreErrors
    {
        /// <summary>The folder is not a readable wallpaper (no or invalid LivelyInfo.json, or a relative path).</summary>
        public const string WallpaperNotFound = Constants.DBus.ErrorPrefix + "WallpaperNotFound";

        /// <summary>No display with that id is connected (or "" was given and there is no display).</summary>
        public const string DisplayNotFound = Constants.DBus.ErrorPrefix + "DisplayNotFound";

        /// <summary>The player could not be started, did not load in time, or the wallpaper file is missing.</summary>
        public const string PlayerFailed = Constants.DBus.ErrorPrefix + "PlayerFailed";

        /// <summary>The settings JSON is malformed or not a settings object.</summary>
        public const string InvalidSettings = Constants.DBus.ErrorPrefix + "InvalidSettings";

        /// <summary>ImportVideo failed: relative or missing path, no video stream, ffmpeg missing or failing.</summary>
        public const string ImportFailed = Constants.DBus.ErrorPrefix + "ImportFailed";

        /// <summary>Anything else, for example a call that arrives while the daemon shuts down.</summary>
        public const string Failed = Constants.DBus.ErrorPrefix + "Failed";
    }
}
