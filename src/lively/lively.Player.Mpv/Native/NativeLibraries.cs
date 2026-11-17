using System.Runtime.InteropServices;

namespace lively.Player.Mpv.Native
{
    /// <summary>The shared libraries the player needs, with install hints for Fedora package names.</summary>
    internal static class NativeLibraries
    {
        private static readonly (string Name, string Hint)[] Required =
        [
            (MpvNative.Lib, "install mpv-libs"),
            (WaylandNative.Lib, "install libwayland-client"),
            (WaylandEglNative.Lib, "install libwayland-egl"),
            (EglNative.Lib, "install libglvnd-egl and mesa-libEGL"),
        ];

        /// <summary>
        /// Loads every required library once, before anything touches the compositor, so a missing library is
        /// reported precisely and no surface flashes on the desktop.
        /// </summary>
        /// <exception cref="PlayerException">A library cannot be loaded.</exception>
        public static void Check()
        {
            foreach (var (name, hint) in Required)
            {
                if (!NativeLibrary.TryLoad(name, out _))
                {
                    throw new PlayerException(name + " not found: " + hint);
                }
            }
        }

        /// <summary>Message for a <see cref="DllNotFoundException"/> that escaped <see cref="Check"/>.</summary>
        public static string Describe(DllNotFoundException e)
        {
            foreach (var (name, hint) in Required)
            {
                if (e.Message.Contains(name, StringComparison.Ordinal))
                {
                    return name + " not found: " + hint;
                }
            }
            return "native library not found: " + e.Message;
        }
    }
}
