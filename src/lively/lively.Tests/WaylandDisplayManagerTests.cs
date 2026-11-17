using lively.Core.Display;
using lively.Core.Display.Wayland;
using lively.Models;
using Xunit.Abstractions;

namespace lively.Tests;

public class WaylandDisplayManagerTests(ITestOutputHelper output)
{
    [WaylandFact]
    public void Enumerate_ListsTheCompositorsOutputs()
    {
        var displays = WaylandOutputEnumerator.Enumerate();
        foreach (var d in displays)
        {
            output.WriteLine($"{d} [{d.Description}] {d.RefreshMilliHz} mHz transform {d.Transform}");
        }

        Assert.NotEmpty(displays);
        Assert.All(displays, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Id));
            Assert.True(d.Width > 0 && d.Height > 0);
            Assert.True(d.Scale >= 1);
        });
        Assert.Equal(displays.Count, displays.Select(x => x.Id).Distinct().Count());
        Assert.Single(displays, x => x.IsPrimary);
    }

    [WaylandFact]
    public void Snapshot_ListsGlobalsAndPrimary()
    {
        var snapshot = WaylandOutputEnumerator.Snapshot();
        output.WriteLine("Globals: " + string.Join(", ", snapshot.Globals.Order(StringComparer.Ordinal)));
        output.WriteLine("Primary output name: " + (snapshot.PrimaryOutputName ?? "(none)"));
        foreach (var d in snapshot.Displays)
        {
            output.WriteLine($"{d}{(d.IsPrimary ? " [primary]" : "")}");
        }

        Assert.Contains("wl_compositor", snapshot.Globals);
        Assert.Contains("wl_output", snapshot.Globals);
        var primary = Assert.Single(snapshot.Displays, x => x.IsPrimary);
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty;
        if (desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase))
        {
            Assert.NotNull(snapshot.PrimaryOutputName);
            Assert.Equal(snapshot.PrimaryOutputName, primary.Id);
        }
    }

    [WaylandFact]
    public void Enumerate_RepeatedCallsGiveTheSameResult()
    {
        var first = WaylandOutputEnumerator.Enumerate();
        var second = WaylandOutputEnumerator.Enumerate();

        Assert.Equal(first.Count, second.Count);
        Assert.All(first.Zip(second), p => Assert.True(p.First.IsIdenticalTo(p.Second)));
    }

    [WaylandFact]
    public void Manager_LoadsDisplaysAndRefreshDoesNotRaiseWithoutChange()
    {
        using var manager = new WaylandDisplayManager(TimeSpan.FromSeconds(5));
        var raised = 0;
        manager.DisplaysChanged += (_, _) => raised++;

        manager.Refresh();

        Assert.NotEmpty(manager.Displays);
        Assert.NotNull(manager.PrimaryDisplay);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Manager_RaisesWhenTheEnumerationChanges()
    {
        var current = FakeDisplayManager.Parse("DP-1:1920x1080@0,0");
        using var manager = new WaylandDisplayManager(TimeSpan.FromMilliseconds(20), () => current);
        using var changed = new SemaphoreSlim(0);
        manager.DisplaysChanged += (_, _) => changed.Release();
        manager.Start();

        current = FakeDisplayManager.Parse("DP-1:1920x1080@0,0;DP-2:1280x720@1920,0");

        Assert.True(changed.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(["DP-1", "DP-2"], manager.Displays.Select(x => x.Id));
    }

    [Fact]
    public void Manager_KeepsPreviousListWhenEnumerationFails()
    {
        var fail = false;
        IReadOnlyList<DisplayMonitor> Enumerate() => fail ? throw new WaylandUnavailableException("gone") : FakeDisplayManager.Parse("DP-1:1920x1080@0,0");
        using var manager = new WaylandDisplayManager(TimeSpan.FromSeconds(5), Enumerate);

        fail = true;
        manager.Refresh();

        Assert.Equal(["DP-1"], manager.Displays.Select(x => x.Id));
    }
}
