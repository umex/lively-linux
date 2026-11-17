using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using lively.Common;
using lively.DBus;
using lively.Helpers;
using Tmds.DBus.Protocol;

namespace lively.Services
{
    /// <summary>
    /// org.freedesktop.Application on <see cref="Constants.DBus.UiObjectPath"/>: a second launch of the UI
    /// calls Activate, which brings the existing main window to the front, or Open with file:// URIs of
    /// videos to import.
    /// </summary>
    /// <remarks>Construct on the UI thread: method calls are dispatched to the SynchronizationContext captured here.</remarks>
    internal sealed class ApplicationActivationHandler : DBusHandler, IApplicationHandler
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly Func<Window?> getWindow;
        private readonly Func<IReadOnlyList<string>, Task> openFiles;

        /// <param name="openFiles">Imports the local paths of an Open call; runs on the UI thread.</param>
        public ApplicationActivationHandler(DBusConnection connection, Func<Window?> getWindow, Func<IReadOnlyList<string>, Task> openFiles)
            : base(connection, Constants.DBus.UiObjectPath, handlesChildPaths: false, handleOnCapturedContext: true)
        {
            this.getWindow = getWindow;
            this.openFiles = openFiles;
        }

        public ValueTask ActivateAsync(Dictionary<string, VariantValue> platformData)
        {
            Logger.Info("Activated by another instance.");
            ShowWindow();
            return ValueTask.CompletedTask;
        }

        public ValueTask OpenAsync(string[] uris, Dictionary<string, VariantValue> platformData)
        {
            Logger.Info("Open requested: " + string.Join(", ", uris));
            var paths = FileUris.ToLocalPaths(uris);
            ShowWindow();
            if (paths.Count > 0)
            {
                // Not awaited: the reply goes out at once, an import can take longer than the caller waits.
                _ = OpenFilesAsync(paths);
            }
            return ValueTask.CompletedTask;
        }

        private async Task OpenFilesAsync(IReadOnlyList<string> paths)
        {
            try
            {
                await openFiles(paths);
            }
            catch (Exception e)
            {
                Logger.Error("Opening files failed: " + e);
            }
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
