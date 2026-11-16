using lively.Core.Display.Wayland;
using lively.Models;

namespace lively.Core.Display
{
    /// <summary>
    /// Displays of the Wayland compositor (wl_output). Hot-plug is detected by re-enumerating
    /// every <c>pollInterval</c>; a persistent connection with an event loop can replace this later.
    /// </summary>
    public sealed class WaylandDisplayManager : DisplayManagerBase
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly TimeSpan pollInterval;
        private readonly Func<IReadOnlyList<DisplayMonitor>> enumerate;
        private readonly object refreshSync = new();
        private readonly CancellationTokenSource cts = new();
        private Task? pollTask;
        private bool disposed;

        public WaylandDisplayManager(TimeSpan pollInterval) : this(pollInterval, WaylandOutputEnumerator.Enumerate)
        {
        }

        /// <param name="enumerate">Source of the displays; replaceable for tests.</param>
        public WaylandDisplayManager(TimeSpan pollInterval, Func<IReadOnlyList<DisplayMonitor>> enumerate)
        {
            this.pollInterval = pollInterval;
            this.enumerate = enumerate;
            RefreshCore(raise: false);
        }

        public override void Refresh() => RefreshCore(raise: true);

        public override void Start()
        {
            lock (refreshSync)
            {
                if (disposed || pollTask != null)
                {
                    return;
                }
                pollTask = Task.Run(() => PollAsync(cts.Token));
            }
        }

        public override void Dispose()
        {
            lock (refreshSync)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
            }
            cts.Cancel();
            try
            {
                pollTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }
            cts.Dispose();
            base.Dispose();
        }

        private async Task PollAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(pollInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    RefreshCore(raise: true);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void RefreshCore(bool raise)
        {
            lock (refreshSync)
            {
                if (disposed)
                {
                    return;
                }
                IReadOnlyList<DisplayMonitor> next;
                try
                {
                    next = enumerate();
                }
                catch (Exception e)
                {
                    Logger.Error("Display enumeration failed, keeping the previous list: " + e.Message);
                    return;
                }
                if (Update(next, raise))
                {
                    Logger.Info("Displays: " + string.Join("; ", next));
                }
            }
        }
    }
}
