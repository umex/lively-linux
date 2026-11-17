using System.Threading.Channels;
using lively.Common;
using lively.Ipc.DBus;
using lively.Models;
using Newtonsoft.Json;
using Tmds.DBus.Protocol;

namespace lively.Ipc
{
    /// <summary>
    /// <see cref="ICoreClient"/> over a <see cref="DBusConnection"/>. The connection is owned by the caller.
    /// </summary>
    public sealed class CoreClient : ICoreClient
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly Core1 proxy;
        private readonly List<IDisposable> watchers = [];
        // Signals are raised one after another on a background task so that handlers keep the signal order
        // and never run on the connection's receive loop.
        private readonly Channel<Action> events = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
        private readonly Task eventLoop;
        private bool connected;
        private bool disposed;

        public CoreClient(DBusConnection connection, string busName = Constants.DBus.DaemonBusName)
        {
            ArgumentNullException.ThrowIfNull(connection);
            proxy = new Core1(connection, busName, Constants.DBus.CoreObjectPath);
            eventLoop = Task.Run(RunEventsAsync);
        }

        public event EventHandler<CoreWallpaperChangedEventArgs>? WallpaperChanged;

        public event EventHandler? DisplaysChanged;

        public event EventHandler<CoreErrorEventArgs>? Error;

        public event EventHandler<bool>? PausedChanged;

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (connected)
            {
                return;
            }
            watchers.Add(await proxy.WatchWallpaperChangedAsync(
                e => Post(() => WallpaperChanged?.Invoke(this, new CoreWallpaperChangedEventArgs(e.Display, e.Folder.Length == 0 ? null : e.Folder))),
                emitOnCapturedContext: false).ConfigureAwait(false));
            ct.ThrowIfCancellationRequested();
            watchers.Add(await proxy.WatchDisplaysChangedAsync(
                () => Post(() => DisplaysChanged?.Invoke(this, EventArgs.Empty)),
                emitOnCapturedContext: false).ConfigureAwait(false));
            ct.ThrowIfCancellationRequested();
            watchers.Add(await proxy.WatchErrorAsync(
                e => Post(() => Error?.Invoke(this, new CoreErrorEventArgs(e.Display, e.Message))),
                emitOnCapturedContext: false).ConfigureAwait(false));
            ct.ThrowIfCancellationRequested();
            watchers.Add(await proxy.WatchPausedChangedAsync(
                paused => Post(() => PausedChanged?.Invoke(this, paused)),
                emitOnCapturedContext: false).ConfigureAwait(false));
            connected = true;
        }

        public async Task<IReadOnlyList<DisplayMonitor>> GetDisplaysAsync()
        {
            var wire = await proxy.GetDisplaysAsync().ConfigureAwait(false);
            return wire.Select(x => DisplayMonitorWire.FromWire(x)).ToList();
        }

        public async Task<IReadOnlyList<RunningWallpaper>> GetWallpapersAsync()
        {
            var wire = await proxy.GetWallpapersAsync().ConfigureAwait(false);
            return wire.Select(x => new RunningWallpaper(x.Item1, x.Item2)).ToList();
        }

        public Task SetWallpaperAsync(string folder, string displayId)
        {
            ArgumentNullException.ThrowIfNull(folder);
            ArgumentNullException.ThrowIfNull(displayId);
            return proxy.SetWallpaperAsync(folder, displayId);
        }

        public Task CloseWallpaperAsync(string displayId)
        {
            ArgumentNullException.ThrowIfNull(displayId);
            return proxy.CloseWallpaperAsync(displayId);
        }

        public Task CloseAllWallpapersAsync() => proxy.CloseAllWallpapersAsync();

        public async Task<SettingsModel> GetSettingsAsync()
        {
            var json = await proxy.GetSettingsAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeObject<SettingsModel>(json)
                ?? throw new InvalidDataException("The core returned empty settings.");
        }

        public Task SetSettingsAsync(SettingsModel settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return proxy.SetSettingsAsync(JsonConvert.SerializeObject(settings, Formatting.None));
        }

        public Task SetPausedAsync(bool paused) => proxy.SetPausedAsync(paused);

        public Task<bool> GetPausedAsync() => proxy.GetPausedAsync();

        public Task<string> ImportVideoAsync(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            return proxy.ImportVideoAsync(path);
        }

        public Task QuitAsync() => proxy.QuitAsync();

        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            foreach (var watcher in watchers)
            {
                watcher.Dispose();
            }
            watchers.Clear();
            events.Writer.TryComplete();
            await eventLoop.ConfigureAwait(false);
        }

        private void Post(Action action)
        {
            events.Writer.TryWrite(action);
        }

        private async Task RunEventsAsync()
        {
            await foreach (var action in events.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Logger.Error("Core signal handler failed: " + e);
                }
            }
        }
    }
}
