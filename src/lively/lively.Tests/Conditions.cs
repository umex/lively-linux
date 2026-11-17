using System.Runtime.InteropServices;
using lively.Core.Library;

namespace lively.Tests;

/// <summary>
/// Runs only inside a Wayland session (WAYLAND_DISPLAY set); xunit 2 has no runtime skip.
/// </summary>
public sealed class WaylandFactAttribute : FactAttribute
{
    public WaylandFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            Skip = "Needs a Wayland session (WAYLAND_DISPLAY is not set).";
        }
    }
}

/// <summary>
/// Runs only with a D-Bus session bus (DBUS_SESSION_BUS_ADDRESS set, e.g. under dbus-run-session).
/// With CI=true it never skips, so a CI job without a bus fails instead of silently skipping.
/// </summary>
public sealed class DBusFactAttribute : FactAttribute
{
    public DBusFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))
            && !string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Needs a D-Bus session bus (DBUS_SESSION_BUS_ADDRESS is not set); run under dbus-run-session.";
        }
    }
}

/// <summary>Runs only where libwayland-client.so.0 can be loaded (no compositor needed).</summary>
public sealed class WaylandLibFactAttribute : FactAttribute
{
    public WaylandLibFactAttribute()
    {
        if (!NativeLibrary.TryLoad("libwayland-client.so.0", out _))
        {
            Skip = "Needs libwayland-client.so.0.";
        }
    }
}

/// <summary>
/// Runs lively-player-mpv on the real desktop: needs a Wayland session, libmpv.so.2, libwayland-egl.so.1 and
/// ffmpeg (to make the test video).
/// </summary>
public sealed class MpvWaylandFactAttribute : FactAttribute
{
    public MpvWaylandFactAttribute()
    {
        var missing = new List<string>();
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            missing.Add("a Wayland session (WAYLAND_DISPLAY)");
        }
        if (!NativeLibrary.TryLoad("libmpv.so.2", out _))
        {
            missing.Add("libmpv.so.2 (mpv-libs)");
        }
        if (!NativeLibrary.TryLoad("libwayland-egl.so.1", out _))
        {
            missing.Add("libwayland-egl.so.1");
        }
        if (FindOnPath("ffmpeg") == null)
        {
            missing.Add("ffmpeg on PATH");
        }
        if (missing.Count > 0)
        {
            Skip = "Needs " + string.Join(", ", missing) + ".";
        }
    }

    public static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}

/// <summary>
/// Runs only when ffmpeg and ffprobe are on PATH. With CI=true it never skips.
/// </summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        Skip = Conditions.FfmpegSkipReason();
    }
}

/// <summary>Needs ffmpeg and ffprobe on PATH and a D-Bus session bus. With CI=true it never skips.</summary>
public sealed class FfmpegDBusFactAttribute : FactAttribute
{
    public FfmpegDBusFactAttribute()
    {
        Skip = Conditions.FfmpegSkipReason() ?? new DBusFactAttribute().Skip;
    }
}

internal static class Conditions
{
    public static string? FfmpegSkipReason()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase)
            || (VideoImporter.FindOnPath("ffmpeg") != null && VideoImporter.FindOnPath("ffprobe") != null))
        {
            return null;
        }
        return "Needs ffmpeg and ffprobe on PATH.";
    }
}
