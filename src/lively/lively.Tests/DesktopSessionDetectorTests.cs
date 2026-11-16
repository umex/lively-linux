using lively.Core.Desktop;
using lively.Models;

namespace lively.Tests;

public class DesktopSessionDetectorTests
{
    private static DesktopSession Detect(params (string Name, string Value)[] env)
    {
        var map = env.ToDictionary(x => x.Name, x => x.Value);
        return DesktopSessionDetector.Detect(name => map.TryGetValue(name, out var v) ? v : null);
    }

    [Fact]
    public void KdeWayland()
    {
        var s = Detect(("XDG_SESSION_TYPE", "wayland"), ("WAYLAND_DISPLAY", "wayland-0"), ("XDG_CURRENT_DESKTOP", "KDE"));

        Assert.Equal(SessionType.Wayland, s.Type);
        Assert.Equal(DesktopEnvironment.Kde, s.Environment);
        Assert.Equal("KDE", s.CurrentDesktop);
    }

    [Fact]
    public void GnomeX11_WithColonList()
    {
        var s = Detect(("XDG_SESSION_TYPE", "x11"), ("DISPLAY", ":0"), ("XDG_CURRENT_DESKTOP", "ubuntu:GNOME"));

        Assert.Equal(SessionType.X11, s.Type);
        Assert.Equal(DesktopEnvironment.Gnome, s.Environment);
    }

    [Theory]
    [InlineData("sway")]
    [InlineData("Hyprland")]
    [InlineData("niri")]
    [InlineData("COSMIC")]
    public void LayerShellCompositors_AreWlrootsLike(string desktop)
    {
        Assert.Equal(DesktopEnvironment.WlrootsLike, Detect(("WAYLAND_DISPLAY", "wayland-1"), ("XDG_CURRENT_DESKTOP", desktop)).Environment);
    }

    [Fact]
    public void SessionTypeTty_FallsBackToDisplayVariables()
    {
        Assert.Equal(SessionType.Wayland, Detect(("XDG_SESSION_TYPE", "tty"), ("WAYLAND_DISPLAY", "wayland-0"), ("DISPLAY", ":0")).Type);
        Assert.Equal(SessionType.X11, Detect(("DISPLAY", ":1")).Type);
    }

    [Fact]
    public void NoVariables_IsUnknown()
    {
        var s = Detect();

        Assert.Equal(SessionType.Unknown, s.Type);
        Assert.Equal(DesktopEnvironment.Unknown, s.Environment);
        Assert.Null(s.CurrentDesktop);
    }

    [Fact]
    public void DesktopSession_IsUsedWhenCurrentDesktopIsMissingOrUnknown()
    {
        Assert.Equal(DesktopEnvironment.Kde, Detect(("DESKTOP_SESSION", "plasma")).Environment);
        Assert.Equal(DesktopEnvironment.Gnome, Detect(("XDG_CURRENT_DESKTOP", "Unity7"), ("DESKTOP_SESSION", "gnome")).Environment);
        Assert.Equal(DesktopEnvironment.Unknown, Detect(("XDG_CURRENT_DESKTOP", "XFCE")).Environment);
    }

    [Fact]
    public void WaylandBackend_PassesOutputAndDesktop()
    {
        var backend = new WaylandDesktopBackend(new DesktopSession(SessionType.Wayland, DesktopEnvironment.Kde, "KDE"));

        var args = backend.GetPlayerArguments(new DisplayMonitor { Id = "DP-1" });

        Assert.Equal(["--wallpaper-backend", "wayland", "--wallpaper-output", "DP-1", "--wallpaper-desktop", "kde"], args);
        Assert.False(backend.CanPlaceWallpapers);
    }

    [Fact]
    public void NullBackend_PassesNothing()
    {
        var backend = new NullDesktopBackend(new DesktopSession(SessionType.Unknown, DesktopEnvironment.Unknown, null));

        Assert.Empty(backend.GetPlayerArguments(new DisplayMonitor { Id = "DP-1" }));
        Assert.False(backend.CanPlaceWallpapers);
    }
}
