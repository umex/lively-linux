using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace lively.Core.Display.Wayland
{
    /// <summary>
    /// Minimal libwayland-client binding: registry and wl_output only.
    /// Checked against wayland-client-core.h / libwayland-client 1.26; needs libwayland ≥ 1.20
    /// for the non-variadic <c>wl_proxy_marshal_array_flags</c>.
    /// </summary>
    internal static unsafe partial class WaylandNative
    {
        private const string Lib = "libwayland-client.so.0";

        public const uint WL_MARSHAL_FLAG_DESTROY = 1;

        // wl_display requests
        public const uint WL_DISPLAY_GET_REGISTRY = 1;
        // wl_registry requests
        public const uint WL_REGISTRY_BIND = 0;
        // wl_output requests (since version 3)
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
        public static partial nint wl_proxy_marshal_array_flags(nint proxy, uint opcode, nint iface, uint version, uint flags, WlArgument* args);

        /// <returns>0 on success, -1 if the proxy already has a listener.</returns>
        [LibraryImport(Lib)]
        public static partial int wl_proxy_add_listener(nint proxy, void* implementation, void* data);

        [LibraryImport(Lib)]
        public static partial uint wl_proxy_get_version(nint proxy);

        [LibraryImport(Lib)]
        public static partial void wl_proxy_destroy(nint proxy);

        // Data symbols. Lazy so that hosts without Wayland never load the library.
        private static readonly Lazy<nint> Library = new(() => NativeLibrary.Load(Lib));
        private static readonly Lazy<nint> Registry = new(() => NativeLibrary.GetExport(Library.Value, "wl_registry_interface"));
        private static readonly Lazy<nint> Output = new(() => NativeLibrary.GetExport(Library.Value, "wl_output_interface"));

        /// <summary>Address of <c>const struct wl_interface wl_registry_interface</c>.</summary>
        public static nint RegistryInterface => Registry.Value;

        /// <summary>Address of <c>const struct wl_interface wl_output_interface</c>.</summary>
        public static nint OutputInterface => Output.Value;

        // struct wl_interface { const char *name; int version; int method_count; ... }
        public static nint InterfaceName(nint iface) => *(nint*)iface;

        public static int InterfaceVersion(nint iface) => *(int*)(iface + IntPtr.Size);
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
        /// <summary>x, y, physical_width, physical_height, subpixel, make, model, transform.</summary>
        public delegate* unmanaged[Cdecl]<void*, nint, int, int, int, int, int, byte*, byte*, int, void> Geometry;
        /// <summary>flags (1 = current, 2 = preferred), width, height, refresh in mHz.</summary>
        public delegate* unmanaged[Cdecl]<void*, nint, uint, int, int, int, void> Mode;
        public delegate* unmanaged[Cdecl]<void*, nint, void> Done;
        public delegate* unmanaged[Cdecl]<void*, nint, int, void> Scale;
        /// <summary>Since version 4.</summary>
        public delegate* unmanaged[Cdecl]<void*, nint, byte*, void> Name;
        /// <summary>Since version 4.</summary>
        public delegate* unmanaged[Cdecl]<void*, nint, byte*, void> Description;
    }
}
