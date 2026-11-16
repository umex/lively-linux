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
