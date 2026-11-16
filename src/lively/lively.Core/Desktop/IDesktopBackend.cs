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
        /// True when the backend can actually place wallpaper windows. Always false in Phase 2:
        /// players are started but not attached to the desktop.
        /// </summary>
        bool CanPlaceWallpapers { get; }

        /// <summary>Extra --wallpaper-* arguments that tell the player how to place itself on <paramref name="display"/>.</summary>
        IReadOnlyList<string> GetPlayerArguments(DisplayMonitor display);
    }
}
