using lively.Daemon;

namespace lively.Tests;

public class DaemonOptionsTests
{
    [Fact]
    public void Parse_NoArguments_GivesDefaults()
    {
        var options = DaemonOptions.Parse([]);

        Assert.False(options.NoRestore);
        Assert.Null(options.Displays);
        Assert.False(options.ShowHelp);
        Assert.False(options.InstallUser);
        Assert.False(options.UninstallUser);
    }

    [Fact]
    public void Parse_ReadsAllOptions()
    {
        var options = DaemonOptions.Parse(["--no-restore", "--displays", "DP-1:1920x1080@0,0;DP-2:1280x1024@1920,0"]);

        Assert.True(options.NoRestore);
        Assert.Equal("DP-1:1920x1080@0,0;DP-2:1280x1024@1920,0", options.Displays);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Parse_Help(string arg)
    {
        Assert.True(DaemonOptions.Parse([arg]).ShowHelp);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--displays")]
    [InlineData("--displays", "nonsense")]
    [InlineData("--displays", "DP-1:1x1;DP-1:2x2")]
    public void Parse_RejectsInvalidArguments(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => DaemonOptions.Parse(args));
    }

    [Fact]
    public void Parse_InstallUser()
    {
        var options = DaemonOptions.Parse(["--install-user"]);

        Assert.True(options.InstallUser);
        Assert.False(options.UninstallUser);
    }

    [Fact]
    public void Parse_UninstallUser()
    {
        var options = DaemonOptions.Parse(["--uninstall-user"]);

        Assert.True(options.UninstallUser);
        Assert.False(options.InstallUser);
    }

    [Fact]
    public void Parse_InstallAndUninstall_AreExclusive()
    {
        var e = Assert.Throws<ArgumentException>(() => DaemonOptions.Parse(["--install-user", "--uninstall-user"]));
        Assert.Contains("--install-user", e.Message);
    }

    [Fact]
    public void Usage_MentionsTheInstallOptions()
    {
        Assert.Contains("--install-user", DaemonOptions.Usage);
        Assert.Contains("--uninstall-user", DaemonOptions.Usage);
    }
}
