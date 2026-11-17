using lively.Core.Display.Wayland;

namespace lively.Tests;

public class KdeProtocolsTests
{
    [Fact]
    public void OutputOrder_MatchesTheXml()
    {
        var iface = KdeProtocols.KdeOutputOrderV1;

        Assert.Equal(("kde_output_order_v1", 1, 1, 2), WaylandInterfaceTable.ReadInterface(iface));
        Assert.Equal(("destroy", "", 0), Summary(WaylandInterfaceTable.ReadMessage(iface, isEvent: false, 0)));
        Assert.Equal(("output", "s", 1), Summary(WaylandInterfaceTable.ReadMessage(iface, isEvent: true, 0)));
        Assert.Equal(("done", "", 0), Summary(WaylandInterfaceTable.ReadMessage(iface, isEvent: true, 1)));
    }

    [Fact]
    public void PrimaryOutput_MatchesTheXml()
    {
        var iface = KdeProtocols.KdePrimaryOutputV1;

        Assert.Equal(("kde_primary_output_v1", 2, 1, 1), WaylandInterfaceTable.ReadInterface(iface));
        Assert.Equal(("destroy", "2", 0), Summary(WaylandInterfaceTable.ReadMessage(iface, isEvent: false, 0)));
        Assert.Equal(("primary_output", "s", 1), Summary(WaylandInterfaceTable.ReadMessage(iface, isEvent: true, 0)));
    }

    [Fact]
    public void Tables_AreBuiltOnce()
    {
        Assert.Equal(KdeProtocols.KdeOutputOrderV1, KdeProtocols.KdeOutputOrderV1);
        Assert.Equal(KdeProtocols.KdePrimaryOutputV1, KdeProtocols.KdePrimaryOutputV1);
    }

    private static (string, string, int) Summary((string Name, string Signature, nint[] Types) m) => (m.Name, m.Signature, m.Types.Length);
}
