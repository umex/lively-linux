using lively.Ipc;
using lively.Models;
using lively.Tests.Fakes;
using Tmds.DBus.Protocol;

namespace lively.Tests;

public sealed class LibraryViewModelTests : IDisposable
{
    private readonly TempDirectory tmp = new();
    private readonly FakeCoreClient core = new()
    {
        Displays =
        [
            new DisplayMonitor { Id = "DP-1", Width = 1920, Height = 1080 },
            new DisplayMonitor { Id = "DP-2", Width = 2560, Height = 1440, X = 1920, IsPrimary = true },
        ],
    };

    public void Dispose() => tmp.Dispose();

    private async Task<LibraryViewModel> CreateConnectedAsync()
    {
        WallpaperFixture.Create(tmp, "rain", "Rain");
        var vm = new LibraryViewModel(core, [tmp.Path], a => a());
        await vm.InitializeAsync();
        return vm;
    }

    [Fact]
    public void AddWallpaper_InsertsInTitleOrder()
    {
        using var other = new TempDirectory();
        WallpaperFixture.Create(tmp, "1", "Aurora");
        WallpaperFixture.Create(tmp, "2", "Waves");
        var vm = new LibraryViewModel([tmp.Path]);

        vm.AddWallpaper(WallpaperFixture.Create(other, "new", "Rain"));

        Assert.Equal(["Aurora", "Rain", "Waves"], vm.LibraryItems.Select(x => x.Title));
    }

    [Fact]
    public void SelectedItem_StoresValueAndNotifies()
    {
        WallpaperFixture.Create(tmp, "1", "Rain");
        var vm = new LibraryViewModel([tmp.Path]);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SelectedItem = vm.LibraryItems[0];

        Assert.Same(vm.LibraryItems[0], vm.SelectedItem);
        Assert.Equal([nameof(LibraryViewModel.SelectedItem)], changed);
    }

    [Fact]
    public async Task Initialize_LoadsDisplaysAndSelectsThePrimary()
    {
        core.Running.Add(new RunningWallpaper("DP-1", "/somewhere"));

        var vm = await CreateConnectedAsync();

        Assert.True(core.Connected);
        Assert.True(vm.IsCoreAvailable);
        Assert.Equal(["DP-1", "DP-2"], vm.Displays.Select(x => x.Id));
        Assert.Equal("DP-2", vm.SelectedDisplay?.Id);
        Assert.Equal("/somewhere", vm.RunningWallpapers["DP-1"]);
        Assert.Equal("Running: \"/somewhere\" on DP-1", vm.StatusMessage);
    }

    [Fact]
    public async Task Initialize_WithoutCore_IsUnavailable()
    {
        var vm = new LibraryViewModel(null, [tmp.Path]);

        await vm.InitializeAsync();

        Assert.False(vm.IsCoreAvailable);
        Assert.Contains("not reachable", vm.StatusMessage);
    }

    [Fact]
    public async Task SetWallpaper_SendsFolderAndSelectedDisplay()
    {
        var vm = await CreateConnectedAsync();
        vm.SelectedDisplay = vm.Displays[0];
        var item = vm.LibraryItems[0];

        await vm.SetWallpaperCommand.ExecuteAsync(item);

        Assert.Equal([("SetWallpaper", item.LivelyInfoFolderPath, "DP-1")], core.Calls);
        Assert.Contains("running on DP-1", vm.StatusMessage);
    }

    [Fact]
    public async Task SetWallpaper_WithoutSelectedDisplay_SendsEmptyId()
    {
        var vm = await CreateConnectedAsync();
        vm.SelectedDisplay = null;
        vm.SelectedItem = vm.LibraryItems[0];

        await vm.SetWallpaperCommand.ExecuteAsync(null);

        Assert.Equal([("SetWallpaper", vm.LibraryItems[0].LivelyInfoFolderPath, "")], core.Calls);
    }

    [Fact]
    public async Task SetWallpaper_WithoutItem_DoesNotCallTheCore()
    {
        var vm = await CreateConnectedAsync();

        await vm.SetWallpaperCommand.ExecuteAsync(null);

        Assert.Empty(core.Calls);
        Assert.Equal("Select a wallpaper first.", vm.StatusMessage);
    }

    [Fact]
    public async Task SetWallpaper_ClientError_GoesToStatusMessage()
    {
        var vm = await CreateConnectedAsync();
        core.CallError = new DBusErrorReplyException(CoreErrors.PlayerFailed, "player did not load");

        await vm.SetWallpaperCommand.ExecuteAsync(vm.LibraryItems[0]);

        Assert.Equal("Could not set \"Rain\": player did not load", vm.StatusMessage);
    }

    [Fact]
    public async Task CloseWallpaper_AndCloseAll()
    {
        var vm = await CreateConnectedAsync();
        vm.SelectedDisplay = vm.Displays[0];

        await vm.CloseWallpaperCommand.ExecuteAsync(null);
        await vm.CloseAllWallpapersCommand.ExecuteAsync(null);

        Assert.Equal([("CloseWallpaper", "DP-1", ""), ("CloseAllWallpapers", "", "")], core.Calls);
    }

    [Fact]
    public async Task CoreEvents_UpdateTheViewModelThroughPost()
    {
        var posted = 0;
        WallpaperFixture.Create(tmp, "rain", "Rain");
        var vm = new LibraryViewModel(core, [tmp.Path], a => { posted++; a(); });
        await vm.InitializeAsync();

        var rain = vm.LibraryItems[0].LivelyInfoFolderPath;
        core.RaiseWallpaperChanged("DP-2", rain);
        Assert.Equal(rain, vm.RunningWallpapers["DP-2"]);
        Assert.Equal("\"Rain\" is running on DP-2.", vm.StatusMessage);
        core.RaiseWallpaperChanged("DP-2", null);
        Assert.False(vm.RunningWallpapers.ContainsKey("DP-2"));
        Assert.Equal("Wallpaper closed on DP-2.", vm.StatusMessage);
        core.RaiseError("DP-2", "player crashed");
        Assert.Equal("Error on DP-2: player crashed", vm.StatusMessage);
        Assert.Equal(3, posted);
    }

    [Fact]
    public async Task Initialize_ReadsThePausedState()
    {
        core.Paused = true;

        var vm = await CreateConnectedAsync();

        Assert.True(vm.IsPaused);
        Assert.Equal("Resume", vm.PauseButtonText);
    }

    [Fact]
    public async Task TogglePause_CallsCoreAndFollowsSignal()
    {
        var posted = 0;
        WallpaperFixture.Create(tmp, "rain", "Rain");
        var vm = new LibraryViewModel(core, [tmp.Path], a => { posted++; a(); });
        await vm.InitializeAsync();
        Assert.False(vm.IsPaused);
        Assert.Equal("Pause", vm.PauseButtonText);
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        await vm.TogglePauseCommand.ExecuteAsync(null);

        Assert.Equal([("SetPaused", "True", "")], core.Calls);
        Assert.False(vm.IsPaused); // follows the signal, not the call

        core.RaisePausedChanged(true);

        Assert.Equal(1, posted);
        Assert.True(vm.IsPaused);
        Assert.Equal("Resume", vm.PauseButtonText);
        Assert.Contains(nameof(LibraryViewModel.PauseButtonText), notified);

        await vm.TogglePauseCommand.ExecuteAsync(null);

        Assert.Equal(("SetPaused", "False", ""), core.Calls[^1]);
    }

    [Fact]
    public async Task TogglePause_ClientError_GoesToStatusMessage()
    {
        var vm = await CreateConnectedAsync();
        core.CallError = new DBusErrorReplyException(CoreErrors.Failed, "daemon is shutting down");

        await vm.TogglePauseCommand.ExecuteAsync(null);

        Assert.Equal("Could not pause the wallpapers: daemon is shutting down", vm.StatusMessage);
        Assert.False(vm.IsPaused);
    }

    [Fact]
    public async Task ImportVideos_CallsCoreAndAddsItems()
    {
        using var library = new TempDirectory();
        WallpaperFixture.Create(tmp, "waves", "Waves");
        var vm = await CreateConnectedAsync();
        core.ImportResultFolder = WallpaperFixture.Create(library, "storm", "Storm");
        var statuses = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryViewModel.StatusMessage))
            {
                statuses.Add(vm.StatusMessage);
            }
        };

        await vm.ImportVideosAsync(["/videos/storm.mp4"]);

        Assert.Equal([("ImportVideo", "/videos/storm.mp4", "")], core.Calls);
        Assert.Equal(["Rain", "Storm", "Waves"], vm.LibraryItems.Select(x => x.Title));
        Assert.Same(vm.LibraryItems[1], vm.SelectedItem);
        Assert.Equal(["Importing storm.mp4…", "Imported 1 wallpaper(s)."], statuses);
    }

    [Fact]
    public async Task ImportVideos_ErrorGoesToStatusMessage()
    {
        var vm = await CreateConnectedAsync();
        core.CallError = new DBusErrorReplyException(CoreErrors.ImportFailed, "\"a.mp4\" has no video stream.");

        await vm.ImportVideosAsync(["/videos/a.mp4", "/videos/b.mp4"]);

        // The second file is still tried after the first failed.
        Assert.Equal([("ImportVideo", "/videos/a.mp4", ""), ("ImportVideo", "/videos/b.mp4", "")], core.Calls);
        Assert.Equal("Could not import b.mp4: \"a.mp4\" has no video stream.", vm.StatusMessage);
        Assert.Equal(["Rain"], vm.LibraryItems.Select(x => x.Title));
    }

    [Fact]
    public async Task ImportVideos_WithoutCore_ReportsUnavailable()
    {
        var vm = new LibraryViewModel(null, [tmp.Path]);

        await vm.ImportVideosAsync(["/videos/a.mp4"]);

        Assert.Equal("lively-daemon is not reachable.", vm.StatusMessage);
    }

    [Fact]
    public async Task DisplaysChanged_ReloadsAndKeepsTheSelection()
    {
        var vm = await CreateConnectedAsync();
        vm.SelectedDisplay = vm.Displays[0];

        core.Displays = [new DisplayMonitor { Id = "HDMI-A-1", IsPrimary = true }, new DisplayMonitor { Id = "DP-1" }];
        core.RaiseDisplaysChanged();

        await Wait.UntilAsync(() => vm.Displays.Count == 2 && vm.Displays[0].Id == "HDMI-A-1", "displays reloaded");
        Assert.Equal("DP-1", vm.SelectedDisplay?.Id);

        core.Displays = [new DisplayMonitor { Id = "HDMI-A-1", IsPrimary = true }];
        core.RaiseDisplaysChanged();

        await Wait.UntilAsync(() => vm.Displays.Count == 1, "displays reloaded");
        Assert.Equal("HDMI-A-1", vm.SelectedDisplay?.Id);
    }
}
