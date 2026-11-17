using lively.Common;
using lively.Models;

namespace lively.Core.Desktop
{
    /// <summary>
    /// Desktop-specific part of putting a player window behind the desktop.
    /// </summary>
    public interface IDesktopBackend
    {
        string Name { get; }

        DesktopSession Session { get; }

        /// <summary>
        /// True when the backend can actually place wallpaper windows behind the desktop: on Wayland, the
        /// compositor advertises <c>zwlr_layer_shell_v1</c>. False otherwise (X11, unknown sessions, fake
        /// displays): players are started but not attached to the desktop.
        /// </summary>
        bool CanPlaceWallpapers { get; }

        /// <summary>Extra --wallpaper-* arguments that tell the player how to place itself on <paramref name="display"/>.</summary>
        IReadOnlyList<string> GetPlayerArguments(DisplayMonitor display);

        /// <summary>
        /// Assembly name of the player process for wallpapers of <paramref name="type"/> (for example
        /// <c>lively-player-mpv</c>), or null for <c>lively-player-dummy</c>.
        /// </summary>
        string? PlayerFor(WallpaperType type);
    }
}
