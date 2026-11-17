using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using lively.Common;
using lively.Core.Wallpapers;
using lively.Ipc;
using Tmds.DBus.Protocol;

namespace lively.Services
{
    /// <summary>
    /// Makes sure lively-daemon owns its bus name: by D-Bus activation when an activation file is installed
    /// (lively-daemon --install-user), else by starting the lively-daemon deployed next to the UI.
    /// </summary>
    internal static class DaemonLauncher
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public const string DaemonName = "lively-daemon";

        /// <summary>After a failed activation, how long a daemon that is starting anyway may take to show up.</summary>
        private static readonly TimeSpan ActivationGrace = TimeSpan.FromSeconds(2);

        /// <returns>True once the daemon owns <see cref="Constants.DBus.DaemonBusName"/>.</returns>
        public static async Task<bool> EnsureRunningAsync(DBusConnection connection, TimeSpan timeout)
        {
            using var watcher = await connection.WatchNameOwnerAsync(Constants.DBus.DaemonBusName).ConfigureAwait(false);
            if (watcher.GetCurrentOwner() != null)
            {
                Logger.Info("lively-daemon is running.");
                return true;
            }

            using (var cts = new CancellationTokenSource(timeout))
            {
                try
                {
                    // The bus replies once the activated daemon owns the name, or when it failed to start.
                    var result = await DBusActivation.StartServiceByNameAsync(connection, Constants.DBus.DaemonBusName)
                        .WaitAsync(cts.Token).ConfigureAwait(false);
                    await watcher.WaitForOwnerAsync(cts.Token).ConfigureAwait(false);
                    Logger.Info(result == DBusActivation.StartReplyAlreadyRunning
                        ? "lively-daemon is running."
                        : "lively-daemon started by D-Bus activation.");
                    return true;
                }
                catch (DBusErrorReplyException e) when (e.ErrorName == DBusActivation.ServiceUnknown)
                {
                    Logger.Info($"No activation file, starting lively-daemon from {AppContext.BaseDirectory}");
                }
                catch (DBusExceptionBase e)
                {
                    // For example a stale activation file whose Exec= no longer exists.
                    Logger.Error("D-Bus activation of lively-daemon failed: " + e.Message);
                    if (await WaitForOwnerAsync(watcher, ActivationGrace).ConfigureAwait(false))
                    {
                        Logger.Info("lively-daemon is running.");
                        return true;
                    }
                    Logger.Info($"Starting lively-daemon from {AppContext.BaseDirectory}");
                }
                catch (OperationCanceledException)
                {
                    Logger.Error($"lively-daemon was not activated within {timeout.TotalSeconds:0} s.");
                    return false;
                }
            }

            return await SpawnAsync(watcher, timeout).ConfigureAwait(false);
        }

        private static async Task<bool> WaitForOwnerAsync(NameOwnerWatcher watcher, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await watcher.WaitForOwnerAsync(cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>Starts the lively-daemon next to the UI (development builds without --install-user).</summary>
        private static async Task<bool> SpawnAsync(NameOwnerWatcher watcher, TimeSpan timeout)
        {
            Process? process;
            try
            {
                var launch = PlayerExecutableResolver.Resolve(DaemonName, []);
                // No stream redirection: the daemon logs to its own file and to the inherited console,
                // and must not die from a closed pipe when the UI exits.
                var psi = new ProcessStartInfo(launch.FileName) { UseShellExecute = false };
                foreach (var arg in launch.Arguments)
                {
                    psi.ArgumentList.Add(arg);
                }
                Logger.Info("Starting " + launch);
                process = Process.Start(psi);
            }
            catch (Exception e)
            {
                Logger.Error("Cannot start lively-daemon: " + e.Message);
                return false;
            }
            if (process == null)
            {
                return false;
            }

            using (process)
            using (var cts = new CancellationTokenSource(timeout))
            {
                var owner = watcher.WaitForOwnerAsync(cts.Token);
                var exited = process.WaitForExitAsync(cts.Token);
                try
                {
                    if (await Task.WhenAny(owner, exited).ConfigureAwait(false) == exited && !owner.IsCompleted)
                    {
                        await exited.ConfigureAwait(false);
                        // Exit code 2: another daemon won the race and owns the name.
                        if (process.ExitCode != 2)
                        {
                            Logger.Error($"lively-daemon exited with code {process.ExitCode}.");
                            return false;
                        }
                    }
                    await owner.ConfigureAwait(false);
                    Logger.Info("lively-daemon started.");
                    return true;
                }
                catch (OperationCanceledException)
                {
                    Logger.Error($"lively-daemon did not take its bus name within {timeout.TotalSeconds:0} s.");
                    return false;
                }
            }
        }
    }
}
