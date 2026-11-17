extern alias MpvPlayer;
using System.Runtime.InteropServices;
using lively.Core.Display.Wayland;
using MpvPlayer::lively.Player.Mpv.Native;

namespace lively.Tests;

public class LayerShellTablesTests
{
    private static nint WaylandExport(string name) =>
        NativeLibrary.GetExport(NativeLibrary.Load("libwayland-client.so.0"), name);

    [WaylandLibFact]
    public void Shell_MatchesTheXml()
    {
        var shell = LayerShellProtocol.ShellInterface;

        Assert.Equal(("zwlr_layer_shell_v1", 4, 2, 0), WaylandInterfaceTable.ReadInterface(shell));
        var (name, signature, types) = WaylandInterfaceTable.ReadMessage(shell, isEvent: false, 0);
        Assert.Equal("get_layer_surface", name);
        Assert.Equal("no?ous", signature);
        Assert.Equal(5, types.Length);
        Assert.Equal(LayerShellProtocol.SurfaceInterface, types[0]);
        Assert.Equal(WaylandExport("wl_surface_interface"), types[1]);
        Assert.Equal(WaylandExport("wl_output_interface"), types[2]);
        Assert.Equal([0, 0], types[3..]);
        Assert.Equal(("destroy", "3", 0), Summary(WaylandInterfaceTable.ReadMessage(shell, isEvent: false, 1)));
    }

    [WaylandLibFact]
    public void Surface_MatchesTheXml()
    {
        var surface = LayerShellProtocol.SurfaceInterface;

        Assert.Equal(("zwlr_layer_surface_v1", 4, 9, 2), WaylandInterfaceTable.ReadInterface(surface));
        string[] methods = ["set_size uu", "set_anchor u", "set_exclusive_zone i", "set_margin iiii", "set_keyboard_interactivity u",
            "get_popup o", "ack_configure u", "destroy ", "set_layer 2u"];
        for (var i = 0; i < methods.Length; i++)
        {
            var m = WaylandInterfaceTable.ReadMessage(surface, isEvent: false, i);
            Assert.Equal(methods[i], m.Name + " " + m.Signature);
            Assert.All(m.Types, t => Assert.Equal(0, t));
        }
        Assert.Equal(7u, LayerShellProtocol.ZWLR_LAYER_SURFACE_V1_DESTROY);
        Assert.Equal(6u, LayerShellProtocol.ZWLR_LAYER_SURFACE_V1_ACK_CONFIGURE);
        Assert.Equal(("configure", "uuu", 3), Summary(WaylandInterfaceTable.ReadMessage(surface, isEvent: true, 0)));
        Assert.Equal(("closed", "", 0), Summary(WaylandInterfaceTable.ReadMessage(surface, isEvent: true, 1)));
    }

    [WaylandLibFact]
    public void Tables_AreBuiltOnce()
    {
        Assert.Equal(LayerShellProtocol.ShellInterface, LayerShellProtocol.ShellInterface);
        Assert.Equal(LayerShellProtocol.SurfaceInterface, LayerShellProtocol.SurfaceInterface);
    }

    [Fact]
    public void Enums_MatchTheXml()
    {
        Assert.Equal(0u, (uint)Layer.Background);
        Assert.Equal(15u, (uint)Anchor.All);
        Assert.Equal(0u, (uint)KeyboardInteractivity.None);
    }

    private static (string, string, int) Summary((string Name, string Signature, nint[] Types) m) => (m.Name, m.Signature, m.Types.Length);
}
