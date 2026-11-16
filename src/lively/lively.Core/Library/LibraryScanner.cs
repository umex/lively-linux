using lively.Common;
using lively.Common.Helpers.Storage;
using lively.Models;

namespace lively.Core.Library
{
    /// <summary>
    /// Reads wallpaper folders (a folder with a LivelyInfo.json) into <see cref="LibraryModel"/>s.
    /// </summary>
    public static class LibraryScanner
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public const string LivelyInfoFileName = "LivelyInfo.json";

        /// <summary>
        /// Parent folders whose subdirectories are wallpapers: the library and the temporary import folder.
        /// </summary>
        public static IReadOnlyList<string> DefaultScanFolders { get; } =
        [
            Path.Combine(Constants.CommonPaths.LibraryDir, "wallpapers"),
            Path.Combine(Constants.CommonPaths.LibraryDir, "saveData", "wptmp"),
        ];

        /// <summary>
        /// Loads wallpapers from the given parent folders, only the top directory level is scanned.
        /// Missing or unreadable parent folders are skipped.
        /// </summary>
        /// <returns>Wallpapers sorted by title.</returns>
        public static List<LibraryModel> ScanFolders(IEnumerable<string> parentFolders)
        {
            var items = new List<LibraryModel>();
            foreach (var parent in parentFolders)
            {
                string[] dirs;
                try
                {
                    dirs = Directory.GetDirectories(parent, "*", SearchOption.TopDirectoryOnly);
                }
                catch (Exception e)
                {
                    Logger.Error("Skipping wp folder-scan: " + e);
                    continue;
                }

                foreach (var dir in dirs)
                {
                    var item = ScanFolder(dir);
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }
            }
            return items.OrderBy(x => x.LivelyInfo.Title ?? string.Empty, StringComparer.CurrentCulture).ToList();
        }

        /// <summary>
        /// Reads one wallpaper folder.
        /// </summary>
        /// <returns>Null when the folder has no readable LivelyInfo.json.</returns>
        public static LibraryModel? ScanFolder(string folderPath)
        {
            var infoPath = Path.Combine(folderPath, LivelyInfoFileName);
            if (!File.Exists(infoPath))
            {
                Logger.Info("Not a lively wallpaper folder, skipping:- " + folderPath);
                return null;
            }

            LivelyInfoModel info;
            try
            {
                info = JsonStorage<LivelyInfoModel>.LoadData(infoPath);
            }
            catch (Exception e)
            {
                Logger.Error("Failed to read " + infoPath + ": " + e);
                return null;
            }

            if (info.Type == WallpaperType.videostream || info.Type == WallpaperType.url)
            {
                // Online content, no file.
                Logger.Info("Loading Wallpaper (no-file):- " + info.FileName + " " + info.Type);
            }
            else if (info.IsAbsolutePath)
            {
                Logger.Info("Loading Wallpaper(absolute):- " + info.FileName + " " + info.Type);
            }
            else
            {
                Logger.Info("Loading Wallpaper(relative):- " + Path.Combine(folderPath, info.FileName ?? string.Empty) + " " + info.Type);
            }

            try
            {
                return new LibraryModel(info, folderPath);
            }
            catch (Exception e)
            {
                Logger.Error("Invalid wallpaper " + folderPath + ": " + e);
                return null;
            }
        }

        /// <summary>
        /// Index at which an item with the given title keeps a title-sorted list sorted (binary search).
        /// </summary>
        public static int FindInsertIndex(IReadOnlyList<LibraryModel> items, string title)
        {
            ArgumentNullException.ThrowIfNull(title);

            int l = 0, r = items.Count - 1;
            while (l <= r)
            {
                var m = (l + r) / 2;
                var res = string.Compare(title, items[m].Title, StringComparison.CurrentCulture);
                if (res == 0)
                {
                    return m;
                }
                if (res > 0)
                {
                    l = m + 1;
                }
                else
                {
                    r = m - 1;
                }
            }
            return l;
        }
    }
}
