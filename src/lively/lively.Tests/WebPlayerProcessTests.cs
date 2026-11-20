using System.Collections.Concurrent;
using System.Diagnostics;
using lively.Common;
using lively.Core;
using lively.Core.Desktop;
using lively.Core.Display;
using lively.Core.Display.Wayland;
using lively.Core.Library;
using lively.Core.Wallpapers;
using lively.Models;
using lively.Models.Message;
using lively.Tests.Fakes;
using Newtonsoft.Json.Linq;
using PlaybackState = lively.Core.Wallpapers.PlaybackState;

namespace lively.Tests;

/// <summary>
/// Runs lively-player-web on the real desktop through the real factory: a test page briefly covers the first
/// output's wallpaper. Serialized with the other tests that draw on the desktop.
/// </summary>
[Collection(DesktopCollection.Name)]
public sealed class WebPlayerProcessTests : IDisposable
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(3);

    private readonly TempDirectory tmp = new();
    private readonly CoreOptions options;
    private readonly string? userCacheDir;
    private readonly bool userCacheExisted;
    private readonly string userDataDir;
    private readonly bool userDataExisted;

    public WebPlayerProcessTests()
    {
        // The players inherit these. Qt Quick keeps a shader pipeline cache under
        // ~/.cache/lively-wallpaper/lively-player-web; tests do not write into the user's home.
        Environment.SetEnvironmentVariable("QSG_RHI_DISABLE_DISK_CACHE", "1");
        // On the private bus of dbus-run-session, Qt's portal lookups would activate an xdg-desktop-portal that
        // outlives the bus (and keeps the test run's output pipe open).
        Environment.SetEnvironmentVariable("QT_NO_XDG_DESKTOP_PORTAL", "1");
        options = new CoreOptions
        {
            PlayerLogDirectory = tmp.CreateDirectory("logs"),
            WebProfileDirectory = tmp.Combine("WebEngine"),
            WallpapersDirectory = tmp.CreateDirectory("library", "wallpapers"),
        };
        userCacheDir = Path.Combine(Constants.CommonPaths.CacheDir, "lively-player-web");
        userCacheExisted = Directory.Exists(userCacheDir);
        // Where QtWebEngine puts a named profile without a storage path.
        userDataDir = Path.Combine(Constants.CommonPaths.AppDataDir, "lively-player-web");
        userDataExisted = Directory.Exists(userDataDir);
    }

    public void Dispose()
    {
        tmp.Dispose();
    }

    private static DisplayMonitor FirstDisplay()
    {
        var displays = WaylandOutputEnumerator.Enumerate();
        Assert.NotEmpty(displays);
        return displays[0];
    }

    private static WaylandDesktopBackend Backend() =>
        new(DesktopSessionDetector.Detect(), hasLayerShell: true);

    private ProcessWallpaperPlayer Create(LibraryModel model, DisplayMonitor display, int volume = 0)
    {
        var factory = new WallpaperPlayerFactory(options, Backend());
        return Assert.IsType<ProcessWallpaperPlayer>(factory.Create(model, display, new PlaybackState(volume, false)));
    }

    private LibraryModel WebModel() => LibraryScanner.ScanFolder(WallpaperFixture.CreateWeb(tmp, "page", "Page"))!;

    private string LogPath(DisplayMonitor display) => Path.Combine(options.PlayerLogDirectory, $"player-web-{display.Id}.log");

    private void AssertHomeUntouched()
    {
        if (!userCacheExisted)
        {
            Assert.False(Directory.Exists(userCacheDir), $"{userCacheDir} was created");
        }
        if (!userDataExisted)
        {
            Assert.False(Directory.Exists(userDataDir), $"{userDataDir} was created");
        }
    }

    [WebPlayerFact]
    public async Task LoadsScreenshotsTakesCommandsAndCloses()
    {
        var display = FirstDisplay();
        var log = LogPath(display);
        using var player = Create(WebModel(), display);
        Assert.EndsWith(WallpaperPlayerFactory.WebPlayerName, player.Launch.FileName);
        var console = new ConsoleRecorder(player);
        var shots = new ConcurrentQueue<LivelyMessageScreenshot>();
        player.MessageReceived += (_, m) =>
        {
            if (m is LivelyMessageScreenshot shot)
            {
                shots.Enqueue(shot);
            }
        };
        try
        {
            await player.ShowAsync();
            Assert.True(player.IsLoaded);
            console.WaitFor(m => m.Any(x => x.StartsWith("WebGL: ", StringComparison.Ordinal)), "WebGL probe reported.");
            var webGl = console.Messages.First(x => x.StartsWith("WebGL: ", StringComparison.Ordinal));
            Assert.StartsWith("WebGL: webgl", webGl);
            Assert.Contains("renderer", webGl);
            console.WaitFor(m => m.Any(x => x.Contains(WallpaperFixture.WebPageReadyMessage)), "the page drew a frame.");

            var file = tmp.Combine("shot.jpg");
            player.SendMessage(new LivelyScreenshotCmd { Format = ScreenshotFormat.jpeg, FilePath = file, Delay = 250, Width = 320 });
            Wait.Until(() => !shots.IsEmpty, "msg_screenshot. Log: " + ReadLog(log));
            var answer = Assert.Single(shots);
            Assert.True(answer.Success, "screenshot failed. Log: " + ReadLog(log));
            Assert.Equal("shot.jpg", answer.FileName);
            var (width, height) = JpegSize(File.ReadAllBytes(file));
            Assert.Equal(320, width);
            Assert.InRange(height, 320 * display.Height / display.Width - 1, 320 * display.Height / display.Width + 1);
            Assert.True(new FileInfo(file).Length > 1000, "a picture, not an empty frame");

            player.Pause();
            player.Play();
            player.SetVolume(30);
            player.SendMessage(new LivelyReloadCmd());
            console.WaitFor(m => m.Contains("received cmd_reload"), "commands echoed.");
            foreach (var name in new[] { "received cmd_suspend", "received cmd_resume", "received cmd_volume" })
            {
                Assert.Contains(name, console.Messages);
            }
            Assert.False(player.IsExited, "still running after the commands; log: " + ReadLog(log));

            var sw = Stopwatch.StartNew();
            await player.CloseAsync();
            Assert.True(player.IsExited);
            Assert.True(sw.Elapsed < CloseTimeout, "closed by cmd_close, not by the kill timeout");
            Assert.True(player.ExitCode == 0, $"exit code {player.ExitCode}; log: {ReadLog(log)}");
        }
        finally
        {
            player.Terminate();
        }

        var profile = Path.Combine(options.WebProfileDirectory, display.Id);
        Assert.True(Directory.Exists(profile) && Directory.EnumerateFileSystemEntries(profile).Any(), $"profile written to {profile}");
        Assert.Contains("profile: " + profile, ReadLog(log));
        AssertHomeUntouched();
    }

    [WebPlayerFact]
    public async Task MediaPlaysAgainAfterSuspendAndResume()
    {
        // Chromium pauses the <audio>/<video> elements of a frozen page and leaves them paused; the player plays
        // them again on resume. The file is silent and the player muted (volume 0), so nothing is heard.
        var display = FirstDisplay();
        var log = LogPath(display);
        var folder = WallpaperFixture.CreateWeb(tmp, "media", "Media");
        File.WriteAllBytes(Path.Combine(folder, "silence.wav"), SilentWav(seconds: 2));
        File.WriteAllText(Path.Combine(folder, "index.html"), """
            <!doctype html>
            <html><head><meta charset="utf-8"><title>media test</title></head>
            <body style="background:#000">
            <audio id="a" autoplay loop src="silence.wav"></audio>
            <script>
            const a = document.getElementById('a');
            a.addEventListener('playing', () => console.log('media playing'));
            a.addEventListener('pause', () => console.log('media paused'));
            </script>
            </body></html>
            """);
        using var player = Create(LibraryScanner.ScanFolder(folder)!, display);
        var console = new ConsoleRecorder(player);
        static int Count(IReadOnlyList<string> messages, string text) => messages.Count(m => m.Contains(text, StringComparison.Ordinal));
        try
        {
            await player.ShowAsync();
            console.WaitFor(m => Count(m, "media playing") == 1, "the page's audio started.");

            player.Pause();
            Wait.Until(() => ReadLog(log).Contains("suspended: page frozen", StringComparison.Ordinal), "page frozen. Log: " + ReadLog(log));
            player.Play();

            console.WaitFor(m => Count(m, "media paused") >= 1 && Count(m, "media playing") >= 2,
                "the frozen page paused its audio and it played again after the resume.");
        }
        finally
        {
            player.Terminate();
        }
    }

    /// <summary>A mono 16-bit 8 kHz WAV file of silence.</summary>
    private static byte[] SilentWav(int seconds)
    {
        const int rate = 8000;
        var dataLength = rate * 2 * seconds;
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8);
            w.Write(36 + dataLength);
            w.Write("WAVEfmt "u8);
            w.Write(16);
            w.Write((short)1); // PCM
            w.Write((short)1); // mono
            w.Write(rate);
            w.Write(rate * 2); // byte rate
            w.Write((short)2); // block align
            w.Write((short)16); // bits per sample
            w.Write("data"u8);
            w.Write(dataLength);
            w.Write(new byte[dataLength]);
        }
        return stream.ToArray();
    }

    [WebPlayerFact]
    public void ExitsOnStdinEof()
    {
        var display = FirstDisplay();
        PlayerLaunchInfo launch;
        using (var unstarted = Create(WebModel(), display))
        {
            launch = unstarted.Launch;
        }
        var psi = new ProcessStartInfo(launch.FileName) { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in launch.Arguments)
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi)!;
        try
        {
            var loaded = Task.Run(() =>
            {
                string? line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    if (line.StartsWith("{\"Type\":2,", StringComparison.Ordinal))
                    {
                        return line;
                    }
                }
                return null;
            });
            Assert.True(loaded.Wait(options.PlayerLoadTimeout), "msg_wploaded within the load timeout; log: " + ReadLog(LogPath(display)));
            Assert.Equal("{\"Type\":2,\"Success\":true}", loaded.Result);

            p.StandardInput.Close();

            Assert.True(p.WaitForExit((int)CloseTimeout.TotalMilliseconds), "exited after stdin EOF");
            Assert.True(p.ExitCode == 0, $"exit code {p.ExitCode}; log: {ReadLog(LogPath(display))}");
        }
        finally
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
            }
        }
    }

    [WebPlayerFact(NeedsFrames = false)]
    public async Task MissingPage_FailsWithExitCode1()
    {
        var display = FirstDisplay();
        var model = WebModel();
        File.Delete(model.FilePath);
        // The factory refuses a model whose file is gone, so start the player directly with the same arguments.
        var launch = PlayerExecutableResolver.Resolve(WallpaperPlayerFactory.WebPlayerName,
            ["--wallpaper-url", Path.Combine(model.LivelyInfoFolderPath, "index.html"), "--wallpaper-type", "web",
             "--wallpaper-display", display.Id, "--wallpaper-output", display.Id, "--web-log", LogPath(display)]);
        await AssertFailsAsync(new ProcessWallpaperPlayer(launch, model, display, options.PlayerLoadTimeout, CloseTimeout), "file not found: ");
    }

    [WebPlayerFact(NeedsFrames = false)]
    public async Task UnknownOutput_FailsWithExitCode1()
    {
        var display = new DisplayMonitor { Id = "LIVELY-TEST-NONE", Width = 1920, Height = 1080 };
        await AssertFailsAsync(Create(WebModel(), display), "unknown output LIVELY-TEST-NONE (outputs: ");
    }

    private async Task AssertFailsAsync(ProcessWallpaperPlayer player, string reason)
    {
        using var _ = player;
        var console = new ConsoleRecorder(player);
        var loaded = new ConcurrentQueue<bool>();
        player.MessageReceived += (_, m) =>
        {
            if (m is LivelyMessageWallpaperLoaded l)
            {
                loaded.Enqueue(l.Success);
            }
        };
        try
        {
            await Assert.ThrowsAsync<WallpaperPlayerException>(() => player.ShowAsync());
            Wait.Until(() => player.IsExited, "player exited");
            Assert.Equal(1, player.ExitCode);
            Assert.Equal([false], loaded);
            Assert.Contains(console.Messages, m => m.StartsWith(reason, StringComparison.Ordinal));
        }
        finally
        {
            player.Terminate();
        }
    }

    [WebPlayerFact]
    public async Task WallpaperService_MakesTheThumbnailOfAWebWallpaper()
    {
        var display = FirstDisplay();
        var folder = WallpaperFixture.CreateWeb(options.WallpapersDirectory, "aurora", "Aurora");
        var displays = new FakeDisplayManager([display]);
        var service = new WallpaperService(displays, new WallpaperPlayerFactory(options, Backend()), new MemoryLayoutStore(), null, options);
        var updated = new ConcurrentQueue<string>();
        service.WallpaperUpdated += (_, e) => updated.Enqueue(e.FolderPath);
        try
        {
            await service.SetWallpaperAsync(LibraryScanner.ScanFolder(folder)!, display);

            // The player waits WebThumbnails.CaptureDelayMs (3 s) after the load.
            await Wait.UntilAsync(() => updated.Contains(folder), "WallpaperUpdated. Log: " + ReadLog(LogPath(display)), TimeSpan.FromSeconds(15));
            var thumbnail = Path.Combine(folder, "thumbnail.jpg");
            Assert.Equal(480, JpegSize(File.ReadAllBytes(thumbnail)).Width);
            Assert.Equal("thumbnail.jpg", (string?)JObject.Parse(File.ReadAllText(Path.Combine(folder, "LivelyInfo.json")))["Thumbnail"]);
            Assert.Equal(thumbnail, LibraryScanner.ScanFolder(folder)!.ThumbnailPath);
        }
        finally
        {
            await service.ShutdownAsync();
            displays.Dispose();
        }
        AssertHomeUntouched();
    }

    /// <summary>Width and height from the first start-of-frame marker of a JPEG file.</summary>
    internal static (int Width, int Height) JpegSize(byte[] data)
    {
        Assert.True(data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8, "JPEG start of image");
        var i = 2;
        while (i + 9 < data.Length)
        {
            Assert.True(data[i] == 0xFF, $"JPEG marker at {i}");
            var marker = data[i + 1];
            var length = (data[i + 2] << 8) | data[i + 3];
            // SOF0..SOF15, except DHT (C4), JPG (C8) and DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                var height = (data[i + 5] << 8) | data[i + 6];
                var width = (data[i + 7] << 8) | data[i + 8];
                return (width, height);
            }
            i += 2 + length;
        }
        Assert.Fail("no JPEG start-of-frame marker");
        return default;
    }

    private static string ReadLog(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new StreamReader(stream).ReadToEnd();
        }
        catch (IOException e)
        {
            return "(no log: " + e.Message + ")";
        }
    }
}
