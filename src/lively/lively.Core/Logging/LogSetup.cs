using lively.Common;
using NLog;

namespace lively.Core.Logging
{
    public static class LogSetup
    {
        private const string Layout = "${longdate}|${level:uppercase=true}|${logger:shortName=true}|${message:withexception=true}";

        /// <summary>
        /// Configures NLog: a file in <paramref name="logDirectory"/> (default <see cref="Constants.CommonPaths.LogDir"/>)
        /// at Debug and, optionally, the console at Info.
        /// </summary>
        public static void Configure(string fileName, bool console, string? logDirectory = null)
        {
            var file = Path.Combine(logDirectory ?? Constants.CommonPaths.LogDir, fileName);
            LogManager.Setup().LoadConfiguration(builder =>
            {
                builder.ForLogger().FilterMinLevel(LogLevel.Debug)
                    .WriteToFile(file, layout: Layout, maxArchiveFiles: 3, archiveAboveSize: 10 * 1024 * 1024);
                if (console)
                {
                    builder.ForLogger().FilterMinLevel(LogLevel.Info).WriteToConsole(layout: Layout);
                }
            });
        }
    }
}
