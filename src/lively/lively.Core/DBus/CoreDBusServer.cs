using lively.Common;
using lively.Core.Display;
using lively.Core.Settings;
using lively.Core.Wallpapers;
using Tmds.DBus.Protocol;

namespace lively.Core.DBus
{
    /// <summary>
    /// Publishes the core on a D-Bus connection: the Core1 object at <see cref="Constants.DBus.CoreObjectPath"/>
    /// and a well-known bus name, whose ownership is also the single-instance lock.
    /// </summary>
    public sealed class CoreDBusServer : IAsyncDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly IWallpaperService wallpaperService;
        private readonly IDisplayManager displayManager;
        private readonly ISettingsService settingsService;
        private readonly Action quit;
        private CoreDBusHandler? handler;
        private string? busName;

        /// <param name="quit">Called (on the thread pool) when a client calls Quit.</param>
        public CoreDBusServer(IWallpaperService wallpaperService, IDisplayManager displayManager, ISettingsService settingsService, Action quit)
        {
            this.wallpaperService = wallpaperService;
            this.displayManager = displayManager;
            this.settingsService = settingsService;
            this.quit = quit;
        }

        /// <summary>
        /// Registers the object and requests <paramref name="name"/>.
        /// </summary>
        /// <returns>False when another connection already owns the name; nothing stays registered then.</returns>
        public async Task<bool> StartAsync(DBusConnection connection, string name = Constants.DBus.DaemonBusName)
        {
            ArgumentNullException.ThrowIfNull(connection);
            if (handler != null)
            {
                throw new InvalidOperationException("The server is already started.");
            }

            var h = new CoreDBusHandler(connection, Constants.DBus.CoreObjectPath, wallpaperService, displayManager, settingsService, quit);
            // The handler must be in place before the name is visible to clients.
            connection.AddMethodHandler(h);
            h.Attach();
            bool owner;
            try
            {
                owner = await connection.TryRequestNameAsync(name, RequestNameOptions.None).ConfigureAwait(false);
            }
            catch
            {
                h.Detach();
                connection.RemoveMethodHandler(h.Path);
                throw;
            }
            if (!owner)
            {
                h.Detach();
                connection.RemoveMethodHandler(h.Path);
                Logger.Info($"Bus name {name} is owned by another process.");
                return false;
            }

            handler = h;
            busName = name;
            Logger.Info($"Serving {Constants.DBus.CoreInterface} at {Constants.DBus.CoreObjectPath} as {name}.");
            return true;
        }

        public async ValueTask DisposeAsync()
        {
            var h = handler;
            if (h == null)
            {
                return;
            }
            handler = null;
            h.Detach();
            try
            {
                h.Connection.RemoveMethodHandler(h.Path);
                if (busName != null)
                {
                    await h.Connection.ReleaseNameAsync(busName).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
            {
                Logger.Debug("Releasing the bus name failed: " + e.Message);
            }
        }
    }
}
