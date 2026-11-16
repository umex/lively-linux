using lively.Core.Desktop;
using lively.Models;

namespace lively.Core.Wallpapers
{
    /// <summary>
    /// Picks the player for a wallpaper. In Phase 2 every wallpaper type runs in lively-player-dummy.
    /// </summary>
    public sealed class WallpaperPlayerFactory : IWallpaperPlayerFactory
    {
        public const string DummyPlayerName = "lively-player-dummy";
        public const int DefaultVolume = 100;

        private readonly CoreOptions options;
        private readonly IDesktopBackend backend;

        public WallpaperPlayerFactory(CoreOptions options, IDesktopBackend backend)
        {
            this.options = options;
            this.backend = backend;
        }

        public IWallpaperPlayer Create(LibraryModel model, DisplayMonitor display)
        {
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
                "--wallpaper-volume", DefaultVolume.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            args.AddRange(backend.GetPlayerArguments(display));
            args.Add("--dummy-log");
            args.Add(Path.Combine(options.PlayerLogDirectory, "player-dummy-" + SafeFileName(display.Id) + ".log"));

            PlayerLaunchInfo launch;
            try
            {
                launch = PlayerExecutableResolver.Resolve(DummyPlayerName, args);
            }
            catch (FileNotFoundException e)
            {
                throw new WallpaperPlayerException(e.Message, e);
            }
            return new ProcessWallpaperPlayer(launch, model, display, options.PlayerLoadTimeout, options.PlayerCloseTimeout, DefaultVolume);
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) || c == '/' ? '_' : c).ToArray());
        }
    }
}
