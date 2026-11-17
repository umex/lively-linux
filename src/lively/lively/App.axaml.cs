using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using lively.Common;
using lively.Core.Library;
using lively.Helpers;
using lively.Ipc;
using lively.Services;
using Microsoft.Extensions.DependencyInjection;
using Tmds.DBus.Protocol;

namespace lively
{
    public class App : Application
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>Time lively-daemon has to take its bus name after it was started.</summary>
        private static readonly TimeSpan DaemonStartTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Application services, available once framework initialization has completed.
        /// </summary>
        public static IServiceProvider Services { get; private set; } = null!;

        private ApplicationActivationHandler? activationHandler;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Directory.CreateDirectory(Path.Combine(Constants.CommonPaths.LibraryDir, "wallpapers"));
            var connection = Program.Instance?.Connection;
            Services = ConfigureServices(connection);

            IReadOnlyList<string> startupFiles = [];
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = Services.GetRequiredService<MainWindow>();
                if (connection != null)
                {
                    // Created here, on the UI thread, so Activate and Open run on Avalonia's dispatcher.
                    var library = Services.GetRequiredService<LibraryViewModel>();
                    activationHandler = new ApplicationActivationHandler(connection, () => desktop.MainWindow,
                        paths => library.ImportVideosAsync(paths));
                    connection.AddMethodHandler(activationHandler);
                }
                desktop.Exit += (_, _) => OnExit();
                startupFiles = FileUris.CommandLinePaths(desktop.Args ?? []);
            }

            base.OnFrameworkInitializationCompleted();
            _ = InitializeCoreAsync(connection, startupFiles);
        }

        private static IServiceProvider ConfigureServices(DBusConnection? connection)
        {
            var services = new ServiceCollection();
            if (connection != null)
            {
                services.AddSingleton(connection);
                services.AddSingleton<ICoreClient>(_ => new CoreClient(connection));
            }
            services.AddSingleton(sp => new LibraryViewModel(
                sp.GetService<ICoreClient>(),
                LibraryScanner.DefaultScanFolders,
                action => Dispatcher.UIThread.Post(action)));
            services.AddTransient<MainWindow>();
            return services.BuildServiceProvider();
        }

        /// <param name="startupFiles">Video files from the command line, imported once the core is reachable.</param>
        private static async Task InitializeCoreAsync(DBusConnection? connection, IReadOnlyList<string> startupFiles)
        {
            var library = Services.GetRequiredService<LibraryViewModel>();
            try
            {
                if (connection != null && !await DaemonLauncher.EnsureRunningAsync(connection, DaemonStartTimeout))
                {
                    library.SetCoreUnavailable("lively-daemon could not be started; see " + Constants.CommonPaths.LogDir + ".");
                    return;
                }
                await library.InitializeAsync();
                if (library.IsCoreAvailable && startupFiles.Count > 0)
                {
                    await library.ImportVideosAsync(startupFiles);
                }
            }
            catch (Exception e)
            {
                Logger.Error("Core initialization failed: " + e);
                library.SetCoreUnavailable("lively-daemon is not reachable: " + e.Message);
            }
        }

        private void OnExit()
        {
            if (activationHandler != null)
            {
                activationHandler.Connection.RemoveMethodHandler(activationHandler.Path);
            }
            if (Services.GetService<ICoreClient>() is { } client)
            {
                client.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            }
        }
    }
}
