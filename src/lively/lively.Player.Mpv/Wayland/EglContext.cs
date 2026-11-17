using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using lively.Player.Mpv.Native;

namespace lively.Player.Mpv.Wayland
{
    /// <summary>
    /// An EGL window surface on the layer surface's wl_surface, with an OpenGL ES 3 (else ES 2, else desktop GL)
    /// context that is current on the main thread for the player's lifetime.
    /// </summary>
    internal sealed unsafe class EglContext : IDisposable
    {
        private static readonly string[] FallbackGlLibraries = ["libGLESv2.so.2", "libOpenGL.so.0", "libGL.so.1"];
        private static readonly Lazy<nint[]> FallbackHandles = new(() => FallbackGlLibraries
            .Select(name => NativeLibrary.TryLoad(name, out var handle) ? handle : 0)
            .Where(handle => handle != 0)
            .ToArray());

        private readonly PlayerLog log;
        private nint display;
        private nint config;
        private nint context;
        private nint eglSurface;
        private nint window;
        private bool initialized;

        private EglContext(PlayerLog log)
        {
            this.log = log;
        }

        /// <summary>mpv's <c>get_proc_address</c>: eglGetProcAddress, else the GL library's export.</summary>
        public static delegate* unmanaged[Cdecl]<void*, byte*, void*> GetProcAddressCallback => &GetProcAddress;

        /// <param name="wlDisplay"><c>struct wl_display*</c>.</param>
        /// <param name="wlSurface">The <c>wl_surface</c> to render into.</param>
        /// <param name="width">Buffer width in pixels (logical width × scale).</param>
        /// <param name="height">Buffer height in pixels.</param>
        /// <exception cref="PlayerException">Any EGL step failed.</exception>
        public static EglContext Create(nint wlDisplay, nint wlSurface, int width, int height, PlayerLog log)
        {
            var egl = new EglContext(log);
            try
            {
                egl.Initialize(wlDisplay, wlSurface, width, height);
                return egl;
            }
            catch
            {
                egl.Dispose();
                throw;
            }
        }

        private void Initialize(nint wlDisplay, nint wlSurface, int width, int height)
        {
            try
            {
                display = EglNative.eglGetPlatformDisplay(EglNative.EGL_PLATFORM_WAYLAND_KHR, wlDisplay, null);
            }
            catch (EntryPointNotFoundException)
            {
                display = 0;
            }
            if (display == 0)
            {
                // EGL 1.4 without eglGetPlatformDisplay: Mesa detects a wl_display pointer.
                display = EglNative.eglGetDisplay(wlDisplay);
            }
            if (display == 0)
            {
                throw new PlayerException("eglGetPlatformDisplay failed: " + EglNative.LastError());
            }

            int major, minor;
            if (EglNative.eglInitialize(display, &major, &minor) != EglNative.EGL_TRUE)
            {
                throw new PlayerException("eglInitialize failed: " + EglNative.LastError());
            }
            initialized = true;
            log.Write($"EGL {major}.{minor} {QueryString(EglNative.EGL_VENDOR)} {QueryString(EglNative.EGL_VERSION)}; APIs: {QueryString(EglNative.EGL_CLIENT_APIS)}");

            if (!TryCreateContext(EglNative.EGL_OPENGL_ES_API, EglNative.EGL_OPENGL_ES2_BIT, [3, 2])
                && !TryCreateContext(EglNative.EGL_OPENGL_API, EglNative.EGL_OPENGL_BIT, [0]))
            {
                throw new PlayerException("cannot create an OpenGL ES or OpenGL context: " + EglNative.LastError());
            }

            window = WaylandEglNative.wl_egl_window_create(wlSurface, width, height);
            if (window == 0)
            {
                throw new PlayerException($"wl_egl_window_create({width}x{height}) failed");
            }

            try
            {
                eglSurface = EglNative.eglCreatePlatformWindowSurface(display, config, window, null);
            }
            catch (EntryPointNotFoundException)
            {
                eglSurface = 0;
            }
            if (eglSurface == 0)
            {
                eglSurface = EglNative.eglCreateWindowSurface(display, config, window, null);
            }
            if (eglSurface == 0)
            {
                throw new PlayerException("eglCreatePlatformWindowSurface failed: " + EglNative.LastError());
            }

            MakeCurrent();
            // Never block in eglSwapBuffers on EGL's own frame callback; the main loop paces with its own.
            if (EglNative.eglSwapInterval(display, 0) != EglNative.EGL_TRUE)
            {
                log.Write("eglSwapInterval(0) failed: " + EglNative.LastError());
            }
        }

        /// <param name="api">EGL_OPENGL_ES_API or EGL_OPENGL_API.</param>
        /// <param name="renderableBit">EGL_RENDERABLE_TYPE bit the config must support.</param>
        /// <param name="versions">EGL_CONTEXT_CLIENT_VERSION values to try in order; 0 = no attribute.</param>
        private bool TryCreateContext(uint api, int renderableBit, int[] versions)
        {
            var apiName = api == EglNative.EGL_OPENGL_ES_API ? "OpenGL ES" : "OpenGL";
            if (EglNative.eglBindAPI(api) != EglNative.EGL_TRUE)
            {
                log.Write($"eglBindAPI({apiName}) failed: {EglNative.LastError()}");
                return false;
            }
            var chosen = ChooseConfig(renderableBit);
            if (chosen == 0)
            {
                log.Write($"no EGL config for {apiName}: {EglNative.LastError()}");
                return false;
            }
            var attribs = stackalloc int[3];
            foreach (var version in versions)
            {
                if (version > 0)
                {
                    attribs[0] = EglNative.EGL_CONTEXT_CLIENT_VERSION;
                    attribs[1] = version;
                    attribs[2] = EglNative.EGL_NONE;
                }
                else
                {
                    attribs[0] = EglNative.EGL_NONE;
                }
                var created = EglNative.eglCreateContext(display, chosen, 0, attribs);
                if (created != 0)
                {
                    config = chosen;
                    context = created;
                    log.Write($"EGL context: {apiName}" + (version > 0 ? " " + version : string.Empty));
                    return true;
                }
                log.Write($"eglCreateContext({apiName} {version}) failed: {EglNative.LastError()}");
            }
            return false;
        }

        /// <summary>
        /// A window config with exactly 8-bit RGB, preferably without alpha (an alpha channel could make the
        /// wallpaper translucent), else with 8-bit alpha, else the first match. eglChooseConfig sorts deeper
        /// formats (10-bit) first, so the configs are filtered here.
        /// </summary>
        private nint ChooseConfig(int renderableBit)
        {
            var attribs = stackalloc int[]
            {
                EglNative.EGL_SURFACE_TYPE, EglNative.EGL_WINDOW_BIT,
                EglNative.EGL_RENDERABLE_TYPE, renderableBit,
                EglNative.EGL_RED_SIZE, 8,
                EglNative.EGL_GREEN_SIZE, 8,
                EglNative.EGL_BLUE_SIZE, 8,
                EglNative.EGL_ALPHA_SIZE, 0,
                EglNative.EGL_NONE,
            };
            int count;
            if (EglNative.eglChooseConfig(display, attribs, null, 0, &count) != EglNative.EGL_TRUE || count <= 0)
            {
                return 0;
            }
            var configs = new nint[count];
            fixed (nint* p = configs)
            {
                if (EglNative.eglChooseConfig(display, attribs, p, count, &count) != EglNative.EGL_TRUE || count <= 0)
                {
                    return 0;
                }
            }

            nint withAlpha = 0;
            for (var i = 0; i < count; i++)
            {
                var c = configs[i];
                if (Attribute(c, EglNative.EGL_RED_SIZE) != 8 || Attribute(c, EglNative.EGL_GREEN_SIZE) != 8 || Attribute(c, EglNative.EGL_BLUE_SIZE) != 8)
                {
                    continue;
                }
                var alpha = Attribute(c, EglNative.EGL_ALPHA_SIZE);
                if (alpha == 0)
                {
                    return c;
                }
                if (alpha == 8 && withAlpha == 0)
                {
                    withAlpha = c;
                }
            }
            return withAlpha != 0 ? withAlpha : configs[0];
        }

        private int Attribute(nint c, int attribute)
        {
            int value;
            return EglNative.eglGetConfigAttrib(display, c, attribute, &value) == EglNative.EGL_TRUE ? value : -1;
        }

        private string QueryString(int name) => Marshal.PtrToStringUTF8(EglNative.eglQueryString(display, name)) ?? "?";

        /// <exception cref="PlayerException">eglMakeCurrent failed.</exception>
        public void MakeCurrent()
        {
            if (EglNative.eglMakeCurrent(display, eglSurface, eglSurface, context) != EglNative.EGL_TRUE)
            {
                throw new PlayerException("eglMakeCurrent failed: " + EglNative.LastError());
            }
        }

        /// <summary>New buffer size in pixels; applies at the next swap.</summary>
        public void Resize(int width, int height)
        {
            WaylandEglNative.wl_egl_window_resize(window, width, height, 0, 0);
        }

        /// <summary>Presents the frame: attaches the buffer, damages and commits the wl_surface.</summary>
        /// <exception cref="PlayerException">eglSwapBuffers failed.</exception>
        public void SwapBuffers()
        {
            if (EglNative.eglSwapBuffers(display, eglSurface) != EglNative.EGL_TRUE)
            {
                throw new PlayerException("eglSwapBuffers failed: " + EglNative.LastError());
            }
        }

        public void Dispose()
        {
            if (display != 0 && initialized)
            {
                EglNative.eglMakeCurrent(display, 0, 0, 0);
                if (eglSurface != 0)
                {
                    EglNative.eglDestroySurface(display, eglSurface);
                    eglSurface = 0;
                }
                if (context != 0)
                {
                    EglNative.eglDestroyContext(display, context);
                    context = 0;
                }
            }
            if (window != 0)
            {
                WaylandEglNative.wl_egl_window_destroy(window);
                window = 0;
            }
            if (display != 0)
            {
                if (initialized)
                {
                    EglNative.eglTerminate(display);
                    initialized = false;
                }
                EglNative.eglReleaseThread();
                display = 0;
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void* GetProcAddress(void* ctx, byte* name)
        {
            try
            {
                var address = EglNative.eglGetProcAddress(name);
                if (address != 0)
                {
                    return (void*)address;
                }
                var symbol = Marshal.PtrToStringUTF8((nint)name);
                if (symbol != null)
                {
                    foreach (var library in FallbackHandles.Value)
                    {
                        if (NativeLibrary.TryGetExport(library, symbol, out address))
                        {
                            return (void*)address;
                        }
                    }
                }
            }
            catch
            {
                // Never throw into libmpv.
            }
            return null;
        }
    }
}
