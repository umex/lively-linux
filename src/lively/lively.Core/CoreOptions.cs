using lively.Common;

namespace lively.Core
{
    /// <summary>
    /// Paths and timings of the core. Tests point the paths at temporary directories.
    /// </summary>
    public sealed class CoreOptions
    {
        public string SettingsPath { get; set; } = Constants.CommonPaths.UserSettingsPath;
        public string LayoutPath { get; set; } = Constants.CommonPaths.WallpaperLayoutPath;

        /// <summary>Time a player has to report msg_wploaded. Kept below the 25 s D-Bus call timeout.</summary>
        public TimeSpan PlayerLoadTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>Time a player has to exit after cmd_close before it is killed.</summary>
        public TimeSpan PlayerCloseTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>Interval of display re-enumeration (hot-plug detection).</summary>
        public TimeSpan DisplayPollInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// When set, displays come from this spec instead of the compositor,
        /// for example "DP-1:1920x1080@0,0;HDMI-A-1:2560x1440@1920,0".
        /// </summary>
        public string? FakeDisplays { get; set; }

        public string PlayerLogDirectory { get; set; } = Constants.CommonPaths.LogDir;
    }
}
