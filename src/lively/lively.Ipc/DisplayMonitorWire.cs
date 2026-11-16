using lively.Models;

namespace lively.Ipc
{
    /// <summary>
    /// Converts <see cref="DisplayMonitor"/> to and from the D-Bus struct (ss(iiii)db):
    /// (id, description, (x, y, width, height), scale, primary).
    /// </summary>
    public static class DisplayMonitorWire
    {
        public static (string, string, (int, int, int, int), double, bool) ToWire(DisplayMonitor display, bool isPrimary)
        {
            ArgumentNullException.ThrowIfNull(display);
            return (display.Id ?? string.Empty,
                display.Description ?? string.Empty,
                (display.X, display.Y, display.Width, display.Height),
                display.Scale,
                isPrimary);
        }

        /// <remarks>Make, model, refresh rate and transform are not transmitted. The scale is rounded up to an integer.</remarks>
        public static DisplayMonitor FromWire((string Id, string Description, (int X, int Y, int Width, int Height) Geometry, double Scale, bool IsPrimary) wire)
        {
            return new DisplayMonitor
            {
                Id = wire.Id,
                Description = wire.Description,
                X = wire.Geometry.X,
                Y = wire.Geometry.Y,
                Width = wire.Geometry.Width,
                Height = wire.Geometry.Height,
                Scale = Math.Max(1, (int)Math.Ceiling(wire.Scale)),
                IsPrimary = wire.IsPrimary,
            };
        }
    }
}
