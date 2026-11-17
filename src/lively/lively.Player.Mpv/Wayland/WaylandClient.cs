using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using lively.Player.Mpv.Native;

namespace lively.Player.Mpv.Wayland
{
    /// <summary>One bound wl_output.</summary>
    internal sealed class WaylandOutput
    {
        public uint Global;
        public uint Version;
        public nint Proxy;
        public string? Name;
        public string? Description;
        public string? Make;
        public string? Model;
        public int Width;
        public int Height;
        public int Scale = 1;

        /// <summary>The id lively.Core gives this output (wl_output.name, else make-model-global).</summary>
        public string Id => !string.IsNullOrEmpty(Name) ? Name : $"{Make}-{Model}-{Global}";
    }

    /// <summary>A configure event of the layer surface, not yet acknowledged.</summary>
    internal readonly record struct LayerConfigure(uint Serial, uint Width, uint Height);

    /// <summary>
    /// The player's Wayland connection: registry, wl_compositor, zwlr_layer_shell_v1 and the outputs, one wl_surface
    /// with a background layer surface, frame callbacks, and the read/dispatch plumbing of the main loop.
    /// All calls on the main thread. Listener callbacks never throw; the first failure is kept in
    /// <see cref="ThrowOnCallbackError"/>.
    /// </summary>
    internal sealed unsafe class WaylandClient : IDisposable
    {
        private const uint MaxCompositorVersion = 4;
        private const uint MaxLayerShellVersion = 4;
        private const uint MaxOutputVersion = 4;

        private readonly PlayerLog log;
        private readonly List<WaylandOutput> outputs = [];
        private readonly List<GCHandle> outputHandles = [];
        private GCHandle self;
        private nint registry;
        private uint compositorGlobal;
        private uint compositorAdvertised;
        private uint layerShellGlobal;
        private uint layerShellAdvertised;
        private nint compositor;
        private nint layerShell;
        private nint surface;
        private nint layerSurface;
        private nint frameCallback;
        private LayerConfigure? pendingConfigure;
        private Exception? callbackError;
        private bool disposed;

        private static readonly Lazy<nint> RegistryListener = new(() =>
        {
            var listener = (WlRegistryListener*)NativeMemory.Alloc((nuint)sizeof(WlRegistryListener));
            listener->Global = &OnGlobal;
            listener->GlobalRemove = &OnGlobalRemove;
            return (nint)listener;
        });

        private static readonly Lazy<nint> OutputListener = new(() =>
        {
            var listener = (WlOutputListener*)NativeMemory.Alloc((nuint)sizeof(WlOutputListener));
            listener->Geometry = &OnOutputGeometry;
            listener->Mode = &OnOutputMode;
            listener->Done = &OnOutputDone;
            listener->Scale = &OnOutputScale;
            listener->Name = &OnOutputName;
            listener->Description = &OnOutputDescription;
            return (nint)listener;
        });

        private static readonly Lazy<nint> LayerSurfaceListener = new(() =>
        {
            var listener = (ZwlrLayerSurfaceListener*)NativeMemory.Alloc((nuint)sizeof(ZwlrLayerSurfaceListener));
            listener->Configure = &OnLayerConfigure;
            listener->Closed = &OnLayerClosed;
            return (nint)listener;
        });

        private static readonly Lazy<nint> FrameListener = new(() =>
        {
            var listener = (WlCallbackListener*)NativeMemory.Alloc((nuint)sizeof(WlCallbackListener));
            listener->Done = &OnFrameDone;
            return (nint)listener;
        });

        private WaylandClient(nint display, PlayerLog log)
        {
            Display = display;
            this.log = log;
            self = GCHandle.Alloc(this);
        }

        /// <summary><c>struct wl_display*</c>.</summary>
        public nint Display { get; }

        public int Fd => WaylandNative.wl_display_get_fd(Display);

        /// <summary>The wl_surface of the layer surface (0 before <see cref="CreateLayerSurface"/>).</summary>
        public nint Surface => surface;

        /// <summary>The wl_surface version (= the bound wl_compositor version).</summary>
        public uint SurfaceVersion => surface != 0 ? WaylandNative.wl_proxy_get_version(surface) : 0;

        public IReadOnlyList<WaylandOutput> Outputs => outputs;

        /// <summary>The compositor closed the layer surface (output gone, or it refused the surface).</summary>
        public bool Closed { get; private set; }

        /// <summary>A frame callback is outstanding; the next frame should wait for its done event.</summary>
        public bool FrameCallbackPending => frameCallback != 0;

        /// <summary>
        /// Connects to $WAYLAND_DISPLAY and binds wl_compositor, zwlr_layer_shell_v1 and every wl_output.
        /// </summary>
        /// <exception cref="PlayerException">No compositor, or it lacks wl_compositor or zwlr_layer_shell_v1.</exception>
        public static WaylandClient Connect(PlayerLog log)
        {
            var display = WaylandNative.wl_display_connect(null);
            if (display == 0)
            {
                throw new PlayerException("cannot connect to the Wayland display (WAYLAND_DISPLAY=" + Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") + ")");
            }
            var client = new WaylandClient(display, log);
            try
            {
                client.BindGlobals();
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private void BindGlobals()
        {
            var args = stackalloc WlArgument[1];
            args[0].n = 0;
            registry = WaylandNative.wl_proxy_marshal_array_flags(Display, WaylandNative.WL_DISPLAY_GET_REGISTRY,
                WaylandNative.RegistryInterface, WaylandNative.wl_proxy_get_version(Display), 0, args);
            if (registry == 0)
            {
                throw new PlayerException("wl_display.get_registry failed");
            }
            WaylandNative.wl_proxy_add_listener(registry, (void*)RegistryListener.Value, (void*)GCHandle.ToIntPtr(self));

            // First roundtrip: the globals (recorded in OnGlobal). Bind, then a second roundtrip for the outputs' events.
            Roundtrip();
            if (compositorGlobal == 0)
            {
                throw new PlayerException("compositor does not advertise wl_compositor");
            }
            if (layerShellGlobal == 0)
            {
                throw new PlayerException("compositor does not support wlr-layer-shell (" + LayerShellProtocol.ShellName + ")");
            }

            var compositorVersion = Math.Min(Math.Min(compositorAdvertised, MaxCompositorVersion),
                (uint)Math.Max(1, WaylandNative.InterfaceVersion(WaylandNative.CompositorInterface)));
            compositor = Bind(compositorGlobal, WaylandNative.CompositorInterface, compositorVersion);
            var layerShellVersion = Math.Min(layerShellAdvertised, MaxLayerShellVersion);
            layerShell = Bind(layerShellGlobal, LayerShellProtocol.ShellInterface, layerShellVersion);
            if (compositor == 0 || layerShell == 0)
            {
                throw new PlayerException("binding wl_compositor or " + LayerShellProtocol.ShellName + " failed");
            }

            var outputVersionLimit = (uint)Math.Max(1, WaylandNative.InterfaceVersion(WaylandNative.OutputInterface));
            foreach (var output in outputs)
            {
                output.Version = Math.Min(Math.Min(output.Version, MaxOutputVersion), outputVersionLimit);
                output.Proxy = Bind(output.Global, WaylandNative.OutputInterface, output.Version);
                if (output.Proxy != 0)
                {
                    var handle = GCHandle.Alloc(output);
                    outputHandles.Add(handle);
                    WaylandNative.wl_proxy_add_listener(output.Proxy, (void*)OutputListener.Value, (void*)GCHandle.ToIntPtr(handle));
                }
            }
            Roundtrip();
            log.Write($"Wayland: wl_compositor v{compositorVersion}, {LayerShellProtocol.ShellName} v{layerShellVersion} (advertised v{layerShellAdvertised}), outputs: "
                + string.Join(", ", outputs.Select(o => $"{o.Id} {o.Width}x{o.Height} scale {o.Scale}")));
        }

        private void Roundtrip()
        {
            if (WaylandNative.wl_display_roundtrip(Display) < 0)
            {
                throw new PlayerException("Wayland roundtrip failed (" + ErrorText() + ")");
            }
            ThrowOnCallbackError();
        }

        private nint Bind(uint name, nint iface, uint version)
        {
            var args = stackalloc WlArgument[4];
            args[0].u = name;
            args[1].s = WaylandNative.InterfaceName(iface);
            args[2].u = version;
            args[3].n = 0;
            return WaylandNative.wl_proxy_marshal_array_flags(registry, WaylandNative.WL_REGISTRY_BIND, iface, version, 0, args);
        }

        /// <summary>
        /// The output called <paramref name="name"/>; without a name, the first one.
        /// </summary>
        /// <exception cref="PlayerException">No such output, or no output at all.</exception>
        public WaylandOutput SelectOutput(string? name)
        {
            var bound = outputs.Where(o => o.Proxy != 0).ToList();
            if (!string.IsNullOrEmpty(name))
            {
                return bound.FirstOrDefault(o => o.Id == name)
                    ?? throw new PlayerException($"output \"{name}\" not found; outputs: " + (bound.Count > 0 ? string.Join(", ", bound.Select(o => o.Id)) : "none"));
            }
            var first = bound.FirstOrDefault() ?? throw new PlayerException("the compositor has no outputs");
            log.Write("no output given, using the first one: " + first.Id);
            return first;
        }

        /// <summary>
        /// Creates the wl_surface and its background layer surface on <paramref name="output"/>, anchored to all
        /// edges with size 0x0 (the compositor picks the output size), no exclusive zone, no keyboard, and commits
        /// it without a buffer; the compositor answers with a configure event.
        /// </summary>
        public void CreateLayerSurface(WaylandOutput output, string nameSpace)
        {
            if (surface != 0)
            {
                throw new InvalidOperationException("The layer surface already exists.");
            }
            var args = stackalloc WlArgument[5];
            args[0].n = 0;
            surface = WaylandNative.wl_proxy_marshal_array_flags(compositor, WaylandNative.WL_COMPOSITOR_CREATE_SURFACE,
                WaylandNative.SurfaceInterface, WaylandNative.wl_proxy_get_version(compositor), 0, args);
            if (surface == 0)
            {
                throw new PlayerException("wl_compositor.create_surface failed");
            }

            var ns = Marshal.StringToCoTaskMemUTF8(nameSpace);
            try
            {
                args[0].n = 0;
                args[1].o = surface;
                args[2].o = output.Proxy;
                args[3].u = (uint)Layer.Background;
                args[4].s = ns;
                layerSurface = WaylandNative.wl_proxy_marshal_array_flags(layerShell, LayerShellProtocol.ZWLR_LAYER_SHELL_V1_GET_LAYER_SURFACE,
                    LayerShellProtocol.SurfaceInterface, WaylandNative.wl_proxy_get_version(layerShell), 0, args);
            }
            finally
            {
                Marshal.FreeCoTaskMem(ns);
            }
            if (layerSurface == 0)
            {
                throw new PlayerException("zwlr_layer_shell_v1.get_layer_surface failed");
            }
            WaylandNative.wl_proxy_add_listener(layerSurface, (void*)LayerSurfaceListener.Value, (void*)GCHandle.ToIntPtr(self));

            args[0].u = 0;
            args[1].u = 0;
            WaylandNative.Request(layerSurface, LayerShellProtocol.ZWLR_LAYER_SURFACE_V1_SET_SIZE, args);
            args[0].u = (uint)Anchor.All;
            WaylandNative.Request(layerSurface, LayerShellProtocol.ZWLR_LAYER_SURFACE_V1_SET_ANCHOR, args);
            args[0].i = -1; // do not move for other surfaces' exclusive zones
            WaylandNative.Request(layerSurface, LayerShellProtocol.ZWLR_LAYER_SURFACE_V1_SET_EXCLUSIVE_ZONE, args);
            args[0].u = (uint)KeyboardInteractivity.None;
            WaylandNative.Request(layerSurface, LayerShellProtocol.ZWLR_LAYER_SURFACE_V1_SET_KEYBOARD_INTERACTIVITY, args);
            Commit();
        }

        /// <summary>The latest configure since the last call, or null.</summary>
        public LayerConfigure? TakeConfigure()
        {
            var configure = pendingConfigure;
            pendingConfigure = null;
            return configure;
        }

        public void AckConfigure(uint serial)
        {
            var args = stackalloc WlArgument[1];
            args[0].u = serial;
            WaylandNative.Request(layerSurface, LayerShellProtocol.ZWLR_LAYER_SURFACE_V1_ACK_CONFIGURE, args);
        }

        /// <summary>wl_surface.set_buffer_scale; skipped for 1 and on wl_surface versions below 3.</summary>
        public void SetBufferScale(int scale)
        {
            if (scale <= 1 || SurfaceVersion < WaylandNative.WL_SURFACE_SET_BUFFER_SCALE_SINCE_VERSION)
            {
                return;
            }
            var args = stackalloc WlArgument[1];
            args[0].i = scale;
            WaylandNative.Request(surface, WaylandNative.WL_SURFACE_SET_BUFFER_SCALE, args);
        }

        /// <summary>
        /// Requests a frame callback; it is sent with the next commit (eglSwapBuffers commits). The callback
        /// proxy is destroyed in its done event.
        /// </summary>
        public void RequestFrame()
        {
            if (frameCallback != 0)
            {
                return;
            }
            var args = stackalloc WlArgument[1];
            args[0].n = 0;
            frameCallback = WaylandNative.wl_proxy_marshal_array_flags(surface, WaylandNative.WL_SURFACE_FRAME,
                WaylandNative.CallbackInterface, WaylandNative.wl_proxy_get_version(surface), 0, args);
            if (frameCallback != 0)
            {
                WaylandNative.wl_proxy_add_listener(frameCallback, (void*)FrameListener.Value, (void*)GCHandle.ToIntPtr(self));
            }
        }

        /// <summary>Stops waiting for the outstanding frame callback (its done event, if it ever comes, is dropped).</summary>
        public void AbandonFrame()
        {
            if (frameCallback != 0)
            {
                WaylandNative.wl_proxy_destroy(frameCallback);
                frameCallback = 0;
            }
        }

        public void Commit() => WaylandNative.Request(surface, WaylandNative.WL_SURFACE_COMMIT);

        /// <returns>True when reading may start; false when events are queued (dispatch them, then try again).</returns>
        public bool PrepareRead() => WaylandNative.wl_display_prepare_read(Display) == 0;

        /// <returns>0 when everything was sent; otherwise the errno (EAGAIN: wait for POLLOUT).</returns>
        public int Flush()
        {
            if (WaylandNative.wl_display_flush(Display) >= 0)
            {
                return 0;
            }
            var errno = Marshal.GetLastPInvokeError();
            return errno != 0 ? errno : -1;
        }

        public bool ReadEvents() => WaylandNative.wl_display_read_events(Display) == 0;

        public void CancelRead() => WaylandNative.wl_display_cancel_read(Display);

        /// <exception cref="PlayerException">The connection failed, or a listener callback failed.</exception>
        public void DispatchPending()
        {
            if (WaylandNative.wl_display_dispatch_pending(Display) < 0)
            {
                throw new PlayerException("lost the Wayland connection (" + ErrorText() + ")");
            }
            ThrowOnCallbackError();
        }

        public void ThrowOnCallbackError()
        {
            if (callbackError != null)
            {
                throw new PlayerException("Wayland callback failed: " + callbackError.Message, callbackError);
            }
        }

        public string ErrorText()
        {
            var errno = WaylandNative.wl_display_get_error(Display);
            return errno switch
            {
                0 => "no error recorded",
                71 => "protocol error (EPROTO); run with WAYLAND_DEBUG=1",
                _ => "errno " + errno,
            };
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (frameCallback != 0)
            {
                WaylandNative.wl_proxy_destroy(frameCallback);
                frameCallback = 0;
            }
            if (layerSurface != 0)
            {
                WaylandNative.Request(layerSurface, LayerShellProtocol.ZWLR_LAYER_SURFACE_V1_DESTROY, WaylandNative.WL_MARSHAL_FLAG_DESTROY);
                layerSurface = 0;
            }
            if (surface != 0)
            {
                WaylandNative.Request(surface, WaylandNative.WL_SURFACE_DESTROY, WaylandNative.WL_MARSHAL_FLAG_DESTROY);
                surface = 0;
            }
            if (layerShell != 0)
            {
                if (WaylandNative.wl_proxy_get_version(layerShell) >= LayerShellProtocol.ZWLR_LAYER_SHELL_V1_DESTROY_SINCE_VERSION)
                {
                    WaylandNative.Request(layerShell, LayerShellProtocol.ZWLR_LAYER_SHELL_V1_DESTROY, WaylandNative.WL_MARSHAL_FLAG_DESTROY);
                }
                else
                {
                    WaylandNative.wl_proxy_destroy(layerShell);
                }
                layerShell = 0;
            }
            if (compositor != 0)
            {
                // wl_compositor has no destructor request.
                WaylandNative.wl_proxy_destroy(compositor);
                compositor = 0;
            }
            foreach (var output in outputs)
            {
                if (output.Proxy == 0)
                {
                    continue;
                }
                if (output.Version >= WaylandNative.WL_OUTPUT_RELEASE_SINCE_VERSION)
                {
                    WaylandNative.Request(output.Proxy, WaylandNative.WL_OUTPUT_RELEASE, WaylandNative.WL_MARSHAL_FLAG_DESTROY);
                }
                else
                {
                    WaylandNative.wl_proxy_destroy(output.Proxy);
                }
                output.Proxy = 0;
            }
            if (registry != 0)
            {
                // wl_registry has no destructor request.
                WaylandNative.wl_proxy_destroy(registry);
                registry = 0;
            }
            WaylandNative.wl_display_flush(Display);
            WaylandNative.wl_display_disconnect(Display);
            foreach (var handle in outputHandles)
            {
                handle.Free();
            }
            outputHandles.Clear();
            if (self.IsAllocated)
            {
                self.Free();
            }
        }

        private static T? Target<T>(void* data) where T : class => GCHandle.FromIntPtr((nint)data).Target as T;

        private static string? Utf8(byte* s) => s == null ? null : Marshal.PtrToStringUTF8((nint)s);

        private static void Fail(void* data, Exception e)
        {
            try
            {
                if (Target<WaylandClient>(data) is { } client)
                {
                    client.callbackError ??= e;
                }
            }
            catch
            {
            }
        }

        #region registry

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnGlobal(void* data, nint registry, uint name, byte* iface, uint version)
        {
            try
            {
                var client = Target<WaylandClient>(data);
                var interfaceName = Utf8(iface);
                if (client == null || interfaceName == null)
                {
                    return;
                }
                switch (interfaceName)
                {
                    case "wl_compositor":
                        client.compositorGlobal = name;
                        client.compositorAdvertised = version;
                        break;
                    case LayerShellProtocol.ShellName:
                        client.layerShellGlobal = name;
                        client.layerShellAdvertised = version;
                        break;
                    case "wl_output":
                        if (client.registry != 0 && client.compositor != 0)
                        {
                            // A hotplugged output; this player stays on the one it was started for.
                            client.log.Write("ignoring new wl_output global " + name);
                            break;
                        }
                        client.outputs.Add(new WaylandOutput { Global = name, Version = version });
                        break;
                }
            }
            catch (Exception e)
            {
                Fail(data, e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnGlobalRemove(void* data, nint registry, uint name)
        {
            // When our output goes away the compositor closes the layer surface (Closed).
        }

        #endregion

        #region layer surface and frame callback

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnLayerConfigure(void* data, nint layerSurface, uint serial, uint width, uint height)
        {
            try
            {
                if (Target<WaylandClient>(data) is { } client)
                {
                    client.pendingConfigure = new LayerConfigure(serial, width, height);
                }
            }
            catch (Exception e)
            {
                Fail(data, e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnLayerClosed(void* data, nint layerSurface)
        {
            try
            {
                if (Target<WaylandClient>(data) is { } client)
                {
                    client.Closed = true;
                }
            }
            catch (Exception e)
            {
                Fail(data, e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnFrameDone(void* data, nint callback, uint time)
        {
            try
            {
                WaylandNative.wl_proxy_destroy(callback);
                if (Target<WaylandClient>(data) is { } client && client.frameCallback == callback)
                {
                    client.frameCallback = 0;
                }
            }
            catch (Exception e)
            {
                Fail(data, e);
            }
        }

        #endregion

        #region output

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnOutputGeometry(void* data, nint output, int x, int y, int physicalWidth, int physicalHeight, int subpixel, byte* make, byte* model, int transform)
        {
            try
            {
                if (Target<WaylandOutput>(data) is { } o)
                {
                    o.Make = Utf8(make);
                    o.Model = Utf8(model);
                }
            }
            catch
            {
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnOutputMode(void* data, nint output, uint flags, int width, int height, int refresh)
        {
            try
            {
                if ((flags & 1) != 0 && Target<WaylandOutput>(data) is { } o)
                {
                    o.Width = width;
                    o.Height = height;
                }
            }
            catch
            {
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnOutputDone(void* data, nint output)
        {
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnOutputScale(void* data, nint output, int factor)
        {
            try
            {
                if (Target<WaylandOutput>(data) is { } o)
                {
                    o.Scale = Math.Max(1, factor);
                }
            }
            catch
            {
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnOutputName(void* data, nint output, byte* name)
        {
            try
            {
                if (Target<WaylandOutput>(data) is { } o)
                {
                    o.Name = Utf8(name);
                }
            }
            catch
            {
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnOutputDescription(void* data, nint output, byte* description)
        {
            try
            {
                if (Target<WaylandOutput>(data) is { } o)
                {
                    o.Description = Utf8(description);
                }
            }
            catch
            {
            }
        }

        #endregion
    }
}
