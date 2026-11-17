using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using lively.Models.Message;
using lively.Player.Mpv.Mpv;
using lively.Player.Mpv.Native;
using lively.Player.Mpv.Protocol;
using lively.Player.Mpv.Wayland;

namespace lively.Player.Mpv
{
    /// <summary>
    /// The player's single-threaded main loop: Wayland, EGL and mpv all live on the calling thread.
    /// It sleeps in poll() on the Wayland socket and the wakeup eventfd (stdin commands, signals, mpv).
    /// </summary>
    /// <remarks>
    /// States: connect → wait for the layer surface's first configure → create EGL and mpv, load the file →
    /// render frames, paced by wl_surface frame callbacks → report msg_wploaded after the file loaded and its
    /// first frame was presented.
    /// </remarks>
    internal sealed class PlayerLoop
    {
        public const string SurfaceNamespace = "lively-wallpaper";

        /// <summary>
        /// How long a wanted frame waits for the previous frame callback. KWin sends none while the screen is locked
        /// or the surface is not painted; the next frame is then drawn anyway (about once per second).
        /// </summary>
        private const int FrameCallbackTimeoutMs = 1000;

        private readonly PlayerOptions options;
        private readonly MessageIo io;
        private readonly PlayerLog log;
        private readonly Wakeup wakeup;
        private readonly ConcurrentQueue<IpcMessage?> commands;
        private volatile string? exitRequest;

        private WaylandClient? wayland;
        private EglContext? egl;
        private MpvRenderer? mpv;
        private int scale = 1;
        private int width;
        private int height;
        private int volume;
        private bool paused;
        private bool fileLoaded;
        private bool needFrame;
        private bool redraw;
        private long framesRendered;
        private long frameRequestedAt;
        private bool frameWanted;
        private bool frameTimeoutLogged;
        private string? mpvFailure;

        public PlayerLoop(PlayerOptions options, MessageIo io, PlayerLog log, Wakeup wakeup, ConcurrentQueue<IpcMessage?> commands)
        {
            this.options = options;
            this.io = io;
            this.log = log;
            this.wakeup = wakeup;
            this.commands = commands;
            volume = options.Volume;
        }

        /// <summary>Asks the loop to exit with code 0 (signal handlers; any thread).</summary>
        public void RequestExit(string reason)
        {
            exitRequest ??= reason;
            wakeup.Signal();
        }

        /// <returns>The exit code: 0 for a requested exit.</returns>
        /// <exception cref="PlayerException">Startup or playback failed.</exception>
        public int Run()
        {
            NativeLibraries.Check();
            try
            {
                wayland = WaylandClient.Connect(log);
                var output = wayland.SelectOutput(options.Output);
                scale = Math.Max(1, output.Scale);
                log.Write($"placing on output {output.Id} ({output.Width}x{output.Height}, scale {scale})");
                wayland.CreateLayerSurface(output, SurfaceNamespace);
                return Loop();
            }
            finally
            {
                Cleanup();
            }
        }

        private unsafe int Loop()
        {
            var client = wayland!;
            var fds = stackalloc PollFd[2];
            while (true)
            {
                while (!client.PrepareRead())
                {
                    client.DispatchPending();
                }

                short waylandEvents = Libc.POLLIN;
                var flush = client.Flush();
                if (flush == Libc.EAGAIN)
                {
                    waylandEvents |= Libc.POLLOUT;
                }
                else if (flush != 0)
                {
                    client.CancelRead();
                    throw new PlayerException("lost the Wayland connection (" + client.ErrorText() + ")");
                }

                fds[0] = new PollFd { Fd = client.Fd, Events = waylandEvents };
                fds[1] = new PollFd { Fd = wakeup.Fd, Events = Libc.POLLIN };
                if (Libc.poll(fds, 2, PollTimeout()) < 0)
                {
                    var errno = Marshal.GetLastPInvokeError();
                    client.CancelRead();
                    if (errno == Libc.EINTR)
                    {
                        continue;
                    }
                    throw new PlayerException("poll failed (errno " + errno + ")");
                }

                if ((fds[0].Revents & (Libc.POLLIN | Libc.POLLERR | Libc.POLLHUP)) != 0)
                {
                    if (!client.ReadEvents())
                    {
                        throw new PlayerException("lost the Wayland connection (" + client.ErrorText() + ")");
                    }
                }
                else
                {
                    client.CancelRead();
                }
                client.DispatchPending();

                if ((fds[1].Revents & Libc.POLLIN) != 0)
                {
                    wakeup.Drain();
                }

                if (Step() is { } code)
                {
                    return code;
                }
            }
        }

        /// <returns>An exit code when the player should exit, else null.</returns>
        private int? Step()
        {
            var client = wayland!;
            while (commands.TryDequeue(out var message))
            {
                if (HandleCommand(message) is { } code)
                {
                    return code;
                }
            }
            if (exitRequest is { } reason)
            {
                log.Write("exiting: " + reason);
                return 0;
            }
            if (client.Closed)
            {
                log.Write("exiting: the compositor closed the layer surface");
                return 0;
            }

            if (client.TakeConfigure() is { } configure)
            {
                HandleConfigure(configure);
            }

            if (mpv == null || egl == null)
            {
                return null;
            }

            mpv.DrainEvents(OnMpvNotice);
            if (mpvFailure != null)
            {
                throw new PlayerException(mpvFailure);
            }

            // Always ask, also while paused, so that mpv's update flags do not pile up.
            if ((mpv.Update() & MpvNative.MPV_RENDER_UPDATE_FRAME) != 0)
            {
                needFrame = true;
            }
            // Before msg_wploaded the first frame is drawn even when paused (cmd_suspend may come early).
            frameWanted = redraw || (needFrame && (!paused || !io.LoadedSent));
            if (frameWanted && client.FrameCallbackPending && FrameWaitMs() >= FrameCallbackTimeoutMs)
            {
                if (!frameTimeoutLogged)
                {
                    log.Write($"no frame callback within {FrameCallbackTimeoutMs} ms (screen locked or surface hidden?), drawing anyway");
                    frameTimeoutLogged = true;
                }
                client.AbandonFrame();
            }
            if (frameWanted && !client.FrameCallbackPending)
            {
                RenderOnce();
                frameWanted = false;
            }
            return null;
        }

        private long FrameWaitMs() => (long)Stopwatch.GetElapsedTime(frameRequestedAt).TotalMilliseconds;

        /// <summary>poll() timeout: infinite, unless a wanted frame waits for a frame callback.</summary>
        private int PollTimeout()
        {
            if (!frameWanted || wayland is not { FrameCallbackPending: true })
            {
                return -1;
            }
            return (int)Math.Clamp(FrameCallbackTimeoutMs - FrameWaitMs(), 0, FrameCallbackTimeoutMs);
        }

        private int? HandleCommand(IpcMessage? message)
        {
            if (message == null)
            {
                log.Write("exiting: stdin closed");
                return 0;
            }

            log.Write("received " + message.Type + (message is LivelyVolumeCmd v ? " " + v.Volume : string.Empty));
            io.Console("received " + message.Type);
            switch (message.Type)
            {
                case MessageType.cmd_close:
                    log.Write("exiting: cmd_close");
                    return 0;
                case MessageType.cmd_suspend:
                    paused = true;
                    mpv?.SetPaused(true);
                    break;
                case MessageType.cmd_resume:
                    paused = false;
                    mpv?.SetPaused(false);
                    needFrame = true;
                    break;
                case MessageType.cmd_volume when message is LivelyVolumeCmd volumeCmd:
                    volume = Math.Clamp(volumeCmd.Volume, 0, 100);
                    mpv?.SetVolume(volume);
                    break;
                case MessageType.cmd_reload:
                    mpv?.LoadFile(options.Url);
                    break;
                default:
                    log.Write("ignored " + message.Type);
                    break;
            }
            return null;
        }

        private void HandleConfigure(LayerConfigure configure)
        {
            var client = wayland!;
            client.AckConfigure(configure.Serial);
            var w = configure.Width > 0 ? (int)configure.Width : options.Width;
            var h = configure.Height > 0 ? (int)configure.Height : options.Height;
            log.Write($"configure {configure.Width}x{configure.Height} (serial {configure.Serial}) -> {w}x{h} scale {scale}");

            if (egl == null)
            {
                width = w;
                height = h;
                egl = EglContext.Create(client.Display, client.Surface, w * scale, h * scale, log);
                // Double-buffered state: applies with the first buffer (eglSwapBuffers commits).
                client.SetBufferScale(scale);
                mpv = MpvRenderer.Create(client.Display, wakeup, log, volume, paused);
                log.Write("loading " + options.Url);
                mpv.LoadFile(options.Url);
                // No commit yet: the surface maps with the first video frame.
                return;
            }

            if (w != width || h != height)
            {
                width = w;
                height = h;
                egl.Resize(w * scale, h * scale);
            }
            needFrame = true;
            // The acknowledged configure needs a commit, also while paused.
            redraw = framesRendered > 0;
        }

        private void OnMpvNotice(MpvNotice notice)
        {
            switch (notice.Kind)
            {
                case MpvNoticeKind.FileLoaded:
                    fileLoaded = true;
                    break;
                case MpvNoticeKind.EndFile when notice.Reason == MpvNative.MPV_END_FILE_REASON_ERROR:
                    mpvFailure ??= $"cannot play {options.Url}: {notice.Error}";
                    break;
                case MpvNoticeKind.EndFile when notice.Reason == MpvNative.MPV_END_FILE_REASON_EOF && !io.LoadedSent:
                    mpvFailure ??= $"cannot play {options.Url}: it ended before the first frame";
                    break;
                case MpvNoticeKind.Shutdown:
                    mpvFailure ??= "mpv shut down";
                    break;
            }
        }

        private void RenderOnce()
        {
            var client = wayland!;
            // A frame mpv announced after the file loaded: the wallpaper is visible once it is presented.
            var videoFrame = fileLoaded && needFrame;
            needFrame = false;
            redraw = false;

            client.RequestFrame();
            frameRequestedAt = Stopwatch.GetTimestamp();
            mpv!.Render(width * scale, height * scale);
            egl!.SwapBuffers();
            mpv.ReportSwap();
            framesRendered++;

            if (videoFrame && !io.LoadedSent)
            {
                log.Write($"loaded {options.Url} ({options.Type}) on {options.Output} {width}x{height}, hwdec: {mpv.CurrentHwdec() ?? "unknown"}");
                io.SendLoaded(true);
            }
        }

        private void Cleanup()
        {
            Try("mpv", () =>
            {
                if (mpv != null)
                {
                    // mpv_render_context_free needs the GL context.
                    Try("eglMakeCurrent", () => egl?.MakeCurrent());
                    mpv.Dispose();
                    mpv = null;
                }
            });
            Try("EGL", () =>
            {
                egl?.Dispose();
                egl = null;
            });
            Try("Wayland", () =>
            {
                wayland?.Dispose();
                wayland = null;
            });
        }

        private void Try(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                log.Write($"cleanup of {what} failed: {e.Message}");
            }
        }
    }
}
