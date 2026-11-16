using lively.Ipc;
using lively.Models;

namespace lively.Tests.Fakes;

/// <summary>In-memory <see cref="ICoreClient"/> that records calls; events are raised by the test.</summary>
public sealed class FakeCoreClient : ICoreClient
{
    public List<DisplayMonitor> Displays { get; set; } = [];
    public List<RunningWallpaper> Running { get; } = [];
    public List<(string Method, string Arg1, string Arg2)> Calls { get; } = [];

    /// <summary>When set, every call except GetDisplays/GetWallpapers throws it.</summary>
    public Exception? CallError { get; set; }

    public bool Connected { get; private set; }

    public event EventHandler<CoreWallpaperChangedEventArgs>? WallpaperChanged;
    public event EventHandler? DisplaysChanged;
    public event EventHandler<CoreErrorEventArgs>? Error;

    public void RaiseWallpaperChanged(string display, string? folder) => WallpaperChanged?.Invoke(this, new CoreWallpaperChangedEventArgs(display, folder));
    public void RaiseDisplaysChanged() => DisplaysChanged?.Invoke(this, EventArgs.Empty);
    public void RaiseError(string display, string message) => Error?.Invoke(this, new CoreErrorEventArgs(display, message));

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

    public Task QuitAsync() => Record("Quit");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task Record(string method, string arg1 = "", string arg2 = "")
    {
        Calls.Add((method, arg1, arg2));
        return CallError != null ? Task.FromException(CallError) : Task.CompletedTask;
    }
}
