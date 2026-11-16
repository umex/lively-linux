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
