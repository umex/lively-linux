namespace lively.Core.Wallpapers
{
    public class WallpaperPlayerException : Exception
    {
        public WallpaperPlayerException(string message, Exception? inner = null) : base(message, inner)
        {
        }
    }
}
