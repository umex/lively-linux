using lively.Core.Desktop;
using lively.Core.Display;
using lively.Core.Settings;
using lively.Core.Wallpapers;
using Microsoft.Extensions.DependencyInjection;

namespace lively.Core
{
    public static class ServiceCollectionExtensions
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>Display used when the session is neither Wayland nor configured with fake displays.</summary>
        public const string FallbackDisplaySpec = "default:1920x1080@0,0";

        /// <summary>
        /// Registers the core services as singletons: <see cref="CoreOptions"/>, <see cref="ISettingsService"/>,
        /// <see cref="IWallpaperLayoutStore"/>, <see cref="DesktopSession"/>, <see cref="IDisplayManager"/>,
        /// <see cref="IDesktopBackend"/>, <see cref="IWallpaperPlayerFactory"/> and <see cref="IWallpaperService"/>.
        /// The display manager is not started; the host calls <see cref="IDisplayManager.Start"/>.
        /// </summary>
        public static IServiceCollection AddLivelyCore(this IServiceCollection services, Action<CoreOptions>? configure = null)
        {
            var options = new CoreOptions();
            configure?.Invoke(options);

            services.AddSingleton(options);
            services.AddSingleton<ISettingsService>(_ => new JsonSettingsService(options.SettingsPath));
            services.AddSingleton<IWallpaperLayoutStore>(_ => new JsonWallpaperLayoutStore(options.LayoutPath));
            services.AddSingleton(_ => DesktopSessionDetector.Detect());
            services.AddSingleton(sp => CreateDisplayManager(options, sp.GetRequiredService<DesktopSession>()));
            services.AddSingleton(sp => CreateDesktopBackend(sp.GetRequiredService<DesktopSession>()));
            services.AddSingleton<IWallpaperPlayerFactory, WallpaperPlayerFactory>();
            services.AddSingleton<IWallpaperService, WallpaperService>();
            return services;
        }

        private static IDisplayManager CreateDisplayManager(CoreOptions options, DesktopSession session)
        {
            if (!string.IsNullOrWhiteSpace(options.FakeDisplays))
            {
                Logger.Info("Using fake displays: " + options.FakeDisplays);
                return new FakeDisplayManager(options.FakeDisplays);
            }
            if (session.Type == SessionType.Wayland)
            {
                return new WaylandDisplayManager(options.DisplayPollInterval);
            }
            Logger.Warn($"No display enumeration for session {session}, using a single fake display {FallbackDisplaySpec}.");
            return new FakeDisplayManager(FallbackDisplaySpec);
        }

        private static IDesktopBackend CreateDesktopBackend(DesktopSession session)
        {
            IDesktopBackend backend = session.Type == SessionType.Wayland
                ? new WaylandDesktopBackend(session)
                : new NullDesktopBackend(session);
            Logger.Info($"Desktop session {session}, backend {backend.Name} (places wallpapers: {backend.CanPlaceWallpapers}).");
            return backend;
        }
    }
}
