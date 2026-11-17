using lively.Core.Wallpapers;
using lively.Daemon;

namespace lively.Tests;

/// <summary>UserInstaller with the real templates (copied from lively.Daemon/data) and temporary XDG homes.</summary>
public sealed class UserInstallerTests : IDisposable
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly PlayerLaunchInfo Daemon = new("/opt/my apps/lively-daemon", []);
    private static readonly PlayerLaunchInfo Ui = new("/usr/bin/dotnet", ["/opt/my apps/lively.dll"]);

    private readonly TempDirectory tmp = new();
    private readonly string dataHome;
    private readonly string configHome;

    public UserInstallerTests()
    {
        dataHome = tmp.Combine("data");
        configHome = tmp.Combine("config");
    }

    public void Dispose() => tmp.Dispose();

    private UserInstaller Create(PlayerLaunchInfo? daemon = null, PlayerLaunchInfo? ui = null, bool withUi = true)
    {
        return new UserInstaller(dataHome, configHome, DataDir, daemon ?? Daemon, withUi ? ui ?? Ui : null);
    }

    private static string ValueOf(string file, string key)
    {
        var line = File.ReadAllLines(file).Single(l => l.StartsWith(key + "=", StringComparison.Ordinal));
        return line[(key.Length + 1)..];
    }

    [Fact]
    public void Install_WritesActivationFileAndLauncher()
    {
        var written = Create().Install();

        var service = Path.Combine(dataHome, "dbus-1", "services", "io.github.lively_linux.Daemon.service");
        var launcher = Path.Combine(dataHome, "applications", "io.github.lively_linux.Lively.desktop");
        Assert.Equal([service, launcher], written);

        Assert.Equal("[D-BUS Service]", File.ReadLines(service).First());
        Assert.Equal("io.github.lively_linux.Daemon", ValueOf(service, "Name"));
        Assert.Equal("\"/opt/my apps/lively-daemon\"", ValueOf(service, "Exec"));

        Assert.Equal("[Desktop Entry]", File.ReadLines(launcher).First());
        Assert.Equal("Lively Wallpaper", ValueOf(launcher, "Name"));
        Assert.Equal("/usr/bin/dotnet \"/opt/my apps/lively.dll\" %U", ValueOf(launcher, "Exec"));
        Assert.Contains("video/mp4;", ValueOf(launcher, "MimeType"));
        var icon = ValueOf(launcher, "Icon");
        Assert.True(Path.IsPathRooted(icon));
        Assert.True(File.Exists(icon), icon);
        Assert.DoesNotContain("@", File.ReadAllText(service) + File.ReadAllText(launcher));
    }

    [Fact]
    public void Install_WithoutUi_WritesOnlyTheActivationFile()
    {
        var installer = Create(withUi: false);

        Assert.Equal([installer.ServicePath], installer.Install());
        Assert.False(File.Exists(installer.LauncherPath));
    }

    [Fact]
    public void Install_IsIdempotent()
    {
        var installer = Create();
        var first = installer.Install();
        var contents = first.Select(File.ReadAllText).ToList();
        var times = first.Select(File.GetLastWriteTimeUtc).ToList();

        var second = Create().Install();

        Assert.Equal(first, second);
        Assert.Equal(contents, second.Select(File.ReadAllText));
        Assert.Equal(times, second.Select(File.GetLastWriteTimeUtc));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(installer.ServicePath)!, "*.tmp"));
    }

    [Fact]
    public void Install_ResolvesABareHostThroughPath()
    {
        var installer = Create(daemon: new PlayerLaunchInfo("sh", ["/opt/lively-daemon.dll"]));

        installer.Install();

        var exec = ValueOf(installer.ServicePath, "Exec");
        Assert.EndsWith("/sh /opt/lively-daemon.dll", exec);
        Assert.True(Path.IsPathRooted(exec.Split(' ')[0]), exec);
    }

    [Fact]
    public void Install_HostNotOnPath_Fails()
    {
        var installer = Create(daemon: new PlayerLaunchInfo("no-such-host-" + Guid.NewGuid().ToString("N"), []));

        var e = Assert.Throws<FileNotFoundException>(() => installer.Install());
        Assert.Contains("PATH", e.Message);
    }

    [Fact]
    public void SetAutostart_WritesAndRemovesTheEntry()
    {
        var installer = Create();
        var path = Path.Combine(configHome, "autostart", "io.github.lively_linux.Daemon.desktop");

        Assert.Equal(path, installer.SetAutostart(true));
        Assert.Equal(path, installer.SetAutostart(true));
        Assert.Equal("\"/opt/my apps/lively-daemon\"", ValueOf(path, "Exec"));
        Assert.Equal("true", ValueOf(path, "NoDisplay"));
        Assert.Equal("true", ValueOf(path, "X-GNOME-Autostart-enabled"));

        Assert.Null(installer.SetAutostart(false));
        Assert.False(File.Exists(path));
        Assert.Null(installer.SetAutostart(false));
    }

    [Fact]
    public void Uninstall_RemovesEverything()
    {
        var installer = Create();
        installer.Install();
        installer.SetAutostart(true);

        var removed = installer.Uninstall();

        Assert.Equal([installer.ServicePath, installer.LauncherPath, installer.AutostartPath], removed);
        Assert.All(removed, p => Assert.False(File.Exists(p), p));
        Assert.Empty(installer.Uninstall());
    }

    [Fact]
    public void QuoteExec_QuotesAPathWithASpace()
    {
        Assert.Equal("\"/opt/my apps/lively-daemon\"", UserInstaller.QuoteExec(Daemon));
        Assert.Equal("\"/opt/my apps/lively-daemon\"", UserInstaller.QuoteExec(Daemon, desktopEntry: false));
    }

    [Fact]
    public void QuoteExec_DotnetForm_IsUnquoted()
    {
        var launch = new PlayerLaunchInfo("/usr/lib64/dotnet/dotnet", ["/home/me/lively/lively-daemon.dll"]);

        Assert.Equal("/usr/lib64/dotnet/dotnet /home/me/lively/lively-daemon.dll", UserInstaller.QuoteExec(launch));
    }

    [Fact]
    public void QuoteExec_EscapesReservedCharacters()
    {
        var launch = new PlayerLaunchInfo("/opt/a\"b$c`d\\e", ["100%", ""]);

        // Service files: shell-style quoting only.
        Assert.Equal("\"/opt/a\\\"b\\$c\\`d\\\\e\" 100% \"\"", UserInstaller.QuoteExec(launch, desktopEntry: false));
        // Desktop entries: then backslashes doubled (string escape) and % doubled.
        Assert.Equal("\"/opt/a\\\\\"b\\\\$c\\\\`d\\\\\\\\e\" 100%% \"\"", UserInstaller.QuoteExec(launch));
    }
}
