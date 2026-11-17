namespace lively.Core.Display.Wayland
{
    /// <summary>
    /// Interface tables of the KDE protocols that name the primary output, hand-built from
    /// <c>protocol/kde-output-order-v1.xml</c> and <c>protocol/kde-primary-output-v1.xml</c>
    /// (plasma-wayland-protocols). Built on first use and never freed.
    /// </summary>
    public static class KdeProtocols
    {
        public const string OutputOrderName = "kde_output_order_v1";
        public const string PrimaryOutputName = "kde_primary_output_v1";

        public const int OutputOrderVersion = 1;
        public const int PrimaryOutputVersion = 2;

        /// <summary>kde_output_order_v1.destroy (destructor).</summary>
        public const uint KDE_OUTPUT_ORDER_V1_DESTROY = 0;
        /// <summary>kde_primary_output_v1.destroy (destructor, since version 2).</summary>
        public const uint KDE_PRIMARY_OUTPUT_V1_DESTROY = 0;
        public const uint KDE_PRIMARY_OUTPUT_V1_DESTROY_SINCE_VERSION = 2;

        private static readonly Lazy<nint> OutputOrder = new(() => WaylandInterfaceTable.Build(
            OutputOrderName, OutputOrderVersion,
            methods: [new("destroy", "", [])],
            events: [new("output", "s", [0]), new("done", "", [])]));

        private static readonly Lazy<nint> PrimaryOutput = new(() => WaylandInterfaceTable.Build(
            PrimaryOutputName, PrimaryOutputVersion,
            methods: [new("destroy", "2", [])],
            events: [new("primary_output", "s", [0])]));

        /// <summary>
        /// <c>kde_output_order_v1</c> v1. Events: <c>output(s output_name)</c>, sent once per output in priority
        /// order (the first one is the primary), then <c>done</c>; a new list follows on every change.
        /// </summary>
        public static nint KdeOutputOrderV1 => OutputOrder.Value;

        /// <summary><c>kde_primary_output_v1</c> v2. Event: <c>primary_output(s output_name)</c>.</summary>
        public static nint KdePrimaryOutputV1 => PrimaryOutput.Value;
    }
}
