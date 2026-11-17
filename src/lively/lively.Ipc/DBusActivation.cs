using Tmds.DBus.Protocol;

namespace lively.Ipc
{
    /// <summary>
    /// Calls on the message bus itself (<c>org.freedesktop.DBus</c>) that Tmds.DBus.Protocol has no helper for.
    /// </summary>
    public static class DBusActivation
    {
        /// <summary>Error of <see cref="StartServiceByNameAsync"/> when no activation file provides the name.</summary>
        public const string ServiceUnknown = "org.freedesktop.DBus.Error.ServiceUnknown";

        /// <summary><see cref="StartServiceByNameAsync"/> result: the service was started.</summary>
        public const uint StartReplySuccess = 1;

        /// <summary><see cref="StartServiceByNameAsync"/> result: the name already had an owner.</summary>
        public const uint StartReplyAlreadyRunning = 2;

        private const string BusName = "org.freedesktop.DBus";
        private const string BusPath = "/org/freedesktop/DBus";
        private const string BusInterface = "org.freedesktop.DBus";

        /// <summary>
        /// Asks the bus to start the service that owns <paramref name="name"/>. The bus replies once the
        /// service owns the name (or failed to start).
        /// </summary>
        /// <returns><see cref="StartReplySuccess"/> or <see cref="StartReplyAlreadyRunning"/>.</returns>
        /// <exception cref="DBusErrorReplyException">
        /// <see cref="ServiceUnknown"/> when the name is not activatable, other errors when the start failed.
        /// </exception>
        public static Task<uint> StartServiceByNameAsync(DBusConnection connection, string name)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(name);
            return connection.CallMethodAsync(CreateMessage(), (Message m, object? _) => m.GetBodyReader().ReadUInt32(), null);

            MessageBuffer CreateMessage()
            {
                var writer = connection.GetMessageWriter();
                writer.WriteMethodCallHeader(
                    destination: BusName,
                    path: BusPath,
                    @interface: BusInterface,
                    signature: "su",
                    member: "StartServiceByName");
                writer.WriteString(name);
                writer.WriteUInt32(0); // flags: unused
                return writer.CreateMessage();
            }
        }

        /// <summary>
        /// Makes the bus re-read its configuration and activation files. Buses usually also watch their
        /// service directories; this makes a new activation file usable at once.
        /// </summary>
        public static Task ReloadConfigAsync(DBusConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.CallMethodAsync(CreateMessage());

            MessageBuffer CreateMessage()
            {
                var writer = connection.GetMessageWriter();
                writer.WriteMethodCallHeader(
                    destination: BusName,
                    path: BusPath,
                    @interface: BusInterface,
                    member: "ReloadConfig");
                return writer.CreateMessage();
            }
        }
    }
}
