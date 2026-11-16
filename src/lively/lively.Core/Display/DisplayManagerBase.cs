using lively.Models;

namespace lively.Core.Display
{
    /// <summary>
    /// Holds the display snapshot, compares new snapshots against it and raises <see cref="DisplaysChanged"/>.
    /// </summary>
    public abstract class DisplayManagerBase : IDisplayManager
    {
        private readonly object sync = new();
        private IReadOnlyList<DisplayMonitor> displays = [];

        public IReadOnlyList<DisplayMonitor> Displays
        {
            get { lock (sync) { return displays; } }
        }

        public DisplayMonitor? PrimaryDisplay
        {
            get
            {
                var current = Displays;
                return current.FirstOrDefault(x => x.IsPrimary) ?? current.FirstOrDefault();
            }
        }

        public event EventHandler? DisplaysChanged;

        public DisplayMonitor? FindById(string id) => Displays.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

        public abstract void Refresh();

        public virtual void Start()
        {
        }

        public virtual void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Replaces the snapshot. Returns true and raises <see cref="DisplaysChanged"/> when it differs from the previous one.
        /// </summary>
        protected bool Update(IReadOnlyList<DisplayMonitor> next, bool raise = true)
        {
            lock (sync)
            {
                if (next.Count == displays.Count && next.Zip(displays).All(p => p.First.IsIdenticalTo(p.Second)))
                {
                    return false;
                }
                displays = next;
            }
            if (raise)
            {
                DisplaysChanged?.Invoke(this, EventArgs.Empty);
            }
            return true;
        }
    }
}
