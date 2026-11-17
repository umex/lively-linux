using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace lively.Player.Mpv.Native
{
    /// <summary>
    /// A non-blocking eventfd that wakes the main loop's poll(): signalled by the stdin reader, the signal
    /// handlers and libmpv's wakeup and render-update callbacks.
    /// </summary>
    internal sealed unsafe class Wakeup : IDisposable
    {
        private int closed;

        private Wakeup(int fd)
        {
            Fd = fd;
        }

        public int Fd { get; }

        /// <summary>The fd as a native callback context for <see cref="SignalFromNative"/>.</summary>
        public void* NativeData => (void*)(nint)Fd;

        /// <summary>
        /// <c>void (*)(void *data)</c> that writes to the eventfd whose number is <c>data</c>. Safe on any thread;
        /// it never calls back into libmpv.
        /// </summary>
        public static delegate* unmanaged[Cdecl]<void*, void> NativeCallback => &SignalFromNative;

        /// <exception cref="PlayerException">eventfd failed.</exception>
        public static Wakeup Create()
        {
            var fd = Libc.eventfd(0, Libc.EFD_CLOEXEC | Libc.EFD_NONBLOCK);
            if (fd < 0)
            {
                throw new PlayerException("eventfd failed (errno " + Marshal.GetLastPInvokeError() + ")");
            }
            return new Wakeup(fd);
        }

        public void Signal()
        {
            if (Volatile.Read(ref closed) == 0)
            {
                Write(Fd);
            }
        }

        /// <summary>Resets the counter.</summary>
        public void Drain()
        {
            ulong value;
            while (Libc.read(Fd, &value, sizeof(ulong)) == sizeof(ulong))
            {
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) == 0)
            {
                Libc.close(Fd);
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void SignalFromNative(void* data)
        {
            try
            {
                Write((int)(nint)data);
            }
            catch
            {
                // An exception escaping an UnmanagedCallersOnly method terminates the process.
            }
        }

        private static void Write(int fd)
        {
            ulong one = 1;
            Libc.write(fd, &one, sizeof(ulong));
        }
    }
}
