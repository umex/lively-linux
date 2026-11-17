using lively.Core.Display.Wayland;

namespace lively.Player.Mpv.Native
{
    /// <summary>
    /// Interface tables of <c>wlr-layer-shell-unstable-v1</c> version 4, hand-built from
    /// <c>protocol/wlr-layer-shell-unstable-v1.xml</c> (what wayland-scanner would generate). Built on first use
    /// and never freed; building them loads libwayland-client for the <c>wl_surface</c>/<c>wl_output</c> types.
    /// </summary>
    public static class LayerShellProtocol
    {
        public const string ShellName = "zwlr_layer_shell_v1";
        public const string SurfaceName = "zwlr_layer_surface_v1";
        public const int Version = 4;

        // zwlr_layer_shell_v1 requests
        public const uint ZWLR_LAYER_SHELL_V1_GET_LAYER_SURFACE = 0;
        public const uint ZWLR_LAYER_SHELL_V1_DESTROY = 1;
        public const uint ZWLR_LAYER_SHELL_V1_DESTROY_SINCE_VERSION = 3;

        // zwlr_layer_surface_v1 requests
        public const uint ZWLR_LAYER_SURFACE_V1_SET_SIZE = 0;
        public const uint ZWLR_LAYER_SURFACE_V1_SET_ANCHOR = 1;
        public const uint ZWLR_LAYER_SURFACE_V1_SET_EXCLUSIVE_ZONE = 2;
        public const uint ZWLR_LAYER_SURFACE_V1_SET_MARGIN = 3;
        public const uint ZWLR_LAYER_SURFACE_V1_SET_KEYBOARD_INTERACTIVITY = 4;
        public const uint ZWLR_LAYER_SURFACE_V1_GET_POPUP = 5;
        public const uint ZWLR_LAYER_SURFACE_V1_ACK_CONFIGURE = 6;
        public const uint ZWLR_LAYER_SURFACE_V1_DESTROY = 7;
        public const uint ZWLR_LAYER_SURFACE_V1_SET_LAYER = 8;

        // Built first: get_layer_surface's new_id argument points to it.
        private static readonly Lazy<nint> Surface = new(() => WaylandInterfaceTable.Build(
            SurfaceName, Version,
            methods:
            [
                new("set_size", "uu", [0, 0]),
                new("set_anchor", "u", [0]),
                new("set_exclusive_zone", "i", [0]),
                new("set_margin", "iiii", [0, 0, 0, 0]),
                new("set_keyboard_interactivity", "u", [0]),
                // xdg_popup; never called, so no xdg-shell table is needed.
                new("get_popup", "o", [0]),
                new("ack_configure", "u", [0]),
                new("destroy", "", []),
                new("set_layer", "2u", [0]),
            ],
            events:
            [
                new("configure", "uuu", [0, 0, 0]),
                new("closed", "", []),
            ]));

        private static readonly Lazy<nint> Shell = new(() => WaylandInterfaceTable.Build(
            ShellName, Version,
            methods:
            [
                new("get_layer_surface", "no?ous", [Surface.Value, WaylandNative.SurfaceInterface, WaylandNative.OutputInterface, 0, 0]),
                new("destroy", "3", []),
            ],
            events: []));

        /// <summary>Address of the <c>zwlr_layer_shell_v1</c> <c>wl_interface</c>.</summary>
        public static nint ShellInterface => Shell.Value;

        /// <summary>Address of the <c>zwlr_layer_surface_v1</c> <c>wl_interface</c>.</summary>
        public static nint SurfaceInterface => Surface.Value;
    }

    /// <summary>zwlr_layer_shell_v1.layer.</summary>
    public enum Layer : uint
    {
        Background = 0,
        Bottom = 1,
        Top = 2,
        Overlay = 3,
    }

    /// <summary>zwlr_layer_surface_v1.anchor.</summary>
    [Flags]
    public enum Anchor : uint
    {
        None = 0,
        Top = 1,
        Bottom = 2,
        Left = 4,
        Right = 8,
        All = Top | Bottom | Left | Right,
    }

    /// <summary>zwlr_layer_surface_v1.keyboard_interactivity.</summary>
    public enum KeyboardInteractivity : uint
    {
        None = 0,
        Exclusive = 1,
        OnDemand = 2,
    }

    /// <summary>struct zwlr_layer_surface_v1_listener.</summary>
    internal unsafe struct ZwlrLayerSurfaceListener
    {
        /// <summary>serial, width, height (surface-local; 0 = the client decides).</summary>
        public delegate* unmanaged[Cdecl]<void*, nint, uint, uint, uint, void> Configure;
        public delegate* unmanaged[Cdecl]<void*, nint, void> Closed;
    }
}
