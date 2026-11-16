using lively.Models;

namespace lively.Core.Desktop
{
    /// <summary>Backend for unsupported sessions: players run but are not placed.</summary>
    public sealed class NullDesktopBackend : IDesktopBackend
    {
        public NullDesktopBackend(DesktopSession session)
        {
            Session = session;
        }

        public string Name => "none";

        public DesktopSession Session { get; }

        public bool CanPlaceWallpapers => false;

        public IReadOnlyList<string> GetPlayerArguments(DisplayMonitor display) => [];
    }
}
