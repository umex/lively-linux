using lively.Models;

namespace lively.Core.Desktop
{
    public sealed class WaylandDesktopBackend : IDesktopBackend
    {
        public WaylandDesktopBackend(DesktopSession session)
        {
            Session = session;
        }

        public string Name => "wayland";

        public DesktopSession Session { get; }

        public bool CanPlaceWallpapers => false;

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
