using System.Collections.Concurrent;
using lively.Core.Library;
using lively.Core.Wallpapers;
using lively.Models;
using lively.Models.Message;

namespace lively.Tests;

/// <summary>
/// Starts lively-player-dummy, which the test project's output contains through its project reference.
/// </summary>
public static class DummyPlayer
{
    public static PlayerLaunchInfo Launch(params string[] args) =>
        PlayerExecutableResolver.Resolve(WallpaperPlayerFactory.DummyPlayerName, args);

    public static LibraryModel Model(TempDirectory tmp, string title = "Rain") =>
        LibraryScanner.ScanFolder(WallpaperFixture.Create(tmp, title.ToLowerInvariant(), title))!;

    public static DisplayMonitor Display(string id = "DP-1") =>
        new() { Id = id, Width = 1920, Height = 1080, Scale = 1, IsPrimary = true };

    public static ProcessWallpaperPlayer Create(TempDirectory tmp, TimeSpan? loadTimeout = null, TimeSpan? closeTimeout = null, params string[] args) =>
        new(Launch(args), Model(tmp), Display(), loadTimeout ?? TimeSpan.FromSeconds(10), closeTimeout ?? TimeSpan.FromSeconds(3));
}

/// <summary>Collects the msg_console texts a player sends.</summary>
public sealed class ConsoleRecorder
{
    private readonly ConcurrentQueue<string> messages = new();

    public ConsoleRecorder(IWallpaperPlayer player)
    {
        player.MessageReceived += (_, m) =>
        {
            if (m is LivelyMessageConsole c && c.Message != null)
            {
                messages.Enqueue(c.Message);
            }
        };
    }

    public IReadOnlyList<string> Messages => messages.ToArray();

    public void WaitFor(Func<IReadOnlyList<string>, bool> condition, string because)
    {
        Wait.Until(() => condition(Messages), because + " Got: " + string.Join(" | ", Messages));
    }
}

public static class Wait
{
    public static void Until(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out: " + because);
            }
            Thread.Sleep(20);
        }
    }

    public static async Task UntilAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out: " + because);
            }
            await Task.Delay(20);
        }
    }
}
