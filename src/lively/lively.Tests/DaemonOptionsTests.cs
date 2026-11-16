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
}
