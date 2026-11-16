namespace lively.Core.Display.Wayland
{
    /// <summary>No Wayland compositor could be reached, or libwayland-client is missing.</summary>
    public sealed class WaylandUnavailableException : Exception
    {
        public WaylandUnavailableException(string message, Exception? inner = null) : base(message, inner)
        {
        }
    }
}
