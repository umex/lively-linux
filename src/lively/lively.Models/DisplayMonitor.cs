using System;

namespace lively.Models
{
    /// <summary>
    /// A connected display (output). Two instances are equal when their <see cref="Id"/> matches.
    /// </summary>
    public class DisplayMonitor : IEquatable<DisplayMonitor>
    {
        /// <summary>Stable output name, for example "DP-1" or "HDMI-A-1".</summary>
        public string Id { get; set; }
        /// <summary>Human readable description from the compositor, may be empty.</summary>
        public string Description { get; set; }
        public string Make { get; set; }
        public string Model { get; set; }
        /// <summary>Position in the compositor's global (logical) coordinate space.</summary>
        public int X { get; set; }
        public int Y { get; set; }
        /// <summary>Size of the current mode in pixels, already rotated for the transform.</summary>
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshMilliHz { get; set; }
        /// <summary>Integer scale factor (Wayland wl_output.scale).</summary>
        public int Scale { get; set; } = 1;
        /// <summary>wl_output.transform: 0 normal, 1-3 rotated 90/180/270, 4-7 flipped variants.</summary>
        public int Transform { get; set; }
        public bool IsPrimary { get; set; }

        public string DisplayName
        {
            get
            {
                var detail = ((Make ?? string.Empty) + " " + (Model ?? string.Empty)).Trim();
                return detail.Length > 0
                    ? $"{Id} - {detail} ({Width}x{Height})"
                    : $"{Id} ({Width}x{Height})";
            }
        }

        /// <summary>
        /// True when every property matches, unlike <see cref="Equals(DisplayMonitor)"/> which compares the id only.
        /// </summary>
        public bool IsIdenticalTo(DisplayMonitor other)
        {
            return other != null
                && Id == other.Id
                && Description == other.Description
                && Make == other.Make
                && Model == other.Model
                && X == other.X
                && Y == other.Y
                && Width == other.Width
                && Height == other.Height
                && RefreshMilliHz == other.RefreshMilliHz
                && Scale == other.Scale
                && Transform == other.Transform
                && IsPrimary == other.IsPrimary;
        }

        public bool Equals(DisplayMonitor other)
        {
            return other != null && string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as DisplayMonitor);

        public override int GetHashCode() => Id == null ? 0 : StringComparer.Ordinal.GetHashCode(Id);

        public override string ToString() => $"{Id} {Width}x{Height}@{X},{Y} scale {Scale}{(IsPrimary ? " primary" : string.Empty)}";
    }
}
