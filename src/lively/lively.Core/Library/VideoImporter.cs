using System.Diagnostics;
using System.Globalization;
using System.Text;
using lively.Common;
using lively.Common.Helpers.Storage;
using lively.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace lively.Core.Library
{
    /// <summary>Exit code and captured output of a finished process.</summary>
    public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>Runs an external program to completion. Exists so that tests can fake ffmpeg.</summary>
    public interface IProcessRunner
    {
        /// <summary>Runs <paramref name="fileName"/> with the arguments (no shell) and captures stdout and stderr.</summary>
        /// <remarks>Cancellation kills the process tree and throws <see cref="OperationCanceledException"/>.</remarks>
        Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct);
    }

    /// <summary><see cref="IProcessRunner"/> over <see cref="Process"/>.</summary>
    public sealed class ProcessRunner : IProcessRunner
    {
        public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                psi.ArgumentList.Add(argument);
            }

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"{fileName} could not be started.");
            // Nothing to send; an open stdin would let ffmpeg wait for keyboard commands.
            process.StandardInput.Close();
            // Both pipes are drained concurrently, a full stderr pipe would otherwise block the process.
            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already exited.
                }
                throw;
            }
            return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
    }

    /// <summary>A video could not be imported; the message is meant for the user.</summary>
    public sealed class VideoImportException(string message, Exception? inner = null) : ImportException(message, inner);

    /// <summary>Video stream properties read by ffprobe. Duration is 0 when unknown.</summary>
    internal sealed record VideoProbe(string Codec, int Width, int Height, double Duration);

    /// <summary>
    /// Imports a video file into the library: a new wallpaper folder with a copy of the video, a thumbnail
    /// made by ffmpeg and a LivelyInfo.json. Needs ffmpeg and ffprobe on PATH.
    /// </summary>
    public sealed class VideoImporter
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public const string ThumbnailFileName = "thumbnail.jpg";
        private const int MaxErrorOutputLength = 500;

        private readonly string wallpapersDirectory;
        private readonly IProcessRunner runner;
        private readonly Func<string, string?> findOnPath;

        /// <param name="wallpapersDirectory">Parent folder of the library's wallpapers; created when missing.</param>
        /// <param name="runner">Runs ffprobe and ffmpeg; <see cref="ProcessRunner"/> when null.</param>
        /// <param name="findOnPath">Resolves a program name to a path, null when missing; a PATH search when null.</param>
        public VideoImporter(string wallpapersDirectory, IProcessRunner? runner = null, Func<string, string?>? findOnPath = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(wallpapersDirectory);
            this.wallpapersDirectory = wallpapersDirectory;
            this.runner = runner ?? new ProcessRunner();
            this.findOnPath = findOnPath ?? FindOnPath;
        }

        public string WallpapersDirectory => wallpapersDirectory;

        /// <summary>Copies the video at <paramref name="path"/> into a new wallpaper folder.</summary>
        /// <returns>The created wallpaper folder (it contains LivelyInfo.json).</returns>
        /// <exception cref="VideoImportException">The file is not an importable video, or ffmpeg failed.</exception>
        /// <exception cref="OperationCanceledException">Cancelled; nothing is left behind.</exception>
        public async Task<string> ImportAsync(string path, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            {
                throw new VideoImportException($"\"{path}\" is not an absolute file path.");
            }
            path = Path.GetFullPath(path);
            if (!File.Exists(path))
            {
                throw new VideoImportException($"\"{path}\" does not exist.");
            }

            var ffprobe = findOnPath("ffprobe");
            var ffmpeg = findOnPath("ffmpeg");
            if (ffprobe == null || ffmpeg == null)
            {
                throw new VideoImportException("ffmpeg/ffprobe not found in PATH; install ffmpeg");
            }

            var fileName = Path.GetFileName(path);
            var title = Path.GetFileNameWithoutExtension(path);
            Logger.Info($"Importing video {path}.");
            string? folder = null;
            try
            {
                var probeResult = await runner.RunAsync(ffprobe,
                    ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name,width,height:format=duration", "-of", "json", path],
                    ct).ConfigureAwait(false);
                if (probeResult.ExitCode != 0)
                {
                    throw new VideoImportException($"\"{fileName}\" is not a readable video (ffprobe exit code {probeResult.ExitCode}): {Shorten(probeResult.StandardError)}");
                }
                VideoProbe probe;
                try
                {
                    probe = ParseProbe(probeResult.StandardOutput);
                }
                catch (VideoImportException e)
                {
                    throw new VideoImportException($"\"{fileName}\" {e.Message}");
                }

                folder = LibraryFolders.CreateUnique(wallpapersDirectory, Slugify(title));

                var copy = Path.Combine(folder, fileName);
                File.Copy(path, copy);
                ct.ThrowIfCancellationRequested();

                var thumbnail = Path.Combine(folder, ThumbnailFileName);
                var seek = probe.Duration > 0 ? Math.Min(1.0, probe.Duration / 10) : 0;
                var thumbResult = await runner.RunAsync(ffmpeg,
                    ["-nostdin", "-v", "error", "-y", "-ss", seek.ToString("0.###", CultureInfo.InvariantCulture), "-i", copy,
                        "-frames:v", "1", "-vf", "scale=480:-2", thumbnail],
                    ct).ConfigureAwait(false);
                if (thumbResult.ExitCode != 0 || !File.Exists(thumbnail))
                {
                    throw new VideoImportException($"The thumbnail of \"{fileName}\" could not be made (ffmpeg exit code {thumbResult.ExitCode}): {Shorten(thumbResult.StandardError)}");
                }

                // Written last: a folder without LivelyInfo.json is not a wallpaper.
                JsonStorage<LivelyInfoModel>.StoreData(Path.Combine(folder, LibraryScanner.LivelyInfoFileName), new LivelyInfoModel
                {
                    Title = title,
                    Desc = $"{probe.Width}x{probe.Height} {probe.Codec}",
                    Type = WallpaperType.video,
                    FileName = fileName,
                    Thumbnail = ThumbnailFileName,
                    Preview = null,
                    IsAbsolutePath = false,
                });
                Logger.Info($"Imported {path} as {folder} ({probe.Width}x{probe.Height} {probe.Codec}, {probe.Duration.ToString(CultureInfo.InvariantCulture)} s).");
                return folder;
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"Import of {path} cancelled.");
                LibraryFolders.Delete(folder);
                throw;
            }
            catch (VideoImportException e)
            {
                Logger.Error($"Import of {path} failed: {e.Message}");
                LibraryFolders.Delete(folder);
                throw;
            }
            catch (Exception e)
            {
                Logger.Error($"Import of {path} failed: {e}");
                LibraryFolders.Delete(folder);
                throw new VideoImportException($"\"{fileName}\" could not be imported: {e.Message}", e);
            }
        }

        /// <summary>Reads the JSON printed by ffprobe for the first video stream and the format duration.</summary>
        /// <exception cref="VideoImportException">The output is not JSON or has no video stream.</exception>
        internal static VideoProbe ParseProbe(string json)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new VideoImportException("could not be read by ffprobe (unexpected output).", e);
            }
            if (root["streams"] is not JArray { Count: > 0 } streams || streams[0] is not JObject stream)
            {
                throw new VideoImportException("has no video stream.");
            }

            var codec = (string?)stream["codec_name"] ?? "unknown";
            var width = ReadInt(stream["width"]);
            var height = ReadInt(stream["height"]);
            var duration = 0.0;
            if (root["format"]?["duration"] is JToken d
                && double.TryParse(d.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                && double.IsFinite(parsed) && parsed > 0)
            {
                duration = parsed;
            }
            return new VideoProbe(codec, width, height, duration);
        }

        /// <summary>Folder name for a title, "video" when nothing is left (see <see cref="LibraryFolders.Slugify"/>).</summary>
        internal static string Slugify(string title) => LibraryFolders.Slugify(title, "video");

        /// <summary>First file named <paramref name="name"/> in a PATH directory, or null.</summary>
        internal static string? FindOnPath(string name)
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static int ReadInt(JToken? token) =>
            token != null && int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

        private static string Shorten(string output)
        {
            var text = output.Trim();
            if (text.Length == 0)
            {
                return "no error output";
            }
            return text.Length > MaxErrorOutputLength ? text[..MaxErrorOutputLength] + "…" : text;
        }
    }
}
