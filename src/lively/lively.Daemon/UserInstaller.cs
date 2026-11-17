using System.Diagnostics;
using System.Text;
using lively.Common;
using lively.Core.Wallpapers;

namespace lively.Daemon
{
    /// <summary>
    /// Per-user desktop integration, written from the templates in the daemon's <c>data</c> directory:
    /// the D-Bus activation file (<c>&lt;dataHome&gt;/dbus-1/services</c>), the application launcher
    /// (<c>&lt;dataHome&gt;/applications</c>) and the login autostart entry (<c>&lt;configHome&gt;/autostart</c>).
    /// Every operation is idempotent; directories are created as needed.
    /// </summary>
    public sealed class UserInstaller
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public const string ServiceFileName = Constants.DBus.DaemonBusName + ".service";
        public const string LauncherFileName = Constants.DBus.UiBusName + ".desktop";
        public const string AutostartFileName = Constants.DBus.DaemonBusName + ".desktop";
        public const string IconFileName = Constants.DBus.UiBusName + ".png";

        // Desktop Entry spec, "The Exec key": arguments containing these must be quoted.
        private const string ReservedChars = " \t\n\"'\\><~|&;$*?#()`";

        private readonly string dataDir;
        private readonly PlayerLaunchInfo daemon;
        private readonly PlayerLaunchInfo? ui;

        /// <param name="dataHome">$XDG_DATA_HOME.</param>
        /// <param name="configHome">$XDG_CONFIG_HOME.</param>
        /// <param name="dataDir">Directory with the templates and the icon.</param>
        /// <param name="daemon">How to start lively-daemon.</param>
        /// <param name="ui">How to start the UI, null to skip the launcher.</param>
        /// <remarks>
        /// A bare executable name (<c>dotnet</c> when the resolver found no absolute host) is resolved through
        /// PATH when a file is written, because the bus and the session start programs without our PATH.
        /// </remarks>
        public UserInstaller(string dataHome, string configHome, string dataDir, PlayerLaunchInfo daemon, PlayerLaunchInfo? ui)
        {
            this.dataDir = dataDir;
            this.daemon = daemon;
            this.ui = ui;
            ServicePath = Path.Combine(dataHome, "dbus-1", "services", ServiceFileName);
            LauncherPath = Path.Combine(dataHome, "applications", LauncherFileName);
            AutostartPath = Path.Combine(configHome, "autostart", AutostartFileName);
        }

        public string ServicePath { get; }

        public string LauncherPath { get; }

        public string AutostartPath { get; }

        /// <summary>Referenced by absolute path from the launcher.</summary>
        public string IconPath => Path.Combine(dataDir, IconFileName);

        /// <summary>Writes the activation file and, unless no UI was given, the launcher.</summary>
        /// <returns>The files written.</returns>
        /// <exception cref="IOException">
        /// A template or the icon is missing, a bare host name is not on PATH, or a file cannot be written.
        /// </exception>
        public IReadOnlyList<string> Install()
        {
            var written = new List<string>();
            WriteFile(ServicePath, Render(ServiceFileName, ("@EXEC@", QuoteExec(WithAbsoluteFileName(daemon), desktopEntry: false))));
            written.Add(ServicePath);
            if (ui != null)
            {
                if (!File.Exists(IconPath))
                {
                    throw new FileNotFoundException("Icon not found: " + IconPath, IconPath);
                }
                WriteFile(LauncherPath, Render(LauncherFileName, ("@UI_EXEC@", QuoteExec(WithAbsoluteFileName(ui))), ("@ICON@", EscapeValue(IconPath))));
                written.Add(LauncherPath);
                UpdateDesktopDatabase();
            }
            return written;
        }

        /// <summary>Removes the activation file, the launcher and the autostart entry.</summary>
        /// <returns>The files that existed and were removed.</returns>
        public IReadOnlyList<string> Uninstall()
        {
            var removed = new List<string>();
            foreach (var path in new[] { ServicePath, LauncherPath, AutostartPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    removed.Add(path);
                }
            }
            if (removed.Contains(LauncherPath))
            {
                UpdateDesktopDatabase();
            }
            return removed;
        }

        /// <summary>Writes or removes the autostart entry that starts lively-daemon at login.</summary>
        /// <returns>The entry's path when written, null when removed.</returns>
        /// <exception cref="IOException">See <see cref="Install"/>.</exception>
        public string? SetAutostart(bool enabled)
        {
            if (!enabled)
            {
                if (File.Exists(AutostartPath))
                {
                    File.Delete(AutostartPath);
                    Logger.Info("Removed the autostart entry " + AutostartPath);
                }
                return null;
            }
            if (WriteFile(AutostartPath, Render(AutostartFileName, ("@EXEC@", QuoteExec(WithAbsoluteFileName(daemon))))))
            {
                Logger.Info("Wrote the autostart entry " + AutostartPath);
            }
            return AutostartPath;
        }

        /// <summary>
        /// Command line for an <c>Exec=</c> key. Arguments that are empty or contain reserved characters are
        /// double-quoted, with <c>"</c>, <c>`</c>, <c>$</c> and <c>\</c> backslash-escaped inside.
        /// </summary>
        /// <param name="desktopEntry">
        /// True for a .desktop file: <c>%</c> becomes <c>%%</c> and backslashes are doubled (the string escape
        /// is applied before the quoting rule). False for a D-Bus .service file, whose <c>Exec=</c> is parsed
        /// shell-style without further escaping.
        /// </param>
        public static string QuoteExec(PlayerLaunchInfo launch, bool desktopEntry = true)
        {
            ArgumentNullException.ThrowIfNull(launch);
            var line = string.Join(' ', new[] { launch.FileName }.Concat(launch.Arguments).Select(QuoteArgument));
            return desktopEntry ? line.Replace("\\", "\\\\").Replace("%", "%%") : line;
        }

        private static string QuoteArgument(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(ReservedChars.ToCharArray()) < 0)
            {
                return arg;
            }
            var sb = new StringBuilder("\"");
            foreach (var c in arg)
            {
                if (c is '"' or '`' or '$' or '\\')
                {
                    sb.Append('\\');
                }
                sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Desktop Entry string escapes for a plain value such as <c>Icon=</c>.</summary>
        private static string EscapeValue(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r");
        }

        private string Render(string template, params (string Placeholder, string Value)[] values)
        {
            var path = Path.Combine(dataDir, template);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Template not found: " + path, path);
            }
            var text = File.ReadAllText(path);
            foreach (var (placeholder, value) in values)
            {
                text = text.Replace(placeholder, value);
            }
            return text;
        }

        /// <returns>False when the file already had this content.</returns>
        private static bool WriteFile(string path, string content)
        {
            if (File.Exists(path) && File.ReadAllText(path) == content)
            {
                return false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Written next to the target and renamed, so the bus never reads a half-written file.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
            return true;
        }

        /// <summary>Resolves a bare executable name (the <c>dotnet</c> host) through PATH.</summary>
        private static PlayerLaunchInfo WithAbsoluteFileName(PlayerLaunchInfo launch)
        {
            if (Path.IsPathRooted(launch.FileName))
            {
                return launch;
            }
            var found = FindOnPath(launch.FileName)
                ?? throw new FileNotFoundException(
                    $"\"{launch.FileName}\" is not on PATH. Set DOTNET_HOST_PATH to the dotnet executable or run " +
                    "lively-daemon through an absolute path to dotnet.", launch.FileName);
            return launch with { FileName = found };
        }

        private static string? FindOnPath(string name)
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Path.IsPathRooted(dir))
                {
                    continue;
                }
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)
                    && (OperatingSystem.IsWindows() || (File.GetUnixFileMode(candidate) & UnixFileMode.UserExecute) != 0))
                {
                    return candidate;
                }
            }
            return null;
        }

        private void UpdateDesktopDatabase()
        {
            var applications = Path.GetDirectoryName(LauncherPath)!;
            if (FindOnPath("update-desktop-database") is not { } tool || !Directory.Exists(applications))
            {
                return;
            }
            try
            {
                var psi = new ProcessStartInfo(tool)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add(applications);
                using var process = Process.Start(psi);
                if (process == null)
                {
                    return;
                }
                _ = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(10_000))
                {
                    process.Kill();
                }
            }
            catch (Exception e)
            {
                Logger.Warn("update-desktop-database failed: " + e.Message);
            }
        }
    }
}
