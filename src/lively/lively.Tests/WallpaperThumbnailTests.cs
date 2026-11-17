using System.Collections.Concurrent;
using lively.Common;
using lively.Core;
using lively.Core.Display;
using lively.Core.Library;
using lively.Core.Wallpapers;
using lively.Models;
using lively.Models.Message;
using lively.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace lively.Tests;

/// <summary>Thumbnails of web wallpapers made by their player (cmd_screenshot / msg_screenshot).</summary>
public sealed class WallpaperThumbnailTests : IDisposable
{
    private readonly TempDirectory tmp = new();
    private readonly FakeDisplayManager displays = new("DP-1:1920x1080@0,0;DP-2:1920x1080@1920,0");
    private readonly FakeWallpaperPlayerFactory factory = new();
    private readonly ConcurrentQueue<string> updated = new();
    private readonly string library;

    public WallpaperThumbnailTests()
    {
        library = tmp.CreateDirectory("library", "wallpapers");
    }

    public void Dispose()
    {
        displays.Dispose();
        tmp.Dispose();
    }

    private WallpaperService CreateService(bool withOptions = true)
    {
        var options = withOptions ? new CoreOptions { WallpapersDirectory = library } : null;
        var service = new WallpaperService(displays, factory, new MemoryLayoutStore(), null, options);
        service.WallpaperUpdated += (_, e) => updated.Enqueue(e.FolderPath);
        return service;
    }

    /// <summary>A wallpaper folder with index.html and a LivelyInfo.json that has an extra field.</summary>
    private static string Wallpaper(string parent, string name, WallpaperType type = WallpaperType.web, string? thumbnail = null)
    {
        var dir = Path.Combine(parent, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "index.html"), "<title>Page</title>");
        if (thumbnail != null)
        {
            File.WriteAllText(Path.Combine(dir, thumbnail), "jpeg");
        }
        var info = new JObject
        {
            ["AppVersion"] = "1.0.0.0",
            ["Title"] = name,
            ["Thumbnail"] = thumbnail,
            ["Preview"] = null,
            ["Desc"] = "kept as it is",
            ["Author"] = "Someone",
            ["Type"] = (int)type,
            ["FileName"] = type == WallpaperType.url ? "https://example.com/" : "index.html",
            ["IsAbsolutePath"] = false,
            ["Custom"] = new JObject { ["Unknown"] = 1 },
        };
        File.WriteAllText(Path.Combine(dir, LibraryScanner.LivelyInfoFileName), info.ToString());
        return dir;
    }

    private static LibraryModel Model(string folder) => LibraryScanner.ScanFolder(folder)!;

    private DisplayMonitor Display(string id) => displays.FindById(id)!;

    private static List<LivelyScreenshotCmd> Requests(FakeWallpaperPlayer player) => player.Sent.OfType<LivelyScreenshotCmd>().ToList();

    private static JObject Info(string folder) => JObject.Parse(File.ReadAllText(Path.Combine(folder, LibraryScanner.LivelyInfoFileName)));

    /// <param name="content">Written to thumbnail.jpg first; null writes nothing.</param>
    private static void Answer(FakeWallpaperPlayer player, string folder, bool success = true, string fileName = "thumbnail.jpg", string? content = "jpeg")
    {
        if (content != null)
        {
            File.WriteAllText(Path.Combine(folder, "thumbnail.jpg"), content);
        }
        player.RaiseMessage(new LivelyMessageScreenshot { FileName = fileName, Success = success });
    }

    /// <summary>Lets a wrongly started background update finish before asserting that nothing happened.</summary>
    private static Task Settle() => Task.Delay(300);

    [Theory]
    [InlineData(WallpaperType.web)]
    [InlineData(WallpaperType.webaudio)]
    [InlineData(WallpaperType.url)]
    public async Task WebWithoutThumbnail_IsAskedForAScreenshot(WallpaperType type)
    {
        var folder = Wallpaper(library, "page", type);
        var service = CreateService();

        await service.SetWallpaperAsync(Model(folder), Display("DP-1"));

        var player = Assert.Single(factory.Created);
        var request = Assert.Single(Requests(player));
        Assert.Equal(ScreenshotFormat.jpeg, request.Format);
        Assert.Equal(Path.Combine(folder, "thumbnail.jpg"), request.FilePath);
        Assert.Equal(3000u, request.Delay);
        Assert.Equal(480u, request.Width);
        Assert.Equal(1, player.MessageSubscribers);
        Assert.Equal("{\"Type\":6,\"Format\":0,\"FilePath\":" + Newtonsoft.Json.JsonConvert.ToString(request.FilePath) + ",\"Delay\":3000,\"Width\":480}",
            IpcMessageJson.Serialize(request));
    }

    [Fact]
    public async Task NoRequest_ForVideosExistingThumbnailsFoldersOutsideTheLibraryOrWithoutOptions()
    {
        var video = Wallpaper(library, "video", WallpaperType.video);
        var withThumbnail = Wallpaper(library, "with-thumbnail", WallpaperType.web, "shot.png");
        var outside = Wallpaper(tmp.CreateDirectory("elsewhere"), "page");
        var service = CreateService();

        await service.SetWallpaperAsync(Model(video), Display("DP-1"));
        await service.SetWallpaperAsync(Model(withThumbnail), Display("DP-2"));
        await service.SetWallpaperAsync(Model(outside), Display("DP-1"));
        var inLibrary = Wallpaper(library, "page");
        var withoutOptions = CreateService(withOptions: false);
        await withoutOptions.SetWallpaperAsync(Model(inLibrary), Display("DP-1"));

        Assert.Equal(4, factory.Created.Count);
        Assert.All(factory.Created, p => Assert.Empty(Requests(p)));
        Assert.All(factory.Created, p => Assert.Equal(0, p.MessageSubscribers));
        await withoutOptions.ShutdownAsync();
    }

    [Fact]
    public async Task ThumbnailNamedButMissing_IsRequested()
    {
        var folder = Wallpaper(library, "page", WallpaperType.web, "gone.jpg");
        File.Delete(Path.Combine(folder, "gone.jpg"));
        var service = CreateService();

        await service.SetWallpaperAsync(Model(folder), Display("DP-1"));

        Assert.Single(Requests(factory.Created[0]));
    }

    [Theory]
    [InlineData("thumbnail.jpg")]
    [InlineData(null)]
    public async Task Success_RecordsOnlyTheThumbnailAndRaisesUpdated(string? fileName)
    {
        var folder = Wallpaper(library, "page");
        var before = Info(folder);
        var service = CreateService();
        await service.SetWallpaperAsync(Model(folder), Display("DP-1"));
        var player = factory.Created[0];

        // The full path is accepted as well as the file name.
        Answer(player, folder, fileName: fileName ?? Path.Combine(folder, "thumbnail.jpg"));

        await Wait.UntilAsync(() => updated.Contains(folder), "WallpaperUpdated");
        var after = Info(folder);
        Assert.Equal("thumbnail.jpg", (string?)after["Thumbnail"]);
        before["Thumbnail"] = "thumbnail.jpg";
        Assert.True(JToken.DeepEquals(before, after), after.ToString());
        Assert.False(File.Exists(Path.Combine(folder, "LivelyInfo.json.tmp")));
        Assert.Equal(Path.Combine(folder, "thumbnail.jpg"), Model(folder).ThumbnailPath);
        Assert.Equal(0, player.MessageSubscribers);

        // Started again, the wallpaper has its thumbnail now.
        await service.SetWallpaperAsync(Model(folder), Display("DP-2"));
        Assert.Empty(Requests(factory.Created[1]));
    }

    [Fact]
    public async Task Failure_MissingOrEmptyFile_AndOtherFileNames_ChangeNothing()
    {
        var failed = Wallpaper(library, "failed");
        var missing = Wallpaper(library, "missing");
        var empty = Wallpaper(library, "empty");
        var service = CreateService();
        await service.SetWallpaperAsync(Model(failed), Display("DP-1"));
        await service.SetWallpaperAsync(Model(missing), Display("DP-2"));

        // A failed capture may still have left a file behind.
        Answer(factory.Created[0], failed, success: false);
        Answer(factory.Created[1], missing, content: null);
        await service.SetWallpaperAsync(Model(empty), Display("DP-1"));
        Answer(factory.Created[2], empty, content: "");
        await Settle();

        Assert.Empty(updated);
        Assert.All(new[] { failed, missing, empty }, f => Assert.Equal(JTokenType.Null, Info(f)["Thumbnail"]!.Type));
        Assert.Equal(0, factory.Created[0].MessageSubscribers);
        Assert.Equal(0, factory.Created[1].MessageSubscribers);
    }

    [Fact]
    public async Task AnswerForAnotherFile_IsIgnoredAndTheRequestStaysOpen()
    {
        var folder = Wallpaper(library, "page");
        var service = CreateService();
        await service.SetWallpaperAsync(Model(folder), Display("DP-1"));
        var player = factory.Created[0];

        Answer(player, folder, fileName: "other.jpg");
        await Settle();
        Assert.Empty(updated);
        Assert.Equal(1, player.MessageSubscribers);

        Answer(player, folder);
        await Wait.UntilAsync(() => updated.Contains(folder), "WallpaperUpdated");
    }

    [Fact]
    public async Task LateAnswer_AfterClose_IsIgnored()
    {
        var folder = Wallpaper(library, "page");
        var service = CreateService();
        await service.SetWallpaperAsync(Model(folder), Display("DP-1"));
        var player = factory.Created[0];

        await service.CloseWallpaperAsync("DP-1");
        Assert.Equal(0, player.MessageSubscribers);
        Answer(player, folder);
        await Settle();

        Assert.Empty(updated);
        Assert.Equal(JTokenType.Null, Info(folder)["Thumbnail"]!.Type);
    }

    [Fact]
    public async Task LateAnswer_AfterTheWallpaperWasReplaced_IsIgnored()
    {
        var folder = Wallpaper(library, "page");
        var other = Wallpaper(library, "other", WallpaperType.video);
        var service = CreateService();
        await service.SetWallpaperAsync(Model(folder), Display("DP-1"));
        var player = factory.Created[0];

        await service.SetWallpaperAsync(Model(other), Display("DP-1"));
        Answer(player, folder);
        await Settle();

        Assert.Empty(updated);
    }

    [Fact]
    public async Task CrashedPlayer_IsForgotten()
    {
        var folder = Wallpaper(library, "page");
        var service = CreateService();
        await service.SetWallpaperAsync(Model(folder), Display("DP-1"));
        var player = factory.Created[0];

        player.SimulateExit();

        await Wait.UntilAsync(() => player.MessageSubscribers == 0, "request dropped after the exit");
        await service.SetWallpaperAsync(Model(folder), Display("DP-1"));
        Assert.Single(Requests(factory.Created[1]));
    }

    [Fact]
    public async Task SameWallpaperOnTwoDisplays_IsRequestedOnce()
    {
        var folder = Wallpaper(library, "page");
        var service = CreateService();

        await service.SetWallpaperAsync(Model(folder), Display("DP-1"));
        await service.SetWallpaperAsync(Model(folder), Display("DP-2"));

        Assert.Single(Requests(factory.Created[0]));
        Assert.Empty(Requests(factory.Created[1]));

        // Once the asked player is gone, the next start asks again.
        await service.CloseWallpaperAsync("DP-1");
        await service.SetWallpaperAsync(Model(folder), Display("DP-1"));
        Assert.Single(Requests(factory.Created[2]));
    }

    [Fact]
    public async Task WhilePaused_TheRequestWaitsForTheResume()
    {
        var folder = Wallpaper(library, "page");
        var service = CreateService();
        await service.SetPausedAsync(true);

        await service.SetWallpaperAsync(Model(folder), Display("DP-1"));
        var player = factory.Created[0];
        Assert.Empty(Requests(player));

        await service.SetPausedAsync(false);
        Assert.Single(Requests(player));

        // Asked once per player, also across further pauses.
        await service.SetPausedAsync(true);
        await service.SetPausedAsync(false);
        Assert.Single(Requests(player));
    }

    [Fact]
    public async Task FailedRequest_IsNotRepeatedOnResume()
    {
        var folder = Wallpaper(library, "page");
        var service = CreateService();
        await service.SetWallpaperAsync(Model(folder), Display("DP-1"));
        var player = factory.Created[0];
        Answer(player, folder, success: false);

        await service.SetPausedAsync(true);
        await service.SetPausedAsync(false);

        Assert.Single(Requests(player));
    }

    [Fact]
    public async Task AnswerAfterTheStaleTime_IsAcceptedWhenNoOtherPlayerTookOver()
    {
        var folder = Wallpaper(library, "page");
        var thumbnails = new WebThumbnails(library, staleAfter: TimeSpan.Zero);
        thumbnails.Updated += (_, f) => updated.Enqueue(f);
        var player = new FakeWallpaperPlayer(Model(folder), Display("DP-1"));
        thumbnails.Request(player);

        await Task.Delay(50);
        Answer(player, folder);

        await Wait.UntilAsync(() => updated.Contains(folder), "late answer accepted");
        Assert.Equal("thumbnail.jpg", (string?)Info(folder)["Thumbnail"]);
    }

    [Fact]
    public void UnansweredRequest_GoesStale()
    {
        var folder = Wallpaper(library, "page");
        var thumbnails = new WebThumbnails(library, staleAfter: TimeSpan.Zero);
        var first = new FakeWallpaperPlayer(Model(folder), Display("DP-1"));
        var second = new FakeWallpaperPlayer(Model(folder), Display("DP-2"));

        thumbnails.Request(first);
        thumbnails.Request(second);

        Assert.Single(Requests(first));
        Assert.Single(Requests(second));
        Assert.Equal(0, first.MessageSubscribers);
        Assert.Equal(1, second.MessageSubscribers);
    }
}
