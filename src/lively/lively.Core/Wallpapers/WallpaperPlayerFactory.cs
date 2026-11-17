using lively.Core.Desktop;
using lively.Models;

namespace lively.Core.Wallpapers
{
    /// <summary>
    /// Picks the player for a wallpaper: the one the desktop backend names (<see cref="IDesktopBackend.PlayerFor"/>,
    /// lively-player-mpv for videos and GIFs and lively-player-web for web pages on Wayland), else lively-player-dummy.
    /// </summary>
    public sealed class WallpaperPlayerFactory : IWallpaperPlayerFactory
    {
        public const string DummyPlayerName = "lively-player-dummy";
        public const string MpvPlayerName = "lively-player-mpv";
        public const string WebPlayerName = "lively-player-web";

        /// <summary>The <see cref="WallpaperPlayerException"/> message when lively-player-web is not next to the daemon.</summary>
        public const string WebPlayerMissingMessage =
            "The web player (lively-player-web) is not built or installed. Building it needs cmake, a C++ compiler and the Qt 6 WebEngine "
            + "and LayerShellQt development packages (Fedora: gcc-c++ cmake qt6-qtbase-devel qt6-qtdeclarative-devel qt6-qtwebengine-devel layer-shell-qt-devel).";

        private readonly CoreOptions options;
        private readonly IDesktopBackend backend;

        public WallpaperPlayerFactory(CoreOptions options, IDesktopBackend backend)
        {
            this.options = options;
            this.backend = backend;
        }

        public IWallpaperPlayer Create(LibraryModel model, DisplayMonitor display, PlaybackState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            var volume = Math.Clamp(state.Volume, 0, 100);
            var url = model.FilePath;
            if (string.IsNullOrEmpty(url))
            {
                throw new WallpaperPlayerException($"Wallpaper file of \"{model.Title}\" is missing ({model.LivelyInfo.FileName}).");
            }

            var args = new List<string>
            {
                "--wallpaper-url", url,
                "--wallpaper-type", model.LivelyInfo.Type.ToString(),
                "--wallpaper-display", display.Id,
                "--wallpaper-geometry", $"{display.Width}x{display.Height}",
                "--wallpaper-volume", volume.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            args.AddRange(backend.GetPlayerArguments(display));

            var player = backend.PlayerFor(model.LivelyInfo.Type) ?? DummyPlayerName;
            var displayName = SafeFileName(display.Id);
            if (LogName(player) is { } logName)
            {
                args.Add($"--{logName}-log");
                args.Add(Path.Combine(options.PlayerLogDirectory, $"player-{logName}-{displayName}.log"));
            }
            if (player == WebPlayerName)
            {
                // One profile per display: concurrent Chromium instances must not share a storage path.
                args.Add("--web-profile");
                args.Add(Path.Combine(options.WebProfileDirectory, displayName));
            }

            PlayerLaunchInfo launch;
            try
            {
                launch = PlayerExecutableResolver.Resolve(options.PlayerDirectory, player, args);
            }
            catch (FileNotFoundException e)
            {
                throw new WallpaperPlayerException(player == WebPlayerName ? WebPlayerMissingMessage : e.Message, e);
            }
            return new ProcessWallpaperPlayer(launch, model, display, options.PlayerLoadTimeout, options.PlayerCloseTimeout, volume);
        }

        /// <summary>The name in the player's <c>--&lt;name&gt;-log</c> argument and log file, null for an unknown player.</summary>
        private static string? LogName(string player) => player switch
        {
            DummyPlayerName => "dummy",
            MpvPlayerName => "mpv",
            WebPlayerName => "web",
            _ => null,
        };

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) || c == '/' ? '_' : c).ToArray());
        }
    }
}
