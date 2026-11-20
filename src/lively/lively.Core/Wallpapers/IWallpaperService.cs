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

    public sealed class WallpaperUpdatedEventArgs : EventArgs
    {
        public WallpaperUpdatedEventArgs(string folderPath)
        {
            FolderPath = folderPath;
        }

        /// <summary>Wallpaper folder whose LivelyInfo.json changed.</summary>
        public string FolderPath { get; }
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
    /// into the service. Pause and volume apply to every running player and to players started later.
    /// </remarks>
    public interface IWallpaperService
    {
        /// <summary>Snapshot of the running players.</summary>
        IReadOnlyList<IWallpaperPlayer> Wallpapers { get; }

        /// <summary>True while the wallpapers are paused (suspended).</summary>
        bool IsPaused { get; }

        /// <summary>Volume of the wallpapers, 0-100.</summary>
        int Volume { get; }

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

        /// <summary>Pauses or resumes every running wallpaper; wallpapers started later follow the state.</summary>
        /// <exception cref="ObjectDisposedException">The service has been shut down.</exception>
        Task SetPausedAsync(bool paused);

        /// <summary>Sets the volume of every running wallpaper and of those started later.</summary>
        /// <param name="volume">Clamped to 0-100.</param>
        /// <exception cref="ObjectDisposedException">The service has been shut down.</exception>
        Task SetVolumeAsync(int volume);

        /// <summary>Closes every player without touching the layout; the service is unusable afterwards.</summary>
        Task ShutdownAsync();

        event EventHandler<WallpaperChangedEventArgs>? WallpaperChanged;

        event EventHandler<WallpaperErrorEventArgs>? WallpaperError;

        /// <summary>
        /// <see cref="IsPaused"/> changed; the argument is the new value. Raised only on a change, with the
        /// service's lock held like the other events, so handlers must not call back into the service.
        /// </summary>
        event EventHandler<bool>? PausedChanged;

        /// <summary>
        /// A wallpaper folder's LivelyInfo.json changed, for now only when a web wallpaper got its thumbnail.
        /// Raised on a thread-pool thread without the service's lock.
        /// </summary>
        event EventHandler<WallpaperUpdatedEventArgs>? WallpaperUpdated;
    }
}
