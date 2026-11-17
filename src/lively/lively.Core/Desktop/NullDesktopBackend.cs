using lively.Common;
using lively.Models;

namespace lively.Core.Desktop
{
    /// <summary>
    /// Backend for unsupported sessions (X11, unknown) and fake displays: every wallpaper runs in
    /// lively-player-dummy, which is not placed on the desktop.
    /// </summary>
    public sealed class NullDesktopBackend : IDesktopBackend
    {
        public NullDesktopBackend(DesktopSession session)
        {
            Session = session;
        }

        public string Name => "none";

        public DesktopSession Session { get; }

        public bool CanPlaceWallpapers => false;

        public IReadOnlyList<string> GetPlayerArguments(DisplayMonitor display) => [];

        public string? PlayerFor(WallpaperType type) => null;
    }
}
