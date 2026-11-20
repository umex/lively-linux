using lively.Common;
using lively.Core.Wallpapers;
using lively.Models;

namespace lively.Core.Desktop
{
    public sealed class WaylandDesktopBackend : IDesktopBackend
    {
        private readonly bool hasLayerShell;

        /// <param name="session">The detected session.</param>
        /// <param name="hasLayerShell">Whether the compositor advertises <c>zwlr_layer_shell_v1</c>.</param>
        public WaylandDesktopBackend(DesktopSession session, bool hasLayerShell)
        {
            Session = session;
            this.hasLayerShell = hasLayerShell;
        }

        public string Name => "wayland";

        public DesktopSession Session { get; }

        /// <summary>True when the compositor advertises <c>zwlr_layer_shell_v1</c> (KDE Plasma, wlroots compositors; not GNOME).</summary>
        public bool CanPlaceWallpapers => hasLayerShell;

        public IReadOnlyList<string> GetPlayerArguments(DisplayMonitor display)
        {
            return
            [
                "--wallpaper-backend", "wayland",
                "--wallpaper-output", display.Id,
                "--wallpaper-desktop", DesktopName(Session.Environment),
            ];
        }

        /// <summary>
        /// Videos and GIFs play in <c>lively-player-mpv</c>, web pages and URLs (<c>web</c>, <c>webaudio</c>,
        /// <c>url</c>) in <c>lively-player-web</c>, also without layer-shell: the player then fails with the precise
        /// reason (no layer-shell, unknown output, missing library). Everything else stays on the dummy.
        /// </summary>
        public string? PlayerFor(WallpaperType type) => type switch
        {
            WallpaperType.video or WallpaperType.gif => WallpaperPlayerFactory.MpvPlayerName,
            WallpaperType.web or WallpaperType.webaudio or WallpaperType.url => WallpaperPlayerFactory.WebPlayerName,
            _ => null,
        };

        private static string DesktopName(DesktopEnvironment environment) => environment switch
        {
            DesktopEnvironment.Kde => "kde",
            DesktopEnvironment.Gnome => "gnome",
            DesktopEnvironment.WlrootsLike => "wlroots",
            _ => "unknown",
        };
    }
}
