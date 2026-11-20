using lively.Helpers;

namespace lively.Tests;

public sealed class FileUrisTests
{
    [Fact]
    public void FromUris_DecodesFileUrisKeepsWebAddressesAndIgnoresOtherSchemes()
    {
        var targets = FileUris.FromUris(["file:///tmp/a%20b.mp4", "https://example.com/page", "not a uri", "ftp://example.com/x",
            "HTTP://Example.com", "file:///tmp/site/"]);

        Assert.Equal(["/tmp/a b.mp4", "https://example.com/page", "HTTP://Example.com", "/tmp/site/"], targets);
    }

    [Theory]
    [InlineData("/tmp/a b.mp4")]
    [InlineData("/tmp/100% #1 clip?.mp4")]
    [InlineData("/tmp/ünïcödé/日本語.mkv")]
    [InlineData("/tmp/pages/index.html")]
    [InlineData("https://example.com/a b?x=1")]
    public void ToUris_RoundTripsThroughFromUris(string target)
    {
        var uris = FileUris.ToUris([target]);

        Assert.Equal([target], FileUris.FromUris(uris));
    }

    [Fact]
    public void ToUris_MakesFileUrisOfPaths()
    {
        Assert.Equal(["file:///tmp/a%20b.html", "https://example.com/"], FileUris.ToUris(["/tmp/a b.html", "https://example.com/"]));
    }

    [Fact]
    public void CommandLineTargets_SkipsOptionsMakesPathsAbsoluteAndKeepsWebAddresses()
    {
        var targets = FileUris.CommandLineTargets(["--verbose", "clip.mp4", "/videos/a.mp4", "file:///videos/b%20c.mp4", "",
            "pages/site", "https://example.com/aquarium", "example.com"]);

        Assert.Equal([Path.GetFullPath("clip.mp4"), "/videos/a.mp4", "/videos/b c.mp4", Path.GetFullPath("pages/site"),
            "https://example.com/aquarium", Path.GetFullPath("example.com")], targets);
    }

    [Theory]
    [InlineData("http://example.com", true)]
    [InlineData("HTTPS://example.com", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("/tmp/http://x", false)]
    [InlineData("httpx://example.com", false)]
    public void IsWebAddress(string target, bool expected)
    {
        Assert.Equal(expected, FileUris.IsWebAddress(target));
    }
}
