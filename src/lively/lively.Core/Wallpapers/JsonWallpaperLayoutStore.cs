using lively.Common.Helpers.Storage;
using lively.Models;

namespace lively.Core.Wallpapers
{
    public sealed class JsonWallpaperLayoutStore : IWallpaperLayoutStore
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly string path;
        private readonly object sync = new();

        public JsonWallpaperLayoutStore(string path)
        {
            this.path = path;
        }

        public IReadOnlyList<WallpaperLayoutModel> Load()
        {
            lock (sync)
            {
                if (!File.Exists(path))
                {
                    return [];
                }

                try
                {
                    var data = JsonStorage<List<WallpaperLayoutModel?>>.LoadData(path);
                    return data
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.DisplayId) && !string.IsNullOrWhiteSpace(x.LivelyInfoPath))
                        .Select(x => x!)
                        .ToList();
                }
                catch (Exception e)
                {
                    Logger.Warn("Ignoring unreadable wallpaper layout " + path + ": " + e.Message);
                    return [];
                }
            }
        }

        public void Save(IEnumerable<WallpaperLayoutModel> layout)
        {
            var data = layout.ToList();
            lock (sync)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                // Write to a temporary file first so a crash never leaves a truncated layout.
                var tmp = path + ".tmp";
                JsonStorage<List<WallpaperLayoutModel>>.StoreData(tmp, data);
                File.Move(tmp, path, overwrite: true);
            }
        }
    }
}
