namespace lively.Core.Desktop
{
    public enum SessionType
    {
        Unknown,
        Wayland,
        X11,
    }

    public enum DesktopEnvironment
    {
        Unknown,
        Kde,
        Gnome,
        /// <summary>Compositors with wlr-layer-shell: Sway, Hyprland, river, niri, COSMIC, labwc, Wayfire, ...</summary>
        WlrootsLike,
    }

    /// <param name="CurrentDesktop">Raw XDG_CURRENT_DESKTOP (or DESKTOP_SESSION) value, for logging.</param>
    public sealed record DesktopSession(SessionType Type, DesktopEnvironment Environment, string? CurrentDesktop)
    {
        public override string ToString() => $"{Type}/{Environment} ({CurrentDesktop ?? "?"})";
    }
}
