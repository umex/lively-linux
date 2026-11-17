using lively.Core.Settings;
using lively.Models;
using Newtonsoft.Json.Linq;

namespace lively.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void MissingFile_GivesFirstRunDefaultsAndSaves()
    {
        using var tmp = new TempDirectory();
        var path = tmp.Combine("config", "Settings.json");

        var service = new JsonSettingsService(path);

        Assert.True(service.Settings.IsFirstRun);
        Assert.Equal(0, service.Settings.WallpaperVolume);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Replace_SavesAndRaisesChanged()
    {
        using var tmp = new TempDirectory();
        var path = tmp.Combine("Settings.json");
        var service = new JsonSettingsService(path);
        SettingsModel? raised = null;
        service.SettingsChanged += (_, s) => raised = s;

        var updated = new SettingsModel { Language = "de-DE", Startup = true };
        service.Replace(updated);

        Assert.Same(updated, raised);
        Assert.Same(updated, service.Settings);
        var reloaded = new JsonSettingsService(path);
        Assert.Equal("de-DE", reloaded.Settings.Language);
        Assert.True(reloaded.Settings.Startup);
        Assert.False(reloaded.Settings.IsFirstRun);
    }

    [Fact]
    public void CorruptFile_IsBackedUpAndReset()
    {
        using var tmp = new TempDirectory();
        var path = tmp.WriteFile("{ broken", "Settings.json");

        var service = new JsonSettingsService(path);

        Assert.False(service.Settings.IsFirstRun);
        Assert.Equal("{ broken", File.ReadAllText(path + ".bak"));
        Assert.NotNull(JObject.Parse(File.ReadAllText(path)));
    }

    [Fact]
    public void ToJson_ApplyJson_RoundTrip()
    {
        using var tmp = new TempDirectory();
        var service = new JsonSettingsService(tmp.Combine("Settings.json"));
        service.Replace(new SettingsModel { Language = "fr-FR" });
        var json = service.ToJson();

        Assert.DoesNotContain('\n', json);
        var other = new JsonSettingsService(tmp.Combine("Other.json"));
        var changed = 0;
        other.SettingsChanged += (_, _) => changed++;
        other.ApplyJson(json);

        Assert.Equal("fr-FR", other.Settings.Language);
        Assert.Equal(1, changed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ broken")]
    [InlineData("[1, 2]")]
    [InlineData("null")]
    [InlineData("{\"Startup\": \"not a bool\"}")]
    public void ApplyJson_Invalid_ThrowsArgumentExceptionAndKeepsSettings(string json)
    {
        using var tmp = new TempDirectory();
        var service = new JsonSettingsService(tmp.Combine("Settings.json"));
        var before = service.Settings;

        Assert.Throws<ArgumentException>(() => service.ApplyJson(json));
        Assert.Same(before, service.Settings);
    }
}
