using System.Collections.Concurrent;
using lively.Core.Wallpapers;
using lively.Models;

namespace lively.Tests.Fakes;

public sealed class FakeWallpaperPlayerFactory : IWallpaperPlayerFactory
{
    private readonly ConcurrentQueue<FakeWallpaperPlayer> created = new();

    /// <summary>Called for each new player before it is returned, e.g. to set <see cref="FakeWallpaperPlayer.OnShow"/>.</summary>
    public Action<FakeWallpaperPlayer>? Configure { get; set; }

    /// <summary>When set, Create throws it.</summary>
    public Exception? CreateError { get; set; }

    public IReadOnlyList<FakeWallpaperPlayer> Created => created.ToArray();

    /// <summary>State passed to the last Create call.</summary>
    public PlaybackState? LastState { get; private set; }

    public IWallpaperPlayer Create(LibraryModel model, DisplayMonitor display, PlaybackState state)
    {
        LastState = state;
        if (CreateError != null)
        {
            throw CreateError;
        }
        var player = new FakeWallpaperPlayer(model, display) { State = state };
        Configure?.Invoke(player);
        created.Enqueue(player);
        return player;
    }
}
