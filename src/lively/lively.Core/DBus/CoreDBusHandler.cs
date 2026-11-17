using lively.Core.Display;
using lively.Core.Library;
using lively.Core.Settings;
using lively.Core.Wallpapers;
using lively.Ipc;
using lively.Models;
using Tmds.DBus.Protocol;

namespace lively.Core.DBus
{
    /// <summary>
    /// Serves io.github.lively_linux.Core1 on top of the core services and turns their events into signals.
    /// </summary>
    /// <remarks>
    /// Methods run on the thread pool (not on the connection's receive loop), because SetWallpaper waits
    /// for the player's handshake. Service events are raised while the service holds its lock, so their
    /// handlers only emit signals and never call back into the service.
    /// </remarks>
    internal sealed class CoreDBusHandler : DBusHandler, ICore1Handler
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly IWallpaperService wallpaperService;
        private readonly IDisplayManager displayManager;
        private readonly ISettingsService settingsService;
        private readonly Action quit;
        private bool attached;

        public CoreDBusHandler(DBusConnection connection, string path, IWallpaperService wallpaperService,
            IDisplayManager displayManager, ISettingsService settingsService, Action quit)
            : base(connection, path, handlesChildPaths: false, synchronizationContext: new SynchronizationContext())
        {
            this.wallpaperService = wallpaperService;
            this.displayManager = displayManager;
            this.settingsService = settingsService;
            this.quit = quit;
        }

        /// <summary>Starts forwarding service events as signals.</summary>
        public void Attach()
        {
            if (attached)
            {
                return;
            }
            attached = true;
            wallpaperService.WallpaperChanged += OnWallpaperChanged;
            wallpaperService.WallpaperError += OnWallpaperError;
            wallpaperService.PausedChanged += OnPausedChanged;
            displayManager.DisplaysChanged += OnDisplaysChanged;
        }

        public void Detach()
        {
            if (!attached)
            {
                return;
            }
            attached = false;
            wallpaperService.WallpaperChanged -= OnWallpaperChanged;
            wallpaperService.WallpaperError -= OnWallpaperError;
            wallpaperService.PausedChanged -= OnPausedChanged;
            displayManager.DisplaysChanged -= OnDisplaysChanged;
        }

        #region methods

        public ValueTask<(string, string, (int, int, int, int), double, bool)[]> GetDisplaysAsync()
        {
            var primary = displayManager.PrimaryDisplay;
            var result = displayManager.Displays
                .Select(d => DisplayMonitorWire.ToWire(d, d.Equals(primary)))
                .ToArray();
            return ValueTask.FromResult(result);
        }

        public ValueTask<(string, string)[]> GetWallpapersAsync()
        {
            var result = wallpaperService.Wallpapers
                .Select(p => (p.Screen.Id, p.Model.LivelyInfoFolderPath))
                .OrderBy(x => x.Id, StringComparer.Ordinal)
                .ToArray();
            return ValueTask.FromResult(result);
        }

        public async ValueTask SetWallpaperAsync(string folder, string display)
        {
            var model = ResolveWallpaper(folder);
            var monitor = ResolveDisplay(display);
            Logger.Info($"D-Bus SetWallpaper {model.LivelyInfoFolderPath} on {monitor.Id}.");
            try
            {
                await wallpaperService.SetWallpaperAsync(model, monitor).ConfigureAwait(false);
            }
            catch (ArgumentException e)
            {
                throw new DBusErrorReplyException(CoreErrors.DisplayNotFound, e.Message);
            }
            catch (WallpaperPlayerException e)
            {
                throw new DBusErrorReplyException(CoreErrors.PlayerFailed, e.Message);
            }
        }

        public async ValueTask CloseWallpaperAsync(string display)
        {
            // Unknown ids are passed on: the layout may hold an entry for a display that is unplugged.
            var id = display.Length == 0 ? ResolveDisplay(display).Id : display;
            Logger.Info($"D-Bus CloseWallpaper {id}.");
            await wallpaperService.CloseWallpaperAsync(id).ConfigureAwait(false);
        }

        public async ValueTask CloseAllWallpapersAsync()
        {
            Logger.Info("D-Bus CloseAllWallpapers.");
            await wallpaperService.CloseAllWallpapersAsync().ConfigureAwait(false);
        }

        public ValueTask<string> GetSettingsAsync()
        {
            return ValueTask.FromResult(settingsService.ToJson());
        }

        public ValueTask SetSettingsAsync(string settings)
        {
            try
            {
                settingsService.ApplyJson(settings);
            }
            catch (ArgumentException e)
            {
                throw new DBusErrorReplyException(CoreErrors.InvalidSettings, e.Message);
            }
            return ValueTask.CompletedTask;
        }

        public async ValueTask SetPausedAsync(bool paused)
        {
            Logger.Info($"D-Bus SetPaused {paused}.");
            await wallpaperService.SetPausedAsync(paused).ConfigureAwait(false);
        }

        public ValueTask<bool> GetPausedAsync()
        {
            return ValueTask.FromResult(wallpaperService.IsPaused);
        }

        public ValueTask QuitAsync()
        {
            Logger.Info("D-Bus Quit.");
            // After the reply has been sent.
            ThreadPool.QueueUserWorkItem(_ => quit());
            return ValueTask.CompletedTask;
        }

        protected override void HandleException(MethodContext context, Exception ex)
        {
            if (ex is not DBusErrorReplyException)
            {
                if (ex is ObjectDisposedException)
                {
                    Logger.Warn($"D-Bus {context.Request.MemberAsString} after shutdown: {ex.Message}");
                }
                else
                {
                    Logger.Error($"D-Bus {context.Request.MemberAsString} failed: {ex}");
                }
                ex = new DBusErrorReplyException(CoreErrors.Failed, ex.Message);
            }
            base.HandleException(context, ex);
        }

        private static LibraryModel ResolveWallpaper(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !System.IO.Path.IsPathRooted(folder))
            {
                throw new DBusErrorReplyException(CoreErrors.WallpaperNotFound, $"\"{folder}\" is not an absolute folder path.");
            }
            var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder));
            return LibraryScanner.ScanFolder(full)
                ?? throw new DBusErrorReplyException(CoreErrors.WallpaperNotFound, $"\"{full}\" has no readable {LibraryScanner.LivelyInfoFileName}.");
        }

        private DisplayMonitor ResolveDisplay(string display)
        {
            if (display.Length == 0)
            {
                return displayManager.PrimaryDisplay
                    ?? throw new DBusErrorReplyException(CoreErrors.DisplayNotFound, "No display is connected.");
            }
            return displayManager.FindById(display)
                ?? throw new DBusErrorReplyException(CoreErrors.DisplayNotFound, $"Display \"{display}\" is not connected.");
        }

        #endregion

        #region signals (raised with the service lock held: emit only)

        private void OnWallpaperChanged(object? sender, WallpaperChangedEventArgs e)
        {
            Connection.EmitWallpaperChanged(Path, e.DisplayId, e.FolderPath ?? string.Empty);
        }

        private void OnWallpaperError(object? sender, WallpaperErrorEventArgs e)
        {
            Connection.EmitError(Path, e.DisplayId, e.Error.Message);
        }

        private void OnDisplaysChanged(object? sender, EventArgs e)
        {
            Connection.EmitDisplaysChanged(Path);
        }

        private void OnPausedChanged(object? sender, bool paused)
        {
            Connection.EmitPausedChanged(Path, paused);
        }

        #endregion
    }
}
