using lively.Common;
using lively.Common.Helpers.Storage;
using lively.Core.Library;
using lively.Models;
using Newtonsoft.Json.Linq;

namespace lively.Tests;

public sealed class WebImporterTests : IDisposable
{
    private readonly TempDirectory tmp = new();
    private readonly string wallpapers;
    private readonly WebImporter importer;

    public WebImporterTests()
    {
        wallpapers = tmp.Combine("library", "wallpapers");
        importer = new WebImporter(wallpapers);
    }

    public void Dispose() => tmp.Dispose();

    private static string Page(string title) => $"<!doctype html><html><head><title>{title}</title></head><body></body></html>";

    private string[] Imported() => Directory.Exists(wallpapers) ? Directory.GetDirectories(wallpapers) : [];

    private static LivelyInfoModel Info(string folder) =>
        JsonStorage<LivelyInfoModel>.LoadData(Path.Combine(folder, LibraryScanner.LivelyInfoFileName));

    private static string[] Files(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f)).Order(StringComparer.Ordinal).ToArray();

    #region files

    [Fact]
    public async Task File_CopiesOnlyThatFileAndWritesLivelyInfo()
    {
        var source = tmp.WriteFile(Page("Aurora &amp; Sky"), "source", "aurora.html");
        tmp.WriteFile("body {}", "source", "style.css");

        var folder = await importer.ImportAsync(source);

        Assert.Equal(Path.Combine(wallpapers, "aurora-sky"), folder);
        Assert.Equal(["LivelyInfo.json", "aurora.html"], Files(folder));
        Assert.Equal(Page("Aurora &amp; Sky"), File.ReadAllText(Path.Combine(folder, "aurora.html")));
        Assert.True(File.Exists(source));

        var info = JObject.Parse(File.ReadAllText(Path.Combine(folder, "LivelyInfo.json")));
        Assert.Equal((int)WallpaperType.web, (int)info["Type"]!);
        Assert.Equal("aurora.html", (string?)info["FileName"]);
        Assert.Equal("Aurora & Sky", (string?)info["Title"]);
        Assert.False((bool)info["IsAbsolutePath"]!);
        Assert.Equal(JTokenType.Null, info["Thumbnail"]!.Type);
        Assert.Equal(JTokenType.Null, info["Preview"]!.Type);

        var scanned = LibraryScanner.ScanFolder(folder);
        Assert.NotNull(scanned);
        Assert.Equal(Path.Combine(folder, "aurora.html"), scanned.FilePath);
        Assert.Equal(WallpaperType.web, scanned.LivelyInfo.Type);
    }

    [Theory]
    [InlineData("page.htm")]
    [InlineData("PAGE.HTML")]
    public async Task File_HtmAndUpperCaseExtensions_AreAccepted(string name)
    {
        var folder = await importer.ImportAsync(tmp.WriteFile("<p>no title</p>", "source", name));

        Assert.Equal(name, Info(folder).FileName);
        Assert.Equal(Path.GetFileNameWithoutExtension(name), Info(folder).Title);
    }

    [Fact]
    public async Task File_WithoutUsableTitleOrSlug_UsesFallbacks()
    {
        var folder = await importer.ImportAsync(tmp.WriteFile("<title>   </title>", "source", "日本.html"));

        Assert.Equal(Path.Combine(wallpapers, "page"), folder);
        Assert.Equal("日本", Info(folder).Title);
    }

    [Fact]
    public async Task File_SameTitleTwice_GetsASuffix()
    {
        var source = tmp.WriteFile(Page("Rain"), "source", "rain.html");

        Assert.Equal(Path.Combine(wallpapers, "rain"), await importer.ImportAsync(source));
        Assert.Equal(Path.Combine(wallpapers, "rain-2"), await importer.ImportAsync(source));
    }

    [Fact]
    public async Task File_NotHtml_IsRefused()
    {
        var e = await Assert.ThrowsAsync<WebImportException>(() => importer.ImportAsync(tmp.WriteFile("text", "source", "notes.txt")));

        Assert.Equal("\"notes.txt\" is not an HTML file (.html or .htm).", e.Message);
        Assert.Empty(Imported());
    }

    [Fact]
    public async Task BadPaths_AreRefused()
    {
        Assert.Contains("is not an absolute path", (await Assert.ThrowsAsync<WebImportException>(() => importer.ImportAsync("pages/a.html"))).Message);
        await Assert.ThrowsAsync<WebImportException>(() => importer.ImportAsync(""));
        Assert.Contains("does not exist", (await Assert.ThrowsAsync<WebImportException>(() => importer.ImportAsync(tmp.Combine("missing.html")))).Message);
        Assert.False(Directory.Exists(wallpapers));
    }

    #endregion

    #region folders

    [Fact]
    public async Task Folder_IsCopiedRecursivelyWithIndexAsEntry()
    {
        var source = tmp.CreateDirectory("source", "My Page");
        File.WriteAllText(Path.Combine(source, "index.html"), Page("Waves"));
        File.WriteAllText(Path.Combine(source, "other.html"), Page("Other"));
        tmp.WriteFile("js", "source", "My Page", "js", "app.js");
        tmp.WriteFile("png", "source", "My Page", "assets", "img", "x.png");
        tmp.CreateDirectory("source", "My Page", "empty");

        var folder = await importer.ImportAsync(source + "/");

        Assert.Equal(Path.Combine(wallpapers, "waves"), folder);
        Assert.Equal(["LivelyInfo.json", "assets/img/x.png", "index.html", "js/app.js", "other.html"], Files(folder));
        Assert.True(Directory.Exists(Path.Combine(folder, "empty")));
        Assert.Equal("png", File.ReadAllText(Path.Combine(folder, "assets", "img", "x.png")));
        var info = Info(folder);
        Assert.Equal(("index.html", "Waves", WallpaperType.web), (info.FileName, info.Title, info.Type));
        Assert.Null(info.Thumbnail);
    }

    [Theory]
    [InlineData("index.html", "index.html", "a.html")]
    [InlineData("index.htm", "index.htm", "a.html")]
    [InlineData("index.html", "index.html", "index.htm")]
    [InlineData("INDEX.HTML", "INDEX.HTML", "b.html")]
    [InlineData("only.htm", "only.htm", "readme.txt")]
    public async Task Folder_EntrySelection(string expected, params string[] files)
    {
        var source = tmp.CreateDirectory("source", "site");
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(source, file), "<p></p>");
        }

        var folder = await importer.ImportAsync(source);

        Assert.Equal(expected, Info(folder).FileName);
        Assert.Equal("site", Info(folder).Title);
    }

    [Fact]
    public async Task Folder_WithoutTopLevelHtml_IsRefused()
    {
        var source = tmp.CreateDirectory("source", "site");
        tmp.WriteFile("<p></p>", "source", "site", "sub", "index.html");
        tmp.WriteFile("text", "source", "site", "readme.txt");

        var e = await Assert.ThrowsAsync<WebImportException>(() => importer.ImportAsync(source));

        Assert.Equal("\"site\" has no HTML file (.html or .htm) at its top level.", e.Message);
        Assert.Empty(Imported());
    }

    [Fact]
    public async Task Folder_WithSeveralPagesAndNoIndex_IsRefused()
    {
        var source = tmp.CreateDirectory("source", "site");
        File.WriteAllText(Path.Combine(source, "b.html"), "");
        File.WriteAllText(Path.Combine(source, "a.htm"), "");

        var e = await Assert.ThrowsAsync<WebImportException>(() => importer.ImportAsync(source));

        Assert.Equal("\"site\" has several HTML files and no index.html (a.htm, b.html); import the page file instead.", e.Message);
        Assert.Empty(Imported());
    }

    [Fact]
    public async Task Folder_WithIndexInTwoCases_IsRefused()
    {
        var source = tmp.CreateDirectory("source", "site");
        File.WriteAllText(Path.Combine(source, "index.html"), "");
        File.WriteAllText(Path.Combine(source, "Index.html"), "");

        var e = await Assert.ThrowsAsync<WebImportException>(() => importer.ImportAsync(source));

        Assert.Equal("\"site\" has several index pages (Index.html, index.html).", e.Message);
    }

    [Fact]
    public async Task Folder_ThatIsOrContainsTheLibrary_IsRefused()
    {
        tmp.WriteFile(Page("Old"), "library", "wallpapers", "old", "index.html");
        tmp.WriteFile(Page("Root"), "index.html");
        tmp.WriteFile(Page("Library"), "library", "index.html");

        foreach (var path in new[] { tmp.Path, tmp.Combine("library"), wallpapers })
        {
            var e = await Assert.ThrowsAsync<WebImportException>(() => importer.ImportAsync(path));
            Assert.EndsWith("contains the wallpaper library and cannot be imported.", e.Message);
        }
        Assert.Equal([Path.Combine(wallpapers, "old")], Imported());
    }

    [Fact]
    public async Task Folder_InsideTheLibrary_CanBeImportedAgain()
    {
        var existing = Path.GetDirectoryName(tmp.WriteFile(Page("Old"), "library", "wallpapers", "old", "index.html"))!;

        var folder = await importer.ImportAsync(existing);

        Assert.Equal(Path.Combine(wallpapers, "old-2"), folder);
        Assert.Equal(["LivelyInfo.json", "index.html"], Files(folder));
    }

    [Fact]
    public async Task Folder_LinksDoNotLeaveTheFolder()
    {
        var outside = tmp.WriteFile("secret", "outside", "secret.txt");
        var source = tmp.CreateDirectory("source", "site");
        File.WriteAllText(Path.Combine(source, "page.html"), Page("Linked"));
        tmp.WriteFile("data", "source", "site", "sub", "data.txt");
        File.CreateSymbolicLink(Path.Combine(source, "index.html"), "page.html");
        File.CreateSymbolicLink(Path.Combine(source, "data-link.txt"), Path.Combine("sub", "data.txt"));
        File.CreateSymbolicLink(Path.Combine(source, "secret.txt"), outside);
        File.CreateSymbolicLink(Path.Combine(source, "up.txt"), Path.Combine("..", "..", "outside", "secret.txt"));
        File.CreateSymbolicLink(Path.Combine(source, "dangling.txt"), "missing.txt");
        Directory.CreateSymbolicLink(Path.Combine(source, "out-dir"), tmp.Combine("outside"));
        Directory.CreateSymbolicLink(Path.Combine(source, "in-dir"), "sub");
        // A file link that only leaves the folder through a linked directory.
        File.CreateSymbolicLink(Path.Combine(source, "via-dir.txt"), Path.Combine("out-dir", "secret.txt"));

        var folder = await importer.ImportAsync(source);

        Assert.Equal(["LivelyInfo.json", "data-link.txt", "index.html", "page.html", "sub/data.txt"], Files(folder));
        Assert.Null(new FileInfo(Path.Combine(folder, "index.html")).LinkTarget);
        Assert.Equal(Page("Linked"), File.ReadAllText(Path.Combine(folder, "index.html")));
        Assert.Equal("data", File.ReadAllText(Path.Combine(folder, "data-link.txt")));
        Assert.Equal("index.html", Info(folder).FileName);
    }

    [Fact]
    public async Task Folder_WhoseOnlyPageLinksOutside_HasNoPage()
    {
        var outside = tmp.WriteFile(Page("Outside"), "outside", "page.html");
        var source = tmp.CreateDirectory("source", "site");
        File.CreateSymbolicLink(Path.Combine(source, "index.html"), outside);

        var e = await Assert.ThrowsAsync<WebImportException>(() => importer.ImportAsync(source));

        Assert.Contains("has no HTML file", e.Message);
    }

    #endregion

    #region existing Lively wallpapers

    private string LivelyFolder(LivelyInfoModel info, params string[] files)
    {
        var source = tmp.CreateDirectory("source", "lively-wp");
        foreach (var file in files)
        {
            tmp.WriteFile(Page(file), "source", "lively-wp", file);
        }
        JsonStorage<LivelyInfoModel>.StoreData(Path.Combine(source, LibraryScanner.LivelyInfoFileName), info);
        return source;
    }

    [Fact]
    public async Task LivelyFolder_KeepsItsLivelyInfo()
    {
        var source = LivelyFolder(new LivelyInfoModel
        {
            Title = "Custom Title",
            Author = "Someone",
            Desc = "A visualiser",
            Type = WallpaperType.webaudio,
            FileName = "main.html",
            Thumbnail = "thumb.png",
            Preview = "missing.gif",
        }, "main.html", "index.html", "thumb.png", "LivelyProperties.json");

        var folder = await importer.ImportAsync(source);

        Assert.Equal(Path.Combine(wallpapers, "custom-title"), folder);
        Assert.Equal(["LivelyInfo.json", "LivelyProperties.json", "index.html", "main.html", "thumb.png"], Files(folder));
        var info = Info(folder);
        Assert.Equal(("Custom Title", "Someone", "A visualiser"), (info.Title, info.Author, info.Desc));
        Assert.Equal((WallpaperType.webaudio, "main.html", false), (info.Type, info.FileName, info.IsAbsolutePath));
        Assert.Equal("thumb.png", info.Thumbnail);
        Assert.Null(info.Preview);
        Assert.Equal(Path.Combine(folder, "thumb.png"), LibraryScanner.ScanFolder(folder)!.ThumbnailPath);
    }

    [Fact]
    public async Task LivelyFolder_WithoutTitle_GetsThePageTitle()
    {
        var source = LivelyFolder(new LivelyInfoModel { Title = " ", Type = WallpaperType.web, FileName = "index.html" }, "index.html");

        var folder = await importer.ImportAsync(source);

        Assert.Equal("index.html", Info(folder).Title);
    }

    [Theory]
    [InlineData(WallpaperType.video, "index.html", false, "is a Lively wallpaper of type video; only web and webaudio wallpapers can be imported as a page folder.")]
    [InlineData(WallpaperType.url, "https://example.com", false, "is a Lively wallpaper of type url;")]
    [InlineData(WallpaperType.web, "missing.html", false, "The page \"missing.html\" named in the LivelyInfo.json of \"lively-wp\" is not in the folder.")]
    [InlineData(WallpaperType.web, "../outside.html", false, "is not in the folder.")]
    [InlineData(WallpaperType.web, "/etc/hostname", false, "is not in the folder.")]
    [InlineData(WallpaperType.web, "index.html", true, "is not in the folder.")]
    [InlineData(WallpaperType.web, "", false, "is not in the folder.")]
    public async Task LivelyFolder_Unusable_IsRefused(WallpaperType type, string fileName, bool absolute, string message)
    {
        tmp.WriteFile(Page("Outside"), "source", "outside.html");
        var source = LivelyFolder(new LivelyInfoModel { Title = "X", Type = type, FileName = fileName, IsAbsolutePath = absolute }, "index.html");

        var e = await Assert.ThrowsAsync<WebImportException>(() => importer.ImportAsync(source));

        Assert.Contains(message, e.Message);
        Assert.Empty(Imported());
    }

    [Fact]
    public async Task LivelyFolder_UnreadableLivelyInfo_IsRefused()
    {
        var source = tmp.CreateDirectory("source", "broken");
        File.WriteAllText(Path.Combine(source, "index.html"), Page("Broken"));
        File.WriteAllText(Path.Combine(source, LibraryScanner.LivelyInfoFileName), "{ not json");

        var e = await Assert.ThrowsAsync<WebImportException>(() => importer.ImportAsync(source));

        Assert.StartsWith("The LivelyInfo.json of \"broken\" cannot be read", e.Message);
        Assert.Empty(Imported());
    }

    #endregion

    #region titles

    [Theory]
    [InlineData("<title>Plain</title>", "Plain")]
    [InlineData("<TITLE lang=\"en\">\n  Hello\n\t World  </TITLE >", "Hello World")]
    [InlineData("<title>&lt;b&gt; &amp; &#x263A; &eacute;</title>", "<b> & ☺ é")]
    [InlineData("<title>First</title><title>Second</title>", "First")]
    [InlineData("<titlex>No</titlex>", null)]
    [InlineData("<title></title>", null)]
    [InlineData("<title>Unclosed", null)]
    [InlineData("", null)]
    public void ReadTitle_Extracts(string html, string? expected)
    {
        Assert.Equal(expected, WebImporter.ReadTitle(tmp.WriteFile(html, "page.html")));
    }

    [Fact]
    public void ReadTitle_IsCapped()
    {
        var title = WebImporter.ReadTitle(tmp.WriteFile("<title>" + new string('a', 99) + " " + new string('b', 300) + "</title>", "page.html"));

        Assert.Equal(new string('a', 99), title);
    }

    [Fact]
    public void ReadTitle_ReadsOnlyTheStartOfTheFile()
    {
        var page = tmp.WriteFile(new string(' ', WebImporter.TitleReadLimit) + "<title>Late</title>", "late.html");
        var early = tmp.WriteFile("﻿<title>Early</title>" + new string(' ', 4 * WebImporter.TitleReadLimit), "early.html");

        Assert.Null(WebImporter.ReadTitle(page));
        Assert.Equal("Early", WebImporter.ReadTitle(early));
    }

    #endregion

    #region failures and cancellation

    [Fact]
    public async Task CopyFailure_RemovesTheNewFolder()
    {
        var source = tmp.CreateDirectory("source", "site");
        File.WriteAllText(Path.Combine(source, "index.html"), Page("Site"));
        File.WriteAllText(Path.Combine(source, "a.js"), "");
        importer.FileCopied = _ => throw new IOException("No space left on device");

        var e = await Assert.ThrowsAsync<WebImportException>(() => importer.ImportAsync(source));

        Assert.Equal("\"site\" could not be imported: No space left on device", e.Message);
        Assert.IsType<IOException>(e.InnerException);
        Assert.Empty(Imported());
    }

    [Fact]
    public async Task Cancel_DuringTheCopy_LeavesNothingBehind()
    {
        var source = tmp.CreateDirectory("source", "site");
        foreach (var name in new[] { "index.html", "a.js", "b.js", "c.js" })
        {
            File.WriteAllText(Path.Combine(source, name), "");
        }
        using var cts = new CancellationTokenSource();
        var copied = 0;
        importer.FileCopied = _ =>
        {
            copied++;
            cts.Cancel();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importer.ImportAsync(source, cts.Token));

        Assert.Equal(1, copied);
        Assert.Empty(Imported());
    }

    [Fact]
    public async Task Cancel_BeforeTheImport_CreatesNothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importer.ImportAsync(tmp.WriteFile(Page("A"), "a.html"), cts.Token));

        Assert.Empty(Imported());
    }

    #endregion

    #region URLs

    [Theory]
    [InlineData("https://example.com", "https://example.com/", "example.com", "example-com")]
    [InlineData("  HTTP://Example.COM/a b/?x=1#top  ", "http://example.com/a%20b/?x=1#top", "example.com/a b/", "example-com-a-b")]
    [InlineData("https://webglsamples.org:8443/aquarium/aquarium.html", "https://webglsamples.org:8443/aquarium/aquarium.html",
        "webglsamples.org:8443/aquarium/aquarium.html", "webglsamples-org-8443-aquarium-aquarium-html")]
    [InlineData("http://localhost:8080", "http://localhost:8080/", "localhost:8080", "localhost-8080")]
    public void AddUrl_WritesAUrlWallpaper(string url, string address, string title, string slug)
    {
        var folder = importer.AddUrl(url);

        Assert.Equal(Path.Combine(wallpapers, slug), folder);
        Assert.Equal(["LivelyInfo.json"], Files(folder));
        var info = Info(folder);
        Assert.Equal((WallpaperType.url, address, title, false), (info.Type, info.FileName, info.Title, info.IsAbsolutePath));
        Assert.Null(info.Thumbnail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example.com")]
    [InlineData("www.example.com/page")]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/home/user/page.html")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://")]
    [InlineData("http:///path")]
    public void AddUrl_NotAWebAddress_IsRefused(string url)
    {
        var e = Assert.Throws<WebImportException>(() => importer.AddUrl(url));

        Assert.Equal($"\"{url.Trim()}\" is not a web address; it must start with http:// or https://.", e.Message);
        Assert.False(Directory.Exists(wallpapers));
    }

    [Theory]
    [InlineData("https://user:secret@example.com/page", "https://example.com/page")]
    [InlineData("http://user@example.com", "http://example.com/")]
    [InlineData("https://:secret@example.com:8443/", "https://example.com:8443/")]
    public void AddUrl_WithCredentials_IsRefusedWithoutEchoingThem(string url, string shown)
    {
        var e = Assert.Throws<WebImportException>(() => importer.AddUrl(url));

        Assert.Equal($"\"{shown}\" has a user name or password; addresses with credentials are not supported.", e.Message);
        Assert.DoesNotContain("secret", e.Message);
        Assert.False(Directory.Exists(wallpapers));
    }

    [Theory]
    [InlineData("mailto:someone@example.com", "…@example.com")]
    [InlineData("ftp://user:secret@example.com/file", "ftp://…@example.com/file")]
    [InlineData("http//user:secret@example.com/a@b", "http//…@example.com/a@b")]
    public void AddUrl_RefusedAddress_HidesItsUserInfo(string url, string shown)
    {
        var e = Assert.Throws<WebImportException>(() => importer.AddUrl(url));

        Assert.Equal($"\"{shown}\" is not a web address; it must start with http:// or https://.", e.Message);
    }

    [Fact]
    public void AddUrl_SameAddressTwice_CreatesTwoFolders()
    {
        Assert.Equal(Path.Combine(wallpapers, "example-com"), importer.AddUrl("https://example.com"));
        Assert.Equal(Path.Combine(wallpapers, "example-com-2"), importer.AddUrl("https://example.com"));
    }

    [Fact]
    public void AddUrl_RoundTripsThroughTheScanner()
    {
        var folder = importer.AddUrl("https://example.com/page?q=1");

        var model = LibraryScanner.ScanFolder(folder);

        Assert.NotNull(model);
        Assert.Equal(WallpaperType.url, model.LivelyInfo.Type);
        Assert.Equal("https://example.com/page?q=1", model.FilePath);
        Assert.Equal("example.com/page", model.Title);
        Assert.Equal(folder, model.LivelyInfoFolderPath);
        Assert.Single(LibraryScanner.ScanFolders([wallpapers]));
    }

    #endregion
}
