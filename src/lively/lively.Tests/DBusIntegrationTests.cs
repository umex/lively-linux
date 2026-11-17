using System.Collections.Concurrent;
using lively.Core;
using lively.Core.DBus;
using lively.Core.Desktop;
using lively.Core.Display;
using lively.Core.Settings;
using lively.Core.Wallpapers;
using lively.Ipc;
using lively.Ipc.DBus;
using lively.Models;
using lively.Tests.Fakes;
using Tmds.DBus.Protocol;

namespace lively.Tests;

/// <summary>
/// Core D-Bus server and client on two connections to the session bus, each test under its own bus name.
/// </summary>
public sealed class DBusIntegrationTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string busName = "io.github.lively_linux.Test.T" + Guid.NewGuid().ToString("N");
    private readonly TempDirectory tmp = new();
    private readonly FakeDisplayManager displays = new("DP-1:1920x1080@0,0;HDMI-A-1:2560x1440@1920,0");
    private readonly FakeWallpaperPlayerFactory fakeFactory = new();
    private readonly MemoryLayoutStore store = new();
    private readonly List<IAsyncDisposable> disposables = [];
    private readonly List<DBusConnection> connections = [];
    private readonly ConcurrentQueue<(string Display, string? Folder)> changed = new();
    private readonly ConcurrentQueue<(string Display, string Message)> errors = new();
    private int displaysChanged;
    private int quitCalls;
    private WallpaperService service = null!;
    private CoreClient client = null!;
    private string rain = null!;
    private string waves = null!;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var d in Enumerable.Reverse(disposables))
        {
            await d.DisposeAsync();
        }
        if (service != null)
        {
            await service.ShutdownAsync();
        }
        foreach (var c in connections)
        {
            c.Dispose();
        }
        displays.Dispose();
        tmp.Dispose();
    }

    private async Task<DBusConnection> ConnectAsync()
    {
        var connection = new DBusConnection(DBusAddress.Session!);
        connections.Add(connection);
        await connection.ConnectAsync();
        return connection;
    }

    private async Task<CoreDBusServer> StartServerAsync(IWallpaperService wallpaperService)
    {
        var server = new CoreDBusServer(wallpaperService, displays, new JsonSettingsService(tmp.Combine("config", "Settings.json")),
            () => Interlocked.Increment(ref quitCalls));
        disposables.Add(server);
        Assert.True(await server.StartAsync(await ConnectAsync(), busName));
        return server;
    }

    /// <summary>Starts the server (with fake players unless a factory is given) and a connected client.</summary>
    private async Task StartAsync(IWallpaperPlayerFactory? factory = null)
    {
        rain = WallpaperFixture.Create(tmp, "rain", "Rain");
        waves = WallpaperFixture.Create(tmp, "waves", "Waves");
        service = new WallpaperService(displays, factory ?? fakeFactory, store);
        await StartServerAsync(service);

        client = new CoreClient(await ConnectAsync(), busName);
        disposables.Add(client);
        client.WallpaperChanged += (_, e) => changed.Enqueue((e.DisplayId, e.FolderPath));
        client.Error += (_, e) => errors.Enqueue((e.DisplayId, e.Message));
        client.DisplaysChanged += (_, _) => Interlocked.Increment(ref displaysChanged);
        await client.ConnectAsync();
    }

    private static async Task<string> ErrorNameOf(Func<Task> call)
    {
        var e = await Assert.ThrowsAsync<DBusErrorReplyException>(call);
        return e.ErrorName;
    }

    [DBusFact]
    public async Task GetDisplays_ReturnsDisplaysWithOnePrimary()
    {
        await StartAsync();

        var result = await client.GetDisplaysAsync();

        Assert.Equal(["DP-1", "HDMI-A-1"], result.Select(x => x.Id));
        Assert.Equal([true, false], result.Select(x => x.IsPrimary));
        var hdmi = result[1];
        Assert.Equal((1920, 0, 2560, 1440, 1), (hdmi.X, hdmi.Y, hdmi.Width, hdmi.Height, hdmi.Scale));
        Assert.Equal("Fake display HDMI-A-1", hdmi.Description);
    }

    [DBusFact]
    public async Task SetWallpaper_EmptyDisplayMeansPrimary_AndSignals()
    {
        await StartAsync();

        await client.SetWallpaperAsync(rain, "");

        Assert.Equal([new RunningWallpaper("DP-1", rain)], await client.GetWallpapersAsync());
        Assert.Equal([("DP-1", rain)], store.Stored);
        await Wait.UntilAsync(() => changed.Contains(("DP-1", rain)), "WallpaperChanged signal");
    }

    [DBusFact]
    public async Task SetWallpaper_OnNamedDisplay_TrailingSeparatorIsIgnored()
    {
        await StartAsync();

        await client.SetWallpaperAsync(waves + "/", "HDMI-A-1");

        Assert.Equal([new RunningWallpaper("HDMI-A-1", waves)], await client.GetWallpapersAsync());
    }

    [DBusFact]
    public async Task SetWallpaper_Errors()
    {
        await StartAsync();

        Assert.Equal(CoreErrors.WallpaperNotFound, await ErrorNameOf(() => client.SetWallpaperAsync(tmp.Combine("missing"), "")));
        Assert.Equal(CoreErrors.WallpaperNotFound, await ErrorNameOf(() => client.SetWallpaperAsync("relative/path", "")));
        Assert.Equal(CoreErrors.WallpaperNotFound, await ErrorNameOf(() => client.SetWallpaperAsync("", "")));
        Assert.Equal(CoreErrors.DisplayNotFound, await ErrorNameOf(() => client.SetWallpaperAsync(rain, "DP-9")));
        Assert.Empty(fakeFactory.Created);
        Assert.Empty(await client.GetWallpapersAsync());
    }

    [DBusFact]
    public async Task SetWallpaper_PlayerFailure_IsPlayerFailedAndErrorSignal()
    {
        await StartAsync();
        fakeFactory.Configure = p => p.OnShow = _ => throw new WallpaperPlayerException("boom");

        Assert.Equal(CoreErrors.PlayerFailed, await ErrorNameOf(() => client.SetWallpaperAsync(rain, "DP-1")));

        await Wait.UntilAsync(() => errors.Contains(("DP-1", "boom")), "Error signal");
        Assert.Empty(store.Stored);
    }

    [DBusFact]
    public async Task SetWallpaper_AfterShutdown_IsFailed()
    {
        await StartAsync();
        await service.ShutdownAsync();

        Assert.Equal(CoreErrors.Failed, await ErrorNameOf(() => client.SetWallpaperAsync(rain, "DP-1")));
        Assert.Equal(CoreErrors.Failed, await ErrorNameOf(() => client.SetPausedAsync(true)));
    }

    [DBusFact]
    public async Task SetPaused_RoundTripsAndSignals()
    {
        await StartAsync();
        var paused = new ConcurrentQueue<bool>();
        client.PausedChanged += (_, p) => paused.Enqueue(p);
        Assert.False(await client.GetPausedAsync());

        await client.SetPausedAsync(true);
        Assert.True(await client.GetPausedAsync());
        await Wait.UntilAsync(() => paused.Count == 1, "PausedChanged(true) signal");

        await client.SetPausedAsync(true); // unchanged: no signal
        await client.SetPausedAsync(false);
        Assert.False(await client.GetPausedAsync());
        await Wait.UntilAsync(() => paused.Count == 2, "PausedChanged(false) signal");
        Assert.Equal([true, false], paused);
    }

    [DBusFact]
    public async Task SlowSetWallpaper_DoesNotBlockOtherCalls()
    {
        await StartAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeFactory.Configure = p => p.OnShow = _ => release.Task;

        var set = client.SetWallpaperAsync(rain, "DP-1");
        await Wait.UntilAsync(() => fakeFactory.Created.Count == 1, "player created");

        var others = await client.GetDisplaysAsync().WaitAsync(Timeout);
        Assert.Equal(2, others.Count);
        Assert.False(set.IsCompleted);

        release.SetResult();
        await set.WaitAsync(Timeout);
        Assert.Equal([new RunningWallpaper("DP-1", rain)], await client.GetWallpapersAsync());
    }

    [DBusFact]
    public async Task CloseWallpaper_AndCloseAll_SignalEmptyFolder()
    {
        await StartAsync();
        await client.SetWallpaperAsync(rain, "DP-1");
        await client.SetWallpaperAsync(waves, "HDMI-A-1");

        await client.CloseWallpaperAsync("");   // primary = DP-1
        Assert.Equal([new RunningWallpaper("HDMI-A-1", waves)], await client.GetWallpapersAsync());
        await Wait.UntilAsync(() => changed.Contains(("DP-1", null)), "DP-1 closed signal");

        await client.CloseAllWallpapersAsync();
        Assert.Empty(await client.GetWallpapersAsync());
        Assert.Empty(store.Stored);
        await Wait.UntilAsync(() => changed.Contains(("HDMI-A-1", null)), "HDMI-A-1 closed signal");
    }

    [DBusFact]
    public async Task CloseWallpaper_UnknownDisplayIsNotAnError()
    {
        await StartAsync();

        await client.CloseWallpaperAsync("DP-9");
    }

    [DBusFact]
    public async Task Settings_RoundTrip_AndInvalidJson()
    {
        await StartAsync();

        Assert.Equal(0, (await client.GetSettingsAsync()).WallpaperVolume);
        await client.SetSettingsAsync(new SettingsModel { Language = "de-DE", Startup = true, WallpaperVolume = 40 });
        var settings = await client.GetSettingsAsync();
        Assert.Equal("de-DE", settings.Language);
        Assert.True(settings.Startup);
        Assert.Equal(40, settings.WallpaperVolume);

        var raw = new Core1(connections[^1], busName, lively.Common.Constants.DBus.CoreObjectPath);
        Assert.Equal(CoreErrors.InvalidSettings, await ErrorNameOf(() => raw.SetSettingsAsync("not json")));
        Assert.Equal(CoreErrors.InvalidSettings, await ErrorNameOf(() => raw.SetSettingsAsync("[1,2]")));
        Assert.Equal("de-DE", (await client.GetSettingsAsync()).Language);
    }

    [DBusFact]
    public async Task DisplaysChanged_IsSignalled()
    {
        await StartAsync();

        displays.SetDisplays(FakeDisplayManager.Parse("DP-1:1920x1080@0,0"));

        await Wait.UntilAsync(() => Volatile.Read(ref displaysChanged) > 0, "DisplaysChanged signal");
        Assert.Equal(["DP-1"], (await client.GetDisplaysAsync()).Select(x => x.Id));
    }

    [DBusFact]
    public async Task Quit_InvokesTheQuitAction()
    {
        await StartAsync();

        await client.QuitAsync();

        await Wait.UntilAsync(() => Volatile.Read(ref quitCalls) == 1, "quit action");
    }

    [DBusFact]
    public async Task SecondServer_DoesNotGetTheName()
    {
        await StartAsync();
        var second = new CoreDBusServer(service, displays, new JsonSettingsService(tmp.Combine("other.json")), () => { });
        disposables.Add(second);

        Assert.False(await second.StartAsync(await ConnectAsync(), busName));

        Assert.Equal(2, (await client.GetDisplaysAsync()).Count);
    }

    [DBusFact]
    public async Task Client_ReceivesSignalsFromARestartedServer()
    {
        await StartAsync();
        await disposables.OfType<CoreDBusServer>().First().DisposeAsync();
        Assert.Equal("org.freedesktop.DBus.Error.ServiceUnknown", await ErrorNameOf(() => client.GetDisplaysAsync()));

        await StartServerAsync(service);
        await client.SetWallpaperAsync(rain, "DP-1");

        await Wait.UntilAsync(() => changed.Contains(("DP-1", rain)), "signal from the new owner");
    }

    [DBusFact]
    public async Task SetWallpaper_WithTheDummyPlayer_EndToEnd()
    {
        var options = new CoreOptions { PlayerLogDirectory = tmp.CreateDirectory("logs") };
        var backend = new NullDesktopBackend(new DesktopSession(SessionType.Unknown, DesktopEnvironment.Unknown, null));
        await StartAsync(new WallpaperPlayerFactory(options, backend));

        await client.SetWallpaperAsync(rain, "HDMI-A-1");

        var player = Assert.Single(service.Wallpapers);
        Assert.True(player.IsLoaded);
        Assert.NotNull(player.Pid);
        Assert.Equal([new RunningWallpaper("HDMI-A-1", rain)], await client.GetWallpapersAsync());

        await client.CloseWallpaperAsync("HDMI-A-1");
        Assert.True(player.IsExited);
        Assert.Contains("--wallpaper-display HDMI-A-1", File.ReadAllText(Path.Combine(options.PlayerLogDirectory, "player-dummy-HDMI-A-1.log")));
    }
}
