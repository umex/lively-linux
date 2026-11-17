using System.Diagnostics;
using lively.Core.Display.Wayland;
using lively.Core.Wallpapers;
using lively.Models;

namespace lively.Tests;

/// <summary>
/// Runs lively-player-mpv on the real desktop: a 2 s test video briefly covers the first output's wallpaper.
/// </summary>
public class MpvPlayerProcessTests
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(3);

    [MpvWaylandFact]
    public async Task ShowCommandsAndClose()
    {
        using var tmp = new TempDirectory();
        var video = MakeVideo(tmp);
        var display = FirstDisplay();
        var log = tmp.Combine("logs", "player-mpv.log");
        var launch = PlayerExecutableResolver.Resolve(WallpaperPlayerFactory.MpvPlayerName, Arguments(video, display, log));
        using var player = new ProcessWallpaperPlayer(launch, DummyPlayer.Model(tmp), display, LoadTimeout, CloseTimeout);
        var console = new ConsoleRecorder(player);

        await player.ShowAsync();
        Assert.True(player.IsLoaded);

        player.Pause();
        player.Play();
        player.SetVolume(30);
        console.WaitFor(m => m.Contains("received cmd_volume"), "commands echoed.");
        Assert.False(player.IsExited, "still running after the commands; log: " + ReadLog(log));

        var sw = Stopwatch.StartNew();
        await player.CloseAsync();

        Assert.True(player.IsExited);
        Assert.True(sw.Elapsed < CloseTimeout, "closed by cmd_close, not by the kill timeout");
        Assert.True(player.ExitCode == 0, $"exit code {player.ExitCode}; log: {ReadLog(log)}");
        var text = ReadLog(log);
        Assert.Contains("cmd_suspend", text);
        Assert.Contains("hwdec", text);
    }

    [MpvWaylandFact]
    public void ExitsOnStdinEof()
    {
        using var tmp = new TempDirectory();
        var video = MakeVideo(tmp);
        var display = FirstDisplay();
        var log = tmp.Combine("player-mpv.log");
        var launch = PlayerExecutableResolver.Resolve(WallpaperPlayerFactory.MpvPlayerName, Arguments(video, display, log));
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
            Assert.True(loaded.Wait(LoadTimeout), "msg_wploaded within the load timeout; log: " + ReadLog(log));
            Assert.Equal("{\"Type\":2,\"Success\":true}", loaded.Result);

            p.StandardInput.Close();

            Assert.True(p.WaitForExit((int)CloseTimeout.TotalMilliseconds), "exited after stdin EOF");
            Assert.True(p.ExitCode == 0, $"exit code {p.ExitCode}; log: {ReadLog(log)}");
        }
        finally
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
            }
        }
    }

    private static string MakeVideo(TempDirectory tmp)
    {
        var video = tmp.Combine("tiny.mp4");
        var psi = new ProcessStartInfo(MpvWaylandFactAttribute.FindOnPath("ffmpeg")!)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "-y", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=duration=2:size=128x128:rate=10", "-pix_fmt", "yuv420p", video })
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(30_000), "ffmpeg finished");
        Assert.True(p.ExitCode == 0, "ffmpeg failed: " + stderr.Result);
        return video;
    }

    private static DisplayMonitor FirstDisplay()
    {
        var displays = WaylandOutputEnumerator.Enumerate();
        Assert.NotEmpty(displays);
        return displays[0];
    }

    private static string[] Arguments(string video, DisplayMonitor display, string log) =>
    [
        "--wallpaper-url", video,
        "--wallpaper-type", "video",
        "--wallpaper-display", display.Id,
        "--wallpaper-geometry", $"{display.Width}x{display.Height}",
        "--wallpaper-volume", "0",
        "--wallpaper-backend", "wayland",
        "--wallpaper-output", display.Id,
        "--wallpaper-desktop", "kde",
        "--mpv-log", log,
    ];

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
