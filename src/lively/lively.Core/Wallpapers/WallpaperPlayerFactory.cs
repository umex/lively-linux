using lively.Core.Desktop;
using lively.Models;

namespace lively.Core.Wallpapers
{
    /// <summary>
    /// Picks the player for a wallpaper: the one the desktop backend names (<see cref="IDesktopBackend.PlayerFor"/>,
    /// lively-player-mpv for videos and GIFs on Wayland), else lively-player-dummy.
    /// </summary>
    public sealed class WallpaperPlayerFactory : IWallpaperPlayerFactory
    {
        public const string DummyPlayerName = "lively-player-dummy";
        public const string MpvPlayerName = "lively-player-mpv";

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
            if (player == MpvPlayerName)
            {
                args.Add("--mpv-log");
                args.Add(Path.Combine(options.PlayerLogDirectory, "player-mpv-" + SafeFileName(display.Id) + ".log"));
            }
            else if (player == DummyPlayerName)
            {
                args.Add("--dummy-log");
                args.Add(Path.Combine(options.PlayerLogDirectory, "player-dummy-" + SafeFileName(display.Id) + ".log"));
            }

            PlayerLaunchInfo launch;
            try
            {
                launch = PlayerExecutableResolver.Resolve(player, args);
            }
            catch (FileNotFoundException e)
            {
                throw new WallpaperPlayerException(e.Message, e);
            }
            return new ProcessWallpaperPlayer(launch, model, display, options.PlayerLoadTimeout, options.PlayerCloseTimeout, volume);
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) || c == '/' ? '_' : c).ToArray());
        }
    }
}
