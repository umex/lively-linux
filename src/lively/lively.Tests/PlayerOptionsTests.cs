extern alias MpvPlayer;
using MpvPlayer::lively.Player.Mpv;

namespace lively.Tests;

public class PlayerOptionsTests
{
    [Fact]
    public void Parse_FullArgumentSet()
    {
        var o = PlayerOptions.Parse(
        [
            "--wallpaper-url", "/w/video.mp4",
            "--wallpaper-type", "video",
            "--wallpaper-display", "DP-1",
            "--wallpaper-geometry", "2560x1440",
            "--wallpaper-volume", "30",
            "--wallpaper-backend", "wayland",
            "--wallpaper-output", "DP-3",
            "--wallpaper-desktop", "kde",
            "--mpv-log", "/tmp/mpv.log",
        ]);

        Assert.Equal("/w/video.mp4", o.Url);
        Assert.Equal("video", o.Type);
        Assert.Equal("DP-1", o.Display);
        Assert.Equal((2560, 1440), (o.Width, o.Height));
        Assert.Equal(30, o.Volume);
        Assert.Equal("wayland", o.Backend);
        Assert.Equal("DP-3", o.Output);
        Assert.Equal("kde", o.Desktop);
        Assert.Equal("/tmp/mpv.log", o.LogPath);
        Assert.Empty(o.Warnings);
    }

    [Fact]
    public void Parse_UnknownWallpaperOptionWithValue_IsIgnored()
    {
        var o = PlayerOptions.Parse(["--wallpaper-foo", "bar", "--wallpaper-url", "/w/a.mp4", "--wallpaper-geometry", "100x50"]);

        Assert.Equal("/w/a.mp4", o.Url);
        Assert.Equal((100, 50), (o.Width, o.Height));
        Assert.Empty(o.Warnings);
    }

    [Fact]
    public void Parse_UnknownWallpaperOptionWithoutValue_IsIgnored()
    {
        var o = PlayerOptions.Parse(["--wallpaper-url", "/w/a.mp4", "--wallpaper-foo", "--wallpaper-geometry", "100x50", "--wallpaper-bar"]);

        Assert.Equal("/w/a.mp4", o.Url);
        Assert.Equal((100, 50), (o.Width, o.Height));
        Assert.Empty(o.Warnings);
    }

    [Fact]
    public void Parse_OtherUnknownOptions_AreIgnoredWithAWarning()
    {
        var o = PlayerOptions.Parse(["--dummy-log", "/tmp/x.log", "--wallpaper-url", "/w/a.mp4", "--wallpaper-geometry", "1x1"]);

        Assert.Equal("/w/a.mp4", o.Url);
        Assert.Null(o.LogPath);
        Assert.Contains("--dummy-log /tmp/x.log", Assert.Single(o.Warnings));
    }

    [Fact]
    public void Parse_Geometry()
    {
        Assert.Equal((2560, 1440), PlayerOptions.ParseGeometry("2560x1440"));
        Assert.Throws<ArgumentException>(() => PlayerOptions.ParseGeometry("2560"));
        Assert.Throws<ArgumentException>(() => PlayerOptions.ParseGeometry("0x10"));
        Assert.Throws<ArgumentException>(() => PlayerOptions.ParseGeometry("-5x10"));
        Assert.Throws<ArgumentException>(() => PlayerOptions.Parse(["--wallpaper-url", "/w/a.mp4", "--wallpaper-geometry", "big"]));
    }

    [Fact]
    public void Parse_MissingUrl_Throws()
    {
        var e = Assert.Throws<ArgumentException>(() => PlayerOptions.Parse(["--wallpaper-geometry", "100x50"]));

        Assert.Contains("--wallpaper-url", e.Message);
    }

    [Fact]
    public void Parse_MissingGeometry_Throws()
    {
        var e = Assert.Throws<ArgumentException>(() => PlayerOptions.Parse(["--wallpaper-url", "/w/a.mp4"]));

        Assert.Contains("--wallpaper-geometry", e.Message);
    }

    [Fact]
    public void Parse_OutputDefaultsToDisplay()
    {
        var o = PlayerOptions.Parse(["--wallpaper-url", "/w/a.mp4", "--wallpaper-geometry", "100x50", "--wallpaper-display", "HDMI-A-1"]);

        Assert.Equal("HDMI-A-1", o.Output);
        Assert.Equal(0, o.Volume);
    }

    [Theory]
    [InlineData("150", 100)]
    [InlineData("-3", 0)]
    [InlineData("loud", 0)]
    public void Parse_Volume_IsClamped(string value, int expected)
    {
        var o = PlayerOptions.Parse(["--wallpaper-url", "/w/a.mp4", "--wallpaper-geometry", "100x50", "--wallpaper-volume", value]);

        Assert.Equal(expected, o.Volume);
    }
}
