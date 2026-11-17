
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
Video and web wallpapers play on **KDE Plasma (Wayland)** <br>
through `lively-player-mpv` and `lively-player-web`. <br>
Importing videos, HTML pages and website addresses, <br>
pause / resume and restoring the wallpapers at login work. <br>
Customizing Lively web wallpapers and audio data do not exist yet.

See the **[Revival Plan]** for the roadmap.

<br>

### Plans

*Everything is subject to change.*

- Language / Runtime : **[.NET 10 SDK]**

- Video Player : **[MPV]** through libmpv, in a pure .NET player

- Interface : **[AvaloniaUI]**

- Web Browser : **[QtWebEngine]** (Chromium), in a C++ / Qt 6 player

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
  on a `wlr-layer-shell` background surface. `lively-player-web` shows local HTML wallpapers and
  websites with QtWebEngine on the same kind of surface; the mouse reaches the page, the keyboard
  does not. Everything else, and every wallpaper on X11, goes to `lively-player-dummy`, which
  renders nothing.

Interfaces, error names, `busctl` examples, start-up and the player protocol are described in **[docs/ipc.md]**.

<br>
<br>

## Data

| What | Where |
| --- | --- |
| Wallpapers | `~/.local/share/lively-wallpaper/library/wallpapers` |
| Settings | `~/.config/lively-wallpaper/Settings.json` |
| Wallpaper layout (which wallpaper on which display) | `~/.config/lively-wallpaper/WallpaperLayout.json` |
| Logs (`ui.log`, `daemon.log`, `player-mpv-<display>.log`, `player-web-<display>.log`, `player-dummy-<display>.log`) | `~/.local/state/lively-wallpaper/logs` |
| Browser profiles of web wallpapers (cache, cookies, local storage), one per display | `~/.cache/lively-wallpaper/WebEngine/<display>` |
| D-Bus activation file (`--install-user`) | `~/.local/share/dbus-1/services/io.github.lively_linux.Daemon.service` |
| Application launcher (`--install-user`) | `~/.local/share/applications/io.github.lively_linux.Lively.desktop` |
| Login autostart (`Startup` setting) | `~/.config/autostart/io.github.lively_linux.Daemon.desktop` |

*`$XDG_DATA_HOME`, `$XDG_CONFIG_HOME`, `$XDG_STATE_HOME` and `$XDG_CACHE_HOME` are used instead when set.*

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

    For web wallpapers, also install CMake, a C++ compiler and the <br>
    Qt 6 WebEngine and LayerShellQt development packages:

    ```shell
    sudo dnf install gcc-c++ cmake qt6-qtbase-devel qt6-qtdeclarative-devel qt6-qtwebengine-devel layer-shell-qt-devel
    ```

    `dotnet build` then builds `lively-player-web` with CMake. <br>
    At run time it needs `qt6-qtwebengine` and `layer-shell-qt`. <br>
    Without these packages the build prints warning `LIVELYWEB01` and goes on; <br>
    web wallpapers then fail with a message naming the packages. <br>
    `dotnet build -p:BuildWebPlayer=false` skips the player. <br>
    *Ubuntu 24.04 ships LayerShellQt for Qt 5 only, so the web player cannot be built there.*

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

    **Import…** imports videos and HTML files (`.html`, `.htm`), <br>
    **Import folder…** a web page folder (with `index.html`), <br>
    **Add URL…** a website (`http://` or `https://`). <br>
    The same works on the command line: <br>
    `./lively/bin/Debug/net10.0/lively ~/Videos/rain.mp4 ~/pages/aurora.html https://example.com`. <br>
    Web wallpapers get a thumbnail from the page a few seconds after they first run. <br>
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
    need a Wayland session, libmpv and ffmpeg, and are skipped otherwise. <br>
    The web player tests need a Wayland session and the built `lively-player-web`; <br>
    they briefly show a test page on the first display. While the session is locked <br>
    most of them are skipped (`LIVELY_TEST_WEB_WHEN_LOCKED=1` runs them anyway).

<br>


<!----------------------------------------------------------------------------->

[Lively Wallpaper]: https://github.com/rocksdanister/lively
[AvaloniaUI]: https://github.com/AvaloniaUI/Avalonia
[.NET 10 SDK]: https://dotnet.microsoft.com/download
[Revival Plan]: REVIVAL_PLAN.md
[docs/ipc.md]: docs/ipc.md
[MPV]: https://github.com/mpv-player/mpv
[QtWebEngine]: https://doc.qt.io/qt-6/qtwebengine-index.html
[mpv-libs]: https://packages.fedoraproject.org/pkgs/mpv/mpv-libs/
[libmpv2]: https://packages.debian.org/libmpv2

[License]: LICENSE


<!---------------------------------[ Badges ]---------------------------------->

[Badge Contributions]: https://img.shields.io/badge/Contributions-Welcome-327c25?style=for-the-badge&logoColor=white&logo=GitHub&labelColor=44A833
[Badge License]: https://img.shields.io/badge/License-GPL_3-02639a.svg?style=for-the-badge&labelColor=blue