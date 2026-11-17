using System.Globalization;
using System.Runtime.InteropServices;
using lively.Player.Mpv.Native;
using lively.Player.Mpv.Wayland;

namespace lively.Player.Mpv.Mpv
{
    internal enum MpvNoticeKind
    {
        FileLoaded,
        EndFile,
        Shutdown,
    }

    /// <summary>An mpv event the main loop acts on.</summary>
    /// <param name="Reason">END_FILE: mpv_end_file_reason.</param>
    /// <param name="Error">END_FILE: mpv's error text when <paramref name="Reason"/> is ERROR.</param>
    internal readonly record struct MpvNotice(MpvNoticeKind Kind, int Reason = 0, string? Error = null);

    /// <summary>
    /// libmpv with the OpenGL render API, drawing into the current EGL context's default framebuffer.
    /// All calls on the main thread with the GL context current. mpv's wakeup and render-update callbacks
    /// only signal the main loop's eventfd.
    /// </summary>
    internal sealed unsafe class MpvRenderer : IDisposable
    {
        private readonly PlayerLog log;
        private nint handle;
        private nint renderContext;

        private MpvRenderer(nint handle, PlayerLog log)
        {
            this.handle = handle;
            this.log = log;
        }

        /// <param name="wlDisplay"><c>struct wl_display*</c>, given to mpv for VA-API hardware decoding.</param>
        /// <param name="wakeup">Signalled by mpv when events or frames are ready.</param>
        /// <param name="volume">0-100; 0 mutes.</param>
        /// <param name="paused">Start paused.</param>
        /// <exception cref="PlayerException">mpv could not be created or initialized.</exception>
        public static MpvRenderer Create(nint wlDisplay, Wakeup wakeup, PlayerLog log, int volume, bool paused)
        {
            // libmpv requires the C numeric locale.
            Libc.setlocale(Libc.LC_NUMERIC, "C");
            var handle = MpvNative.mpv_create();
            if (handle == 0)
            {
                throw new PlayerException("mpv_create failed");
            }
            var mpv = new MpvRenderer(handle, log);
            try
            {
                mpv.Initialize(wlDisplay, wakeup, volume, paused);
                return mpv;
            }
            catch
            {
                mpv.Dispose();
                throw;
            }
        }

        private void Initialize(nint wlDisplay, Wakeup wakeup, int volume, bool paused)
        {
            var api = MpvNative.mpv_client_api_version();
            log.Write($"libmpv client API {api >> 16}.{api & 0xFFFF}");

            Check(MpvNative.mpv_set_option_string(handle, "vo", "libmpv"), "vo=libmpv");
            SetOption("hwdec", "auto-safe");
            SetOption("loop-file", "inf");
            SetOption("keep-open", "no");
            SetOption("input-default-bindings", "no");
            SetOption("osc", "no");
            SetOption("terminal", "no");
            SetOption("msg-level", "all=warn");
            SetOption("audio-client-name", "lively-wallpaper");
            SetOption("volume", Math.Clamp(volume, 0, 100).ToString(CultureInfo.InvariantCulture));
            SetOption("mute", volume <= 0 ? "yes" : "no");
            if (paused)
            {
                SetOption("pause", "yes");
            }

            MpvNative.mpv_request_log_messages(handle, "warn");
            MpvNative.mpv_set_wakeup_callback(handle, Wakeup.NativeCallback, wakeup.NativeData);
            Check(MpvNative.mpv_initialize(handle), "mpv_initialize");

            var init = new MpvOpenglInitParams { GetProcAddress = EglContext.GetProcAddressCallback, GetProcAddressCtx = null };
            fixed (byte* apiType = "opengl\0"u8)
            {
                var parameters = stackalloc MpvRenderParam[]
                {
                    new(MpvNative.MPV_RENDER_PARAM_API_TYPE, apiType),
                    new(MpvNative.MPV_RENDER_PARAM_OPENGL_INIT_PARAMS, &init),
                    new(MpvNative.MPV_RENDER_PARAM_WL_DISPLAY, (void*)wlDisplay),
                    new(MpvNative.MPV_RENDER_PARAM_INVALID, null),
                };
                nint context;
                Check(MpvNative.mpv_render_context_create(&context, handle, parameters), "mpv_render_context_create");
                renderContext = context;
            }
            MpvNative.mpv_render_context_set_update_callback(renderContext, Wakeup.NativeCallback, wakeup.NativeData);
        }

        /// <summary>Starts <paramref name="url"/> (path or URL), replacing the current file.</summary>
        public void LoadFile(string url) => Command("loadfile", url, "replace");

        public void SetPaused(bool paused) => SetProperty("pause", paused ? "yes" : "no");

        /// <summary>0-100; 0 mutes.</summary>
        public void SetVolume(int volume)
        {
            volume = Math.Clamp(volume, 0, 100);
            if (volume > 0)
            {
                SetProperty("volume", volume.ToString(CultureInfo.InvariantCulture));
            }
            SetProperty("mute", volume == 0 ? "yes" : "no");
        }

        /// <summary>mpv_render_context_update: mpv_render_update_flag bits; call after every render-update wakeup.</summary>
        public ulong Update() => MpvNative.mpv_render_context_update(renderContext);

        /// <summary>Draws the current video frame into framebuffer 0 of <paramref name="width"/>×<paramref name="height"/> pixels.</summary>
        public void Render(int width, int height)
        {
            var fbo = new MpvOpenglFbo { Fbo = 0, W = width, H = height, InternalFormat = 0 };
            var flipY = 1;
            var parameters = stackalloc MpvRenderParam[]
            {
                new(MpvNative.MPV_RENDER_PARAM_OPENGL_FBO, &fbo),
                new(MpvNative.MPV_RENDER_PARAM_FLIP_Y, &flipY),
                new(MpvNative.MPV_RENDER_PARAM_INVALID, null),
            };
            var result = MpvNative.mpv_render_context_render(renderContext, parameters);
            if (result < 0)
            {
                log.Write("mpv_render_context_render: " + MpvNative.ErrorString(result));
            }
        }

        /// <summary>Tells mpv the frame was presented (after eglSwapBuffers); improves its frame timing.</summary>
        public void ReportSwap() => MpvNative.mpv_render_context_report_swap(renderContext);

        /// <summary>Handles every queued mpv event; log messages go to the player log, the rest to <paramref name="notify"/>.</summary>
        public void DrainEvents(Action<MpvNotice> notify)
        {
            while (true)
            {
                var e = MpvNative.mpv_wait_event(handle, 0);
                switch (e->EventId)
                {
                    case MpvNative.MPV_EVENT_NONE:
                        return;
                    case MpvNative.MPV_EVENT_LOG_MESSAGE:
                        var message = (MpvEventLogMessage*)e->Data;
                        log.Write($"mpv [{Utf8(message->Prefix)}] {Utf8(message->Level)}: {Utf8(message->Text)?.TrimEnd()}");
                        break;
                    case MpvNative.MPV_EVENT_FILE_LOADED:
                        log.Write("mpv: file loaded");
                        notify(new MpvNotice(MpvNoticeKind.FileLoaded));
                        break;
                    case MpvNative.MPV_EVENT_END_FILE:
                        var end = (MpvEventEndFile*)e->Data;
                        var error = end->Reason == MpvNative.MPV_END_FILE_REASON_ERROR ? MpvNative.ErrorString(end->Error) : null;
                        log.Write($"mpv: end of file, reason {EndReason(end->Reason)}" + (error != null ? ": " + error : string.Empty));
                        notify(new MpvNotice(MpvNoticeKind.EndFile, end->Reason, error));
                        break;
                    case MpvNative.MPV_EVENT_SHUTDOWN:
                        log.Write("mpv: shutdown");
                        notify(new MpvNotice(MpvNoticeKind.Shutdown));
                        break;
                    case MpvNative.MPV_EVENT_QUEUE_OVERFLOW:
                        log.Write("mpv: event queue overflow");
                        break;
                }
            }
        }

        /// <summary>The hardware decoder in use (<c>hwdec-current</c>), "no" for software decoding, null if unknown.</summary>
        public string? CurrentHwdec()
        {
            var value = MpvNative.mpv_get_property_string(handle, "hwdec-current");
            if (value == 0)
            {
                return null;
            }
            try
            {
                return Marshal.PtrToStringUTF8(value);
            }
            finally
            {
                MpvNative.mpv_free(value);
            }
        }

        /// <summary>Frees the render context (the GL context must be current), then the mpv instance.</summary>
        public void Dispose()
        {
            if (renderContext != 0)
            {
                MpvNative.mpv_render_context_free(renderContext);
                renderContext = 0;
            }
            if (handle != 0)
            {
                MpvNative.mpv_terminate_destroy(handle);
                handle = 0;
            }
        }

        private void SetOption(string name, string value)
        {
            var result = MpvNative.mpv_set_option_string(handle, name, value);
            if (result < 0)
            {
                log.Write($"mpv option {name}={value}: {MpvNative.ErrorString(result)}");
            }
        }

        private void SetProperty(string name, string value)
        {
            var result = MpvNative.mpv_set_property_string(handle, name, value);
            if (result < 0)
            {
                log.Write($"mpv property {name}={value}: {MpvNative.ErrorString(result)}");
            }
        }

        private void Command(params string[] args)
        {
            var native = stackalloc byte*[args.Length + 1];
            try
            {
                for (var i = 0; i < args.Length; i++)
                {
                    native[i] = (byte*)Marshal.StringToCoTaskMemUTF8(args[i]);
                }
                native[args.Length] = null;
                var result = MpvNative.mpv_command(handle, native);
                if (result < 0)
                {
                    log.Write($"mpv command {args[0]}: {MpvNative.ErrorString(result)}");
                }
            }
            finally
            {
                for (var i = 0; i < args.Length; i++)
                {
                    if (native[i] != null)
                    {
                        Marshal.FreeCoTaskMem((nint)native[i]);
                    }
                }
            }
        }

        private static void Check(int result, string what)
        {
            if (result < 0)
            {
                throw new PlayerException(what + " failed: " + MpvNative.ErrorString(result));
            }
        }

        private static string? Utf8(byte* s) => s == null ? null : Marshal.PtrToStringUTF8((nint)s);

        private static string EndReason(int reason) => reason switch
        {
            MpvNative.MPV_END_FILE_REASON_EOF => "eof",
            MpvNative.MPV_END_FILE_REASON_STOP => "stop",
            MpvNative.MPV_END_FILE_REASON_QUIT => "quit",
            MpvNative.MPV_END_FILE_REASON_ERROR => "error",
            MpvNative.MPV_END_FILE_REASON_REDIRECT => "redirect",
            _ => reason.ToString(CultureInfo.InvariantCulture),
        };
    }
}
