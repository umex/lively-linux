using lively.Common;
using lively.Common.Helpers.Storage;
using lively.Core.Library;
using lively.Models;
using lively.Models.Message;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace lively.Core.Wallpapers
{
    /// <summary>
    /// Makes the missing thumbnail of a web wallpaper (web, webaudio, url) in the library: asks its player for a
    /// screenshot (cmd_screenshot) and, when the player answers msg_screenshot with Success, records the picture
    /// in the folder's LivelyInfo.json.
    /// </summary>
    /// <remarks>
    /// At most one request per wallpaper folder is outstanding, and each player is asked at most once. A failed
    /// or unanswered request is not repeated until the wallpaper is started again.
    /// </remarks>
    internal sealed class WebThumbnails
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public const string ThumbnailFileName = VideoImporter.ThumbnailFileName;
        /// <summary>Time the page gets to draw before the capture, in milliseconds.</summary>
        public const uint CaptureDelayMs = 3000;

        private readonly string wallpapersDirectory;
        private readonly TimeSpan staleAfter;
        private readonly object sync = new();
        // Wallpaper folder -> the player asked for its thumbnail.
        private readonly Dictionary<string, PendingRequest> pending = new(StringComparer.Ordinal);
        // Players asked already (or that need nothing), so that a resume does not ask them again.
        private readonly HashSet<IWallpaperPlayer> handled = [];
        // LivelyInfo.json updates, one at a time.
        private readonly object fileLock = new();

        private sealed record PendingRequest(IWallpaperPlayer Player, string Folder, DateTime Time);

        /// <param name="wallpapersDirectory">Only wallpaper folders below this one get thumbnails.</param>
        /// <param name="staleAfter">
        /// An unanswered request older than this no longer blocks a request by another player of the same folder.
        /// </param>
        public WebThumbnails(string wallpapersDirectory, TimeSpan? staleAfter = null)
        {
            this.wallpapersDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(wallpapersDirectory));
            this.staleAfter = staleAfter ?? TimeSpan.FromSeconds(60);
        }

        /// <summary>Raised (on a thread-pool thread) with the wallpaper folder whose LivelyInfo.json got a thumbnail.</summary>
        public event EventHandler<string>? Updated;

        public static bool IsWebType(WallpaperType type) => type is WallpaperType.web or WallpaperType.webaudio or WallpaperType.url;

        /// <summary>
        /// Sends cmd_screenshot to a loaded, playing <paramref name="player"/> when its wallpaper needs a thumbnail.
        /// Does nothing for a player asked before.
        /// </summary>
        public void Request(IWallpaperPlayer player)
        {
            var model = player.Model;
            if (!IsWebType(model.LivelyInfo.Type))
            {
                return;
            }
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(model.LivelyInfoFolderPath));
            var file = Path.Combine(folder, ThumbnailFileName);
            lock (sync)
            {
                if (player.IsExited || !handled.Add(player))
                {
                    return;
                }
                if (!folder.StartsWith(wallpapersDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    Logger.Debug($"No thumbnail for {folder}: not in the library {wallpapersDirectory}.");
                    return;
                }
                if (HasThumbnail(folder))
                {
                    return;
                }
                if (pending.TryGetValue(folder, out var other))
                {
                    if (DateTime.UtcNow - other.Time < staleAfter)
                    {
                        Logger.Debug($"Thumbnail of {folder} already requested from the player on {other.Player.Screen.Id}.");
                        return;
                    }
                    Logger.Warn($"No screenshot answer from the player on {other.Player.Screen.Id} for {folder}; asking another one.");
                    Drop(other);
                }
                pending[folder] = new PendingRequest(player, folder, DateTime.UtcNow);
                player.MessageReceived += OnMessageReceived;
            }

            Logger.Info($"Requesting a thumbnail of {folder} from the player on {player.Screen.Id}.");
            try
            {
                player.SendMessage(new LivelyScreenshotCmd
                {
                    Format = ScreenshotFormat.jpeg,
                    FilePath = file,
                    Delay = CaptureDelayMs,
                    Width = VideoImporter.ThumbnailWidth,
                });
            }
            catch (Exception e)
            {
                Logger.Error($"Sending cmd_screenshot to the player on {player.Screen.Id} failed: {e.Message}");
                lock (sync)
                {
                    if (pending.TryGetValue(folder, out var request) && request.Player == player)
                    {
                        Drop(request);
                    }
                }
            }
        }

        /// <summary>Stops waiting for <paramref name="player"/> (closed or exited); a later answer is ignored.</summary>
        public void Forget(IWallpaperPlayer player)
        {
            lock (sync)
            {
                handled.Remove(player);
                var request = pending.Values.FirstOrDefault(x => x.Player == player);
                if (request != null)
                {
                    Logger.Info($"The player on {player.Screen.Id} closed before answering the screenshot request for {request.Folder}.");
                    Drop(request);
                }
            }
        }

        private void OnMessageReceived(object? sender, IpcMessage message)
        {
            if (message is not LivelyMessageScreenshot shot || sender is not IWallpaperPlayer player)
            {
                return;
            }
            PendingRequest? request;
            lock (sync)
            {
                request = pending.Values.FirstOrDefault(x => x.Player == player);
                if (request == null)
                {
                    return;
                }
                var expected = Path.Combine(request.Folder, ThumbnailFileName);
                if (shot.FileName != ThumbnailFileName && shot.FileName != expected)
                {
                    Logger.Warn($"Ignoring msg_screenshot for \"{shot.FileName}\" from the player on {player.Screen.Id}; expected {ThumbnailFileName}.");
                    return;
                }
                Drop(request);
            }

            if (!shot.Success)
            {
                Logger.Warn($"The player on {player.Screen.Id} could not take the thumbnail of {request.Folder}.");
                return;
            }
            // The answer arrives on the player's output reader; keep file work off it.
            _ = Task.Run(() => Apply(request.Folder));
        }

        /// <summary>Records the written thumbnail in LivelyInfo.json (only the Thumbnail field changes) and raises <see cref="Updated"/>.</summary>
        private void Apply(string folder)
        {
            var file = Path.Combine(folder, ThumbnailFileName);
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length == 0)
                {
                    Logger.Warn($"The player reported a thumbnail for {folder}, but {file} is missing or empty.");
                    return;
                }
                var infoPath = Path.Combine(folder, LibraryScanner.LivelyInfoFileName);
                lock (fileLock)
                {
                    var json = JObject.Parse(File.ReadAllText(infoPath));
                    json["Thumbnail"] = ThumbnailFileName;
                    // Written to a temporary file first so a crash never leaves a truncated LivelyInfo.json.
                    var tmp = infoPath + ".tmp";
                    File.WriteAllText(tmp, json.ToString(Formatting.Indented));
                    File.Move(tmp, infoPath, overwrite: true);
                }
                Logger.Info($"Thumbnail of {folder} recorded.");
            }
            catch (Exception e)
            {
                Logger.Error($"Recording the thumbnail of {folder} failed: {e.Message}");
                return;
            }
            try
            {
                Updated?.Invoke(this, folder);
            }
            catch (Exception e)
            {
                Logger.Error("Thumbnail Updated handler failed: " + e);
            }
        }

        /// <summary>Whether the folder's LivelyInfo.json, as on disk now, names a thumbnail that exists.</summary>
        private static bool HasThumbnail(string folder)
        {
            try
            {
                var info = JsonStorage<LivelyInfoModel>.LoadData(Path.Combine(folder, LibraryScanner.LivelyInfoFileName));
                return info != null && !string.IsNullOrWhiteSpace(info.Thumbnail)
                    && new LibraryModel(info, folder).ThumbnailPath is { } path && File.Exists(path);
            }
            catch (Exception e)
            {
                // Without a readable LivelyInfo.json there is nothing to record the thumbnail in.
                Logger.Warn($"No thumbnail for {folder}: {LibraryScanner.LivelyInfoFileName} is not readable ({e.Message}).");
                return true;
            }
        }

        // With sync held.
        private void Drop(PendingRequest request)
        {
            pending.Remove(request.Folder);
            request.Player.MessageReceived -= OnMessageReceived;
        }
    }
}
