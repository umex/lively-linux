using System.Globalization;
using System.Text.RegularExpressions;
using lively.Models;

namespace lively.Core.Display
{
    /// <summary>
    /// Displays from a fixed list, for tests and for hosts without a supported display server.
    /// </summary>
    public sealed partial class FakeDisplayManager : DisplayManagerBase
    {
        public FakeDisplayManager(IEnumerable<DisplayMonitor> displays)
        {
            Update(Normalize(displays), raise: false);
        }

        public FakeDisplayManager(string spec) : this(Parse(spec))
        {
        }

        /// <summary>Replaces the displays and raises <see cref="IDisplayManager.DisplaysChanged"/> if they differ.</summary>
        public void SetDisplays(IEnumerable<DisplayMonitor> displays)
        {
            Update(Normalize(displays));
        }

        public override void Refresh()
        {
        }

        /// <summary>
        /// Parses "ID:WxH@X,Y" entries separated by ';', for example
        /// "DP-1:1920x1080@0,0;HDMI-A-1:2560x1440@1920,0". The position may be omitted ("DP-1:1920x1080").
        /// The first display is the primary one.
        /// </summary>
        /// <exception cref="FormatException">An entry does not match the format, or an id repeats.</exception>
        public static IReadOnlyList<DisplayMonitor> Parse(string spec)
        {
            ArgumentNullException.ThrowIfNull(spec);
            var result = new List<DisplayMonitor>();
            foreach (var raw in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var match = EntryRegex().Match(raw);
                if (!match.Success)
                {
                    throw new FormatException($"Invalid display \"{raw}\", expected ID:WIDTHxHEIGHT@X,Y.");
                }
                var id = match.Groups["id"].Value;
                if (result.Any(x => x.Id == id))
                {
                    throw new FormatException($"Display id \"{id}\" is listed twice.");
                }
                result.Add(new DisplayMonitor
                {
                    Id = id,
                    Description = "Fake display " + id,
                    Width = int.Parse(match.Groups["w"].Value, CultureInfo.InvariantCulture),
                    Height = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture),
                    X = match.Groups["x"].Success ? int.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture) : 0,
                    Y = match.Groups["y"].Success ? int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture) : 0,
                    RefreshMilliHz = 60000,
                    Scale = 1,
                    IsPrimary = result.Count == 0,
                });
            }
            if (result.Count == 0)
            {
                throw new FormatException("Display spec is empty.");
            }
            return result;
        }

        private static List<DisplayMonitor> Normalize(IEnumerable<DisplayMonitor> displays)
        {
            var list = displays.ToList();
            if (list.Count > 0 && !list.Any(x => x.IsPrimary))
            {
                list[0].IsPrimary = true;
            }
            return list;
        }

        [GeneratedRegex(@"^(?<id>[^:;\s]+):(?<w>\d{1,5})x(?<h>\d{1,5})(@(?<x>-?\d{1,6}),(?<y>-?\d{1,6}))?$")]
        private static partial Regex EntryRegex();
    }
}
