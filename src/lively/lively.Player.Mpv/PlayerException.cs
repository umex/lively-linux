namespace lively.Player.Mpv
{
    /// <summary>A failure with a message meant for the user (sent as msg_console error, then exit 1).</summary>
    public sealed class PlayerException : Exception
    {
        public PlayerException(string message) : base(message)
        {
        }

        public PlayerException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
