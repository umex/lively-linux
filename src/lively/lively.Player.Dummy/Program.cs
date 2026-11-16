using System.Runtime.InteropServices;
using System.Text;
using lively.Models.Message;

namespace lively.Player.Dummy
{
    /// <summary>
    /// Stand-in wallpaper player: speaks the player protocol but renders nothing.
    /// Used to test the core and the UI end to end before real players exist.
    /// </summary>
    /// <remarks>
    /// Test flags: --dummy-load-delay &lt;ms&gt; (default 200), --dummy-hang (never reports loaded),
    /// --dummy-fail (reports Success:false and exits 1), --dummy-log &lt;path&gt;.
    /// Exits 0 on cmd_close, stdin EOF, SIGTERM and SIGINT.
    /// </remarks>
    internal static class Program
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
        private static readonly object WriteLock = new();
        private static StreamWriter stdout = null!;
        private static StreamWriter? log;

        public static async Task<int> Main(string[] args)
        {
            stdout = new StreamWriter(Console.OpenStandardOutput(), Utf8NoBom) { AutoFlush = true, NewLine = "\n" };
            var options = DummyOptions.Parse(args);
            if (options.LogPath != null)
            {
                try
                {
                    var dir = Path.GetDirectoryName(options.LogPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    log = new StreamWriter(new FileStream(options.LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Utf8NoBom) { AutoFlush = true };
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Cannot open log " + options.LogPath + ": " + e.Message);
                }
            }

            var stop = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stop.TrySetResult("SIGTERM"); });
            using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; stop.TrySetResult("SIGINT"); });

            var commandLoop = Task.Run(() => ReadCommandsAsync(stop));

            Log("started pid " + Environment.ProcessId + " args: " + string.Join(' ', args));
            Send(new LivelyMessageConsole { Message = "started " + string.Join(' ', args), Category = ConsoleMessageType.log });

            if (!options.Hang)
            {
                var delay = Task.Delay(options.LoadDelayMs);
                if (await Task.WhenAny(delay, stop.Task) == delay)
                {
                    if (options.Fail)
                    {
                        Log("reporting failure");
                        Send(new LivelyMessageWallpaperLoaded { Success = false });
                        return 1;
                    }
                    Log("loaded " + options.Url + " (" + options.Type + ") on " + options.Display + " " + options.Geometry);
                    Send(new LivelyMessageWallpaperLoaded { Success = true });
                }
            }

            var reason = await stop.Task;
            Log("exiting: " + reason);
            _ = commandLoop;
            return 0;
        }

        private static async Task ReadCommandsAsync(TaskCompletionSource<string> stop)
        {
            try
            {
                using var stdin = new StreamReader(Console.OpenStandardInput(), Utf8NoBom);
                while (true)
                {
                    var line = await stdin.ReadLineAsync();
                    if (line == null)
                    {
                        stop.TrySetResult("stdin closed");
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    IpcMessage? message;
                    try
                    {
                        message = IpcMessageJson.Deserialize(line);
                    }
                    catch (Exception e)
                    {
                        Log("invalid message: " + line);
                        Send(new LivelyMessageConsole { Message = "invalid message: " + e.Message, Category = ConsoleMessageType.error });
                        continue;
                    }
                    if (message == null)
                    {
                        Log("ignored message: " + line);
                        continue;
                    }

                    Log("received " + message.Type + (message is LivelyVolumeCmd volume ? " " + volume.Volume : string.Empty));
                    Send(new LivelyMessageConsole { Message = "received " + message.Type, Category = ConsoleMessageType.log });
                    if (message.Type == MessageType.cmd_close)
                    {
                        stop.TrySetResult("cmd_close");
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                stop.TrySetResult("stdin error: " + e.Message);
            }
        }

        private static void Send(IpcMessage message)
        {
            lock (WriteLock)
            {
                try
                {
                    stdout.WriteLine(IpcMessageJson.Serialize(message));
                }
                catch (IOException)
                {
                    // Core went away; stdin EOF will end the process.
                }
            }
        }

        private static void Log(string text)
        {
            lock (WriteLock)
            {
                try
                {
                    log?.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + text);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    internal sealed class DummyOptions
    {
        public string? Url { get; private set; }
        public string? Type { get; private set; }
        public string? Display { get; private set; }
        public string? Geometry { get; private set; }
        public int LoadDelayMs { get; private set; } = 200;
        public bool Hang { get; private set; }
        public bool Fail { get; private set; }
        public string? LogPath { get; private set; }

        /// <summary>Unknown --wallpaper-* options (and their values) are ignored.</summary>
        public static DummyOptions Parse(string[] args)
        {
            var o = new DummyOptions();
            for (var i = 0; i < args.Length; i++)
            {
                string? Value() => i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;
                switch (args[i])
                {
                    case "--wallpaper-url": o.Url = Value(); break;
                    case "--wallpaper-type": o.Type = Value(); break;
                    case "--wallpaper-display": o.Display = Value(); break;
                    case "--wallpaper-geometry": o.Geometry = Value(); break;
                    case "--dummy-load-delay": o.LoadDelayMs = int.TryParse(Value(), out var ms) && ms >= 0 ? ms : 200; break;
                    case "--dummy-hang": o.Hang = true; break;
                    case "--dummy-fail": o.Fail = true; break;
                    case "--dummy-log": o.LogPath = Value(); break;
                    default:
                        if (args[i].StartsWith("--", StringComparison.Ordinal))
                        {
                            Value();
                        }
                        break;
                }
            }
            return o;
        }
    }
}
