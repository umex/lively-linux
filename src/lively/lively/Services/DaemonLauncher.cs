using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using lively.Common;
using lively.Core.Wallpapers;
using Tmds.DBus.Protocol;

namespace lively.Services
{
    /// <summary>Starts lively-daemon (deployed next to the UI) unless it already owns its bus name.</summary>
    internal static class DaemonLauncher
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public const string DaemonName = "lively-daemon";

        /// <returns>True once the daemon owns <see cref="Constants.DBus.DaemonBusName"/>.</returns>
        public static async Task<bool> EnsureRunningAsync(DBusConnection connection, TimeSpan timeout)
        {
            using var watcher = await connection.WatchNameOwnerAsync(Constants.DBus.DaemonBusName).ConfigureAwait(false);
            if (watcher.GetCurrentOwner() != null)
            {
                Logger.Info("lively-daemon is running.");
                return true;
            }

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
