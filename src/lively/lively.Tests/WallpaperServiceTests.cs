using System.Collections.Concurrent;
using lively.Core;
using lively.Core.Desktop;
using lively.Core.Display;
using lively.Core.Library;
using lively.Core.Settings;
using lively.Core.Wallpapers;
using lively.Models;
using lively.Models.Message;
using lively.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace lively.Tests;

public sealed class WallpaperServiceTests : IDisposable
{
    private readonly TempDirectory tmp = new();
    private readonly FakeDisplayManager displays = new("DP-1:1920x1080@0,0;DP-2:1920x1080@1920,0");
    private readonly FakeWallpaperPlayerFactory factory = new();
    private readonly ConcurrentQueue<(string Display, string? Folder)> changed = new();
    private readonly ConcurrentQueue<(string Display, Exception Error)> errors = new();
    private readonly string rain;
    private readonly string waves;

    public WallpaperServiceTests()
    {
        rain = WallpaperFixture.Create(tmp, "rain", "Rain");
        waves = WallpaperFixture.Create(tmp, "waves", "Waves");
    }

    public void Dispose()
    {
        displays.Dispose();
        tmp.Dispose();
    }

    private WallpaperService CreateService(MemoryLayoutStore store, ISettingsService? settings = null)
    {
        var service = new WallpaperService(displays, factory, store, settings);
        service.WallpaperChanged += (_, e) => changed.Enqueue((e.DisplayId, e.FolderPath));
        service.WallpaperError += (_, e) => errors.Enqueue((e.DisplayId, e.Error));
        return service;
    }

    private static LibraryModel Model(string folder) => LibraryScanner.ScanFolder(folder)!;

    private DisplayMonitor Display(string id) => displays.FindById(id)!;

    private static IEnumerable<(string, string?)> Running(WallpaperService s) =>
        s.Wallpapers.Select(p => (p.Screen.Id, (string?)p.Model.LivelyInfoFolderPath)).OrderBy(x => x.Item1);

    [Fact]
    public async Task Set_StartsPlayerSavesLayoutAndRaises()
    {
        var store = new MemoryLayoutStore();
        var service = CreateService(store);

        await service.SetWallpaperAsync(Model(rain), Display("DP-1"));

        var player = Assert.Single(factory.Created);
        Assert.True(player.IsLoaded);
        Assert.Equal([("DP-1", rain)], Running(service));
        Assert.Equal([("DP-1", rain)], store.Stored);
        Assert.Equal([("DP-1", (string?)rain)], changed);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Set_ReplacesTheWallpaperOnTheSameDisplay()
    {
        var store = new MemoryLayoutStore();
        var service = CreateService(store);
        await service.SetWallpaperAsync(Model(rain), Display("DP-1"));
        await service.SetWallpaperAsync(Model(rain), Display("DP-2"));

        await service.SetWallpaperAsync(Model(waves), Display("DP-1"));

        var old = factory.Created[0];
        Assert.Equal(1, old.CloseCalls);
        Assert.True(old.IsDisposed);
        Assert.Equal([("DP-1", waves), ("DP-2", rain)], Running(service));
        Assert.Equal([("DP-1", waves), ("DP-2", rain)], store.Stored.OrderBy(x => x.DisplayId));
        Assert.Equal([("DP-1", (string?)rain), ("DP-2", rain), ("DP-1", waves)], changed);
    }

    [Fact]
    public async Task Set_Failure_RaisesErrorRethrowsAndKeepsLayout()
    {
        var store = new MemoryLayoutStore();
        var service = CreateService(store);
        await service.SetWallpaperAsync(Model(rain), Display("DP-1"));
        factory.Configure = p => p.OnShow = _ => throw new WallpaperPlayerException("boom");

        await Assert.ThrowsAsync<WallpaperPlayerException>(() => service.SetWallpaperAsync(Model(waves), Display("DP-1")));

        Assert.Empty(service.Wallpapers);
        Assert.True(factory.Created[1].IsDisposed);
        Assert.Equal([("DP-1", rain)], store.Stored);
        Assert.Equal([("DP-1", (string?)rain), ("DP-1", null)], changed);
        var error = Assert.Single(errors);
        Assert.Equal("DP-1", error.Display);
        Assert.Equal("boom", error.Error.Message);
    }

    [Fact]
    public async Task Set_FactoryError_IsReported()
    {
        var service = CreateService(new MemoryLayoutStore());
        factory.CreateError = new WallpaperPlayerException("no player");

        await Assert.ThrowsAsync<WallpaperPlayerException>(() => service.SetWallpaperAsync(Model(rain), Display("DP-1")));

        Assert.Single(errors);
        Assert.Empty(changed);
    }

    [Fact]
    public async Task Set_UnknownDisplay_Throws()
    {
        var service = CreateService(new MemoryLayoutStore());

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetWallpaperAsync(Model(rain), new DisplayMonitor { Id = "HDMI-A-9" }));

        Assert.Empty(factory.Created);
    }

    [Fact]
    public async Task Set_Cancelled_DoesNotReportAnError()
    {
        var store = new MemoryLayoutStore();
        var service = CreateService(store);
        factory.Configure = p => p.OnShow = ct => Task.Delay(Timeout.Infinite, ct);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SetWallpaperAsync(Model(rain), Display("DP-1"), cts.Token));

        Assert.Empty(service.Wallpapers);
        Assert.Empty(errors);
        Assert.Empty(store.Stored);
    }

    [Fact]
    public async Task ConcurrentSets_OnOneDisplay_LeaveOnePlayer()
    {
        var service = CreateService(new MemoryLayoutStore());
        factory.Configure = p => p.OnShow = _ => Task.Delay(50);

        await Task.WhenAll(
            service.SetWallpaperAsync(Model(rain), Display("DP-1")),
            service.SetWallpaperAsync(Model(waves), Display("DP-1")),
            service.SetWallpaperAsync(Model(rain), Display("DP-1")));

        Assert.Single(service.Wallpapers);
        Assert.Equal(3, factory.Created.Count);
        Assert.Equal(2, factory.Created.Count(p => p.IsDisposed));
    }

    [Fact]
    public async Task Close_RemovesLayoutEntryAndRaisesNull()
    {
        var store = new MemoryLayoutStore();
        var service = CreateService(store);
        await service.SetWallpaperAsync(Model(rain), Display("DP-1"));
        await service.SetWallpaperAsync(Model(waves), Display("DP-2"));

        await service.CloseWallpaperAsync("DP-1");
        await service.CloseWallpaperAsync("DP-9"); // nothing there: no-op

        Assert.Equal([("DP-2", waves)], Running(service));
        Assert.Equal([("DP-2", waves)], store.Stored);
        Assert.Equal(("DP-1", (string?)null), changed.Last());
        Assert.True(factory.Created[0].IsDisposed);
    }

    [Fact]
    public async Task CloseAll_ClearsLayout()
    {
        var store = new MemoryLayoutStore();
        var service = CreateService(store);
        await service.SetWallpaperAsync(Model(rain), Display("DP-1"));
        await service.SetWallpaperAsync(Model(waves), Display("DP-2"));

        await service.CloseAllWallpapersAsync();

        Assert.Empty(service.Wallpapers);
        Assert.Empty(store.Stored);
        Assert.Equal([("DP-1", (string?)null), ("DP-2", null)], changed.Skip(2).OrderBy(x => x.Display));
        Assert.All(factory.Created, p => Assert.Equal(1, p.CloseCalls));
    }

    [Fact]
    public async Task Restore_StartsPresentDisplaysAndKeepsTheRest()
    {
        var store = new MemoryLayoutStore(
            new WallpaperLayoutModel("DP-1", rain),
            new WallpaperLayoutModel("DP-2", tmp.Combine("deleted-wallpaper")),
            new WallpaperLayoutModel("HDMI-A-1", waves));
        var service = CreateService(store);

        await service.RestoreWallpaperAsync();
        await service.RestoreWallpaperAsync(); // already running: nothing new

        Assert.Equal([("DP-1", rain)], Running(service));
        Assert.Single(factory.Created);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(3, service.Layout.Count);
    }

    [Fact]
    public async Task Restore_FailedPlayer_KeepsLayoutEntry()
    {
        var store = new MemoryLayoutStore(new WallpaperLayoutModel("DP-1", rain), new WallpaperLayoutModel("DP-2", waves));
        var service = CreateService(store);
        factory.Configure = p => { if (p.Screen.Id == "DP-1") p.OnShow = _ => throw new WallpaperPlayerException("boom"); };

        await service.RestoreWallpaperAsync();

        Assert.Equal([("DP-2", waves)], Running(service));
        Assert.Equal("DP-1", Assert.Single(errors).Display);
        Assert.Equal([("DP-1", rain), ("DP-2", waves)], store.Stored);
    }

    [Fact]
    public async Task UnexpectedExit_RemovesPlayerKeepsLayoutAndReports()
    {
        var store = new MemoryLayoutStore();
        var service = CreateService(store);
        await service.SetWallpaperAsync(Model(rain), Display("DP-1"));

        factory.Created[0].SimulateExit();

        await Wait.UntilAsync(() => service.Wallpapers.Count == 0 && !errors.IsEmpty, "exit handled");
        Assert.Equal([("DP-1", (string?)rain), ("DP-1", null)], changed);
        Assert.IsType<WallpaperPlayerException>(Assert.Single(errors).Error);
        Assert.Equal([("DP-1", rain)], store.Stored);
        Assert.True(factory.Created[0].IsDisposed);
    }

    [Fact]
    public async Task DisplayRemovedAndBack_ClosesThenRestores()
    {
        var store = new MemoryLayoutStore();
        var service = CreateService(store);
        await service.SetWallpaperAsync(Model(rain), Display("DP-1"));
        await service.SetWallpaperAsync(Model(waves), Display("DP-2"));

        displays.SetDisplays(FakeDisplayManager.Parse("DP-1:1920x1080@0,0"));
        await Wait.UntilAsync(() => service.Wallpapers.Count == 1, "DP-2 wallpaper closed");

        Assert.Equal([("DP-1", rain)], Running(service));
        Assert.Equal([("DP-1", rain), ("DP-2", waves)], store.Stored.OrderBy(x => x.DisplayId));
        Assert.Equal(("DP-2", (string?)null), changed.Last());
        Assert.Empty(errors);

        displays.SetDisplays(FakeDisplayManager.Parse("DP-1:1920x1080@0,0;DP-2:1920x1080@1920,0"));
        await Wait.UntilAsync(() => service.Wallpapers.Count == 2, "DP-2 wallpaper restored");

        Assert.Equal([("DP-1", rain), ("DP-2", waves)], Running(service));
        Assert.Equal(3, factory.Created.Count);
    }

    [Fact]
    public async Task Shutdown_ClosesPlayersAndKeepsLayout()
    {
        var store = new MemoryLayoutStore();
        var service = CreateService(store);
        await service.SetWallpaperAsync(Model(rain), Display("DP-1"));
        await service.SetWallpaperAsync(Model(waves), Display("DP-2"));
        var saves = store.SaveCount;

        await service.ShutdownAsync();
        await service.ShutdownAsync();

        Assert.Empty(service.Wallpapers);
        Assert.All(factory.Created, p => Assert.True(p.IsDisposed));
        Assert.Equal(saves, store.SaveCount);
        Assert.Equal(2, store.Stored.Count);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.SetWallpaperAsync(Model(rain), Display("DP-1")));
    }

    [Fact]
    public async Task SetPaused_PausesAllPlayersAndRaisesOnce()
    {
        var service = CreateService(new MemoryLayoutStore());
        var raised = new ConcurrentQueue<bool>();
        service.PausedChanged += (_, paused) => raised.Enqueue(paused);
        await service.SetWallpaperAsync(Model(rain), Display("DP-1"));
        await service.SetWallpaperAsync(Model(waves), Display("DP-2"));

        await service.SetPausedAsync(true);
        await service.SetPausedAsync(true); // no change: nothing sent or raised

        Assert.True(service.IsPaused);
        Assert.Equal(2, factory.Created.Count);
        Assert.All(factory.Created, p => Assert.IsType<LivelySuspendCmd>(Assert.Single(p.Sent)));
        Assert.Equal([true], raised);

        await service.SetPausedAsync(false);

        Assert.False(service.IsPaused);
        Assert.All(factory.Created, p => Assert.IsType<LivelyResumeCmd>(p.Sent.Last()));
        Assert.Equal([true, false], raised);
    }

    [Fact]
    public async Task SetWallpaper_WhilePaused_NewPlayerIsPaused()
    {
        var service = CreateService(new MemoryLayoutStore());
        await service.SetPausedAsync(true);

        await service.SetWallpaperAsync(Model(rain), Display("DP-1"));

        var player = Assert.Single(factory.Created);
        Assert.Equal(new PlaybackState(0, true), player.State);
        Assert.IsType<LivelySuspendCmd>(Assert.Single(player.Sent));
    }

    [Fact]
    public async Task SetVolume_AppliesToRunningAndNewPlayers()
    {
        var service = CreateService(new MemoryLayoutStore());
        await service.SetWallpaperAsync(Model(rain), Display("DP-1"));
        Assert.Equal(new PlaybackState(0, false), factory.LastState);

        await service.SetVolumeAsync(30);
        await service.SetWallpaperAsync(Model(waves), Display("DP-2"));

        Assert.Equal(30, service.Volume);
        Assert.Equal(30, Assert.IsType<LivelyVolumeCmd>(Assert.Single(factory.Created[0].Sent)).Volume);
        Assert.Equal(new PlaybackState(30, false), factory.Created[1].State);
        Assert.Empty(factory.Created[1].Sent);

        await service.SetVolumeAsync(150);

        Assert.Equal(100, service.Volume);
        Assert.All(factory.Created, p => Assert.Equal(100, Assert.IsType<LivelyVolumeCmd>(p.Sent.Last()).Volume));
    }

    [Fact]
    public async Task SettingsChange_UpdatesVolume()
    {
        var settings = new JsonSettingsService(tmp.Combine("config", "Settings.json"));
        settings.Replace(new SettingsModel { WallpaperVolume = 25 });
        var service = CreateService(new MemoryLayoutStore(), settings);
        Assert.Equal(25, service.Volume);
        await service.SetWallpaperAsync(Model(rain), Display("DP-1"));
        var player = Assert.Single(factory.Created);
        Assert.Equal(new PlaybackState(25, false), player.State);

        settings.Replace(new SettingsModel { WallpaperVolume = 40 });

        await Wait.UntilAsync(() => service.Volume == 40, "volume from the settings");
        Assert.Equal(40, Assert.IsType<LivelyVolumeCmd>(Assert.Single(player.Sent)).Volume);

        await service.ShutdownAsync();
        settings.Replace(new SettingsModel { WallpaperVolume = 50 }); // unsubscribed: no effect, no error
        Assert.Equal(40, service.Volume);
    }

    [Fact]
    public async Task SetPausedAndVolume_AfterShutdown_Throw()
    {
        var service = CreateService(new MemoryLayoutStore());
        await service.ShutdownAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.SetPausedAsync(true));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.SetVolumeAsync(10));
    }

    [Fact]
    public async Task AddLivelyCore_WithFakeDisplays_ResolvesEverything()
    {
        using var provider = new ServiceCollection()
            .AddLivelyCore(o =>
            {
                o.SettingsPath = tmp.Combine("config", "Settings.json");
                o.LayoutPath = tmp.Combine("config", "WallpaperLayout.json");
                o.PlayerLogDirectory = tmp.Combine("logs");
                o.FakeDisplays = "A:1280x720@0,0;B:1280x720@1280,0";
            })
            .BuildServiceProvider();

        var displayManager = provider.GetRequiredService<IDisplayManager>();
        Assert.IsType<FakeDisplayManager>(displayManager);
        Assert.Equal(["A", "B"], displayManager.Displays.Select(x => x.Id));
        Assert.IsType<WallpaperService>(provider.GetRequiredService<IWallpaperService>());
        Assert.IsType<WallpaperPlayerFactory>(provider.GetRequiredService<IWallpaperPlayerFactory>());
        Assert.NotNull(provider.GetRequiredService<IDesktopBackend>());
        Assert.True(provider.GetRequiredService<ISettingsService>().Settings.IsFirstRun);
        Assert.Same(provider.GetRequiredService<CoreOptions>(), provider.GetRequiredService<CoreOptions>());

        // The service follows the registered settings.
        provider.GetRequiredService<ISettingsService>().Replace(new SettingsModel { WallpaperVolume = 70 });
        await Wait.UntilAsync(() => provider.GetRequiredService<IWallpaperService>().Volume == 70, "volume from the settings");
    }

    [Fact]
    public async Task EndToEnd_DummyPlayerThroughTheService()
    {
        var layoutPath = tmp.Combine("config", "WallpaperLayout.json");
        await using var provider = new ServiceCollection()
            .AddLivelyCore(o =>
            {
                o.SettingsPath = tmp.Combine("config", "Settings.json");
                o.LayoutPath = layoutPath;
                o.PlayerLogDirectory = tmp.Combine("logs");
                o.FakeDisplays = "DP-1:1920x1080@0,0";
            })
            .BuildServiceProvider();
        var service = provider.GetRequiredService<IWallpaperService>();

        await service.SetWallpaperAsync(Model(rain), provider.GetRequiredService<IDisplayManager>().PrimaryDisplay!);

        var player = Assert.Single(service.Wallpapers);
        Assert.True(player.IsLoaded);
        Assert.NotNull(player.Pid);
        var stored = Assert.Single(new JsonWallpaperLayoutStore(layoutPath).Load());
        Assert.Equal(("DP-1", rain), (stored.DisplayId, stored.LivelyInfoPath));

        await service.ShutdownAsync();

        Assert.True(player.IsExited);
        Assert.Single(new JsonWallpaperLayoutStore(layoutPath).Load());
        Assert.Contains("exiting: cmd_close", File.ReadAllText(tmp.Combine("logs", "player-dummy-DP-1.log")));
    }
}
