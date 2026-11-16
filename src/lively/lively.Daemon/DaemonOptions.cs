using lively.Core.Display;

namespace lively.Daemon
{
    /// <summary>Command line of lively-daemon.</summary>
    public sealed class DaemonOptions
    {
        public const string Usage =
            """
            Usage: lively-daemon [--no-restore] [--displays SPEC]

              --no-restore      do not start the wallpapers of the saved layout
              --displays SPEC   use fake displays instead of the compositor's, for example
                                "DP-1:1920x1080@0,0;HDMI-A-1:2560x1440@1920,0"
              -h, --help        show this help

            Exit codes: 0 normal exit, 1 invalid arguments or session bus lost,
                        2 already running, 3 no session bus.
            """;

        /// <summary>Do not restore the saved wallpaper layout on start.</summary>
        public bool NoRestore { get; init; }

        /// <summary>Fake display spec (see <see cref="FakeDisplayManager.Parse"/>), null for real displays.</summary>
        public string? Displays { get; init; }

        public bool ShowHelp { get; init; }

        /// <exception cref="ArgumentException">Unknown option, missing value or invalid display spec.</exception>
        public static DaemonOptions Parse(IReadOnlyList<string> args)
        {
            bool noRestore = false, help = false;
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
                    case "-h":
                    case "--help":
                        help = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option \"{args[i]}\".");
                }
            }
            return new DaemonOptions { NoRestore = noRestore, Displays = displays, ShowHelp = help };
        }
    }
}
