using lively.Core;
using lively.Core.Library;
using Newtonsoft.Json.Linq;

namespace lively.Tests;

public sealed class VideoImporterTests : IDisposable
{
    private const string ProbeJson = """
        {"programs":[],"streams":[{"codec_name":"h264","width":1920,"height":1080}],"format":{"duration":"30.000000"}}
        """;

    private readonly TempDirectory tmp = new();
    private readonly FakeProcessRunner runner = new();
    private readonly string wallpapers;
    private readonly VideoImporter importer;

    public VideoImporterTests()
    {
        wallpapers = tmp.Combine("wallpapers");
        importer = new VideoImporter(wallpapers, runner, name => "/usr/bin/" + name);
    }

    public void Dispose() => tmp.Dispose();

    private string Source(string fileName) => tmp.WriteFile("video bytes", "source", fileName);

    /// <summary>Records calls; ffprobe prints <see cref="ProbeResult"/>, ffmpeg writes its output file.</summary>
    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];
        public ProcessResult ProbeResult { get; set; } = new(0, ProbeJson, "");
        public Func<ProcessResult>? Ffmpeg { get; set; }

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls.Add((fileName, arguments));
            if (fileName.EndsWith("ffprobe", StringComparison.Ordinal))
            {
                return Task.FromResult(ProbeResult);
            }
            if (Ffmpeg != null)
            {
                return Task.FromResult(Ffmpeg());
            }
            File.WriteAllText(arguments[^1], "jpeg");
            return Task.FromResult(new ProcessResult(0, "", ""));
        }
    }

    /// <summary>Writes a one-second 64x64 test video with the real ffmpeg.</summary>
    internal static async Task CreateTinyVideoAsync(string path)
    {
        var result = await new ProcessRunner().RunAsync("ffmpeg",
            ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=duration=1:size=64x64:rate=10", "-pix_fmt", "yuv420p", path],
            CancellationToken.None);
        Assert.True(result.ExitCode == 0, "ffmpeg failed: " + result.StandardError);
    }

    [Fact]
    public async Task Import_CopiesTheVideoAndWritesLivelyInfo()
    {
        var source = Source("My Clip (1).mp4");

        var folder = await importer.ImportAsync(source);

        Assert.Equal(Path.Combine(wallpapers, "my-clip-1"), folder);
        Assert.Equal("video bytes", File.ReadAllText(Path.Combine(folder, "My Clip (1).mp4")));
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(folder, "thumbnail.jpg")));

        var info = JObject.Parse(File.ReadAllText(Path.Combine(folder, "LivelyInfo.json")));
        Assert.Equal(JTokenType.Integer, info["Type"]!.Type);
        Assert.Equal(7, (int)info["Type"]!);
        Assert.False((bool)info["IsAbsolutePath"]!);
        Assert.Equal("thumbnail.jpg", (string?)info["Thumbnail"]);
        Assert.Equal("My Clip (1).mp4", (string?)info["FileName"]);
        Assert.Equal("My Clip (1)", (string?)info["Title"]);
        Assert.Equal("1920x1080 h264", (string?)info["Desc"]);
        Assert.Equal(JTokenType.Null, info["Preview"]!.Type);
        Assert.False(string.IsNullOrEmpty((string?)info["AppVersion"]));

        var scanned = LibraryScanner.ScanFolder(folder);
        Assert.NotNull(scanned);
        Assert.Equal(Path.Combine(folder, "My Clip (1).mp4"), scanned.FilePath);
        Assert.Equal(Path.Combine(folder, "thumbnail.jpg"), scanned.ThumbnailPath);
    }

    [Fact]
    public async Task Import_RunsFfprobeAndFfmpegWithTheExpectedArguments()
    {
        var source = Source("clip.mp4");

        var folder = await importer.ImportAsync(source);

        Assert.Equal(2, runner.Calls.Count);
        var (probe, probeArgs) = runner.Calls[0];
        Assert.Equal("/usr/bin/ffprobe", probe);
        Assert.Equal(["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name,width,height:format=duration", "-of", "json", source], probeArgs);
        var (ffmpeg, ffmpegArgs) = runner.Calls[1];
        Assert.Equal("/usr/bin/ffmpeg", ffmpeg);
        Assert.Equal(["-nostdin", "-v", "error", "-y", "-ss", "1", "-i", Path.Combine(folder, "clip.mp4"), "-frames:v", "1", "-vf", "scale=480:-2",
            Path.Combine(folder, "thumbnail.jpg")], ffmpegArgs);
    }

    [Theory]
    [InlineData("""{"format":{"duration":"5.000000"}}""", "0.5")]
    [InlineData("""{"format":{"duration":"N/A"}}""", "0")]
    [InlineData("""{}""", "0")]
    public async Task Import_SeeksToATenthOfShortVideos(string formatJson, string expectedSeek)
    {
        var root = JObject.Parse(formatJson);
        root["streams"] = JArray.Parse("""[{"codec_name":"vp9","width":64,"height":48}]""");
        runner.ProbeResult = new ProcessResult(0, root.ToString(), "");

        await importer.ImportAsync(Source("clip.webm"));

        var args = runner.Calls[1].Arguments;
        Assert.Equal(expectedSeek, args[args.ToList().IndexOf("-ss") + 1]);
    }

    [Fact]
    public async Task Import_SameTitleTwice_GetsASuffix()
    {
        var source = Source("Rain.mp4");

        var first = await importer.ImportAsync(source);
        var second = await importer.ImportAsync(source);
        var third = await importer.ImportAsync(source);

        Assert.Equal(Path.Combine(wallpapers, "rain"), first);
        Assert.Equal(Path.Combine(wallpapers, "rain-2"), second);
        Assert.Equal(Path.Combine(wallpapers, "rain-3"), third);
    }

    [Theory]
    [InlineData("My Clip (1)", "my-clip-1")]
    [InlineData("  --Hello__World--  ", "hello-world")]
    [InlineData("Ünïcödé", "n-c-d")]
    [InlineData("日本語", "video")]
    [InlineData("", "video")]
    [InlineData("a.b.c", "a-b-c")]
    public void Slugify(string title, string expected)
    {
        Assert.Equal(expected, VideoImporter.Slugify(title));
    }

    [Fact]
    public void Slugify_LimitsTheLength()
    {
        var slug = VideoImporter.Slugify(new string('a', 59) + " " + new string('b', 100));

        Assert.Equal(new string('a', 59), slug);
        Assert.Equal(60, VideoImporter.Slugify(new string('x', 100)).Length);
    }

    [Fact]
    public void ParseProbe_ReadsStreamAndDuration()
    {
        var probe = VideoImporter.ParseProbe(ProbeJson);

        Assert.Equal(new VideoProbe("h264", 1920, 1080, 30), probe);
    }

    [Fact]
    public async Task Import_FfprobeFails_ReportsStderrAndLeavesNothing()
    {
        runner.ProbeResult = new ProcessResult(1, "", "  clip.mp4: Invalid data found when processing input\n");

        var e = await Assert.ThrowsAsync<VideoImportException>(() => importer.ImportAsync(Source("clip.mp4")));

        Assert.Contains("clip.mp4: Invalid data found when processing input", e.Message);
        Assert.Single(runner.Calls);
        Assert.False(Directory.Exists(wallpapers) && Directory.EnumerateFileSystemEntries(wallpapers).Any());
    }

    [Fact]
    public async Task Import_NoVideoStream_Fails()
    {
        runner.ProbeResult = new ProcessResult(0, """{"programs":[],"streams":[],"format":{"duration":"3.0"}}""", "");

        var e = await Assert.ThrowsAsync<VideoImportException>(() => importer.ImportAsync(Source("song.mp3")));

        Assert.Contains("has no video stream", e.Message);
        Assert.False(Directory.Exists(wallpapers) && Directory.EnumerateFileSystemEntries(wallpapers).Any());
    }

    [Fact]
    public async Task Import_ThumbnailFails_RemovesTheFolder()
    {
        runner.Ffmpeg = () => new ProcessResult(1, "", "Conversion failed!");

        var e = await Assert.ThrowsAsync<VideoImportException>(() => importer.ImportAsync(Source("clip.mp4")));

        Assert.Contains("Conversion failed!", e.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(wallpapers));
    }

    [Fact]
    public async Task Import_Cancelled_RemovesTheFolderAndRethrows()
    {
        runner.Ffmpeg = () => throw new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importer.ImportAsync(Source("clip.mp4")));

        Assert.Empty(Directory.EnumerateFileSystemEntries(wallpapers));
    }

    [Fact]
    public async Task Import_UnexpectedError_IsWrappedAndRemovesTheFolder()
    {
        runner.Ffmpeg = () => throw new System.ComponentModel.Win32Exception("permission denied");

        var e = await Assert.ThrowsAsync<VideoImportException>(() => importer.ImportAsync(Source("clip.mp4")));

        Assert.Contains("permission denied", e.Message);
        Assert.IsType<System.ComponentModel.Win32Exception>(e.InnerException);
        Assert.Empty(Directory.EnumerateFileSystemEntries(wallpapers));
    }

    [Theory]
    [InlineData("ffmpeg")]
    [InlineData("ffprobe")]
    public async Task Import_MissingTool_AsksToInstallFfmpeg(string missing)
    {
        var withoutTool = new VideoImporter(wallpapers, runner, name => name == missing ? null : "/usr/bin/" + name);

        var e = await Assert.ThrowsAsync<VideoImportException>(() => withoutTool.ImportAsync(Source("clip.mp4")));

        Assert.Contains("install ffmpeg", e.Message);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Import_RelativeOrMissingPath_Fails()
    {
        await Assert.ThrowsAsync<VideoImportException>(() => importer.ImportAsync("relative/clip.mp4"));
        await Assert.ThrowsAsync<VideoImportException>(() => importer.ImportAsync(""));
        var e = await Assert.ThrowsAsync<VideoImportException>(() => importer.ImportAsync(tmp.Combine("missing.mp4")));

        Assert.Contains("does not exist", e.Message);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void WallpapersDirectory_DefaultIsTheFirstScanFolder()
    {
        Assert.Equal(LibraryScanner.DefaultScanFolders[0], new CoreOptions().WallpapersDirectory);
    }

    [FfmpegFact]
    public async Task Import_RealTinyVideo_CreatesThumbnailAndScannableWallpaper()
    {
        var source = tmp.Combine("tiny.mp4");
        await CreateTinyVideoAsync(source);
        var real = new VideoImporter(wallpapers);

        var folder = await real.ImportAsync(source);

        Assert.Equal(Path.Combine(wallpapers, "tiny"), folder);
        Assert.True(new FileInfo(Path.Combine(folder, "thumbnail.jpg")).Length > 0);
        var item = LibraryScanner.ScanFolder(folder);
        Assert.NotNull(item);
        Assert.NotNull(item.FilePath);
        Assert.NotNull(item.ThumbnailPath);
        Assert.Equal("64x64 h264", item.LivelyInfo.Desc);
    }

    [FfmpegFact]
    public async Task Import_RealNonVideo_FailsWithoutLeavingAFolder()
    {
        var source = tmp.WriteFile("not a video", "notes.mp4");
        var real = new VideoImporter(wallpapers);

        await Assert.ThrowsAsync<VideoImportException>(() => real.ImportAsync(source));

        Assert.False(Directory.Exists(wallpapers) && Directory.EnumerateFileSystemEntries(wallpapers).Any());
    }
}
