using System.Collections.Concurrent;
using System.Text;
using lively.Models.Message;
using lively.Player.Mpv.Native;

namespace lively.Player.Mpv.Protocol
{
    /// <summary>
    /// The player side of the line protocol: messages to the core on stdout, commands from the core on stdin.
    /// </summary>
    internal sealed class MessageIo
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private readonly object writeLock = new();
        private readonly StreamWriter stdout;
        private int loadedSent;

        public MessageIo()
        {
            stdout = new StreamWriter(System.Console.OpenStandardOutput(), Utf8NoBom) { AutoFlush = true, NewLine = "\n" };
        }

        /// <summary>True once msg_wploaded (success or failure) was sent.</summary>
        public bool LoadedSent => Volatile.Read(ref loadedSent) == 1;

        public void Send(IpcMessage message)
        {
            var line = IpcMessageJson.Serialize(message);
            lock (writeLock)
            {
                try
                {
                    stdout.WriteLine(line);
                }
                catch (IOException)
                {
                    // The core went away; stdin EOF ends the process.
                }
            }
        }

        /// <summary>Sends msg_wploaded once; later calls do nothing.</summary>
        public void SendLoaded(bool success)
        {
            if (Interlocked.Exchange(ref loadedSent, 1) == 0)
            {
                Send(new LivelyMessageWallpaperLoaded { Success = success });
            }
        }

        public void Console(string text, ConsoleMessageType category = ConsoleMessageType.log)
        {
            Send(new LivelyMessageConsole { Message = text, Category = category });
        }

        /// <summary>
        /// Reads commands on a background thread and enqueues them, then null at end of file; signals
        /// <paramref name="wakeup"/> after each.
        /// </summary>
        public void StartStdinReader(ConcurrentQueue<IpcMessage?> queue, Wakeup wakeup, PlayerLog log)
        {
            var thread = new Thread(() => ReadStdin(queue, wakeup, log))
            {
                IsBackground = true,
                Name = "stdin reader",
            };
            thread.Start();
        }

        private void ReadStdin(ConcurrentQueue<IpcMessage?> queue, Wakeup wakeup, PlayerLog log)
        {
            try
            {
                using var stdin = new StreamReader(System.Console.OpenStandardInput(), Utf8NoBom);
                string? line;
                while ((line = stdin.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }
                    IpcMessage? message;
                    try
                    {
                        message = IpcMessageJson.Deserialize(line);
                    }
                    catch (Exception e)
                    {
                        log.Write("invalid message: " + line);
                        Console("invalid message: " + e.Message, ConsoleMessageType.error);
                        continue;
                    }
                    if (message == null)
                    {
                        log.Write("ignored message: " + line);
                        continue;
                    }
                    queue.Enqueue(message);
                    wakeup.Signal();
                }
            }
            catch (Exception e)
            {
                log.Write("stdin error: " + e.Message);
            }
            queue.Enqueue(null);
            wakeup.Signal();
        }
    }
}
