namespace lively.Daemon
{
    /// <summary>Process exit code, set by <see cref="DaemonService"/> before it stops the host.</summary>
    public sealed class DaemonExitCode
    {
        public const int Success = 0;
        public const int Failure = 1;
        public const int AlreadyRunning = 2;
        public const int NoSessionBus = 3;

        public int Value { get; set; } = Success;
    }
}
