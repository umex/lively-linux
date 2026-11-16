using lively.Models;

namespace lively.Core.Wallpapers
{
    /// <summary>
    /// Persists which wallpaper the user wants on which display (the user's intent, not what is running).
    /// </summary>
    public interface IWallpaperLayoutStore
    {
        /// <summary>Never throws: a missing or corrupt file gives an empty layout.</summary>
        IReadOnlyList<WallpaperLayoutModel> Load();

        /// <summary>Atomically replaces the stored layout.</summary>
        void Save(IEnumerable<WallpaperLayoutModel> layout);
    }
}
