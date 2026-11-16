using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using lively.Common;
using Microsoft.Extensions.DependencyInjection;

namespace lively
{
    public partial class App : Application
    {
        /// <summary>
        /// Application services, available once framework initialization has completed.
        /// </summary>
        public static IServiceProvider Services { get; private set; } = null!;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Directory.CreateDirectory(Path.Combine(Constants.CommonPaths.LibraryDir, "wallpapers"));
            Services = ConfigureServices();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = Services.GetRequiredService<MainWindow>();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<LibraryViewModel>();
            services.AddTransient<MainWindow>();
            return services.BuildServiceProvider();
        }
    }
}
