using System.Text;

namespace lively.Player.Mpv
{
    /// <summary>
    /// Optional log file (<c>--mpv-log</c>): appended, shared for reading, UTF-8 without BOM, timestamped lines.
    /// Thread-safe; never throws.
    /// </summary>
    internal sealed class PlayerLog : IDisposable
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private readonly object gate = new();
        private StreamWriter? writer;

        private PlayerLog(StreamWriter? writer)
        {
            this.writer = writer;
        }

        /// <summary>A log that writes nothing.</summary>
        public static PlayerLog None => new(null);

        public static PlayerLog Open(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return None;
            }
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                return new PlayerLog(new StreamWriter(stream, Utf8NoBom) { AutoFlush = true, NewLine = "\n" });
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Cannot open log " + path + ": " + e.Message);
                return None;
            }
        }

        public void Write(string text)
        {
            lock (gate)
            {
                if (writer == null)
                {
                    return;
                }
                try
                {
                    writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + " " + text);
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException)
                {
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                try
                {
                    writer?.Dispose();
                }
                catch (IOException)
                {
                }
                writer = null;
            }
        }
    }
}
