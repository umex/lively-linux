using System.Runtime.InteropServices;

namespace lively.Player.Mpv.Native
{
    /// <summary>The few libc calls the event loop needs (glibc, x86_64/aarch64).</summary>
    internal static unsafe partial class Libc
    {
        // The runtime maps "libc" to the C library of the platform (libc.so.6 on glibc).
        private const string Lib = "libc";

        public const short POLLIN = 0x1;
        public const short POLLOUT = 0x4;
        public const short POLLERR = 0x8;
        public const short POLLHUP = 0x10;

        public const int EFD_CLOEXEC = 0x80000;
        public const int EFD_NONBLOCK = 0x800;

        public const int EINTR = 4;
        public const int EAGAIN = 11;

        public const int LC_NUMERIC = 1;

        /// <returns>Number of ready descriptors, 0 on timeout, -1 on error (errno).</returns>
        [LibraryImport(Lib, SetLastError = true)]
        public static partial int poll(PollFd* fds, nuint nfds, int timeout);

        [LibraryImport(Lib, SetLastError = true)]
        public static partial int eventfd(uint initval, int flags);

        [LibraryImport(Lib, SetLastError = true)]
        public static partial nint read(int fd, void* buf, nuint count);

        [LibraryImport(Lib, SetLastError = true)]
        public static partial nint write(int fd, void* buf, nuint count);

        [LibraryImport(Lib, SetLastError = true)]
        public static partial int close(int fd);

        /// <returns>The new locale name, or 0 on error. Static storage; do not free.</returns>
        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        public static partial nint setlocale(int category, string? locale);
    }

    /// <summary>struct pollfd.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }
}
