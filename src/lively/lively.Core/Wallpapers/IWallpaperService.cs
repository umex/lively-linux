using lively.Models;

namespace lively.Core.Wallpapers
{
    public sealed class WallpaperChangedEventArgs : EventArgs
    {
        public WallpaperChangedEventArgs(string displayId, string? folderPath)
        {
            DisplayId = displayId;
            FolderPath = folderPath;
        }

        public string DisplayId { get; }

        /// <summary>Wallpaper folder now running on the display, null when it was closed.</summary>
        public string? FolderPath { get; }
    }

    public sealed class WallpaperErrorEventArgs : EventArgs
    {
        public WallpaperErrorEventArgs(string displayId, Exception error)
        {
            DisplayId = displayId;
            Error = error;
        }

        public string DisplayId { get; }

        public Exception Error { get; }
    }

    /// <summary>
    /// Owns the running wallpapers (one per display) and the persisted layout.
    /// The layout file is the user's intent; running players are that intent limited to present displays.
    /// </summary>
    /// <remarks>
    /// Events are raised while the service's internal lock is held, so handlers must not wait on calls
    /// into the service.
    /// </remarks>
    public interface IWallpaperService
    {
        /// <summary>Snapshot of the running players.</summary>
        IReadOnlyList<IWallpaperPlayer> Wallpapers { get; }

        /// <summary>
        /// Replaces the wallpaper on <paramref name="display"/> and records it in the layout.
        /// Completes once the player has loaded the wallpaper.
        /// </summary>
        /// <exception cref="ArgumentException">The display is not connected.</exception>
        /// <exception cref="WallpaperPlayerException">The player failed; the layout is unchanged.</exception>
        Task SetWallpaperAsync(LibraryModel model, DisplayMonitor display, CancellationToken ct = default);

        /// <summary>Closes the wallpaper on the display and removes it from the layout.</summary>
        Task CloseWallpaperAsync(string displayId);

        /// <summary>Closes every wallpaper and clears the layout.</summary>
        Task CloseAllWallpapersAsync();

        /// <summary>Starts the layout's wallpapers on the displays that are present and not running one.</summary>
        Task RestoreWallpaperAsync(CancellationToken ct = default);

        /// <summary>Closes every player without touching the layout; the service is unusable afterwards.</summary>
        Task ShutdownAsync();

        event EventHandler<WallpaperChangedEventArgs>? WallpaperChanged;

        event EventHandler<WallpaperErrorEventArgs>? WallpaperError;
    }
}
