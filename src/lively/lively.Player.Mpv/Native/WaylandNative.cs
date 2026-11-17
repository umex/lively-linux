using System.Runtime.InteropServices;

namespace lively.Player.Mpv.Native
{
    /// <summary>
    /// libwayland-client binding of the player: a superset of lively.Core's (display event loop, wl_compositor,
    /// wl_surface, wl_callback). Checked against wayland-client-core.h / wayland-client-protocol.h of
    /// libwayland 1.26; needs libwayland ≥ 1.20 for <c>wl_proxy_marshal_array_flags</c>.
    /// </summary>
    internal static unsafe partial class WaylandNative
    {
        public const string Lib = "libwayland-client.so.0";

        public const uint WL_MARSHAL_FLAG_DESTROY = 1;

        // wl_display requests
        public const uint WL_DISPLAY_GET_REGISTRY = 1;
        // wl_registry requests
        public const uint WL_REGISTRY_BIND = 0;
        // wl_compositor requests
        public const uint WL_COMPOSITOR_CREATE_SURFACE = 0;
        // wl_surface requests
        public const uint WL_SURFACE_DESTROY = 0;
        public const uint WL_SURFACE_ATTACH = 1;
        public const uint WL_SURFACE_DAMAGE = 2;
        public const uint WL_SURFACE_FRAME = 3;
        public const uint WL_SURFACE_COMMIT = 6;
        public const uint WL_SURFACE_SET_BUFFER_SCALE = 8;
        public const uint WL_SURFACE_SET_BUFFER_SCALE_SINCE_VERSION = 3;
        public const uint WL_SURFACE_DAMAGE_BUFFER = 9;
        // wl_output requests
        public const uint WL_OUTPUT_RELEASE = 0;
        public const uint WL_OUTPUT_RELEASE_SINCE_VERSION = 3;

        [LibraryImport(Lib)]
        public static partial nint wl_display_connect(byte* name);

        [LibraryImport(Lib)]
        public static partial void wl_display_disconnect(nint display);

        /// <returns>Number of dispatched events, -1 on error.</returns>
        [LibraryImport(Lib)]
        public static partial int wl_display_roundtrip(nint display);

        [LibraryImport(Lib)]
        public static partial int wl_display_get_fd(nint display);

        /// <returns>0 when the default queue is empty and reading may start, -1 when events are pending (dispatch them first).</returns>
        [LibraryImport(Lib)]
        public static partial int wl_display_prepare_read(nint display);

        /// <returns>0 on success, -1 on error (the connection is broken).</returns>
        [LibraryImport(Lib)]
        public static partial int wl_display_read_events(nint display);

        [LibraryImport(Lib)]
        public static partial void wl_display_cancel_read(nint display);

        /// <returns>Number of dispatched events, -1 on error.</returns>
        [LibraryImport(Lib)]
        public static partial int wl_display_dispatch_pending(nint display);

        /// <returns>Bytes sent, -1 on error (errno; EAGAIN when the socket buffer is full).</returns>
        [LibraryImport(Lib, SetLastError = true)]
        public static partial int wl_display_flush(nint display);

        /// <returns>The errno of the last fatal error, 0 if none (EPROTO for a protocol error).</returns>
        [LibraryImport(Lib)]
        public static partial int wl_display_get_error(nint display);

        [LibraryImport(Lib)]
        public static partial nint wl_proxy_marshal_array_flags(nint proxy, uint opcode, nint iface, uint version, uint flags, WlArgument* args);

        /// <returns>0 on success, -1 if the proxy already has a listener.</returns>
        [LibraryImport(Lib)]
        public static partial int wl_proxy_add_listener(nint proxy, void* implementation, void* data);

        [LibraryImport(Lib)]
        public static partial uint wl_proxy_get_version(nint proxy);

        [LibraryImport(Lib)]
        public static partial void wl_proxy_destroy(nint proxy);

        // Data symbols, resolved on first use.
        private static readonly Lazy<nint> Library = new(() => NativeLibrary.Load(Lib));
        private static readonly Lazy<nint> Registry = new(() => NativeLibrary.GetExport(Library.Value, "wl_registry_interface"));
        private static readonly Lazy<nint> Compositor = new(() => NativeLibrary.GetExport(Library.Value, "wl_compositor_interface"));
        private static readonly Lazy<nint> Surface = new(() => NativeLibrary.GetExport(Library.Value, "wl_surface_interface"));
        private static readonly Lazy<nint> Callback = new(() => NativeLibrary.GetExport(Library.Value, "wl_callback_interface"));
        private static readonly Lazy<nint> Output = new(() => NativeLibrary.GetExport(Library.Value, "wl_output_interface"));

        public static nint RegistryInterface => Registry.Value;

        public static nint CompositorInterface => Compositor.Value;

        public static nint SurfaceInterface => Surface.Value;

        public static nint CallbackInterface => Callback.Value;

        public static nint OutputInterface => Output.Value;

        // struct wl_interface { const char *name; int version; ... }
        public static nint InterfaceName(nint iface) => *(nint*)iface;

        public static int InterfaceVersion(nint iface) => *(int*)(iface + IntPtr.Size);

        /// <summary>A request without a new object. <paramref name="args"/> must not be null even without arguments.</summary>
        public static void Request(nint proxy, uint opcode, WlArgument* args, uint flags = 0)
        {
            wl_proxy_marshal_array_flags(proxy, opcode, 0, wl_proxy_get_version(proxy), flags, args);
        }

        /// <summary>A request without arguments.</summary>
        public static void Request(nint proxy, uint opcode, uint flags = 0)
        {
            WlArgument none = default;
            wl_proxy_marshal_array_flags(proxy, opcode, 0, wl_proxy_get_version(proxy), flags, &none);
        }
    }

    /// <summary>union wl_argument (8 bytes on 64-bit).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    internal struct WlArgument
    {
        [FieldOffset(0)] public int i;
        [FieldOffset(0)] public uint u;
        [FieldOffset(0)] public nint s;
        [FieldOffset(0)] public nint o;
        [FieldOffset(0)] public uint n;
    }

    /// <summary>struct wl_registry_listener.</summary>
    internal unsafe struct WlRegistryListener
    {
        public delegate* unmanaged[Cdecl]<void*, nint, uint, byte*, uint, void> Global;
        public delegate* unmanaged[Cdecl]<void*, nint, uint, void> GlobalRemove;
    }

    /// <summary>struct wl_output_listener, version 4 event order.</summary>
    internal unsafe struct WlOutputListener
    {
        public delegate* unmanaged[Cdecl]<void*, nint, int, int, int, int, int, byte*, byte*, int, void> Geometry;
        public delegate* unmanaged[Cdecl]<void*, nint, uint, int, int, int, void> Mode;
        public delegate* unmanaged[Cdecl]<void*, nint, void> Done;
        public delegate* unmanaged[Cdecl]<void*, nint, int, void> Scale;
        public delegate* unmanaged[Cdecl]<void*, nint, byte*, void> Name;
        public delegate* unmanaged[Cdecl]<void*, nint, byte*, void> Description;
    }

    /// <summary>struct wl_callback_listener.</summary>
    internal unsafe struct WlCallbackListener
    {
        /// <summary>callback_data: for wl_surface.frame, the current time in milliseconds.</summary>
        public delegate* unmanaged[Cdecl]<void*, nint, uint, void> Done;
    }
}
