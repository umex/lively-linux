namespace lively.Core.Desktop
{
    public static class DesktopSessionDetector
    {
        private static readonly string[] WlrootsLikeNames =
            ["sway", "hyprland", "river", "niri", "cosmic", "labwc", "wayfire", "qtile", "dwl", "hikari", "mango"];

        public static DesktopSession Detect() => Detect(Environment.GetEnvironmentVariable);

        /// <summary>
        /// Session type from XDG_SESSION_TYPE, else WAYLAND_DISPLAY / DISPLAY; desktop from the
        /// ':'-separated XDG_CURRENT_DESKTOP, else DESKTOP_SESSION.
        /// </summary>
        public static DesktopSession Detect(Func<string, string?> getEnv)
        {
            var sessionType = DetectSessionType(getEnv);

            var current = getEnv("XDG_CURRENT_DESKTOP");
            var desktop = FromNames(current);
            string? raw = string.IsNullOrWhiteSpace(current) ? null : current;
            if (desktop == DesktopEnvironment.Unknown)
            {
                var session = getEnv("DESKTOP_SESSION");
                var fromSession = FromNames(session);
                if (fromSession != DesktopEnvironment.Unknown || raw == null)
                {
                    desktop = fromSession;
                    raw ??= string.IsNullOrWhiteSpace(session) ? null : session;
                }
            }
            return new DesktopSession(sessionType, desktop, raw);
        }

        private static SessionType DetectSessionType(Func<string, string?> getEnv)
        {
            var type = getEnv("XDG_SESSION_TYPE")?.Trim();
            if (string.Equals(type, "wayland", StringComparison.OrdinalIgnoreCase))
            {
                return SessionType.Wayland;
            }
            if (string.Equals(type, "x11", StringComparison.OrdinalIgnoreCase))
            {
                return SessionType.X11;
            }
            // "tty", unset, ...: look at the display variables instead.
            if (!string.IsNullOrWhiteSpace(getEnv("WAYLAND_DISPLAY")))
            {
                return SessionType.Wayland;
            }
            if (!string.IsNullOrWhiteSpace(getEnv("DISPLAY")))
            {
                return SessionType.X11;
            }
            return SessionType.Unknown;
        }

        private static DesktopEnvironment FromNames(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DesktopEnvironment.Unknown;
            }
            foreach (var part in value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var name = part.ToLowerInvariant();
                if (name is "kde" or "plasma" or "plasmawayland" or "plasmax11")
                {
                    return DesktopEnvironment.Kde;
                }
                if (name is "gnome" or "gnome-classic" or "gnome-xorg" or "gnome-wayland")
                {
                    return DesktopEnvironment.Gnome;
                }
                if (WlrootsLikeNames.Contains(name))
                {
                    return DesktopEnvironment.WlrootsLike;
                }
            }
            return DesktopEnvironment.Unknown;
        }
    }
}
