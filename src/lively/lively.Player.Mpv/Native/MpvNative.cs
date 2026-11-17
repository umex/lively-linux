using System.Runtime.InteropServices;

namespace lively.Player.Mpv.Native
{
    /// <summary>
    /// libmpv 2.x (client API 2.x): client.h, render.h, render_gl.h.
    /// </summary>
    internal static unsafe partial class MpvNative
    {
        public const string Lib = "libmpv.so.2";

        // mpv_event_id
        public const int MPV_EVENT_NONE = 0;
        public const int MPV_EVENT_SHUTDOWN = 1;
        public const int MPV_EVENT_LOG_MESSAGE = 2;
        public const int MPV_EVENT_START_FILE = 6;
        public const int MPV_EVENT_END_FILE = 7;
        public const int MPV_EVENT_FILE_LOADED = 8;
        public const int MPV_EVENT_PLAYBACK_RESTART = 21;
        public const int MPV_EVENT_QUEUE_OVERFLOW = 24;

        // mpv_end_file_reason
        public const int MPV_END_FILE_REASON_EOF = 0;
        public const int MPV_END_FILE_REASON_STOP = 2;
        public const int MPV_END_FILE_REASON_QUIT = 3;
        public const int MPV_END_FILE_REASON_ERROR = 4;
        public const int MPV_END_FILE_REASON_REDIRECT = 5;

        // mpv_render_param_type
        public const int MPV_RENDER_PARAM_INVALID = 0;
        public const int MPV_RENDER_PARAM_API_TYPE = 1;
        public const int MPV_RENDER_PARAM_OPENGL_INIT_PARAMS = 2;
        public const int MPV_RENDER_PARAM_OPENGL_FBO = 3;
        public const int MPV_RENDER_PARAM_FLIP_Y = 4;
        public const int MPV_RENDER_PARAM_WL_DISPLAY = 9;
        public const int MPV_RENDER_PARAM_ADVANCED_CONTROL = 10;
        public const int MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME = 12;

        // mpv_render_update_flag
        public const ulong MPV_RENDER_UPDATE_FRAME = 1;

        [LibraryImport(Lib)]
        public static partial ulong mpv_client_api_version();

        [LibraryImport(Lib)]
        public static partial nint mpv_create();

        [LibraryImport(Lib)]
        public static partial int mpv_initialize(nint handle);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        public static partial int mpv_set_option_string(nint handle, string name, string data);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        public static partial int mpv_set_property_string(nint handle, string name, string data);

        /// <returns>A string to release with <see cref="mpv_free"/>, or 0.</returns>
        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        public static partial nint mpv_get_property_string(nint handle, string name);

        [LibraryImport(Lib)]
        public static partial void mpv_free(nint data);

        /// <param name="args">NULL-terminated array of UTF-8 strings.</param>
        [LibraryImport(Lib)]
        public static partial int mpv_command(nint handle, byte** args);

        /// <returns>Never null; <c>EventId == MPV_EVENT_NONE</c> when the queue is empty. Valid until the next call.</returns>
        [LibraryImport(Lib)]
        public static partial MpvEvent* mpv_wait_event(nint handle, double timeout);

        [LibraryImport(Lib)]
        public static partial void mpv_set_wakeup_callback(nint handle, delegate* unmanaged[Cdecl]<void*, void> callback, void* data);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        public static partial int mpv_request_log_messages(nint handle, string minLevel);

        /// <returns>Static string.</returns>
        [LibraryImport(Lib)]
        public static partial nint mpv_error_string(int error);

        [LibraryImport(Lib)]
        public static partial void mpv_terminate_destroy(nint handle);

        [LibraryImport(Lib)]
        public static partial int mpv_render_context_create(nint* context, nint handle, MpvRenderParam* parameters);

        [LibraryImport(Lib)]
        public static partial void mpv_render_context_set_update_callback(nint context, delegate* unmanaged[Cdecl]<void*, void> callback, void* data);

        /// <returns>mpv_render_update_flag bits.</returns>
        [LibraryImport(Lib)]
        public static partial ulong mpv_render_context_update(nint context);

        [LibraryImport(Lib)]
        public static partial int mpv_render_context_render(nint context, MpvRenderParam* parameters);

        [LibraryImport(Lib)]
        public static partial void mpv_render_context_report_swap(nint context);

        [LibraryImport(Lib)]
        public static partial void mpv_render_context_free(nint context);

        public static string ErrorString(int error) => Marshal.PtrToStringUTF8(mpv_error_string(error)) ?? ("error " + error);
    }

    /// <summary>struct mpv_event (24 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MpvEvent
    {
        public int EventId;
        public int Error;
        public ulong ReplyUserdata;
        public nint Data;
    }

    /// <summary>struct mpv_event_end_file (client API 2.x).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MpvEventEndFile
    {
        public int Reason;
        public int Error;
        public long PlaylistEntryId;
        public long PlaylistInsertId;
        public int PlaylistInsertNumEntries;
    }

    /// <summary>struct mpv_event_log_message.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MpvEventLogMessage
    {
        public byte* Prefix;
        public byte* Level;
        public byte* Text;
        public int LogLevel;
    }

    /// <summary>struct mpv_render_param (16 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MpvRenderParam
    {
        public int Type;
        public void* Data;

        public MpvRenderParam(int type, void* data)
        {
            Type = type;
            Data = data;
        }
    }

    /// <summary>struct mpv_opengl_init_params (client API 2.x: no extra_exts).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MpvOpenglInitParams
    {
        public delegate* unmanaged[Cdecl]<void*, byte*, void*> GetProcAddress;
        public void* GetProcAddressCtx;
    }

    /// <summary>struct mpv_opengl_fbo.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MpvOpenglFbo
    {
        public int Fbo;
        public int W;
        public int H;
        /// <summary>0 = unknown.</summary>
        public int InternalFormat;
    }
}
