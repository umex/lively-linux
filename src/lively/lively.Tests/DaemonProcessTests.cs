using System.Diagnostics;
using lively.Common;
using lively.Core.Wallpapers;
using lively.Daemon;
using lively.Ipc;
using Tmds.DBus.Protocol;

namespace lively.Tests;

/// <summary>
/// Runs the real lively-daemon binary, with its XDG directories in a temporary folder. The bus tests are
/// meant for dbus-run-session; on a desktop session that already runs lively-daemon they do nothing.
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

    private ProcessStartInfo DaemonStartInfo(string[] args)
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
        return psi;
    }

    private Process StartDaemon(params string[] args)
    {
        var process = Process.Start(DaemonStartInfo(args))!;
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

    /// <summary>Runs lively-daemon to completion and returns its exit code and standard output.</summary>
    private async Task<(int ExitCode, string Output)> RunDaemonAsync(params string[] args)
    {
        using var process = Process.Start(DaemonStartInfo(args))!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        return (process.ExitCode, await output + await error);
    }

    [Fact]
    public async Task InstallUser_WritesFilesIntoXdgDirs()
    {
        tmp.WriteFile("{\"Startup\": true}", "config", "lively-wallpaper", "Settings.json");
        var service = tmp.Combine("data", "dbus-1", "services", "io.github.lively_linux.Daemon.service");
        var launcher = tmp.Combine("data", "applications", "io.github.lively_linux.Lively.desktop");
        var autostart = tmp.Combine("config", "autostart", "io.github.lively_linux.Daemon.desktop");

        var (exitCode, output) = await RunDaemonAsync("--install-user");

        Assert.True(exitCode == 0, output);
        foreach (var file in new[] { service, launcher, autostart })
        {
            Assert.True(File.Exists(file), file + "\n" + output);
            Assert.Contains(file, output);
        }
        var daemonExec = UserInstaller.QuoteExec(PlayerExecutableResolver.Resolve("lively-daemon", []), desktopEntry: false);
        Assert.Contains("Exec=" + daemonExec + "\n", File.ReadAllText(service));
        Assert.Contains("Exec=" + UserInstaller.QuoteExec(PlayerExecutableResolver.Resolve("lively", [])) + " %U\n",
            File.ReadAllText(launcher));

        (exitCode, output) = await RunDaemonAsync("--uninstall-user");

        Assert.True(exitCode == 0, output);
        foreach (var file in new[] { service, launcher, autostart })
        {
            Assert.False(File.Exists(file), file);
            Assert.Contains(file, output);
        }
    }

    [DBusFact]
    public async Task Daemon_KeepsTheAutostartEntryInLineWithTheStartupSetting()
    {
        using var connection = new DBusConnection(DBusAddress.Session!);
        await connection.ConnectAsync();
        using var watcher = await connection.WatchNameOwnerAsync(Constants.DBus.DaemonBusName);
        if (watcher.GetCurrentOwner() != null)
        {
            return; // a real daemon runs on this bus
        }
        tmp.WriteFile("{\"Startup\": true}", "config", "lively-wallpaper", "Settings.json");
        var autostart = tmp.Combine("config", "autostart", "io.github.lively_linux.Daemon.desktop");

        var daemon = StartDaemon("--no-restore", "--displays", "DP-1:1920x1080@0,0");
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        {
            await watcher.WaitForOwnerAsync(cts.Token);
        }
        // Written right after the daemon takes its name.
        Wait.Until(() => File.Exists(autostart), "autostart entry written");

        await using var client = new CoreClient(connection);
        var settings = await client.GetSettingsAsync();
        settings.Startup = false;
        await client.SetSettingsAsync(settings);
        Wait.Until(() => !File.Exists(autostart), "autostart entry removed");

        await client.QuitAsync();
        await daemon.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, daemon.ExitCode);
    }
}
