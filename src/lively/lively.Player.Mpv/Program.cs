using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using lively.Models.Message;
using lively.Player.Mpv.Native;
using lively.Player.Mpv.Protocol;

namespace lively.Player.Mpv
{
    /// <summary>
    /// Video and GIF wallpaper player: libmpv renders through EGL/OpenGL ES into a wlr-layer-shell background
    /// surface on one Wayland output. Speaks the player protocol (docs/ipc.md) on stdin/stdout.
    /// </summary>
    /// <remarks>
    /// Needs libmpv.so.2 (mpv-libs), libwayland-client, libwayland-egl, libEGL and a compositor with
    /// zwlr_layer_shell_v1. Exits 0 on cmd_close, stdin EOF, SIGTERM, SIGINT and when the compositor closes the
    /// surface; on a failure it sends msg_console (error), msg_wploaded Success:false if it had not loaded, and
    /// exits 1.
    /// </remarks>
    internal static class Program
    {
        public static int Main(string[] args)
        {
            var io = new MessageIo();
            PlayerOptions options;
            try
            {
                options = PlayerOptions.Parse(args);
            }
            catch (ArgumentException e)
            {
                return Fail(io, PlayerLog.None, e.Message);
            }

            using var log = PlayerLog.Open(options.LogPath);
            log.Write("started pid " + Environment.ProcessId + " args: " + string.Join(' ', args));
            foreach (var warning in options.Warnings)
            {
                log.Write(warning);
            }
            io.Console("started " + string.Join(' ', args));

            Wakeup wakeup;
            try
            {
                wakeup = Wakeup.Create();
            }
            catch (PlayerException e)
            {
                return Fail(io, log, e.Message);
            }

            using (wakeup)
            {
                var commands = new ConcurrentQueue<IpcMessage?>();
                var loop = new PlayerLoop(options, io, log, wakeup, commands);
                using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; loop.RequestExit("SIGTERM"); });
                using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; loop.RequestExit("SIGINT"); });
                io.StartStdinReader(commands, wakeup, log);
                try
                {
                    var code = loop.Run();
                    log.Write("exit code " + code);
                    return code;
                }
                catch (PlayerException e)
                {
                    return Fail(io, log, e.Message);
                }
                catch (DllNotFoundException e)
                {
                    return Fail(io, log, NativeLibraries.Describe(e));
                }
                catch (Exception e)
                {
                    log.Write(e.ToString());
                    return Fail(io, log, "unexpected error: " + e.Message);
                }
            }
        }

        private static int Fail(MessageIo io, PlayerLog log, string message)
        {
            log.Write("failed: " + message);
            Console.Error.WriteLine("lively-player-mpv: " + message);
            io.Console(message, ConsoleMessageType.error);
            io.SendLoaded(false);
            return 1;
        }
    }
}
