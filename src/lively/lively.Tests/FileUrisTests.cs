using lively.Helpers;

namespace lively.Tests;

public sealed class FileUrisTests
{
    [Fact]
    public void ToLocalPaths_DecodesFileUrisAndIgnoresOtherSchemes()
    {
        var paths = FileUris.ToLocalPaths(["file:///tmp/a%20b.mp4", "https://example.com/video.mp4", "not a uri", "file:///tmp/c.webm"]);

        Assert.Equal(["/tmp/a b.mp4", "/tmp/c.webm"], paths);
    }

    [Theory]
    [InlineData("/tmp/a b.mp4")]
    [InlineData("/tmp/100% #1 clip?.mp4")]
    [InlineData("/tmp/ünïcödé/日本語.mkv")]
    public void ToUris_RoundTripsThroughToLocalPaths(string path)
    {
        var uris = FileUris.ToUris([path]);

        Assert.StartsWith("file:///", uris[0]);
        Assert.Equal([path], FileUris.ToLocalPaths(uris));
    }

    [Fact]
    public void CommandLinePaths_SkipsOptionsAndMakesPathsAbsolute()
    {
        var paths = FileUris.CommandLinePaths(["--verbose", "clip.mp4", "/videos/a.mp4", "file:///videos/b%20c.mp4", ""]);

        Assert.Equal([Path.GetFullPath("clip.mp4"), "/videos/a.mp4", "/videos/b c.mp4"], paths);
    }
}
