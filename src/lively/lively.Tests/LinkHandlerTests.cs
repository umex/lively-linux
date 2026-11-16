using lively.Common.Helpers;

namespace lively.Tests;

public class LinkHandlerTests
{
    [Fact]
    public void SanitizeUrl_WithoutScheme_AssumesHttps()
    {
        Assert.Equal("https://example.com/", LinkHandler.SanitizeUrl("example.com").AbsoluteUri);
    }

    [Fact]
    public void SanitizeUrl_WithScheme_IsKept()
    {
        Assert.Equal("http://example.com/page", LinkHandler.SanitizeUrl("http://example.com/page").AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeUrl_Blank_Throws(string address)
    {
        Assert.Throws<ArgumentException>(() => LinkHandler.SanitizeUrl(address));
    }
}
