using lively.Core.Display;
using lively.Models;

namespace lively.Tests;

public class FakeDisplayManagerTests
{
    [Fact]
    public void Parse_ReadsEntriesAndMarksFirstPrimary()
    {
        var displays = FakeDisplayManager.Parse("DP-1:1920x1080@0,0; HDMI-A-1:2560x1440@1920,-200");

        Assert.Equal(2, displays.Count);
        Assert.Equal(("DP-1", 1920, 1080, 0, 0, true), (displays[0].Id, displays[0].Width, displays[0].Height, displays[0].X, displays[0].Y, displays[0].IsPrimary));
        Assert.Equal(("HDMI-A-1", 2560, 1440, 1920, -200, false), (displays[1].Id, displays[1].Width, displays[1].Height, displays[1].X, displays[1].Y, displays[1].IsPrimary));
    }

    [Fact]
    public void Parse_PositionIsOptional()
    {
        var d = Assert.Single(FakeDisplayManager.Parse("default:1920x1080"));

        Assert.Equal((0, 0), (d.X, d.Y));
    }

    [Theory]
    [InlineData("")]
    [InlineData(";")]
    [InlineData("DP-1")]
    [InlineData("DP-1:1920")]
    [InlineData("DP-1:axb@0,0")]
    [InlineData("DP-1:1920x1080@0")]
    [InlineData("DP-1:1920x1080;DP-1:1280x720@1920,0")]
    public void Parse_Invalid_Throws(string spec)
    {
        Assert.Throws<FormatException>(() => FakeDisplayManager.Parse(spec));
    }

    [Fact]
    public void Lookup_PrimaryAndFindById()
    {
        using var manager = new FakeDisplayManager("DP-1:1920x1080@0,0;DP-2:1920x1080@1920,0");

        Assert.Equal("DP-1", manager.PrimaryDisplay?.Id);
        Assert.Equal("DP-2", manager.FindById("DP-2")?.Id);
        Assert.Null(manager.FindById("DP-3"));
    }

    [Fact]
    public void SetDisplays_RaisesOnlyWhenSomethingChanged()
    {
        using var manager = new FakeDisplayManager("DP-1:1920x1080@0,0");
        var raised = 0;
        manager.DisplaysChanged += (_, _) => raised++;

        manager.SetDisplays(FakeDisplayManager.Parse("DP-1:1920x1080@0,0"));
        Assert.Equal(0, raised);

        manager.SetDisplays(FakeDisplayManager.Parse("DP-1:2560x1440@0,0"));
        Assert.Equal(1, raised);

        manager.SetDisplays(FakeDisplayManager.Parse("DP-1:2560x1440@0,0;DP-2:1920x1080@2560,0"));
        Assert.Equal(2, raised);
        Assert.Equal(["DP-1", "DP-2"], manager.Displays.Select(x => x.Id));
    }

    [Fact]
    public void ListWithoutPrimary_FirstBecomesPrimary()
    {
        using var manager = new FakeDisplayManager([new DisplayMonitor { Id = "A" }, new DisplayMonitor { Id = "B" }]);

        Assert.Equal("A", manager.PrimaryDisplay?.Id);
    }

    [Fact]
    public void DisplayMonitor_EqualityIsById()
    {
        var a = new DisplayMonitor { Id = "DP-1", Width = 1920 };
        var b = new DisplayMonitor { Id = "DP-1", Width = 2560 };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.IsIdenticalTo(b));
        Assert.NotEqual(a, new DisplayMonitor { Id = "DP-2", Width = 1920 });
    }
}
