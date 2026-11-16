using System.Diagnostics;
using lively.Common;
using lively.Core.Wallpapers;
using lively.Ipc;
using Tmds.DBus.Protocol;

namespace lively.Tests;

/// <summary>
/// Runs the real lively-daemon binary on the session bus, with its XDG directories in a temporary folder.
/// Meant for dbus-run-session; on a desktop session that already runs lively-daemon the test does nothing.
/// </summary>
public sealed class DaemonProcessTests : IDisposable
{
    private readonly TempDirectory tmp = new();
    private readonly List<Process> processes = [];

    public void Dispose()
    {
        foreach (var p in processes)
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
            }
            p.Dispose();
        }
        tmp.Dispose();
    }

    private Process StartDaemon(params string[] args)
    {
        var launch = PlayerExecutableResolver.Resolve("lively-daemon", args);
        var psi = new ProcessStartInfo(launch.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in launch.Arguments)
        {
            psi.ArgumentList.Add(a);
        }
        psi.Environment["XDG_CONFIG_HOME"] = tmp.CreateDirectory("config");
        psi.Environment["XDG_DATA_HOME"] = tmp.CreateDirectory("data");
        psi.Environment["XDG_STATE_HOME"] = tmp.CreateDirectory("state");
        psi.Environment["XDG_CACHE_HOME"] = tmp.CreateDirectory("cache");
        var process = Process.Start(psi)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        processes.Add(process);
        return process;
    }

    [DBusFact]
    public async Task Daemon_IsSingleInstance_AndQuitsOverDBus()
    {
        using var connection = new DBusConnection(DBusAddress.Session!);
        await connection.ConnectAsync();
        using var watcher = await connection.WatchNameOwnerAsync(Constants.DBus.DaemonBusName);
        if (watcher.GetCurrentOwner() != null)
        {
            return; // a real daemon runs on this bus
        }

        var first = StartDaemon("--no-restore", "--displays", "DP-1:1920x1080@0,0");
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        {
            await watcher.WaitForOwnerAsync(cts.Token);
        }

        var second = StartDaemon("--no-restore", "--displays", "DP-1:1920x1080@0,0");
        await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(2, second.ExitCode);

        await using var client = new CoreClient(connection);
        Assert.Equal(["DP-1"], (await client.GetDisplaysAsync()).Select(x => x.Id));
        await client.QuitAsync();
        await first.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, first.ExitCode);
        Assert.True(File.Exists(tmp.Combine("state", "lively-wallpaper", "logs", "daemon.log")));
    }
}
