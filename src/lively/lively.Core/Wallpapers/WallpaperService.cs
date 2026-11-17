using lively.Core.Display;
using lively.Core.Library;
using lively.Core.Settings;
using lively.Models;

namespace lively.Core.Wallpapers
{
    public sealed class WallpaperService : IWallpaperService
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly IDisplayManager displayManager;
        private readonly IWallpaperPlayerFactory playerFactory;
        private readonly IWallpaperLayoutStore layoutStore;
        private readonly ISettingsService? settings;
        // Serializes every mutation (set, close, restore, display and exit handling).
        private readonly SemaphoreSlim gate = new(1, 1);
        // Guarded by its own lock so that Wallpapers can be read while a mutation is running.
        private readonly List<IWallpaperPlayer> players = [];
        private readonly List<WallpaperLayoutModel> layout;
        private bool isShutdown;
        // Written with the gate held, read without it.
        private volatile bool isPaused;
        private volatile int volume;

        /// <param name="settings">
        /// Source of <see cref="SettingsModel.WallpaperVolume"/>; the volume follows its changes. Without it the
        /// wallpapers start muted.
        /// </param>
        public WallpaperService(IDisplayManager displayManager, IWallpaperPlayerFactory playerFactory, IWallpaperLayoutStore layoutStore,
            ISettingsService? settings = null)
        {
            this.displayManager = displayManager;
            this.playerFactory = playerFactory;
            this.layoutStore = layoutStore;
            this.settings = settings;
            layout = layoutStore.Load().ToList();
            volume = Math.Clamp(settings?.Settings.WallpaperVolume ?? 0, 0, 100);
            displayManager.DisplaysChanged += OnDisplaysChanged;
            if (settings != null)
            {
                settings.SettingsChanged += OnSettingsChanged;
            }
        }

        public IReadOnlyList<IWallpaperPlayer> Wallpapers
        {
            get { lock (players) { return players.ToList(); } }
        }

        public bool IsPaused => isPaused;

        public int Volume => volume;

        /// <summary>Copy of the layout as currently intended (also what the layout file holds).</summary>
        public IReadOnlyList<WallpaperLayoutModel> Layout
        {
            get { lock (players) { return layout.Select(x => new WallpaperLayoutModel(x.DisplayId, x.LivelyInfoPath)).ToList(); } }
        }

        public event EventHandler<WallpaperChangedEventArgs>? WallpaperChanged;

        public event EventHandler<WallpaperErrorEventArgs>? WallpaperError;

        public event EventHandler<bool>? PausedChanged;

        public async Task SetWallpaperAsync(LibraryModel model, DisplayMonitor display, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(display);

            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfShutdown();
                var current = displayManager.FindById(display.Id)
                    ?? throw new ArgumentException($"Display \"{display.Id}\" is not connected.", nameof(display));
                await SetCoreAsync(model, current, ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task CloseWallpaperAsync(string displayId)
        {
            ArgumentNullException.ThrowIfNull(displayId);

            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                ThrowIfShutdown();
                var player = Find(displayId);
                if (player != null)
                {
                    Remove(player);
                    await CloseQuietlyAsync(player).ConfigureAwait(false);
                }
                bool layoutChanged;
                lock (players)
                {
                    layoutChanged = layout.RemoveAll(x => x.DisplayId == displayId) > 0;
                }
                if (layoutChanged)
                {
                    SaveLayout();
                }
                if (player != null)
                {
                    RaiseChanged(displayId, null);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task CloseAllWallpapersAsync()
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                ThrowIfShutdown();
                var closed = RemoveAll();
                await Task.WhenAll(closed.Select(CloseQuietlyAsync)).ConfigureAwait(false);
                bool layoutChanged;
                lock (players)
                {
                    layoutChanged = layout.Count > 0;
                    layout.Clear();
                }
                if (layoutChanged)
                {
                    SaveLayout();
                }
                foreach (var player in closed)
                {
                    RaiseChanged(player.Screen.Id, null);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task RestoreWallpaperAsync(CancellationToken ct = default)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfShutdown();
                await RestoreCoreAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task SetPausedAsync(bool paused)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                ThrowIfShutdown();
                if (isPaused == paused)
                {
                    return;
                }
                isPaused = paused;
                Logger.Info(paused ? "Pausing the wallpapers." : "Resuming the wallpapers.");
                ForEachPlayer(p =>
                {
                    if (paused)
                    {
                        p.Pause();
                    }
                    else
                    {
                        p.Play();
                    }
                });
                RaisePausedChanged(paused);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task SetVolumeAsync(int volume)
        {
            var value = Math.Clamp(volume, 0, 100);
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                ThrowIfShutdown();
                if (this.volume == value)
                {
                    return;
                }
                this.volume = value;
                Logger.Info($"Wallpaper volume {value}.");
                ForEachPlayer(p => p.SetVolume(value));
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task ShutdownAsync()
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (isShutdown)
                {
                    return;
                }
                isShutdown = true;
                displayManager.DisplaysChanged -= OnDisplaysChanged;
                if (settings != null)
                {
                    settings.SettingsChanged -= OnSettingsChanged;
                }
                var closed = RemoveAll();
                Logger.Info($"Shutting down, closing {closed.Count} wallpaper(s).");
                await Task.WhenAll(closed.Select(CloseQuietlyAsync)).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        #region core (called with the gate held)

        private async Task SetCoreAsync(LibraryModel model, DisplayMonitor display, CancellationToken ct)
        {
            var folder = model.LivelyInfoFolderPath;
            var previous = Find(display.Id);
            if (previous != null)
            {
                Remove(previous);
                await CloseQuietlyAsync(previous).ConfigureAwait(false);
            }

            IWallpaperPlayer? player = null;
            var paused = isPaused;
            try
            {
                player = playerFactory.Create(model, display, new PlaybackState(volume, paused));
                await player.ShowAsync(ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                player?.Dispose();
                Logger.Error($"Failed to set {folder} on {display.Id}: {e.Message}");
                if (previous != null)
                {
                    RaiseChanged(display.Id, null);
                }
                if (e is not OperationCanceledException)
                {
                    RaiseError(display.Id, e);
                }
                throw;
            }

            if (paused)
            {
                // A player can only be suspended once it has loaded.
                player.Pause();
            }
            player.Exited += OnPlayerExited;
            lock (players)
            {
                players.Add(player);
            }

            bool layoutChanged;
            lock (players)
            {
                var entry = layout.FirstOrDefault(x => x.DisplayId == display.Id);
                layoutChanged = entry == null || entry.LivelyInfoPath != folder;
                if (entry == null)
                {
                    layout.Add(new WallpaperLayoutModel(display.Id, folder));
                }
                else
                {
                    entry.LivelyInfoPath = folder;
                }
            }
            if (layoutChanged)
            {
                SaveLayout();
            }

            Logger.Info($"Wallpaper {folder} running on {display.Id}.");
            RaiseChanged(display.Id, folder);

            if (player.IsExited)
            {
                // Exited between the load and the subscription.
                OnPlayerExited(player, EventArgs.Empty);
            }
        }

        private async Task RestoreCoreAsync(CancellationToken ct)
        {
            List<WallpaperLayoutModel> entries;
            lock (players)
            {
                entries = layout.Select(x => new WallpaperLayoutModel(x.DisplayId, x.LivelyInfoPath)).ToList();
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                var display = displayManager.FindById(entry.DisplayId);
                if (display == null)
                {
                    Logger.Debug($"Restore: display {entry.DisplayId} is not connected, keeping {entry.LivelyInfoPath} for later.");
                    continue;
                }
                if (Find(entry.DisplayId) != null)
                {
                    continue;
                }
                var model = LibraryScanner.ScanFolder(entry.LivelyInfoPath);
                if (model == null)
                {
                    Logger.Warn($"Restore: {entry.LivelyInfoPath} is not a readable wallpaper, skipping {entry.DisplayId}.");
                    continue;
                }
                try
                {
                    await SetCoreAsync(model, display, ct).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Already logged and reported through WallpaperError; the layout entry stays.
                }
            }
        }

        #endregion

        #region background events

        private void OnPlayerExited(object? sender, EventArgs e)
        {
            if (sender is IWallpaperPlayer player)
            {
                _ = Task.Run(() => HandlePlayerExitAsync(player));
            }
        }

        private async Task HandlePlayerExitAsync(IWallpaperPlayer player)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Players closed on purpose are removed before closing, so they are not found here.
                if (!Remove(player))
                {
                    return;
                }
                player.Exited -= OnPlayerExited;
                player.Dispose();
                var id = player.Screen.Id;
                Logger.Error($"Wallpaper player on {id} exited unexpectedly ({player.Model.LivelyInfoFolderPath}).");
                RaiseChanged(id, null);
                RaiseError(id, new WallpaperPlayerException($"Wallpaper player on {id} exited unexpectedly."));
            }
            catch (Exception ex)
            {
                Logger.Error("Handling player exit failed: " + ex);
            }
            finally
            {
                gate.Release();
            }
        }

        private void OnDisplaysChanged(object? sender, EventArgs e)
        {
            _ = Task.Run(HandleDisplaysChangedAsync);
        }

        private void OnSettingsChanged(object? sender, SettingsModel e)
        {
            var value = Math.Clamp(e.WallpaperVolume, 0, 100);
            if (value != volume)
            {
                // The event is synchronous; the volume is applied once the gate is free.
                _ = ApplySettingsVolumeAsync(value);
            }
        }

        private async Task ApplySettingsVolumeAsync(int value)
        {
            try
            {
                await SetVolumeAsync(value).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Shut down meanwhile.
            }
            catch (Exception ex)
            {
                Logger.Error("Applying the wallpaper volume from the settings failed: " + ex);
            }
        }

        private async Task HandleDisplaysChangedAsync()
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (isShutdown)
                {
                    return;
                }
                var present = displayManager.Displays.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
                List<IWallpaperPlayer> vanished;
                lock (players)
                {
                    vanished = players.Where(p => !present.Contains(p.Screen.Id)).ToList();
                    players.RemoveAll(vanished.Contains);
                }
                if (vanished.Count > 0)
                {
                    Logger.Info("Displays removed: " + string.Join(", ", vanished.Select(p => p.Screen.Id)) + "; closing their wallpapers (layout kept).");
                    await Task.WhenAll(vanished.Select(CloseQuietlyAsync)).ConfigureAwait(false);
                    foreach (var player in vanished)
                    {
                        RaiseChanged(player.Screen.Id, null);
                    }
                }
                await RestoreCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error("Handling display change failed: " + ex);
            }
            finally
            {
                gate.Release();
            }
        }

        #endregion

        #region helpers

        private void ThrowIfShutdown()
        {
            ObjectDisposedException.ThrowIf(isShutdown, this);
        }

        private IWallpaperPlayer? Find(string displayId)
        {
            lock (players)
            {
                return players.FirstOrDefault(p => p.Screen.Id == displayId);
            }
        }

        private bool Remove(IWallpaperPlayer player)
        {
            lock (players)
            {
                return players.Remove(player);
            }
        }

        private void ForEachPlayer(Action<IWallpaperPlayer> action)
        {
            foreach (var player in Wallpapers)
            {
                try
                {
                    action(player);
                }
                catch (Exception e)
                {
                    Logger.Error($"Sending to the wallpaper on {player.Screen.Id} failed: {e.Message}");
                }
            }
        }

        private List<IWallpaperPlayer> RemoveAll()
        {
            lock (players)
            {
                var all = players.ToList();
                players.Clear();
                return all;
            }
        }

        private async Task CloseQuietlyAsync(IWallpaperPlayer player)
        {
            player.Exited -= OnPlayerExited;
            try
            {
                await player.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Error($"Closing the wallpaper on {player.Screen.Id} failed: {e.Message}");
            }
            finally
            {
                player.Dispose();
            }
        }

        private void SaveLayout()
        {
            List<WallpaperLayoutModel> copy;
            lock (players)
            {
                copy = layout.Select(x => new WallpaperLayoutModel(x.DisplayId, x.LivelyInfoPath)).ToList();
            }
            try
            {
                layoutStore.Save(copy);
            }
            catch (Exception e)
            {
                Logger.Error("Failed to save the wallpaper layout: " + e.Message);
            }
        }

        private void RaiseChanged(string displayId, string? folder)
        {
            try
            {
                WallpaperChanged?.Invoke(this, new WallpaperChangedEventArgs(displayId, folder));
            }
            catch (Exception e)
            {
                Logger.Error("WallpaperChanged handler failed: " + e);
            }
        }

        private void RaisePausedChanged(bool paused)
        {
            try
            {
                PausedChanged?.Invoke(this, paused);
            }
            catch (Exception e)
            {
                Logger.Error("PausedChanged handler failed: " + e);
            }
        }

        private void RaiseError(string displayId, Exception error)
        {
            try
            {
                WallpaperError?.Invoke(this, new WallpaperErrorEventArgs(displayId, error));
            }
            catch (Exception e)
            {
                Logger.Error("WallpaperError handler failed: " + e);
            }
        }

        #endregion
    }
}
