using lively.Models;

namespace lively.Ipc
{
    /// <summary>
    /// Client of the core's D-Bus interface (io.github.lively_linux.Core1).
    /// </summary>
    /// <remarks>
    /// Failed calls throw <see cref="Tmds.DBus.Protocol.DBusErrorReplyException"/> whose ErrorName is one of
    /// <see cref="CoreErrors"/> (or a bus error such as org.freedesktop.DBus.Error.ServiceUnknown).
    /// Events are raised in signal order on a background thread, never on the caller's context.
    /// </remarks>
    public interface ICoreClient : IAsyncDisposable
    {
        /// <summary>Subscribes to the core's signals. Call once before relying on the events.</summary>
        Task ConnectAsync(CancellationToken ct = default);

        Task<IReadOnlyList<DisplayMonitor>> GetDisplaysAsync();

        Task<IReadOnlyList<RunningWallpaper>> GetWallpapersAsync();

        /// <param name="folder">Absolute path of the wallpaper folder (the one with LivelyInfo.json).</param>
        /// <param name="displayId">Display id, or "" for the primary display.</param>
        Task SetWallpaperAsync(string folder, string displayId);

        /// <param name="displayId">Display id, or "" for the primary display.</param>
        Task CloseWallpaperAsync(string displayId);

        Task CloseAllWallpapersAsync();

        Task<SettingsModel> GetSettingsAsync();

        Task SetSettingsAsync(SettingsModel settings);

        /// <summary>Pauses or resumes every wallpaper.</summary>
        Task SetPausedAsync(bool paused);

        Task<bool> GetPausedAsync();

        /// <summary>Copies a video file into the library.</summary>
        /// <param name="path">Absolute path of the video file.</param>
        /// <returns>The new wallpaper folder.</returns>
        Task<string> ImportVideoAsync(string path);

        /// <summary>Asks the daemon to close its wallpapers and exit.</summary>
        Task QuitAsync();

        event EventHandler<CoreWallpaperChangedEventArgs>? WallpaperChanged;

        event EventHandler? DisplaysChanged;

        event EventHandler<CoreErrorEventArgs>? Error;

        /// <summary>The wallpapers were paused (true) or resumed (false).</summary>
        event EventHandler<bool>? PausedChanged;
    }
}
