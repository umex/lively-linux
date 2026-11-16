using lively.Models;

namespace lively.Core.Display
{
    public interface IDisplayManager : IDisposable
    {
        /// <summary>Snapshot of the connected displays.</summary>
        IReadOnlyList<DisplayMonitor> Displays { get; }

        DisplayMonitor? PrimaryDisplay { get; }

        DisplayMonitor? FindById(string id);

        /// <summary>Re-reads the displays now; raises <see cref="DisplaysChanged"/> when anything differs.</summary>
        void Refresh();

        /// <summary>Starts watching for display changes (hot-plug). Safe to call more than once.</summary>
        void Start();

        /// <summary>
        /// Raised after <see cref="Displays"/> has changed, on the thread that noticed it
        /// (a thread-pool thread for background polling).
        /// </summary>
        event EventHandler? DisplaysChanged;
    }
}
