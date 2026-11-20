using lively.Core.Wallpapers;
using lively.Models;
using lively.Models.Message;

namespace lively.Tests.Fakes;

public sealed class FakeWallpaperPlayer(LibraryModel model, DisplayMonitor screen) : IWallpaperPlayer
{
    private int exited;

    /// <summary>Runs inside ShowAsync; throw to simulate a load failure.</summary>
    public Func<CancellationToken, Task> OnShow { get; set; } = _ => Task.CompletedTask;

    /// <summary>State the factory created the player with.</summary>
    public PlaybackState? State { get; init; }

    public bool IsLoaded { get; private set; }
    public bool IsExited => Volatile.Read(ref exited) == 1;
    public LibraryModel Model { get; } = model;
    public DisplayMonitor Screen { get; } = screen;
    public int? Pid => null;
    public int CloseCalls { get; private set; }
    public bool IsDisposed { get; private set; }
    public List<IpcMessage> Sent { get; } = [];

    public event EventHandler? Loaded;
    public event EventHandler? Exited;
    public event EventHandler<IpcMessage>? MessageReceived;

    /// <summary>Number of MessageReceived subscribers.</summary>
    public int MessageSubscribers => MessageReceived?.GetInvocationList().Length ?? 0;

    /// <summary>Simulates a message from the player process.</summary>
    public void RaiseMessage(IpcMessage message) => MessageReceived?.Invoke(this, message);

    public async Task ShowAsync(CancellationToken ct = default)
    {
        await OnShow(ct);
        IsLoaded = true;
        Loaded?.Invoke(this, EventArgs.Empty);
    }

    public Task CloseAsync()
    {
        CloseCalls++;
        SimulateExit();
        return Task.CompletedTask;
    }

    public void Terminate() => SimulateExit();

    /// <summary>Simulates the player process ending (crash or kill).</summary>
    public void SimulateExit()
    {
        if (Interlocked.Exchange(ref exited, 1) == 0)
        {
            Exited?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Pause() => Sent.Add(new LivelySuspendCmd());
    public void Play() => Sent.Add(new LivelyResumeCmd());
    public void SetVolume(int volume) => Sent.Add(new LivelyVolumeCmd { Volume = volume });
    public void SetMute(bool mute) => Sent.Add(new LivelyVolumeCmd { Volume = mute ? 0 : 100 });
    public void SendMessage(IpcMessage message) => Sent.Add(message);

    public void Dispose() => IsDisposed = true;
}
