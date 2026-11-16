using System;
using Avalonia;
using lively.Core.Logging;
using lively.Services;

namespace lively
{
    class Program
    {
        /// <summary>Single-instance lock and session bus connection, set before Avalonia starts.</summary>
        internal static SingleInstance? Instance { get; private set; }

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        public static int Main(string[] args)
        {
            LogSetup.Configure("ui.log", console: true);
            using var instance = SingleInstance.Acquire();
            if (!instance.IsPrimary)
            {
                instance.ActivatePrimary(TimeSpan.FromSeconds(5));
                return 0;
            }

            Instance = instance;
            try
            {
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                NLog.LogManager.Shutdown();
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
