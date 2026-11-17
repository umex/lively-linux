using lively.Core.Desktop;
using lively.Core.Display;
using lively.Core.Display.Wayland;
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
            services.AddSingleton(_ => new WaylandStartupSnapshot());
            services.AddSingleton(sp => CreateDisplayManager(options, sp.GetRequiredService<DesktopSession>(), sp.GetRequiredService<WaylandStartupSnapshot>()));
            services.AddSingleton(sp => CreateDesktopBackend(options, sp.GetRequiredService<DesktopSession>(), sp.GetRequiredService<WaylandStartupSnapshot>()));
            services.AddSingleton<IWallpaperPlayerFactory, WallpaperPlayerFactory>();
            services.AddSingleton<IWallpaperService, WallpaperService>();
            // Video import into the library (needs ffmpeg/ffprobe on PATH at import time).
            services.AddSingleton(_ => new Library.VideoImporter(options.WallpapersDirectory));
            return services;
        }

        private static IDisplayManager CreateDisplayManager(CoreOptions options, DesktopSession session, WaylandStartupSnapshot startup)
        {
            if (!string.IsNullOrWhiteSpace(options.FakeDisplays))
            {
                Logger.Info("Using fake displays: " + options.FakeDisplays);
                return new FakeDisplayManager(options.FakeDisplays);
            }
            if (session.Type == SessionType.Wayland)
            {
                // The first enumeration reuses the snapshot taken for the backend probe; later polls connect again.
                var shared = 1;
                return new WaylandDisplayManager(options.DisplayPollInterval, () =>
                    Interlocked.Exchange(ref shared, 0) == 1 && startup.Value is { } snapshot
                        ? snapshot.Displays
                        : WaylandOutputEnumerator.Enumerate());
            }
            Logger.Warn($"No display enumeration for session {session}, using a single fake display {FallbackDisplaySpec}.");
            return new FakeDisplayManager(FallbackDisplaySpec);
        }

        private static IDesktopBackend CreateDesktopBackend(CoreOptions options, DesktopSession session, WaylandStartupSnapshot startup)
        {
            IDesktopBackend backend;
            if (!string.IsNullOrWhiteSpace(options.FakeDisplays))
            {
                // Fake displays cannot be placed on real outputs; this keeps tests and --displays runs on the dummy player.
                backend = new NullDesktopBackend(session);
            }
            else if (session.Type == SessionType.Wayland)
            {
                var hasLayerShell = startup.Value?.Globals.Contains("zwlr_layer_shell_v1") ?? false;
                backend = new WaylandDesktopBackend(session, hasLayerShell);
            }
            else
            {
                backend = new NullDesktopBackend(session);
            }
            Logger.Info($"Desktop session {session}, backend {backend.Name} (places wallpapers: {backend.CanPlaceWallpapers}).");
            return backend;
        }

        /// <summary>
        /// One <see cref="WaylandOutputEnumerator.Snapshot"/> taken on first use and shared by the desktop backend
        /// (layer-shell probe) and the display manager's first enumeration, so start-up connects only once.
        /// Never throws: a failed snapshot is logged and gives null.
        /// </summary>
        private sealed class WaylandStartupSnapshot
        {
            private readonly Lazy<WaylandSnapshot?> snapshot = new(Take);

            public WaylandSnapshot? Value => snapshot.Value;

            private static WaylandSnapshot? Take()
            {
                try
                {
                    return WaylandOutputEnumerator.Snapshot();
                }
                catch (Exception e)
                {
                    Logger.Warn("Wayland probe failed, assuming no wlr-layer-shell: " + e.Message);
                    return null;
                }
            }
        }
    }
}
