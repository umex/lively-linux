using System.Runtime.InteropServices;

namespace lively.Player.Mpv.Native
{
    /// <summary>libEGL (EGL 1.5, egl.h / eglext.h). EGLBoolean is <c>unsigned int</c>, handles are pointers.</summary>
    internal static unsafe partial class EglNative
    {
        public const string Lib = "libEGL.so.1";

        public const uint EGL_TRUE = 1;
        public const int EGL_SUCCESS = 0x3000;
        public const int EGL_NONE = 0x3038;

        public const uint EGL_PLATFORM_WAYLAND_KHR = 0x31D8;
        public const uint EGL_OPENGL_ES_API = 0x30A0;
        public const uint EGL_OPENGL_API = 0x30A2;

        // eglChooseConfig attributes
        public const int EGL_ALPHA_SIZE = 0x3021;
        public const int EGL_BLUE_SIZE = 0x3022;
        public const int EGL_GREEN_SIZE = 0x3023;
        public const int EGL_RED_SIZE = 0x3024;
        public const int EGL_SURFACE_TYPE = 0x3033;
        public const int EGL_RENDERABLE_TYPE = 0x3040;
        public const int EGL_WINDOW_BIT = 0x4;
        public const int EGL_OPENGL_ES2_BIT = 0x4;
        public const int EGL_OPENGL_BIT = 0x8;
        public const int EGL_OPENGL_ES3_BIT = 0x40;

        // eglCreateContext attributes
        public const int EGL_CONTEXT_CLIENT_VERSION = 0x3098;

        // eglQueryString names
        public const int EGL_VENDOR = 0x3053;
        public const int EGL_VERSION = 0x3054;
        public const int EGL_CLIENT_APIS = 0x308D;

        [LibraryImport(Lib)]
        public static partial nint eglGetPlatformDisplay(uint platform, nint nativeDisplay, nint* attribs);

        [LibraryImport(Lib)]
        public static partial nint eglGetDisplay(nint nativeDisplay);

        [LibraryImport(Lib)]
        public static partial uint eglInitialize(nint display, int* major, int* minor);

        [LibraryImport(Lib)]
        public static partial nint eglQueryString(nint display, int name);

        [LibraryImport(Lib)]
        public static partial uint eglBindAPI(uint api);

        [LibraryImport(Lib)]
        public static partial uint eglChooseConfig(nint display, int* attribs, nint* configs, int configSize, int* numConfig);

        [LibraryImport(Lib)]
        public static partial uint eglGetConfigAttrib(nint display, nint config, int attribute, int* value);

        [LibraryImport(Lib)]
        public static partial nint eglCreateContext(nint display, nint config, nint shareContext, int* attribs);

        /// <param name="nativeWindow">For EGL_PLATFORM_WAYLAND_KHR: the <c>struct wl_egl_window*</c>.</param>
        [LibraryImport(Lib)]
        public static partial nint eglCreatePlatformWindowSurface(nint display, nint config, nint nativeWindow, nint* attribs);

        [LibraryImport(Lib)]
        public static partial nint eglCreateWindowSurface(nint display, nint config, nint nativeWindow, int* attribs);

        [LibraryImport(Lib)]
        public static partial uint eglMakeCurrent(nint display, nint draw, nint read, nint context);

        [LibraryImport(Lib)]
        public static partial uint eglSwapBuffers(nint display, nint surface);

        [LibraryImport(Lib)]
        public static partial uint eglSwapInterval(nint display, int interval);

        [LibraryImport(Lib)]
        public static partial uint eglDestroySurface(nint display, nint surface);

        [LibraryImport(Lib)]
        public static partial uint eglDestroyContext(nint display, nint context);

        [LibraryImport(Lib)]
        public static partial uint eglTerminate(nint display);

        [LibraryImport(Lib)]
        public static partial uint eglReleaseThread();

        [LibraryImport(Lib)]
        public static partial int eglGetError();

        [LibraryImport(Lib)]
        public static partial nint eglGetProcAddress(byte* name);

        /// <summary>The last EGL error as text, e.g. <c>EGL_BAD_MATCH (0x3009)</c>.</summary>
        public static string LastError()
        {
            var code = eglGetError();
            var name = code switch
            {
                0x3000 => "EGL_SUCCESS",
                0x3001 => "EGL_NOT_INITIALIZED",
                0x3002 => "EGL_BAD_ACCESS",
                0x3003 => "EGL_BAD_ALLOC",
                0x3004 => "EGL_BAD_ATTRIBUTE",
                0x3005 => "EGL_BAD_CONFIG",
                0x3006 => "EGL_BAD_CONTEXT",
                0x3007 => "EGL_BAD_CURRENT_SURFACE",
                0x3008 => "EGL_BAD_DISPLAY",
                0x3009 => "EGL_BAD_MATCH",
                0x300A => "EGL_BAD_NATIVE_PIXMAP",
                0x300B => "EGL_BAD_NATIVE_WINDOW",
                0x300C => "EGL_BAD_PARAMETER",
                0x300D => "EGL_BAD_SURFACE",
                0x300E => "EGL_CONTEXT_LOST",
                _ => "EGL error",
            };
            return $"{name} (0x{code:X4})";
        }
    }
}
