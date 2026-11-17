using System.Runtime.InteropServices;

namespace lively.Player.Mpv.Native
{
    /// <summary>libwayland-egl (wayland-egl-core.h): the native window EGL renders into.</summary>
    internal static partial class WaylandEglNative
    {
        public const string Lib = "libwayland-egl.so.1";

        /// <returns><c>struct wl_egl_window*</c>, 0 on failure.</returns>
        [LibraryImport(Lib)]
        public static partial nint wl_egl_window_create(nint surface, int width, int height);

        /// <summary>Takes effect on the next eglSwapBuffers.</summary>
        [LibraryImport(Lib)]
        public static partial void wl_egl_window_resize(nint window, int width, int height, int dx, int dy);

        [LibraryImport(Lib)]
        public static partial void wl_egl_window_destroy(nint window);
    }
}
