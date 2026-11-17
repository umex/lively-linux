using lively.Models;
using lively.Models.Message;

namespace lively.Core.Wallpapers
{
    /// <summary>
    /// One running wallpaper on one display.
    /// </summary>
    public interface IWallpaperPlayer : IDisposable
    {
        /// <summary>True once the player reported msg_wploaded with Success.</summary>
        bool IsLoaded { get; }

        bool IsExited { get; }

        LibraryModel Model { get; }

        DisplayMonitor Screen { get; }

        /// <summary>Process id once started, else null.</summary>
        int? Pid { get; }

        /// <summary>
        /// Starts the player and waits until it reports that the wallpaper is loaded.
        /// On failure the player is terminated before the exception is thrown.
        /// </summary>
        /// <exception cref="WallpaperPlayerException">Start failure, Success:false, timeout or early exit.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
        Task ShowAsync(CancellationToken ct = default);

        void Pause();

        void Play();

        /// <summary>Asks the player to close, kills it after the close timeout.</summary>
        Task CloseAsync();

        /// <summary>Kills the player (and its children) immediately.</summary>
        void Terminate();

        /// <param name="volume">0-100.</param>
        void SetVolume(int volume);

        /// <summary>Mute sends volume 0 and remembers the previous volume for unmute.</summary>
        void SetMute(bool mute);

        void SendMessage(IpcMessage message);

        event EventHandler? Loaded;

        /// <summary>Raised once when the player process has exited, for whatever reason.</summary>
        event EventHandler? Exited;

        event EventHandler<IpcMessage>? MessageReceived;
    }

    public interface IWallpaperPlayerFactory
    {
        /// <summary>Creates a player (not yet started) for <paramref name="model"/> on <paramref name="display"/>.</summary>
        /// <param name="state">
        /// Volume the player starts with. Pausing is up to the caller (the player can only be suspended once loaded).
        /// </param>
        /// <exception cref="WallpaperPlayerException">No player is available for the wallpaper type.</exception>
        IWallpaperPlayer Create(LibraryModel model, DisplayMonitor display, PlaybackState state);
    }
}
