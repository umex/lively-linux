using lively.Core.Wallpapers;
using lively.Models;

namespace lively.Tests.Fakes;

public sealed class MemoryLayoutStore(params WallpaperLayoutModel[] initial) : IWallpaperLayoutStore
{
    private List<WallpaperLayoutModel> stored = [.. initial];

    public int SaveCount { get; private set; }

    public IReadOnlyList<(string DisplayId, string Path)> Stored
    {
        get { lock (this) { return stored.Select(x => (x.DisplayId, x.LivelyInfoPath)).ToList(); } }
    }

    public IReadOnlyList<WallpaperLayoutModel> Load()
    {
        lock (this) { return stored.Select(x => new WallpaperLayoutModel(x.DisplayId, x.LivelyInfoPath)).ToList(); }
    }

    public void Save(IEnumerable<WallpaperLayoutModel> layout)
    {
        lock (this)
        {
            stored = layout.Select(x => new WallpaperLayoutModel(x.DisplayId, x.LivelyInfoPath)).ToList();
            SaveCount++;
        }
    }
}
