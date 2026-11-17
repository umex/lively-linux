using lively.Core.Display;

namespace lively.Daemon
{
    /// <summary>Command line of lively-daemon.</summary>
    public sealed class DaemonOptions
    {
        public const string Usage =
            """
            Usage: lively-daemon [--no-restore] [--displays SPEC]
                   lively-daemon --install-user | --uninstall-user

              --no-restore      do not start the wallpapers of the saved layout
              --displays SPEC   use fake displays instead of the compositor's, for example
                                "DP-1:1920x1080@0,0;HDMI-A-1:2560x1440@1920,0"
              --install-user    install the D-Bus activation file and the application launcher
                                for the current user (pointing at this build), then exit
              --uninstall-user  remove them and the login autostart entry, then exit
              -h, --help        show this help

            Exit codes: 0 normal exit, 1 invalid arguments or session bus lost,
                        2 already running, 3 no session bus.
            """;

        /// <summary>Do not restore the saved wallpaper layout on start.</summary>
        public bool NoRestore { get; init; }

        /// <summary>Fake display spec (see <see cref="FakeDisplayManager.Parse"/>), null for real displays.</summary>
        public string? Displays { get; init; }

        public bool ShowHelp { get; init; }

        /// <summary>Install the per-user activation file and launcher (<see cref="UserInstaller"/>) and exit.</summary>
        public bool InstallUser { get; init; }

        /// <summary>Remove the per-user files and exit.</summary>
        public bool UninstallUser { get; init; }

        /// <exception cref="ArgumentException">
        /// Unknown option, missing value, invalid display spec, or both --install-user and --uninstall-user.
        /// </exception>
        public static DaemonOptions Parse(IReadOnlyList<string> args)
        {
            bool noRestore = false, help = false, install = false, uninstall = false;
            string? displays = null;
            for (int i = 0; i < args.Count; i++)
            {
                switch (args[i])
                {
                    case "--no-restore":
                        noRestore = true;
                        break;
                    case "--displays":
                        if (i + 1 >= args.Count)
                        {
                            throw new ArgumentException("--displays needs a value.");
                        }
                        displays = args[++i];
                        try
                        {
                            FakeDisplayManager.Parse(displays);
                        }
                        catch (FormatException e)
                        {
                            throw new ArgumentException(e.Message, e);
                        }
                        break;
                    case "--install-user":
                        install = true;
                        break;
                    case "--uninstall-user":
                        uninstall = true;
                        break;
                    case "-h":
                    case "--help":
                        help = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option \"{args[i]}\".");
                }
            }
            if (install && uninstall)
            {
                throw new ArgumentException("--install-user and --uninstall-user cannot be combined.");
            }
            return new DaemonOptions
            {
                NoRestore = noRestore, Displays = displays, ShowHelp = help, InstallUser = install, UninstallUser = uninstall,
            };
        }
    }
}
