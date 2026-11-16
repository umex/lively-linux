using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using lively.Models;
using lively.Models.Message;
using Newtonsoft.Json;

namespace lively.Core.Wallpapers
{
    /// <summary>
    /// A player running as a child process that speaks the line-based JSON protocol
    /// (<see cref="IpcMessageJson"/>) on stdin/stdout. stderr is logged as warnings.
    /// </summary>
    public sealed class ProcessWallpaperPlayer : IWallpaperPlayer
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        /// <summary>Time given to the stdout reader to drain after the process has exited.</summary>
        private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(1);

        private readonly PlayerLaunchInfo launch;
        private readonly TimeSpan loadTimeout;
        private readonly TimeSpan closeTimeout;
        private readonly TaskCompletionSource loadedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource exitedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object writeLock = new();
        private Process? process;
        private Task stdoutTask = Task.CompletedTask;
        private int started;
        private int exitedRaised;
        private int disposed;
        private bool stdinClosed;
        private int volume;
        private bool muted;

        public ProcessWallpaperPlayer(PlayerLaunchInfo launch, LibraryModel model, DisplayMonitor screen,
            TimeSpan loadTimeout, TimeSpan closeTimeout, int initialVolume = 100)
        {
            this.launch = launch;
            this.loadTimeout = loadTimeout;
            this.closeTimeout = closeTimeout;
            Model = model;
            Screen = screen;
            volume = Math.Clamp(initialVolume, 0, 100);
        }

        public bool IsLoaded { get; private set; }

        public bool IsExited => Volatile.Read(ref exitedRaised) == 1;

        public LibraryModel Model { get; }

        public DisplayMonitor Screen { get; }

        public int? Pid { get; private set; }

        /// <summary>Exit code once exited, else null.</summary>
        public int? ExitCode { get; private set; }

        public event EventHandler? Loaded;

        public event EventHandler? Exited;

        public event EventHandler<IpcMessage>? MessageReceived;

        private string Tag => $"[{Screen.Id} pid {Pid?.ToString() ?? "-"}]";

        public async Task ShowAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref started, 1) == 1)
            {
                throw new InvalidOperationException("The player was already started.");
            }
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);

            var psi = new ProcessStartInfo(launch.FileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Utf8NoBom,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
            };
            foreach (var arg in launch.Arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            var p = new Process { StartInfo = psi };
            try
            {
                p.Start();
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
            {
                p.Dispose();
                MarkExited(null);
                throw new WallpaperPlayerException($"Failed to start player {launch.FileName}: {e.Message}", e);
            }

            process = p;
            Pid = p.Id;
            p.StandardInput.AutoFlush = true;
            p.StandardInput.NewLine = "\n";
            Logger.Info($"{Tag} Started {launch}");

            stdoutTask = Task.Run(() => ReadStdoutAsync(p.StandardOutput));
            _ = Task.Run(() => ReadStderrAsync(p.StandardError));
            _ = Task.Run(() => WatchExitAsync(p));

            try
            {
                await loadedTcs.Task.WaitAsync(loadTimeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Logger.Error($"{Tag} Did not report loaded within {loadTimeout.TotalSeconds:0.#} s, killing it.");
                Terminate();
                throw new WallpaperPlayerException($"Player did not load the wallpaper within {loadTimeout.TotalSeconds:0.#} s.");
            }
            catch (Exception e) when (e is OperationCanceledException or WallpaperPlayerException)
            {
                Logger.Error($"{Tag} Load failed: {e.Message}");
                Terminate();
                throw;
            }

            IsLoaded = true;
            Logger.Info($"{Tag} Loaded {Model.LivelyInfoFolderPath}");
            Loaded?.Invoke(this, EventArgs.Empty);
        }

        public void Pause() => SendMessage(new LivelySuspendCmd());

        public void Play() => SendMessage(new LivelyResumeCmd());

        public void SetVolume(int value)
        {
            lock (writeLock)
            {
                volume = Math.Clamp(value, 0, 100);
                muted = false;
            }
            SendMessage(new LivelyVolumeCmd { Volume = Math.Clamp(value, 0, 100) });
        }

        public void SetMute(bool mute)
        {
            int send;
            lock (writeLock)
            {
                if (muted == mute)
                {
                    return;
                }
                muted = mute;
                send = mute ? 0 : volume;
            }
            SendMessage(new LivelyVolumeCmd { Volume = send });
        }

        public void SendMessage(IpcMessage message)
        {
            var line = IpcMessageJson.Serialize(message);
            lock (writeLock)
            {
                var p = process;
                if (p == null || stdinClosed || IsExited)
                {
                    Logger.Debug($"{Tag} Not running, dropping {message.Type}");
                    return;
                }
                try
                {
                    p.StandardInput.WriteLine(line);
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    Logger.Debug($"{Tag} Failed to send {message.Type}: {e.Message}");
                }
            }
        }

        public async Task CloseAsync()
        {
            var p = process;
            if (p == null || IsExited)
            {
                return;
            }

            SendMessage(new LivelyCloseCmd());
            CloseStdin(); // stdin EOF is the second, protocol-independent close signal.
            try
            {
                await exitedTcs.Task.WaitAsync(closeTimeout).ConfigureAwait(false);
                Logger.Info($"{Tag} Closed");
                return;
            }
            catch (TimeoutException)
            {
                Logger.Warn($"{Tag} Did not exit within {closeTimeout.TotalSeconds:0.#} s after cmd_close, killing it.");
            }

            Terminate();
            try
            {
                await exitedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Logger.Error($"{Tag} Still running after kill.");
            }
        }

        public void Terminate()
        {
            var p = process;
            if (p == null)
            {
                return;
            }
            try
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                Logger.Debug($"{Tag} Kill failed: {e.Message}");
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 1)
            {
                return;
            }
            var p = process;
            if (p == null)
            {
                return;
            }
            Terminate();
            // Let the exit watcher finish before the Process object goes away.
            exitedTcs.Task.Wait(TimeSpan.FromSeconds(2));
            CloseStdin();
            p.Dispose();
        }

        private void CloseStdin()
        {
            lock (writeLock)
            {
                if (stdinClosed || process == null)
                {
                    return;
                }
                stdinClosed = true;
                try
                {
                    process.StandardInput.Close();
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
                {
                }
            }
        }

        private async Task ReadStdoutAsync(StreamReader reader)
        {
            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    HandleLine(line);
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
                Logger.Debug($"{Tag} stdout closed: {e.Message}");
            }
        }

        private async Task ReadStderrAsync(StreamReader reader)
        {
            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        Logger.Warn($"{Tag} stderr: {line}");
                    }
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }

        private void HandleLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            IpcMessage? message;
            try
            {
                message = IpcMessageJson.Deserialize(line);
            }
            catch (JsonException e)
            {
                Logger.Warn($"{Tag} Ignoring malformed line ({e.Message}): {line}");
                return;
            }
            if (message == null)
            {
                Logger.Debug($"{Tag} Ignoring message without handler: {line}");
                return;
            }

            switch (message)
            {
                case LivelyMessageWallpaperLoaded loaded:
                    if (loaded.Success)
                    {
                        loadedTcs.TrySetResult();
                    }
                    else
                    {
                        loadedTcs.TrySetException(new WallpaperPlayerException("Player reported that the wallpaper failed to load."));
                    }
                    break;
                case LivelyMessageConsole console:
                    if (console.Category == ConsoleMessageType.error)
                    {
                        Logger.Error($"{Tag} {console.Message}");
                    }
                    else
                    {
                        Logger.Info($"{Tag} {console.Message}");
                    }
                    break;
            }

            try
            {
                MessageReceived?.Invoke(this, message);
            }
            catch (Exception e)
            {
                Logger.Error($"{Tag} MessageReceived handler failed: {e}");
            }
        }

        private async Task WatchExitAsync(Process p)
        {
            int? code = null;
            try
            {
                await p.WaitForExitAsync().ConfigureAwait(false);
                // Read what the player wrote just before exiting (for example msg_wploaded Success:false).
                await Task.WhenAny(stdoutTask, Task.Delay(OutputDrainTimeout)).ConfigureAwait(false);
                code = p.ExitCode;
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
                Logger.Debug($"{Tag} Exit watch failed: {e.Message}");
            }
            MarkExited(code);
        }

        private void MarkExited(int? code)
        {
            if (Interlocked.Exchange(ref exitedRaised, 1) == 1)
            {
                return;
            }
            ExitCode = code;
            loadedTcs.TrySetException(new WallpaperPlayerException($"Player exited (code {code?.ToString() ?? "?"}) before loading the wallpaper."));
            // Observe the exception when nobody awaits the load (exit after a successful load).
            _ = loadedTcs.Task.Exception;
            Logger.Info($"{Tag} Exited with code {code?.ToString() ?? "?"}");
            try
            {
                Exited?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception e)
            {
                Logger.Error($"{Tag} Exited handler failed: {e}");
            }
            finally
            {
                exitedTcs.TrySetResult();
            }
        }
    }
}
