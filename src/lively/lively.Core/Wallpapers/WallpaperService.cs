using lively.Core.Display;
using lively.Core.Library;
using lively.Models;

namespace lively.Core.Wallpapers
{
    public sealed class WallpaperService : IWallpaperService
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly IDisplayManager displayManager;
        private readonly IWallpaperPlayerFactory playerFactory;
        private readonly IWallpaperLayoutStore layoutStore;
        // Serializes every mutation (set, close, restore, display and exit handling).
        private readonly SemaphoreSlim gate = new(1, 1);
        // Guarded by its own lock so that Wallpapers can be read while a mutation is running.
        private readonly List<IWallpaperPlayer> players = [];
        private readonly List<WallpaperLayoutModel> layout;
        private bool isShutdown;

        public WallpaperService(IDisplayManager displayManager, IWallpaperPlayerFactory playerFactory, IWallpaperLayoutStore layoutStore)
        {
            this.displayManager = displayManager;
            this.playerFactory = playerFactory;
            this.layoutStore = layoutStore;
            layout = layoutStore.Load().ToList();
            displayManager.DisplaysChanged += OnDisplaysChanged;
        }

        public IReadOnlyList<IWallpaperPlayer> Wallpapers
        {
            get { lock (players) { return players.ToList(); } }
        }

        /// <summary>Copy of the layout as currently intended (also what the layout file holds).</summary>
        public IReadOnlyList<WallpaperLayoutModel> Layout
        {
            get { lock (players) { return layout.Select(x => new WallpaperLayoutModel(x.DisplayId, x.LivelyInfoPath)).ToList(); } }
        }

        public event EventHandler<WallpaperChangedEventArgs>? WallpaperChanged;

        public event EventHandler<WallpaperErrorEventArgs>? WallpaperError;

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
            try
            {
                player = playerFactory.Create(model, display);
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
