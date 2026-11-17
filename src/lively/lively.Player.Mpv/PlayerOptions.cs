using System.Globalization;

namespace lively.Player.Mpv
{
    /// <summary>Command line of lively-player-mpv.</summary>
    /// <remarks>
    /// <c>--wallpaper-url &lt;path or url&gt;</c> and <c>--wallpaper-geometry &lt;W&gt;x&lt;H&gt;</c> are required;
    /// <c>--wallpaper-type</c>, <c>--wallpaper-display</c>, <c>--wallpaper-volume &lt;0-100&gt;</c> (default 0, muted),
    /// <c>--wallpaper-backend</c>, <c>--wallpaper-output</c> (default: the display), <c>--wallpaper-desktop</c> and
    /// <c>--mpv-log &lt;path&gt;</c> are optional. Unknown options are ignored (their value too, when the next token
    /// does not start with <c>--</c>); those that are not <c>--wallpaper-*</c> are reported in <see cref="Warnings"/>.
    /// </remarks>
    public sealed class PlayerOptions
    {
        private readonly List<string> warnings = [];

        public string Url { get; private set; } = string.Empty;

        public string? Type { get; private set; }

        public string? Display { get; private set; }

        /// <summary>Logical width of the display (fallback when the compositor configures the surface with 0).</summary>
        public int Width { get; private set; }

        /// <summary>Logical height of the display (fallback when the compositor configures the surface with 0).</summary>
        public int Height { get; private set; }

        /// <summary>0-100; 0 mutes.</summary>
        public int Volume { get; private set; }

        public string? Backend { get; private set; }

        /// <summary>Wayland output name to place the wallpaper on; defaults to <see cref="Display"/>.</summary>
        public string? Output { get; private set; }

        public string? Desktop { get; private set; }

        /// <summary><c>--mpv-log</c> path, or null.</summary>
        public string? LogPath { get; private set; }

        /// <summary>Ignored arguments worth logging.</summary>
        public IReadOnlyList<string> Warnings => warnings;

        /// <exception cref="ArgumentException">Missing or invalid <c>--wallpaper-url</c> or <c>--wallpaper-geometry</c>.</exception>
        public static PlayerOptions Parse(IReadOnlyList<string> args)
        {
            ArgumentNullException.ThrowIfNull(args);
            var o = new PlayerOptions();
            string? geometry = null;
            for (var i = 0; i < args.Count; i++)
            {
                var arg = args[i];
                string? Value() => i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;
                switch (arg)
                {
                    case "--wallpaper-url": o.Url = Value() ?? string.Empty; break;
                    case "--wallpaper-type": o.Type = Value(); break;
                    case "--wallpaper-display": o.Display = Value(); break;
                    case "--wallpaper-geometry": geometry = Value(); break;
                    case "--wallpaper-volume": o.Volume = o.ParseVolume(Value()); break;
                    case "--wallpaper-backend": o.Backend = Value(); break;
                    case "--wallpaper-output": o.Output = Value(); break;
                    case "--wallpaper-desktop": o.Desktop = Value(); break;
                    case "--mpv-log": o.LogPath = Value(); break;
                    default:
                        if (arg.StartsWith("--wallpaper-", StringComparison.Ordinal))
                        {
                            // Players must ignore --wallpaper-* arguments they do not know.
                            Value();
                        }
                        else if (arg.StartsWith("--", StringComparison.Ordinal))
                        {
                            var value = Value();
                            o.warnings.Add("ignored unknown option " + arg + (value != null ? " " + value : string.Empty));
                        }
                        else
                        {
                            o.warnings.Add("ignored argument " + arg);
                        }
                        break;
                }
            }

            if (string.IsNullOrEmpty(o.Url))
            {
                throw new ArgumentException("missing --wallpaper-url");
            }
            if (geometry == null)
            {
                throw new ArgumentException("missing --wallpaper-geometry");
            }
            (o.Width, o.Height) = ParseGeometry(geometry);
            if (string.IsNullOrEmpty(o.Display))
            {
                o.Display = null;
            }
            if (string.IsNullOrEmpty(o.Output))
            {
                o.Output = o.Display;
            }
            return o;
        }

        /// <summary>Parses <c>&lt;W&gt;x&lt;H&gt;</c> with positive integers.</summary>
        /// <exception cref="ArgumentException">Malformed.</exception>
        public static (int Width, int Height) ParseGeometry(string geometry)
        {
            var parts = geometry.Split('x', 'X');
            if (parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var w)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
                && w > 0 && h > 0)
            {
                return (w, h);
            }
            throw new ArgumentException($"invalid --wallpaper-geometry \"{geometry}\", expected <width>x<height>");
        }

        private int ParseVolume(string? value)
        {
            if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var volume))
            {
                return Math.Clamp(volume, 0, 100);
            }
            warnings.Add($"invalid --wallpaper-volume \"{value}\", using 0");
            return 0;
        }
    }
}
