using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using lively.Common;
using lively.Core.Library;
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

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = Services.GetRequiredService<MainWindow>();
                if (connection != null)
                {
                    // Created here, on the UI thread, so Activate runs on Avalonia's dispatcher.
                    activationHandler = new ApplicationActivationHandler(connection, () => desktop.MainWindow);
                    connection.AddMethodHandler(activationHandler);
                }
                desktop.Exit += (_, _) => OnExit();
            }

            base.OnFrameworkInitializationCompleted();
            _ = InitializeCoreAsync(connection);
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

        private static async Task InitializeCoreAsync(DBusConnection? connection)
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
