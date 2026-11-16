using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using lively.Models;

namespace lively.Core.Display.Wayland
{
    /// <summary>
    /// Lists the compositor's wl_output globals with a short-lived connection.
    /// Works on any Wayland compositor; uses wl_output version 4 (name, description) when available.
    /// </summary>
    public static unsafe class WaylandOutputEnumerator
    {
        private const uint MaxOutputVersion = 4;

        private sealed class OutputState
        {
            public uint Global;
            public uint Version;
            public nint Proxy;
            public GCHandle Handle;
            public string? Name;
            public string? Description;
            public string? Make;
            public string? Model;
            public int X;
            public int Y;
            public int Transform;
            public int Width;
            public int Height;
            public int Refresh;
            public bool HasCurrentMode;
            public int Scale = 1;
        }

        private sealed class EnumerationState
        {
            public readonly List<OutputState> Outputs = [];
            public Exception? Error;
        }

        // Allocated once and never freed: libwayland keeps the pointer for the proxy's lifetime.
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
            listener->Geometry = &OnGeometry;
            listener->Mode = &OnMode;
            listener->Done = &OnDone;
            listener->Scale = &OnScale;
            listener->Name = &OnName;
            listener->Description = &OnDescription;
            return (nint)listener;
        });

        /// <summary>
        /// Connects to $WAYLAND_DISPLAY and returns the outputs, sorted left to right.
        /// </summary>
        /// <exception cref="WaylandUnavailableException">No compositor, missing library, or a protocol error.</exception>
        public static IReadOnlyList<DisplayMonitor> Enumerate()
        {
            nint display;
            try
            {
                display = WaylandNative.wl_display_connect(null);
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                throw new WaylandUnavailableException("libwayland-client is not available: " + e.Message, e);
            }
            if (display == 0)
            {
                throw new WaylandUnavailableException("Cannot connect to the Wayland display (WAYLAND_DISPLAY=" + Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") + ").");
            }

            var state = new EnumerationState();
            var stateHandle = GCHandle.Alloc(state);
            nint registry = 0;
            try
            {
                var args = stackalloc WlArgument[1];
                args[0].n = 0;
                registry = WaylandNative.wl_proxy_marshal_array_flags(display, WaylandNative.WL_DISPLAY_GET_REGISTRY,
                    WaylandNative.RegistryInterface, WaylandNative.wl_proxy_get_version(display), 0, args);
                if (registry == 0)
                {
                    throw new WaylandUnavailableException("wl_display.get_registry failed.");
                }
                WaylandNative.wl_proxy_add_listener(registry, (void*)RegistryListener.Value, (void*)GCHandle.ToIntPtr(stateHandle));

                // First roundtrip: globals (outputs are bound in OnGlobal). Second: the outputs' events.
                if (WaylandNative.wl_display_roundtrip(display) < 0 || WaylandNative.wl_display_roundtrip(display) < 0)
                {
                    throw new WaylandUnavailableException("Wayland roundtrip failed.");
                }
                if (state.Error != null)
                {
                    throw new WaylandUnavailableException("Wayland callback failed: " + state.Error.Message, state.Error);
                }

                return ToDisplays(state.Outputs);
            }
            finally
            {
                foreach (var output in state.Outputs)
                {
                    ReleaseOutput(output);
                    if (output.Handle.IsAllocated)
                    {
                        output.Handle.Free();
                    }
                }
                if (registry != 0)
                {
                    // wl_registry has no destructor request.
                    WaylandNative.wl_proxy_destroy(registry);
                }
                WaylandNative.wl_display_disconnect(display);
                stateHandle.Free();
            }
        }

        private static void ReleaseOutput(OutputState output)
        {
            if (output.Proxy == 0)
            {
                return;
            }
            if (output.Version >= WaylandNative.WL_OUTPUT_RELEASE_SINCE_VERSION)
            {
                // wl_output.release has no arguments; the pointer is never read.
                WlArgument none = default;
                WaylandNative.wl_proxy_marshal_array_flags(output.Proxy, WaylandNative.WL_OUTPUT_RELEASE, 0,
                    WaylandNative.wl_proxy_get_version(output.Proxy), WaylandNative.WL_MARSHAL_FLAG_DESTROY, &none);
            }
            else
            {
                WaylandNative.wl_proxy_destroy(output.Proxy);
            }
            output.Proxy = 0;
        }

        private static List<DisplayMonitor> ToDisplays(List<OutputState> outputs)
        {
            var result = outputs.Select(o =>
            {
                var rotated = (o.Transform & 1) == 1; // 90, 270, flipped-90, flipped-270
                return new DisplayMonitor
                {
                    Id = !string.IsNullOrEmpty(o.Name) ? o.Name : $"{o.Make}-{o.Model}-{o.Global}",
                    Description = o.Description ?? string.Empty,
                    Make = o.Make ?? string.Empty,
                    Model = o.Model ?? string.Empty,
                    X = o.X,
                    Y = o.Y,
                    Width = rotated ? o.Height : o.Width,
                    Height = rotated ? o.Width : o.Height,
                    RefreshMilliHz = o.Refresh,
                    Scale = o.Scale > 0 ? o.Scale : 1,
                    Transform = o.Transform,
                };
            })
            .OrderBy(x => x.X).ThenBy(x => x.Y).ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

            // Wayland has no primary output: take the one at the origin, else the top-left one.
            var primary = result.FirstOrDefault(x => x.X == 0 && x.Y == 0)
                ?? result.OrderBy(x => x.Y).ThenBy(x => x.X).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (primary != null)
            {
                primary.IsPrimary = true;
            }
            return result;
        }

        private static T? Target<T>(void* data) where T : class => GCHandle.FromIntPtr((nint)data).Target as T;

        private static string? Utf8(byte* s) => s == null ? null : Marshal.PtrToStringUTF8((nint)s);

        #region registry callbacks

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OnGlobal(void* data, nint registry, uint name, byte* iface, uint version)
        {
            EnumerationState? state = null;
            try
            {
                state = Target<EnumerationState>(data);
                if (state == null || Utf8(iface) != "wl_output")
                {
                    return;
                }

                var outputInterface = WaylandNative.OutputInterface;
                var bindVersion = Math.Min(Math.Min(version, MaxOutputVersion), (uint)Math.Max(1, WaylandNative.InterfaceVersion(outputInterface)));
                var output = new OutputState { Global = name, Version = bindVersion };
                output.Handle = GCHandle.Alloc(output);
                state.Outputs.Add(output);

                var args = stackalloc WlArgument[4];
                args[0].u = name;
                args[1].s = WaylandNative.InterfaceName(outputInterface);
                args[2].u = bindVersion;
                args[3].n = 0;
                output.Proxy = WaylandNative.wl_proxy_marshal_array_flags(registry, WaylandNative.WL_REGISTRY_BIND, outputInterface, bindVersion, 0, args);
                if (output.Proxy != 0)
                {
                    WaylandNative.wl_proxy_add_listener(output.Proxy, (void*)OutputListener.Value, (void*)GCHandle.ToIntPtr(output.Handle));
                }
            }
            catch (Exception e)
            {
                // An exception escaping an UnmanagedCallersOnly method terminates the process.
                if (state != null)
                {
                    state.Error ??= e;
                }
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OnGlobalRemove(void* data, nint registry, uint name)
        {
        }

        #endregion

        #region output callbacks

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OnGeometry(void* data, nint output, int x, int y, int physicalWidth, int physicalHeight, int subpixel, byte* make, byte* model, int transform)
        {
            try
            {
                var o = Target<OutputState>(data);
                if (o == null)
                {
                    return;
                }
                o.X = x;
                o.Y = y;
                o.Make = Utf8(make);
                o.Model = Utf8(model);
                o.Transform = transform;
            }
            catch
            {
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OnMode(void* data, nint output, uint flags, int width, int height, int refresh)
        {
            try
            {
                var o = Target<OutputState>(data);
                if (o == null)
                {
                    return;
                }
                var current = (flags & 1) != 0;
                // Keep the current mode; before one is seen, any mode is better than none.
                if (current || !o.HasCurrentMode)
                {
                    o.Width = width;
                    o.Height = height;
                    o.Refresh = refresh;
                    o.HasCurrentMode |= current;
                }
            }
            catch
            {
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OnDone(void* data, nint output)
        {
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OnScale(void* data, nint output, int factor)
        {
            try
            {
                var o = Target<OutputState>(data);
                if (o != null)
                {
                    o.Scale = factor;
                }
            }
            catch
            {
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OnName(void* data, nint output, byte* name)
        {
            try
            {
                var o = Target<OutputState>(data);
                if (o != null)
                {
                    o.Name = Utf8(name);
                }
            }
            catch
            {
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OnDescription(void* data, nint output, byte* description)
        {
            try
            {
                var o = Target<OutputState>(data);
                if (o != null)
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
