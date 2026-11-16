using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace lively.Common
{
    public static class Constants
    {
        public static class CommonPaths
        {
            private const string AppDirName = "lively-wallpaper";

            // XDG base directories. .NET maps LocalApplicationData to $XDG_DATA_HOME (~/.local/share)
            // and ApplicationData to $XDG_CONFIG_HOME (~/.config); state and cache have no SpecialFolder.

            /// <summary>User data: wallpaper library. ~/.local/share/lively-wallpaper</summary>
            public static string AppDataDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDirName);
            /// <summary>Settings files. ~/.config/lively-wallpaper</summary>
            public static string ConfigDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDirName);
            /// <summary>Logs and other state. ~/.local/state/lively-wallpaper</summary>
            public static string StateDir { get; } = Path.Combine(GetXdgDir("XDG_STATE_HOME", ".local", "state"), AppDirName);
            /// <summary>Disposable files. ~/.cache/lively-wallpaper</summary>
            public static string CacheDir { get; } = Path.Combine(GetXdgDir("XDG_CACHE_HOME", ".cache"), AppDirName);

            /// <summary>
            /// Default wallpaper library location, wallpapers are in the "wallpapers" subdirectory.
            /// </summary>
            public static string LibraryDir { get; } = Path.Combine(AppDataDir, "library");
            public static string LogDir { get; } = Path.Combine(StateDir, "logs");
            public static string TempDir { get; } = Path.Combine(CacheDir, "temp");
            public static string TempCefDir { get; } = Path.Combine(CacheDir, "Cef");
            public static string TempWebView2Dir { get; } = Path.Combine(CacheDir, "WebView2");
            public static string AppRulesPath { get; } = Path.Combine(ConfigDir, "AppRules.json");
            public static string WallpaperLayoutPath { get; } = Path.Combine(ConfigDir, "WallpaperLayout.json");
            public static string UserSettingsPath { get; } = Path.Combine(ConfigDir, "Settings.json");
            public static string WeatherSettingsPath { get; } = Path.Combine(ConfigDir, "WeatherSettings.json");

            private static string GetXdgDir(string variable, params string[] homeFallback)
            {
                var value = Environment.GetEnvironmentVariable(variable);
                // The XDG spec says relative paths are invalid and must be ignored.
                if (!string.IsNullOrEmpty(value) && Path.IsPathRooted(value))
                {
                    return value;
                }

                var path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                foreach (var part in homeFallback)
                {
                    path = Path.Combine(path, part);
                }
                return path;
            }
        }

        /// <summary>
        /// Session bus names. Owning a bus name is also the single-instance lock.
        /// </summary>
        public static class DBus
        {
            public const string DaemonBusName = "io.github.lively_linux.Daemon";
            public const string CoreObjectPath = "/io/github/lively_linux/Core";
            public const string CoreInterface = "io.github.lively_linux.Core1";
            public const string UiBusName = "io.github.lively_linux.Lively";
            public const string UiObjectPath = "/io/github/lively_linux/Lively";
            /// <summary>Followed by WallpaperNotFound, DisplayNotFound, PlayerFailed or InvalidSettings.</summary>
            public const string ErrorPrefix = "io.github.lively_linux.Error.";
        }
        
        public static class ApplicationType
        {
            public static bool IsMSIX { get; } = false;
            //todo: make compile-time flag.
            public static bool IsTestBuild { get; } = false;
        }

        public static class Weather
        {
            //todo: make compile-time flag.
            public static string OpenWeatherMapAPIKey = string.Empty;
        }
    }
}
