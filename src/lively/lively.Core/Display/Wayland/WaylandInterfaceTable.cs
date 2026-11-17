using System;
using System.Runtime.InteropServices;
using System.Text;

namespace lively.Core.Display.Wayland
{
    /// <summary>
    /// Builds native <c>struct wl_interface</c> tables for protocols that libwayland-client does not export
    /// (what wayland-scanner would generate as <c>*-protocol.c</c>).
    /// </summary>
    /// <remarks>
    /// Self-contained on purpose: this file is also compiled into the player project through
    /// <c>&lt;Compile Include=... Link=...&gt;</c>, so it must not depend on anything else in lively.Core.
    /// <para>
    /// The memory is allocated with <see cref="NativeMemory.Alloc(nuint)"/> and never freed: libwayland keeps the
    /// interface, message and type pointers in every proxy created with the table, for the process lifetime.
    /// Build each table once (e.g. behind a <see cref="Lazy{T}"/>).
    /// </para>
    /// The layout is the LP64 one (x86_64, aarch64).
    /// </remarks>
    public static unsafe class WaylandInterfaceTable
    {
        /// <summary>
        /// One request or event.
        /// </summary>
        /// <param name="Name">Request or event name.</param>
        /// <param name="Signature">
        /// wl_message signature: an optional "since" version prefix, then one letter per argument
        /// (<c>i u f s o n a h</c>), each optionally preceded by <c>?</c> (nullable).
        /// </param>
        /// <param name="Types">
        /// One entry per argument: the <c>wl_interface*</c> of an <c>o</c>/<c>n</c> argument with a fixed
        /// interface, else 0.
        /// </param>
        public readonly record struct Message(string Name, string Signature, nint[] Types);

        /// <summary><c>struct wl_message</c>.</summary>
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        public struct WlMessage
        {
            /// <summary><c>const char *name</c>.</summary>
            [FieldOffset(0)] public byte* Name;
            /// <summary><c>const char *signature</c>.</summary>
            [FieldOffset(8)] public byte* Signature;
            /// <summary><c>const struct wl_interface **types</c>.</summary>
            [FieldOffset(16)] public nint* Types;
        }

        /// <summary><c>struct wl_interface</c>.</summary>
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        public struct WlInterface
        {
            /// <summary><c>const char *name</c>.</summary>
            [FieldOffset(0)] public byte* Name;
            /// <summary><c>int version</c>.</summary>
            [FieldOffset(8)] public int Version;
            /// <summary><c>int method_count</c>.</summary>
            [FieldOffset(12)] public int MethodCount;
            /// <summary><c>const struct wl_message *methods</c>.</summary>
            [FieldOffset(16)] public WlMessage* Methods;
            /// <summary><c>int event_count</c> (followed by 4 bytes of padding).</summary>
            [FieldOffset(24)] public int EventCount;
            /// <summary><c>const struct wl_message *events</c>.</summary>
            [FieldOffset(32)] public WlMessage* Events;
        }

        /// <summary>
        /// Allocates a <c>wl_interface</c> with its message arrays, type arrays and strings.
        /// </summary>
        /// <returns>The address of the <c>wl_interface</c>; valid for the process lifetime.</returns>
        /// <exception cref="ArgumentException">A message's <see cref="Message.Types"/> length does not match its signature.</exception>
        public static nint Build(string name, int version, Message[] methods, Message[] events)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(methods);
            ArgumentNullException.ThrowIfNull(events);
            ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
            if (IntPtr.Size != 8)
            {
                throw new PlatformNotSupportedException("Wayland interface tables are laid out for 64-bit processes only.");
            }
            // Validate everything before allocating anything.
            foreach (var message in methods)
            {
                Validate(message);
            }
            foreach (var message in events)
            {
                Validate(message);
            }

            var iface = (WlInterface*)NativeMemory.AllocZeroed((nuint)sizeof(WlInterface));
            iface->Name = Utf8z(name);
            iface->Version = version;
            iface->MethodCount = methods.Length;
            iface->Methods = BuildMessages(methods);
            iface->EventCount = events.Length;
            iface->Events = BuildMessages(events);
            return (nint)iface;
        }

        /// <summary>Reads back the header of a <c>wl_interface</c> (built here or exported by libwayland).</summary>
        public static (string Name, int Version, int MethodCount, int EventCount) ReadInterface(nint iface)
        {
            ArgumentOutOfRangeException.ThrowIfZero(iface);
            var p = (WlInterface*)iface;
            return (Marshal.PtrToStringUTF8((nint)p->Name) ?? string.Empty, p->Version, p->MethodCount, p->EventCount);
        }

        /// <summary>Reads back one request (<paramref name="isEvent"/> false) or event of a <c>wl_interface</c>.</summary>
        public static (string Name, string Signature, nint[] Types) ReadMessage(nint iface, bool isEvent, int index)
        {
            ArgumentOutOfRangeException.ThrowIfZero(iface);
            var p = (WlInterface*)iface;
            var count = isEvent ? p->EventCount : p->MethodCount;
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);

            var message = (isEvent ? p->Events : p->Methods) + index;
            var signature = Marshal.PtrToStringUTF8((nint)message->Signature) ?? string.Empty;
            var types = new nint[ArgumentCount(signature)];
            for (var i = 0; i < types.Length; i++)
            {
                types[i] = message->Types[i];
            }
            return (Marshal.PtrToStringUTF8((nint)message->Name) ?? string.Empty, signature, types);
        }

        /// <summary>
        /// Number of arguments in a wl_message signature: the letters <c>i u f s o n a h</c>;
        /// the "since" version digits and <c>?</c> markers are skipped.
        /// </summary>
        /// <exception cref="ArgumentException">An unknown character.</exception>
        public static int ArgumentCount(string signature)
        {
            ArgumentNullException.ThrowIfNull(signature);
            var count = 0;
            foreach (var c in signature)
            {
                switch (c)
                {
                    case 'i' or 'u' or 'f' or 's' or 'o' or 'n' or 'a' or 'h':
                        count++;
                        break;
                    case '?' or (>= '0' and <= '9'):
                        break;
                    default:
                        throw new ArgumentException($"Invalid character '{c}' in wl_message signature \"{signature}\".", nameof(signature));
                }
            }
            return count;
        }

        private static void Validate(Message message)
        {
            if (string.IsNullOrEmpty(message.Name))
            {
                throw new ArgumentException("A message has no name.");
            }
            ArgumentNullException.ThrowIfNull(message.Signature);
            ArgumentNullException.ThrowIfNull(message.Types);
            var count = ArgumentCount(message.Signature);
            if (message.Types.Length != count)
            {
                throw new ArgumentException(
                    $"Message {message.Name} \"{message.Signature}\" has {count} arguments but {message.Types.Length} types.");
            }
        }

        private static WlMessage* BuildMessages(Message[] messages)
        {
            // Never a null pointer, even for an empty array.
            var array = (WlMessage*)NativeMemory.AllocZeroed((nuint)Math.Max(1, messages.Length), (nuint)sizeof(WlMessage));
            for (var i = 0; i < messages.Length; i++)
            {
                var m = messages[i];
                array[i].Name = Utf8z(m.Name);
                array[i].Signature = Utf8z(m.Signature);
                var types = (nint*)NativeMemory.AllocZeroed((nuint)Math.Max(1, m.Types.Length), (nuint)sizeof(nint));
                for (var t = 0; t < m.Types.Length; t++)
                {
                    types[t] = m.Types[t];
                }
                array[i].Types = types;
            }
            return array;
        }

        private static byte* Utf8z(string s)
        {
            var length = Encoding.UTF8.GetByteCount(s);
            var buffer = (byte*)NativeMemory.Alloc((nuint)length + 1);
            Encoding.UTF8.GetBytes(s, new Span<byte>(buffer, length));
            buffer[length] = 0;
            return buffer;
        }
    }
}
