using lively.Common;
using lively.Core.DBus;
using lively.Core.Display;
using lively.Core.Library;
using lively.Core.Settings;
using lively.Core.Wallpapers;
using lively.Models;
using Microsoft.Extensions.Hosting;
using Tmds.DBus.Protocol;

namespace lively.Daemon
{
    /// <summary>
    /// Connects to the session bus, publishes the core as <see cref="Constants.DBus.DaemonBusName"/>,
    /// starts display watching and restores the saved wallpapers. Stopping closes every player.
    /// Keeps the login autostart entry in line with <see cref="SettingsModel.Startup"/>.
    /// </summary>
    public sealed class DaemonService : IHostedService
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly DaemonOptions options;
        private readonly DaemonExitCode exitCode;
        private readonly IHostApplicationLifetime lifetime;
        private readonly IWallpaperService wallpaperService;
        private readonly IDisplayManager displayManager;
        private readonly ISettingsService settingsService;
        private readonly VideoImporter importer;
        private readonly WebImporter webImporter;
        private readonly CancellationTokenSource stopping = new();
        private DBusConnection? connection;
        private CoreDBusServer? server;
        private Task? restoreTask;
        // Settings change on D-Bus handler threads; one writer at a time.
        private readonly object autostartLock = new();
        private UserInstaller? installer;

        public DaemonService(DaemonOptions options, DaemonExitCode exitCode, IHostApplicationLifetime lifetime,
            IWallpaperService wallpaperService, IDisplayManager displayManager, ISettingsService settingsService, VideoImporter importer,
            WebImporter webImporter)
        {
            this.options = options;
            this.exitCode = exitCode;
            this.lifetime = lifetime;
            this.wallpaperService = wallpaperService;
            this.displayManager = displayManager;
            this.settingsService = settingsService;
            this.importer = importer;
            this.webImporter = webImporter;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var address = DBusAddress.Session;
            if (string.IsNullOrEmpty(address))
            {
                Fail(DaemonExitCode.NoSessionBus, "No D-Bus session bus (DBUS_SESSION_BUS_ADDRESS is not set).");
                return;
            }

            connection = new DBusConnection(address);
            try
            {
                await connection.ConnectAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is DBusExceptionBase or IOException or System.Net.Sockets.SocketException)
            {
                Fail(DaemonExitCode.NoSessionBus, $"Cannot connect to the session bus {address}: {e.Message}");
                return;
            }

            server = new CoreDBusServer(wallpaperService, displayManager, settingsService, importer, webImporter, Quit);
            if (!await server.StartAsync(connection, Constants.DBus.DaemonBusName).ConfigureAwait(false))
            {
                server = null;
                Fail(DaemonExitCode.AlreadyRunning, $"lively-daemon is already running ({Constants.DBus.DaemonBusName} is owned).");
                return;
            }

            settingsService.SettingsChanged += OnSettingsChanged;
            ApplyAutostart(settingsService.Settings.Startup);

            displayManager.Start();
            Logger.Info("Displays: " + string.Join("; ", displayManager.Displays));
            if (options.NoRestore)
            {
                Logger.Info("Not restoring the wallpaper layout (--no-restore).");
            }
            else
            {
                restoreTask = Task.Run(RestoreAsync);
            }
            _ = WatchDisconnectAsync(connection);
            Logger.Info($"lively-daemon {Environment.ProcessId} ready.");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            stopping.Cancel();
            if (server == null)
            {
                connection?.Dispose();
                return;
            }

            Logger.Info("Stopping.");
            settingsService.SettingsChanged -= OnSettingsChanged;
            if (restoreTask != null)
            {
                await restoreTask.ConfigureAwait(false);
            }
            try
            {
                await wallpaperService.ShutdownAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Error("Closing the wallpapers failed: " + e);
            }
            await server.DisposeAsync().ConfigureAwait(false);
            connection?.Dispose();
            Logger.Info("Stopped.");
        }

        private async Task RestoreAsync()
        {
            try
            {
                await wallpaperService.RestoreWallpaperAsync(stopping.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                Logger.Error("Restoring the wallpapers failed: " + e);
            }
        }

        private void OnSettingsChanged(object? sender, SettingsModel settings) => ApplyAutostart(settings.Startup);

        /// <summary>Writes or removes the autostart entry; a failure is logged, never fatal.</summary>
        private void ApplyAutostart(bool enabled)
        {
            lock (autostartLock)
            {
                try
                {
                    installer ??= new UserInstaller(Constants.CommonPaths.XdgDataHome, Constants.CommonPaths.XdgConfigHome,
                        Path.Combine(AppContext.BaseDirectory, "data"), PlayerExecutableResolver.Resolve("lively-daemon", []), ui: null);
                    installer.SetAutostart(enabled);
                }
                catch (Exception e)
                {
                    Logger.Error($"Cannot {(enabled ? "write" : "remove")} the autostart entry: {e.Message}");
                }
            }
        }

        private async Task WatchDisconnectAsync(DBusConnection c)
        {
            var reason = await c.DisconnectedAsync().ConfigureAwait(false);
            if (stopping.IsCancellationRequested)
            {
                return;
            }
            Logger.Error("Lost the session bus connection: " + reason?.Message);
            exitCode.Value = DaemonExitCode.Failure;
            lifetime.StopApplication();
        }

        private void Quit()
        {
            Logger.Info("Quit requested over D-Bus.");
            lifetime.StopApplication();
        }

        private void Fail(int code, string message)
        {
            Logger.Error(message);
            exitCode.Value = code;
            lifetime.StopApplication();
        }
    }
}
