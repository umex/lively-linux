using lively.Core;
using lively.Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace lively.Daemon
{
    internal static class Program
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public static async Task<int> Main(string[] args)
        {
            DaemonOptions options;
            try
            {
                options = DaemonOptions.Parse(args);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine("lively-daemon: " + e.Message);
                Console.Error.WriteLine(DaemonOptions.Usage);
                return DaemonExitCode.Failure;
            }
            if (options.ShowHelp)
            {
                Console.WriteLine(DaemonOptions.Usage);
                return DaemonExitCode.Success;
            }

            LogSetup.Configure("daemon.log", console: true);
            try
            {
                // No args: the command-line configuration provider rejects switches without a value.
                var builder = Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                builder.Logging.AddNLog();
                builder.Services.Configure<ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true);
                builder.Services.AddSingleton(options);
                var exitCode = new DaemonExitCode();
                builder.Services.AddSingleton(exitCode);
                builder.Services.AddLivelyCore(o => o.FakeDisplays = options.Displays);
                builder.Services.AddHostedService<DaemonService>();

                using var host = builder.Build();
                await host.RunAsync().ConfigureAwait(false);
                return exitCode.Value;
            }
            catch (Exception e)
            {
                Logger.Fatal(e, "lively-daemon failed.");
                return DaemonExitCode.Failure;
            }
            finally
            {
                NLog.LogManager.Shutdown();
            }
        }
    }
}
