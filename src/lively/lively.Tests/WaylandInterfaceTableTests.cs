using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static lively.Core.Display.Wayland.WaylandInterfaceTable;

namespace lively.Tests;

public class WaylandInterfaceTableTests
{
    [Fact]
    public void Build_RoundTripsNameVersionAndCounts()
    {
        var iface = Build("test_iface_v1", 3,
            methods: [new("destroy", "", []), new("set_size", "uu", [0, 0])],
            events: [new("configure", "uuu", [0, 0, 0])]);

        Assert.NotEqual(0, iface);
        Assert.Equal(("test_iface_v1", 3, 2, 1), ReadInterface(iface));
    }

    [Fact]
    public void Build_RoundTripsMessages()
    {
        var iface = Build("test_shell_v1", 4,
            methods: [new("get_surface", "no?ous", [0x1000, 0x2000, 0x3000, 0, 0]), new("destroy", "3", [])],
            events: [new("ping", "2u", [0])]);

        var (name, signature, types) = ReadMessage(iface, isEvent: false, 0);
        Assert.Equal("get_surface", name);
        Assert.Equal("no?ous", signature);
        Assert.Equal([0x1000, 0x2000, 0x3000, 0, 0], types);

        var destroy = ReadMessage(iface, isEvent: false, 1);
        Assert.Equal(("destroy", "3"), (destroy.Name, destroy.Signature));
        Assert.Empty(destroy.Types);

        var ping = ReadMessage(iface, isEvent: true, 0);
        Assert.Equal(("ping", "2u"), (ping.Name, ping.Signature));
        Assert.Equal([0], ping.Types);

        Assert.Throws<ArgumentOutOfRangeException>(() => ReadMessage(iface, isEvent: true, 1));
    }

    [Fact]
    public void Build_ZeroArgumentMessageAndEmptyArraysHaveValidPointers()
    {
        var iface = Build("empty_v1", 1, methods: [new("destroy", "", [])], events: []);

        // Raw offsets of the C layout: wl_interface.methods @16, event_count @24, events @32;
        // wl_message.signature @8, types @16.
        var methods = Marshal.ReadIntPtr(iface, 16);
        Assert.NotEqual(0, methods);
        Assert.NotEqual(0, Marshal.ReadIntPtr(methods, 16));
        Assert.Equal(0, Marshal.ReadByte(Marshal.ReadIntPtr(methods, 8))); // "" is still a NUL-terminated string
        Assert.Equal(0, Marshal.ReadInt32(iface, 24));
        Assert.NotEqual(0, Marshal.ReadIntPtr(iface, 32));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("2", 0)]
    [InlineData("2u", 1)]
    [InlineData("no?ous", 5)]
    [InlineData("iufsonah", 8)]
    [InlineData("12?s?o", 2)]
    public void ArgumentCount_CountsArgumentLettersOnly(string signature, int expected)
    {
        Assert.Equal(expected, ArgumentCount(signature));
    }

    [Fact]
    public void ArgumentCount_RejectsUnknownCharacters()
    {
        Assert.Throws<ArgumentException>(() => ArgumentCount("ux"));
    }

    [Fact]
    public void Build_RejectsMismatchedTypes()
    {
        Assert.Throws<ArgumentException>(() => Build("bad_v1", 1, methods: [new("set", "uu", [0])], events: []));
        Assert.Throws<ArgumentException>(() => Build("bad_v1", 1, methods: [], events: [new("ev", "", [0])]));
    }

    [Fact]
    public void Structs_HaveTheLp64Layout()
    {
        Assert.Equal(24, Unsafe.SizeOf<WlMessage>());
        Assert.Equal(40, Unsafe.SizeOf<WlInterface>());
    }
}
