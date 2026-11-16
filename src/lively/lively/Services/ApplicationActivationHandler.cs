using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using lively.Common;
using lively.DBus;
using Tmds.DBus.Protocol;

namespace lively.Services
{
    /// <summary>
    /// org.freedesktop.Application on <see cref="Constants.DBus.UiObjectPath"/>: a second launch of the UI
    /// calls Activate, which brings the existing main window to the front.
    /// </summary>
    /// <remarks>Construct on the UI thread: method calls are dispatched to the SynchronizationContext captured here.</remarks>
    internal sealed class ApplicationActivationHandler : DBusHandler, IApplicationHandler
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly Func<Window?> getWindow;

        public ApplicationActivationHandler(DBusConnection connection, Func<Window?> getWindow)
            : base(connection, Constants.DBus.UiObjectPath, handlesChildPaths: false, handleOnCapturedContext: true)
        {
            this.getWindow = getWindow;
        }

        public ValueTask ActivateAsync(Dictionary<string, VariantValue> platformData)
        {
            Logger.Info("Activated by another instance.");
            ShowWindow();
            return ValueTask.CompletedTask;
        }

        public ValueTask OpenAsync(string[] uris, Dictionary<string, VariantValue> platformData)
        {
            Logger.Info("Open requested (not supported yet): " + string.Join(", ", uris));
            ShowWindow();
            return ValueTask.CompletedTask;
        }

        public ValueTask ActivateActionAsync(string actionName, VariantValue[] parameter, Dictionary<string, VariantValue> platformData)
        {
            Logger.Info($"Action \"{actionName}\" requested (no actions yet).");
            ShowWindow();
            return ValueTask.CompletedTask;
        }

        private void ShowWindow()
        {
            var window = getWindow();
            if (window == null)
            {
                return;
            }
            window.Show();
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
        }
    }
}
