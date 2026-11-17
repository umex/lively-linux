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

        private static string DesktopName(DesktopEnvironment environment) => environment switch
        {
            DesktopEnvironment.Kde => "kde",
            DesktopEnvironment.Gnome => "gnome",
            DesktopEnvironment.WlrootsLike => "wlroots",
            _ => "unknown",
        };
    }
}
