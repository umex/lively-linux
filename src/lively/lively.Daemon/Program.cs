using lively.Common;
using lively.Common.Helpers.Storage;
using lively.Core;
using lively.Core.Logging;
using lively.Core.Wallpapers;
using lively.Ipc;
using lively.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace lively.Daemon
{
    internal static class Program
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public static async Task<int> Main(string[] args)
        {
            DaemonOptions options;
            try
            {
                options = DaemonOptions.Parse(args);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine("lively-daemon: " + e.Message);
                Console.Error.WriteLine(DaemonOptions.Usage);
                return DaemonExitCode.Failure;
            }
            if (options.ShowHelp)
            {
                Console.WriteLine(DaemonOptions.Usage);
                return DaemonExitCode.Success;
            }
            if (options.InstallUser || options.UninstallUser)
            {
                return await RunUserInstallerAsync(options.InstallUser).ConfigureAwait(false);
            }

            LogSetup.Configure("daemon.log", console: true);
            try
            {
                // No args: the command-line configuration provider rejects switches without a value.
                var builder = Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                builder.Logging.AddNLog();
                builder.Services.Configure<ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true);
                builder.Services.AddSingleton(options);
                var exitCode = new DaemonExitCode();
                builder.Services.AddSingleton(exitCode);
                builder.Services.AddLivelyCore(o => o.FakeDisplays = options.Displays);
                builder.Services.AddHostedService<DaemonService>();

                using var host = builder.Build();
                await host.RunAsync().ConfigureAwait(false);
                return exitCode.Value;
            }
            catch (Exception e)
            {
                Logger.Fatal(e, "lively-daemon failed.");
                return DaemonExitCode.Failure;
            }
            finally
            {
                NLog.LogManager.Shutdown();
            }
        }

        /// <summary>--install-user / --uninstall-user: no host, no log file, output on the console.</summary>
        private static async Task<int> RunUserInstallerAsync(bool install)
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var daemon = PlayerExecutableResolver.Resolve("lively-daemon", []);
                PlayerLaunchInfo? ui = null;
                try
                {
                    ui = PlayerExecutableResolver.Resolve("lively", []);
                }
                catch (FileNotFoundException)
                {
                    if (install)
                    {
                        Console.WriteLine($"The UI (lively) is not in {baseDir}: skipping the application launcher. " +
                            "Run lively-daemon --install-user from the UI's output directory to install it.");
                    }
                }
                var installer = new UserInstaller(Constants.CommonPaths.XdgDataHome, Constants.CommonPaths.XdgConfigHome,
                    Path.Combine(baseDir, "data"), daemon, ui);

                if (install)
                {
                    foreach (var path in installer.Install())
                    {
                        Console.WriteLine("Wrote " + path);
                    }
                    // Keep an existing autostart entry pointing at this build; a running daemon also applies the
                    // setting on start and on every change.
                    if (ReadStartupSetting() is { } startup)
                    {
                        var autostart = installer.SetAutostart(startup);
                        if (autostart != null)
                        {
                            Console.WriteLine("Wrote " + autostart);
                        }
                    }
                }
                else
                {
                    var removed = installer.Uninstall();
                    foreach (var path in removed)
                    {
                        Console.WriteLine("Removed " + path);
                    }
                    if (removed.Count == 0)
                    {
                        Console.WriteLine("Nothing to remove.");
                    }
                }
                await ReloadBusConfigAsync().ConfigureAwait(false);
                return DaemonExitCode.Success;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("lively-daemon: " + e.Message);
                return DaemonExitCode.Failure;
            }
        }

        /// <summary>The Startup setting from an existing settings file, without creating or repairing it.</summary>
        private static bool? ReadStartupSetting()
        {
            var path = Constants.CommonPaths.UserSettingsPath;
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                return JsonStorage<SettingsModel>.LoadData(path).Startup;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"lively-daemon: cannot read {path}, leaving the autostart entry as it is: {e.Message}");
                return null;
            }
        }

        /// <summary>Best effort: makes the session bus see the new activation file at once.</summary>
        private static async Task ReloadBusConfigAsync()
        {
            var address = DBusAddress.Session;
            if (string.IsNullOrEmpty(address))
            {
                return;
            }
            try
            {
                using var connection = new DBusConnection(address);
                await connection.ConnectAsync().ConfigureAwait(false);
                await DBusActivation.ReloadConfigAsync(connection).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("lively-daemon: could not ask the session bus to reload its configuration: " + e.Message);
            }
        }
    }
}
