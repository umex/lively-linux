using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using lively.Common.Helpers.Archive;

namespace lively.Tests;

public class ZipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateThenExtract_RoundTripsFolder(bool trailingSeparator)
    {
        using var tmp = new TempDirectory();
        var source = WallpaperFixture.Create(tmp, "source", "Rain");
        tmp.WriteFile("nested", "source", "assets", "data.txt");
        var zip = tmp.Combine("out.zip");
        var dest = tmp.Combine("dest");

        ZipCreate.CreateZip(zip, [trailingSeparator ? source + Path.DirectorySeparatorChar : source]);
        Assert.True(ZipExtract.CheckLivelyZip(zip));
        ZipExtract.ZipExtractFile(zip, dest, livelyFileCheck: true);

        Assert.True(File.Exists(Path.Combine(dest, "LivelyInfo.json")));
        Assert.Equal("video", File.ReadAllText(Path.Combine(dest, "wallpaper.mp4")));
        Assert.Equal("nested", File.ReadAllText(Path.Combine(dest, "assets", "data.txt")));
    }

    [Fact]
    public void CreateZip_FromFileList_UsesPathsRelativeToParent()
    {
        using var tmp = new TempDirectory();
        var a = tmp.WriteFile("a", "parent", "a.txt");
        var b = tmp.WriteFile("b", "parent", "sub", "b.txt");
        var zip = tmp.Combine("out.zip");

        ZipCreate.CreateZip(zip, [new ZipCreate.FileData { ParentDirectory = tmp.Combine("parent"), Files = [a, b] }]);

        using var zf = new ZipFile(zip);
        Assert.Equal(["a.txt", "sub/b.txt"], zf.Cast<ZipEntry>().Select(x => x.Name).Order());
    }

    [Fact]
    public void Extract_NotALivelyZip_ThrowsWhenChecked()
    {
        using var tmp = new TempDirectory();
        var zip = CreateRawZip(tmp, ("readme.txt", "hello"));

        Assert.False(ZipExtract.CheckLivelyZip(zip));
        Assert.ThrowsAny<Exception>(() => ZipExtract.ZipExtractFile(zip, tmp.Combine("dest"), livelyFileCheck: true));
    }

    [Fact]
    public void CheckLivelyZip_NotAZipFile_ReturnsFalse()
    {
        using var tmp = new TempDirectory();

        Assert.False(ZipExtract.CheckLivelyZip(tmp.WriteFile("not a zip", "file.zip")));
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("sub/../../escaped.txt")]
    public void Extract_EntryOutsideDestination_IsRejected(string entryName)
    {
        using var tmp = new TempDirectory();
        var zip = CreateRawZip(tmp, ("LivelyInfo.json", "{}"), (entryName, "evil"));
        var dest = tmp.CreateDirectory("nested", "dest");

        Assert.Throws<InvalidDataException>(() => ZipExtract.ZipExtractFile(zip, dest, livelyFileCheck: true));
        Assert.False(File.Exists(tmp.Combine("nested", "escaped.txt")));
        Assert.False(File.Exists(tmp.Combine("escaped.txt")));
    }

    /// <summary>
    /// Writes a zip with entry names used verbatim, bypassing the name cleaning ZipCreate does.
    /// </summary>
    private static string CreateRawZip(TempDirectory tmp, params (string Name, string Content)[] entries)
    {
        var zip = tmp.Combine("raw.zip");
        using var fs = File.Create(zip);
        using var zs = new ZipOutputStream(fs);
        foreach (var (name, content) in entries)
        {
            zs.PutNextEntry(new ZipEntry(name));
            var bytes = Encoding.UTF8.GetBytes(content);
            zs.Write(bytes, 0, bytes.Length);
            zs.CloseEntry();
        }
        return zip;
    }
}
