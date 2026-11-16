
# Lively Linux   ![Badge Contributions]   [![Badge License]][License]

*Experimental Linux version of **[Lively Wallpaper]**.*

<br>

<div align = center>

<img
    src = 'resources/main.png'
    height = 400
/>

</div>

<br>
<br>

## Progress

The project is in its very early stages. <br>
The user interface, the core daemon and the <br>
IPC between them work, but wallpapers are only <br>
handled by a dummy player that renders nothing.

See the **[Revival Plan]** for the roadmap.

<br>

### Plans

*Everything is subject to change.*

- Language / Runtime : **[.NET 10 SDK]**

- Video Player : **[MPV]** / **[MPV.Net]**

- Interface : **[AvaloniaUI]**

- Web Browser : ***???***

- Desktop : ***KDE ???***

<br>
<br>

## Architecture

Lively Linux is split into processes that talk over the D-Bus session bus:

- **`lively`** — the Avalonia user interface, a pure client. It is single instance: starting it
  again brings the existing window to the front.

- **`lively-daemon`** — the core: displays (Wayland outputs), settings, the wallpaper layout and
  the player processes. The UI starts it when it is not running; it keeps running when the UI is
  closed. It owns `io.github.lively_linux.Daemon` and serves `io.github.lively_linux.Core1`.

- **Players** — one process per display, driven by the daemon with JSON lines on stdin/stdout
  (the message names of upstream Lively). For now this is `lively-player-dummy`.

Interfaces, error names, `busctl` examples and the player protocol are described in **[docs/ipc.md]**.

<br>
<br>

## Data

| What | Where |
| --- | --- |
| Wallpapers | `~/.local/share/lively-wallpaper/library/wallpapers` |
| Settings | `~/.config/lively-wallpaper/Settings.json` |
| Wallpaper layout (which wallpaper on which display) | `~/.config/lively-wallpaper/WallpaperLayout.json` |
| Logs (`ui.log`, `daemon.log`, `player-dummy-<display>.log`) | `~/.local/state/lively-wallpaper/logs` |

*`$XDG_DATA_HOME`, `$XDG_CONFIG_HOME` and `$XDG_STATE_HOME` are used instead when set.*

<br>
<br>

## Building

1.  Install the **[.NET 10 SDK]**

2. Navigate to `src/lively`

3. Build the project with:

    ```shell
    dotnet build
    ```

4. Run the app with:

    ```shell
    dotnet run --project lively
    ```

    This also starts `lively-daemon` from the same output folder. <br>
    Right-click a wallpaper and choose **Set Wallpaper**.

5. Or run the daemon on its own and control it with `busctl`:

    ```shell
    ./lively/bin/Debug/net10.0/lively-daemon
    ```

    Options: `--no-restore` (do not start the saved layout), <br>
    `--displays "DP-1:1920x1080@0,0;HDMI-A-1:2560x1440@1920,0"` (fake displays). <br>
    Stop it with `Ctrl+C`, `SIGTERM` or `busctl --user call io.github.lively_linux.Daemon /io/github/lively_linux/Core io.github.lively_linux.Core1 Quit`.

6. Run the tests with:

    ```shell
    dbus-run-session -- dotnet test
    ```

    `dbus-run-session` gives the D-Bus tests a private session bus; <br>
    without a bus they are skipped.

<br>


<!----------------------------------------------------------------------------->

[Lively Wallpaper]: https://github.com/rocksdanister/lively
[AvaloniaUI]: https://github.com/AvaloniaUI/Avalonia
[.NET 10 SDK]: https://dotnet.microsoft.com/download
[Revival Plan]: REVIVAL_PLAN.md
[docs/ipc.md]: docs/ipc.md
[MPV.Net]: https://github.com/mireo91/Mpv.NET-lib-
[MPV]: https://github.com/mpv-player/mpv

[License]: LICENSE


<!---------------------------------[ Badges ]---------------------------------->

[Badge Contributions]: https://img.shields.io/badge/Contributions-Welcome-327c25?style=for-the-badge&logoColor=white&logo=GitHub&labelColor=44A833
[Badge License]: https://img.shields.io/badge/License-GPL_3-02639a.svg?style=for-the-badge&labelColor=blue