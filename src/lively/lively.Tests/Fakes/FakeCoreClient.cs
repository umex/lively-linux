using lively.Ipc;
using lively.Models;

namespace lively.Tests.Fakes;

/// <summary>In-memory <see cref="ICoreClient"/> that records calls; events are raised by the test.</summary>
public sealed class FakeCoreClient : ICoreClient
{
    public List<DisplayMonitor> Displays { get; set; } = [];
    public List<RunningWallpaper> Running { get; } = [];
    public List<(string Method, string Arg1, string Arg2)> Calls { get; } = [];

    /// <summary>When set, every recorded call (the ones in <see cref="Calls"/>) throws it.</summary>
    public Exception? CallError { get; set; }

    public bool Connected { get; private set; }

    /// <summary>Returned by GetPausedAsync.</summary>
    public bool Paused { get; set; }

    public event EventHandler<CoreWallpaperChangedEventArgs>? WallpaperChanged;
    public event EventHandler? DisplaysChanged;
    public event EventHandler<CoreErrorEventArgs>? Error;
    public event EventHandler<bool>? PausedChanged;
    public event EventHandler<CoreWallpaperUpdatedEventArgs>? WallpaperUpdated;

    public void RaiseWallpaperChanged(string display, string? folder) => WallpaperChanged?.Invoke(this, new CoreWallpaperChangedEventArgs(display, folder));
    public void RaiseDisplaysChanged() => DisplaysChanged?.Invoke(this, EventArgs.Empty);
    public void RaiseError(string display, string message) => Error?.Invoke(this, new CoreErrorEventArgs(display, message));
    public void RaisePausedChanged(bool paused) => PausedChanged?.Invoke(this, paused);
    public void RaiseWallpaperUpdated(string folder) => WallpaperUpdated?.Invoke(this, new CoreWallpaperUpdatedEventArgs(folder));

    public Task ConnectAsync(CancellationToken ct = default)
    {
        Connected = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DisplayMonitor>> GetDisplaysAsync() => Task.FromResult<IReadOnlyList<DisplayMonitor>>(Displays.ToList());

    public Task<IReadOnlyList<RunningWallpaper>> GetWallpapersAsync() => Task.FromResult<IReadOnlyList<RunningWallpaper>>(Running.ToList());

    public Task SetWallpaperAsync(string folder, string displayId) => Record("SetWallpaper", folder, displayId);

    public Task CloseWallpaperAsync(string displayId) => Record("CloseWallpaper", displayId);

    public Task CloseAllWallpapersAsync() => Record("CloseAllWallpapers");

    public Task<SettingsModel> GetSettingsAsync() => Task.FromResult(new SettingsModel());

    public Task SetSettingsAsync(SettingsModel settings) => Record("SetSettings");

    public Task SetPausedAsync(bool paused) => Record("SetPaused", paused.ToString());

    public Task<bool> GetPausedAsync() => Task.FromResult(Paused);

    /// <summary>Returned by ImportVideoAsync, ImportWebAsync and AddUrlAsync.</summary>
    public string ImportResultFolder { get; set; } = "";

    public async Task<string> ImportVideoAsync(string path)
    {
        await Record("ImportVideo", path);
        return ImportResultFolder;
    }

    public async Task<string> ImportWebAsync(string path)
    {
        await Record("ImportWeb", path);
        return ImportResultFolder;
    }

    public async Task<string> AddUrlAsync(string url)
    {
        await Record("AddUrl", url);
        return ImportResultFolder;
    }

    public Task QuitAsync() => Record("Quit");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task Record(string method, string arg1 = "", string arg2 = "")
    {
        Calls.Add((method, arg1, arg2));
        return CallError != null ? Task.FromException(CallError) : Task.CompletedTask;
    }
}
