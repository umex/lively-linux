
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

The project is in its early stages. <br>
Video wallpapers play on **KDE Plasma (Wayland)** <br>
through `lively-player-mpv`. Importing videos, <br>
pause / resume and restoring the wallpapers <br>
at login work. Web wallpapers do not exist yet.

See the **[Revival Plan]** for the roadmap.

<br>

### Plans

*Everything is subject to change.*

- Language / Runtime : **[.NET 10 SDK]**

- Video Player : **[MPV]** through libmpv, in a pure .NET player

- Interface : **[AvaloniaUI]**

- Web Browser : ***???***

- Desktop : **KDE Plasma (Wayland)** first, <br>
  other `wlr-layer-shell` compositors next

<br>
<br>

## Architecture

Lively Linux is split into processes that talk over the D-Bus session bus:

- **`lively`** — the Avalonia user interface, a pure client. It is single instance: starting it
  again brings the existing window to the front, and `lively video.mp4` imports the file there.

- **`lively-daemon`** — the core: displays (Wayland outputs), settings, the wallpaper layout,
  video import and the player processes. The session bus starts it on demand (D-Bus activation)
  once `lively-daemon --install-user` was run; without that the UI starts it. It keeps running
  when the UI is closed. It owns `io.github.lively_linux.Daemon` and serves
  `io.github.lively_linux.Core1`.

- **Players** — one process per display, driven by the daemon with JSON lines on stdin/stdout
  (the message names of upstream Lively). `lively-player-mpv` plays videos and GIFs with libmpv
  on a `wlr-layer-shell` background surface. Everything else, and every wallpaper on X11, goes to
  `lively-player-dummy`, which renders nothing.

Interfaces, error names, `busctl` examples, start-up and the player protocol are described in **[docs/ipc.md]**.

<br>
<br>

## Data

| What | Where |
| --- | --- |
| Wallpapers | `~/.local/share/lively-wallpaper/library/wallpapers` |
| Settings | `~/.config/lively-wallpaper/Settings.json` |
| Wallpaper layout (which wallpaper on which display) | `~/.config/lively-wallpaper/WallpaperLayout.json` |
| Logs (`ui.log`, `daemon.log`, `player-mpv-<display>.log`, `player-dummy-<display>.log`) | `~/.local/state/lively-wallpaper/logs` |
| D-Bus activation file (`--install-user`) | `~/.local/share/dbus-1/services/io.github.lively_linux.Daemon.service` |
| Application launcher (`--install-user`) | `~/.local/share/applications/io.github.lively_linux.Lively.desktop` |
| Login autostart (`Startup` setting) | `~/.config/autostart/io.github.lively_linux.Daemon.desktop` |

*`$XDG_DATA_HOME`, `$XDG_CONFIG_HOME` and `$XDG_STATE_HOME` are used instead when set.*

<br>
<br>

## Building

1.  Install the **[.NET 10 SDK]**, libmpv and ffmpeg:

    ```shell
    sudo dnf install mpv-libs ffmpeg-free   # Fedora (or ffmpeg from RPM Fusion)
    sudo apt install libmpv2 ffmpeg         # Debian / Ubuntu
    ```

    libmpv (**[mpv-libs]** / **[libmpv2]**) plays the videos, <br>
    `ffmpeg` and `ffprobe` are needed to import them.

2. Navigate to `src/lively`

3. Build the project with:

    ```shell
    dotnet build
    ```

4. Let the session bus start the daemon:

    ```shell
    ./lively/bin/Debug/net10.0/lively-daemon --install-user
    ```

    This writes the D-Bus activation file and the application launcher, <br>
    both pointing at this build folder. Run it again after building into <br>
    another folder, and `Quit` a running daemon to pick up a rebuild. <br>
    `--uninstall-user` removes the files and the autostart entry again. <br>
    The activated daemon logs to `daemon.log` and to the journal: <br>
    `journalctl --user -u 'dbus-:*lively*'`.

    Without this step the UI starts `lively-daemon` from its own folder.

5. Run the app with:

    ```shell
    dotnet run --project lively
    ```

    Import a video with **Import video…**, or pass it on the command line: <br>
    `./lively/bin/Debug/net10.0/lively ~/Videos/rain.mp4`. <br>
    Right-click a wallpaper and choose **Set Wallpaper**; **Pause** pauses every wallpaper <br>
    (not remembered across daemon restarts). <br>
    Wallpapers start muted (`WallpaperVolume` 0).

    *On KDE Plasma the wallpaper covers the desktop icons and widgets <br>
    while it runs; they come back when it is closed.*

6. Or run the daemon on its own and control it with `busctl`:

    ```shell
    ./lively/bin/Debug/net10.0/lively-daemon
    ```

    Options: `--no-restore` (do not start the saved layout), <br>
    `--displays "DP-1:1920x1080@0,0;HDMI-A-1:2560x1440@1920,0"` (fake displays, dummy player only). <br>
    Stop it with `Ctrl+C`, `SIGTERM` or `busctl --user call io.github.lively_linux.Daemon /io/github/lively_linux/Core io.github.lively_linux.Core1 Quit`. <br>
    Restore at login with `busctl --user call io.github.lively_linux.Daemon /io/github/lively_linux/Core io.github.lively_linux.Core1 SetSettings s '{"Startup":true}'` <br>
    (this replaces every setting; missing ones get their defaults).

7. Run the tests with:

    ```shell
    dbus-run-session -- dotnet test
    ```

    `dbus-run-session` gives the D-Bus tests a private session bus; <br>
    without a bus they are skipped. The player and import tests <br>
    need a Wayland session, libmpv and ffmpeg, and are skipped otherwise.

<br>


<!----------------------------------------------------------------------------->

[Lively Wallpaper]: https://github.com/rocksdanister/lively
[AvaloniaUI]: https://github.com/AvaloniaUI/Avalonia
[.NET 10 SDK]: https://dotnet.microsoft.com/download
[Revival Plan]: REVIVAL_PLAN.md
[docs/ipc.md]: docs/ipc.md
[MPV]: https://github.com/mpv-player/mpv
[mpv-libs]: https://packages.fedoraproject.org/pkgs/mpv/mpv-libs/
[libmpv2]: https://packages.debian.org/libmpv2

[License]: LICENSE


<!---------------------------------[ Badges ]---------------------------------->

[Badge Contributions]: https://img.shields.io/badge/Contributions-Welcome-327c25?style=for-the-badge&logoColor=white&logo=GitHub&labelColor=44A833
[Badge License]: https://img.shields.io/badge/License-GPL_3-02639a.svg?style=for-the-badge&labelColor=blue