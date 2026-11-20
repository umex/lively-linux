using System.Diagnostics;
using lively.Common;
using lively.Core;
using lively.Core.Desktop;
using lively.Core.Wallpapers;
using lively.Models;
using lively.Models.Message;
// lively.Common has an unused upstream enum of the same name.
using PlaybackState = lively.Core.Wallpapers.PlaybackState;

namespace lively.Tests;

public class ProcessWallpaperPlayerTests
{
    [Fact]
    public void Resolver_FindsTheDummyNextToTheTests()
    {
        var launch = DummyPlayer.Launch("--x");

        Assert.Contains(WallpaperPlayerFactory.DummyPlayerName, string.Join(' ', [launch.FileName, .. launch.Arguments]));
        Assert.Equal("--x", launch.Arguments[^1]);
    }

    [Fact]
    public void Resolver_MissingExecutable_Throws()
    {
        using var tmp = new TempDirectory();

        Assert.Throws<FileNotFoundException>(() => PlayerExecutableResolver.Resolve(tmp.Path, "no-such-player", []));
    }

    [Fact]
    public void Resolver_FallsBackToDotnetDll()
    {
        using var tmp = new TempDirectory();
        tmp.WriteFile("", "some-player.dll");

        var launch = PlayerExecutableResolver.Resolve(tmp.Path, "some-player", ["--a"]);

        Assert.Equal("dotnet", Path.GetFileNameWithoutExtension(launch.FileName));
        Assert.Equal([tmp.Combine("some-player.dll"), "--a"], launch.Arguments);
    }

    [Fact]
    public async Task ShowAsync_WaitsForLoaded()
    {
        using var tmp = new TempDirectory();
        using var player = DummyPlayer.Create(tmp, args: ["--dummy-load-delay", "100", "--wallpaper-display", "DP-1"]);
        var console = new ConsoleRecorder(player);
        var loaded = 0;
        player.Loaded += (_, _) => loaded++;

        await player.ShowAsync();

        Assert.True(player.IsLoaded);
        Assert.False(player.IsExited);
        Assert.NotNull(player.Pid);
        Assert.Equal(1, loaded);
        Assert.StartsWith("started", Assert.Single(console.Messages));
    }

    [Fact]
    public async Task Commands_ReachThePlayer()
    {
        using var tmp = new TempDirectory();
        var log = tmp.Combine("dummy.log");
        using var player = DummyPlayer.Create(tmp, args: ["--dummy-load-delay", "0", "--dummy-log", log]);
        var console = new ConsoleRecorder(player);
        await player.ShowAsync();

        player.Pause();
        player.Play();
        player.SetVolume(30);
        player.SetMute(true);
        player.SetMute(true); // no-op
        player.SetMute(false);
        player.SendMessage(new LivelyReloadCmd());

        string[] expected = ["received cmd_suspend", "received cmd_resume", "received cmd_volume", "received cmd_volume", "received cmd_volume", "received cmd_reload"];
        console.WaitFor(m => m.Count >= 1 + expected.Length, "all commands echoed.");
        Assert.Equal(expected, console.Messages.Skip(1));
        var volumes = File.ReadAllLines(log).Where(l => l.Contains("received cmd_volume")).Select(l => l.Split(' ')[^1]);
        Assert.Equal(["30", "0", "30"], volumes);
    }

    [Fact]
    public async Task CloseAsync_ExitsCleanlyAndRaisesExitedOnce()
    {
        using var tmp = new TempDirectory();
        using var player = DummyPlayer.Create(tmp, args: ["--dummy-load-delay", "0"]);
        var exited = 0;
        player.Exited += (_, _) => Interlocked.Increment(ref exited);
        await player.ShowAsync();

        var sw = Stopwatch.StartNew();
        await player.CloseAsync();

        Assert.True(player.IsExited);
        Assert.Equal(0, player.ExitCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), "closed by cmd_close, not by the kill timeout");
        await player.CloseAsync(); // second close is a no-op
        player.Pause(); // dropped, no exception
        Assert.Equal(1, exited);
    }

    [Fact]
    public async Task Hang_TimesOutAndKills()
    {
        using var tmp = new TempDirectory();
        using var player = DummyPlayer.Create(tmp, loadTimeout: TimeSpan.FromMilliseconds(700), args: ["--dummy-hang"]);

        var e = await Assert.ThrowsAsync<WallpaperPlayerException>(() => player.ShowAsync());

        Assert.Contains("did not load", e.Message);
        await Wait.UntilAsync(() => player.IsExited, "player killed after the timeout");
        Assert.False(player.IsLoaded);
    }

    [Fact]
    public async Task Fail_ThrowsAndExits()
    {
        using var tmp = new TempDirectory();
        using var player = DummyPlayer.Create(tmp, args: ["--dummy-load-delay", "0", "--dummy-fail"]);

        var e = await Assert.ThrowsAsync<WallpaperPlayerException>(() => player.ShowAsync());

        Assert.Contains("failed to load", e.Message);
        await Wait.UntilAsync(() => player.IsExited, "player exited");
    }

    [Fact]
    public async Task EarlyExit_Throws()
    {
        using var tmp = new TempDirectory();
        var launch = new PlayerLaunchInfo("/bin/sh", ["-c", "exit 3"]);
        using var player = new ProcessWallpaperPlayer(launch, DummyPlayer.Model(tmp), DummyPlayer.Display(), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));

        var e = await Assert.ThrowsAsync<WallpaperPlayerException>(() => player.ShowAsync());

        Assert.Contains("code 3", e.Message);
        Assert.True(player.IsExited);
    }

    [Fact]
    public async Task MissingExecutable_Throws()
    {
        using var tmp = new TempDirectory();
        var launch = new PlayerLaunchInfo(tmp.Combine("does-not-exist"), []);
        using var player = new ProcessWallpaperPlayer(launch, DummyPlayer.Model(tmp), DummyPlayer.Display(), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<WallpaperPlayerException>(() => player.ShowAsync());
    }

    [Fact]
    public async Task Cancel_KillsAndThrows()
    {
        using var tmp = new TempDirectory();
        using var player = DummyPlayer.Create(tmp, args: ["--dummy-hang"]);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => player.ShowAsync(cts.Token));
        await Wait.UntilAsync(() => player.IsExited, "player killed after cancel");
    }

    [Fact]
    public async Task UnexpectedExit_RaisesExited()
    {
        using var tmp = new TempDirectory();
        using var player = DummyPlayer.Create(tmp, args: ["--dummy-load-delay", "0"]);
        using var exited = new SemaphoreSlim(0);
        player.Exited += (_, _) => exited.Release();
        await player.ShowAsync();

        Process.GetProcessById(player.Pid!.Value).Kill();

        Assert.True(await exited.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(player.IsExited);
    }

    [Fact]
    public async Task CloseAsync_KillsAPlayerThatIgnoresClose()
    {
        using var tmp = new TempDirectory();
        // Reports loaded, then ignores stdin and SIGTERM; its child "sleep" must die with it.
        var script = "trap '' TERM; echo '{\"Type\":2,\"Success\":true}'; sleep 30 & echo $! > " + tmp.Combine("child.pid") + "; wait";
        var launch = new PlayerLaunchInfo("/bin/sh", ["-c", script]);
        using var player = new ProcessWallpaperPlayer(launch, DummyPlayer.Model(tmp), DummyPlayer.Display(), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(300));
        await player.ShowAsync();
        await Wait.UntilAsync(() => File.Exists(tmp.Combine("child.pid")) && File.ReadAllText(tmp.Combine("child.pid")).Trim().Length > 0, "child pid written");
        var childPid = int.Parse(File.ReadAllText(tmp.Combine("child.pid")).Trim());

        await player.CloseAsync();

        Assert.True(player.IsExited);
        Assert.NotEqual(0, player.ExitCode);
        await Wait.UntilAsync(() => !IsRunning(childPid), "child process killed with the player");
    }

    [Fact]
    public void Dummy_ExitsOnStdinEof()
    {
        var launch = DummyPlayer.Launch("--dummy-load-delay", "0");
        var psi = new ProcessStartInfo(launch.FileName) { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in launch.Arguments)
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi)!;
        Assert.Contains("\"Type\":1", p.StandardOutput.ReadLine());
        Assert.Equal("{\"Type\":2,\"Success\":true}", p.StandardOutput.ReadLine());

        p.StandardInput.Close();

        Assert.True(p.WaitForExit(5000));
        Assert.Equal(0, p.ExitCode);
    }

    [Fact]
    public async Task Factory_LaunchesDummyWithWallpaperArguments()
    {
        using var tmp = new TempDirectory();
        var options = new CoreOptions { PlayerLogDirectory = tmp.Combine("logs") };
        var factory = new WallpaperPlayerFactory(options, WaylandBackend());
        // Pictures have no real player yet, also on Wayland.
        var model = Core.Library.LibraryScanner.ScanFolder(WallpaperFixture.Create(tmp, "picture", "Picture", WallpaperType.picture))!;

        using var player = factory.Create(model, DummyPlayer.Display("DP-2"), new PlaybackState(0, false));
        var console = new ConsoleRecorder(player);
        await player.ShowAsync();
        await player.CloseAsync();

        var started = console.Messages[0];
        Assert.Contains("--wallpaper-url " + model.FilePath, started);
        Assert.Contains("--wallpaper-type picture", started);
        Assert.Contains("--wallpaper-display DP-2", started);
        Assert.Contains("--wallpaper-geometry 1920x1080", started);
        Assert.Contains("--wallpaper-volume 0", started);
        Assert.Contains("--wallpaper-output DP-2", started);
        var log = File.ReadAllText(Path.Combine(options.PlayerLogDirectory, "player-dummy-DP-2.log"));
        Assert.Contains("loaded " + model.FilePath + " (picture) on DP-2 1920x1080", log);
        Assert.Contains("exiting: cmd_close", log);
    }

    [Theory]
    [InlineData(WallpaperType.video)]
    [InlineData(WallpaperType.gif)]
    public void Factory_VideoOnWayland_UsesMpvPlayer(WallpaperType type)
    {
        using var tmp = new TempDirectory();
        var options = new CoreOptions { PlayerLogDirectory = tmp.Combine("logs") };
        var factory = new WallpaperPlayerFactory(options, WaylandBackend());
        var model = Core.Library.LibraryScanner.ScanFolder(WallpaperFixture.Create(tmp, "clip", "Clip", type))!;

        using var player = Assert.IsType<ProcessWallpaperPlayer>(factory.Create(model, DummyPlayer.Display("DP-2"), new PlaybackState(25, false)));

        var launch = player.Launch;
        var executable = launch.FileName.EndsWith(WallpaperPlayerFactory.MpvPlayerName, StringComparison.Ordinal) ? launch.FileName : launch.Arguments[0];
        Assert.Matches("lively-player-mpv(\\.dll)?$", executable);
        Assert.Contains("--mpv-log", launch.Arguments);
        Assert.DoesNotContain("--dummy-log", launch.Arguments);
        Assert.Equal(Path.Combine(options.PlayerLogDirectory, "player-mpv-DP-2.log"), launch.Arguments[launch.Arguments.ToList().IndexOf("--mpv-log") + 1]);
        Assert.Equal("25", launch.Arguments[launch.Arguments.ToList().IndexOf("--wallpaper-volume") + 1]);
        Assert.Equal("DP-2", launch.Arguments[launch.Arguments.ToList().IndexOf("--wallpaper-output") + 1]);
    }

    [Theory]
    [InlineData(WallpaperType.web)]
    [InlineData(WallpaperType.webaudio)]
    [InlineData(WallpaperType.url)]
    public void Factory_WebOnWayland_UsesWebPlayer(WallpaperType type)
    {
        using var tmp = new TempDirectory();
        // A stand-in for the player: the resolver accepts <name>.dll, and the factory only builds the launch.
        var players = tmp.CreateDirectory("players");
        File.WriteAllText(Path.Combine(players, WallpaperPlayerFactory.WebPlayerName + ".dll"), "");
        var options = new CoreOptions
        {
            PlayerLogDirectory = tmp.Combine("logs"),
            WebProfileDirectory = tmp.Combine("WebEngine"),
            PlayerDirectory = players,
        };
        var factory = new WallpaperPlayerFactory(options, WaylandBackend());

        using var player = Assert.IsType<ProcessWallpaperPlayer>(factory.Create(WebModel(tmp, type), DummyPlayer.Display("DP-2"), new PlaybackState(25, false)));

        var args = player.Launch.Arguments.ToList();
        Assert.Equal(Path.Combine(players, "lively-player-web.dll"), args[0]);
        Assert.Equal(type.ToString(), args[args.IndexOf("--wallpaper-type") + 1]);
        Assert.Equal(Path.Combine(options.PlayerLogDirectory, "player-web-DP-2.log"), args[args.IndexOf("--web-log") + 1]);
        Assert.Equal(Path.Combine(options.WebProfileDirectory, "DP-2"), args[args.IndexOf("--web-profile") + 1]);
        Assert.Equal("DP-2", args[args.IndexOf("--wallpaper-output") + 1]);
        Assert.DoesNotContain("--mpv-log", args);
        Assert.DoesNotContain("--dummy-log", args);
    }

    [Fact]
    public void Factory_UrlOnWayland_PassesTheAddress()
    {
        using var tmp = new TempDirectory();
        var players = tmp.CreateDirectory("players");
        File.WriteAllText(Path.Combine(players, WallpaperPlayerFactory.WebPlayerName + ".dll"), "");
        var factory = new WallpaperPlayerFactory(new CoreOptions { PlayerLogDirectory = tmp.Combine("logs"), PlayerDirectory = players }, WaylandBackend());

        using var player = Assert.IsType<ProcessWallpaperPlayer>(factory.Create(WebModel(tmp, WallpaperType.url), DummyPlayer.Display(), new PlaybackState(0, false)));

        var args = player.Launch.Arguments.ToList();
        Assert.Equal("https://example.com/page", args[args.IndexOf("--wallpaper-url") + 1]);
    }

    [Fact]
    public void Factory_MissingWebPlayer_SaysHowToBuildIt()
    {
        using var tmp = new TempDirectory();
        var options = new CoreOptions { PlayerLogDirectory = tmp.Combine("logs"), PlayerDirectory = tmp.CreateDirectory("empty") };
        var factory = new WallpaperPlayerFactory(options, WaylandBackend());

        var e = Assert.Throws<WallpaperPlayerException>(() => factory.Create(WebModel(tmp, WallpaperType.web), DummyPlayer.Display(), new PlaybackState(0, false)));

        Assert.Equal(WallpaperPlayerFactory.WebPlayerMissingMessage, e.Message);
        Assert.Contains("lively-player-web", e.Message);
        Assert.Contains("qt6-qtwebengine-devel layer-shell-qt-devel", e.Message);
        Assert.IsType<FileNotFoundException>(e.InnerException);
    }

    [Fact]
    public void Factory_MissingOtherPlayer_KeepsTheResolverMessage()
    {
        using var tmp = new TempDirectory();
        var empty = tmp.CreateDirectory("empty");
        var factory = new WallpaperPlayerFactory(new CoreOptions { PlayerLogDirectory = tmp.Combine("logs"), PlayerDirectory = empty }, WaylandBackend());

        var e = Assert.Throws<WallpaperPlayerException>(() => factory.Create(DummyPlayer.Model(tmp), DummyPlayer.Display(), new PlaybackState(0, false)));

        Assert.Equal($"Executable \"{WallpaperPlayerFactory.MpvPlayerName}\" was not found in {empty}.", e.Message);
    }

    [Fact]
    public void Factory_VideoWithoutDesktopBackend_UsesDummy()
    {
        using var tmp = new TempDirectory();
        var factory = new WallpaperPlayerFactory(new CoreOptions { PlayerLogDirectory = tmp.Combine("logs") },
            new NullDesktopBackend(new DesktopSession(SessionType.X11, DesktopEnvironment.Kde, "KDE")));

        using var player = Assert.IsType<ProcessWallpaperPlayer>(factory.Create(DummyPlayer.Model(tmp), DummyPlayer.Display(), new PlaybackState(0, false)));

        Assert.Contains(WallpaperPlayerFactory.DummyPlayerName, string.Join(' ', [player.Launch.FileName, .. player.Launch.Arguments]));
        Assert.DoesNotContain(WallpaperPlayerFactory.MpvPlayerName, string.Join(' ', [player.Launch.FileName, .. player.Launch.Arguments]));
        Assert.Contains("--dummy-log", player.Launch.Arguments);
        Assert.DoesNotContain("--mpv-log", player.Launch.Arguments);
    }

    [Fact]
    public void Factory_MissingWallpaperFile_Throws()
    {
        using var tmp = new TempDirectory();
        var model = DummyPlayer.Model(tmp);
        File.Delete(Path.Combine(model.LivelyInfoFolderPath, "wallpaper.mp4"));
        model = Core.Library.LibraryScanner.ScanFolder(model.LivelyInfoFolderPath)!;
        var factory = new WallpaperPlayerFactory(new CoreOptions(), new NullDesktopBackend(new DesktopSession(SessionType.Unknown, DesktopEnvironment.Unknown, null)));

        Assert.Throws<WallpaperPlayerException>(() => factory.Create(model, DummyPlayer.Display(), new PlaybackState(0, false)));
    }

    private static LibraryModel WebModel(TempDirectory tmp, WallpaperType type)
    {
        if (type == WallpaperType.url)
        {
            return new LibraryModel(new LivelyInfoModel { Title = "Site", Type = type, FileName = "https://example.com/page" }, tmp.CreateDirectory("site"));
        }
        var dir = tmp.CreateDirectory("page");
        File.WriteAllText(Path.Combine(dir, "index.html"), "<html></html>");
        return new LibraryModel(new LivelyInfoModel { Title = "Page", Type = type, FileName = "index.html" }, dir);
    }

    private static WaylandDesktopBackend WaylandBackend() =>
        new(new DesktopSession(SessionType.Wayland, DesktopEnvironment.Kde, "KDE"), hasLayerShell: true);

    private static bool IsRunning(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
