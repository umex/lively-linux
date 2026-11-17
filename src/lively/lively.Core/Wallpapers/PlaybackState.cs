namespace lively.Core.Wallpapers
{
    /// <summary>Playback state a new player starts with.</summary>
    /// <param name="Volume">0-100.</param>
    /// <param name="IsPaused">The player is suspended right after it has loaded.</param>
    public sealed record PlaybackState(int Volume, bool IsPaused);
}
