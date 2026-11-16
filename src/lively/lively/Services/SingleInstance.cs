using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using lively.Common;
using Tmds.DBus.Protocol;

namespace lively.Services
{
    /// <summary>
    /// Single-instance lock of the UI: owning <see cref="Constants.DBus.UiBusName"/> on the session bus.
    /// The connection is kept for the whole run and also used to talk to lively-daemon.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private SingleInstance(DBusConnection? connection, bool isPrimary)
        {
            Connection = connection;
            IsPrimary = isPrimary;
        }

        /// <summary>Session bus connection, null when there is no session bus (degraded mode).</summary>
        public DBusConnection? Connection { get; }

        /// <summary>True when this process owns the UI bus name, or when there is no bus to ask.</summary>
        public bool IsPrimary { get; }

        /// <summary>Connects to the session bus and requests the UI bus name. Call before Avalonia starts.</summary>
        public static SingleInstance Acquire()
        {
            var address = DBusAddress.Session;
            if (string.IsNullOrEmpty(address))
            {
                Logger.Warn("No D-Bus session bus: running without single-instance handling and without lively-daemon.");
                return new SingleInstance(null, true);
            }

            var connection = new DBusConnection(address);
            try
            {
                connection.ConnectAsync().AsTask().GetAwaiter().GetResult();
                var owner = connection.TryRequestNameAsync(Constants.DBus.UiBusName, RequestNameOptions.None).GetAwaiter().GetResult();
                return new SingleInstance(connection, owner);
            }
            catch (Exception e)
            {
                Logger.Warn($"Cannot use the session bus {address}: {e.Message}. Running without single-instance handling.");
                connection.Dispose();
                return new SingleInstance(null, true);
            }
        }

        /// <summary>
        /// Asks the primary instance to show its window (org.freedesktop.Application.Activate),
        /// forwarding the activation token of this launch.
        /// </summary>
        public bool ActivatePrimary(TimeSpan timeout)
        {
            if (Connection == null)
            {
                return false;
            }
            var platformData = new Dictionary<string, VariantValue>();
            var token = Environment.GetEnvironmentVariable("XDG_ACTIVATION_TOKEN");
            if (!string.IsNullOrEmpty(token))
            {
                platformData["activation-token"] = token;
            }
            var startupId = Environment.GetEnvironmentVariable("DESKTOP_STARTUP_ID");
            if (!string.IsNullOrEmpty(startupId))
            {
                platformData["desktop-startup-id"] = startupId;
            }

            try
            {
                var app = new lively.Ipc.DBus.FreeDesktop.Application(Connection, Constants.DBus.UiBusName, Constants.DBus.UiObjectPath);
                app.ActivateAsync(platformData).WaitAsync(timeout).GetAwaiter().GetResult();
                Logger.Info("Lively is already running, activated the existing window.");
                return true;
            }
            catch (Exception e) when (e is DBusExceptionBase or TimeoutException)
            {
                Logger.Warn("Lively is already running but could not be activated: " + e.Message);
                return false;
            }
        }

        public void Dispose()
        {
            Connection?.Dispose();
        }
    }
}
