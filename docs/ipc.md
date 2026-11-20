# Processes and IPC

Lively Linux runs as three kinds of processes:

```
 lively (Avalonia UI)                 lively-daemon (core)                  players
 ───────────────────                  ────────────────────                  ───────
 io.github.lively_linux.Lively        io.github.lively_linux.Daemon
 org.freedesktop.Application  ◄─┐     /io/github/lively_linux/Core
                                │     io.github.lively_linux.Core1
 CoreClient ── D-Bus calls ─────┼───► CoreDBusServer ── WallpaperService ──► lively-player-mpv (DP-1)
            ◄─ D-Bus signals ───┼──── (WallpaperChanged, DisplaysChanged,   ├─► lively-player-web (DP-3)
                                │      Error, PausedChanged,                │   JSON lines on stdin/stdout
 second `lively` ── Activate ───┘      WallpaperUpdated)                    └── one process per display
                    or Open
```

- **lively** is a pure client. On start it takes the UI bus name (single instance), makes sure
  `lively-daemon` owns the daemon bus name (D-Bus activation, else it starts the daemon itself,
  see [Start-up](#start-up)), then talks to it over the session bus.
- **lively-daemon** hosts `lively.Core`: displays, settings, the wallpaper layout, the import of
  videos, web pages and website addresses, and the player processes. It keeps running when the UI
  is closed.
- **Players** render one wallpaper on one display:

  | Session | `video`, `gif` | `web`, `webaudio`, `url` | Other wallpaper types |
  | --- | --- | --- | --- |
  | Wayland | `lively-player-mpv` (libmpv on a `wlr-layer-shell` background surface) | `lively-player-web` (QtWebEngine on a `wlr-layer-shell` background surface) | `lively-player-dummy` |
  | X11, unknown session, fake displays (`--displays`) | `lively-player-dummy` | `lively-player-dummy` | `lively-player-dummy` |

  `lively-player-dummy` speaks the protocol but renders nothing. On a Wayland compositor without
  `zwlr_layer_shell_v1` (GNOME) videos and web pages still go to their players, which fail with
  that reason (`PlayerFailed` and an `Error` signal). When `lively-player-web` was not built (see
  the [README](../README.md#building)), `SetWallpaper` fails with `PlayerFailed` and a message that
  names the packages needed to build it.

## Bus names, paths, interfaces

| What | Value |
| --- | --- |
| Daemon bus name | `io.github.lively_linux.Daemon` |
| Core object path | `/io/github/lively_linux/Core` |
| Core interface | `io.github.lively_linux.Core1` ([XML](../src/lively/lively.Ipc/dbus-xml/io.github.lively_linux.Core1.xml)) |
| UI bus name | `io.github.lively_linux.Lively` |
| UI object path | `/io/github/lively_linux/Lively` |
| UI interface | `org.freedesktop.Application` ([XML](../src/lively/lively.Ipc/dbus-xml/org.freedesktop.Application.xml)) |
| Error names | `io.github.lively_linux.Error.*` (below) |

The C# side is generated from the XML files by `Tmds.DBus.Generator` (proxies in `lively.Ipc`,
handlers in `lively.Core` and `lively`). The constants live in `lively.Common/Constants.cs`
(`Constants.DBus`).

## Core1 methods and signals

| Member | Signature | Description |
| --- | --- | --- |
| `GetDisplays` | → `a(ss(iiii)db)` | `(id, description, (x, y, width, height), scale, primary)` per connected display. Exactly one is primary. |
| `GetWallpapers` | → `a(ss)` | `(display id, wallpaper folder)` of the running wallpapers. |
| `SetWallpaper` | `ss` (folder, display) → | Starts the wallpaper in `folder` on `display`, replacing the one running there, and records it in the layout. Returns once the player reported `msg_wploaded` (at most 15 s), or fails. |
| `CloseWallpaper` | `s` (display) → | Closes the wallpaper on `display` and removes it from the layout. An unknown id is not an error (it may belong to an unplugged display). |
| `CloseAllWallpapers` | → | Closes every wallpaper and clears the layout. |
| `GetSettings` | → `s` | Settings as JSON. |
| `SetSettings` | `s` → | Replaces the settings with the given JSON object and saves them. A changed `WallpaperVolume` (0-100, 0 = muted) applies to the running wallpapers at once; `Startup` writes or removes the autostart entry. |
| `SetPaused` | `b` → | Pauses (`true`) or resumes (`false`) every wallpaper (`cmd_suspend` / `cmd_resume`). Wallpapers started while paused are paused once loaded. Not persisted: a new daemon starts unpaused. |
| `GetPaused` | → `b` | Whether the wallpapers are paused. |
| `ImportVideo` | `s` (path) → `s` (folder) | Copies the video at the absolute `path` into a new folder under `library/wallpapers` (with `thumbnail.jpg` and a `LivelyInfo.json` of type `video`) and returns that folder. The folder name is the file name in lower case `a-z0-9-` (at most 60 characters), with `-2`, `-3`, … when taken. Needs `ffmpeg` and `ffprobe` on the daemon's PATH. No signal is sent; the caller adds the folder to its library. |
| `ImportWeb` | `s` (path) → `s` (folder) | Copies a web page into a new folder under `library/wallpapers` and returns that folder. An `.html`/`.htm` file is copied alone. A folder is copied with its subfolders; its page is `index.html`, else `index.htm`, else its only top-level HTML file (none or several is an error). The title is the page's `<title>` (first 64 KiB, at most 100 characters), else the file or folder name. A folder that already has a `LivelyInfo.json` of type `web` or `webaudio` keeps it (other types are refused). A folder that is or contains the library is refused. Symbolic links to directories, and links that leave the folder, are skipped. `LivelyInfo.json` is written last, without a thumbnail. |
| `AddUrl` | `s` (url) → `s` (folder) | Adds an absolute `http`/`https` address with a host as a new `url` wallpaper (`FileName` is the normalized address, the title is host and path) and returns its folder. No network access. Addresses with a user name or password are refused; error messages and logs never contain them. The same address twice gives two folders. |
| `Quit` | → | Closes the wallpapers (the layout is kept) and exits the daemon. |
| signal `WallpaperChanged` | `ss` (display, folder) | A wallpaper started, or closed when `folder` is `""`. Sent for every change, also for those made by other clients, restores and crashed players. |
| signal `DisplaysChanged` | | The display list changed; call `GetDisplays`. |
| signal `Error` | `ss` (display, message) | A player failed to load or exited unexpectedly. |
| signal `PausedChanged` | `b` (paused) | The paused state changed (sent only on a change). |
| signal `WallpaperUpdated` | `s` (folder) | The `LivelyInfo.json` of a wallpaper folder changed; reload that folder. Sent when a web wallpaper got its thumbnail ([Thumbnails](#thumbnails-of-web-wallpapers)). |

Rules:

- **`""` means the primary display** for `SetWallpaper` and `CloseWallpaper`.
- The primary display is the first output of `kde_output_order_v1` (KDE's priority order,
  `kscreen-doctor` "priority 1") when the compositor advertises it, else the output named by
  `kde_primary_output_v1`. Without either (core Wayland has no primary concept), or when the named
  output is not listed, it is the output at (0, 0), else the first by (y, x, id). KWin in Plasma 6.8
  advertises `kde_output_order_v1` only.
- `folder` must be an absolute path to a folder that contains `LivelyInfo.json`; a trailing `/` is
  ignored. It does not have to be inside the library.
- The display id is the Wayland output name (`DP-1`, `HDMI-A-1`, ...). Scale is the integer
  `wl_output` scale, sent as a double so that fractional scales fit later.
- The layout (`WallpaperLayout.json`) is the user's intent. At start the daemon restores every
  entry whose display is connected; entries of unplugged displays are kept and restored when the
  display comes back (displays are polled every 5 s).

### Errors

| Error name | When |
| --- | --- |
| `io.github.lively_linux.Error.WallpaperNotFound` | The folder is relative, missing, or has no readable `LivelyInfo.json`. |
| `io.github.lively_linux.Error.DisplayNotFound` | No display with that id is connected (or `""` and no display at all). |
| `io.github.lively_linux.Error.PlayerFailed` | The wallpaper file is missing, the player could not start, reported `Success:false`, exited early, or did not load within 15 s. |
| `io.github.lively_linux.Error.InvalidSettings` | `SetSettings` got malformed JSON or not a JSON object. |
| `io.github.lively_linux.Error.ImportFailed` | `ImportVideo` got a relative or missing path, the file has no video stream, or ffmpeg/ffprobe is missing or failed; `ImportWeb` found no usable page or a refused folder; `AddUrl` got no absolute http(s) address. Nothing is left in the library. |
| `io.github.lively_linux.Error.Failed` | Anything else, for example a call that arrives while the daemon shuts down. |

### Settings JSON

`SettingsModel` serialized by Newtonsoft.Json, compact:

```json
{"AppVersion":"1.0.0.0","Language":"en-US","Startup":false,"IsFirstRun":true,"WallpaperVolume":0}
```

`SetSettings` replaces the whole object; missing properties get their defaults.

`WallpaperVolume` is 0 (muted) by default, so wallpapers start without sound. The UI's Library page
has a **Mute**/**Unmute** button and a 0-100 slider for it: it reads the settings when it connects
and writes a change with `GetSettings`, then `SetSettings` of the same object with the new volume
(the other settings are kept), 300 ms after the last slider move or at once for the button. Unmute
restores the last volume above 0, else 50. There is no settings-changed signal, so a change made
by another client shows in the UI after its next start.

### Concurrency

Method calls run on the thread pool, so a `SetWallpaper` waiting for its player does not block
other calls. Wallpaper changes are serialized in the daemon: a second `SetWallpaper` waits for the
first. The player load timeout (15 s) stays below the usual D-Bus client call timeout
(25 s in libdbus, sd-bus and busctl).

## Single instance

- **Daemon:** owning `io.github.lively_linux.Daemon` is the lock. A second `lively-daemon` exits
  with code 2. Exit codes: 0 normal, 1 invalid arguments or session bus lost (also a failed
  `--install-user`/`--uninstall-user`), 2 already running, 3 no session bus.
- **UI:** owning `io.github.lively_linux.Lively` is the lock. A second `lively` calls
  `org.freedesktop.Application.Activate` on the first (forwarding `XDG_ACTIVATION_TOKEN` as
  `activation-token` and `DESKTOP_STARTUP_ID` as `desktop-startup-id` in `platform_data`) and exits
  with code 0. The first instance shows and activates its window. With file arguments
  (`lively ~/Videos/rain.mp4`, paths, `file://` URIs or `http(s)://` addresses) the second instance
  calls `Open` with their URIs instead (`file://` for paths, addresses unchanged), and the first
  instance imports them: folders and `.html`/`.htm` files with `ImportWeb`, addresses with
  `AddUrl`, everything else with `ImportVideo`. The first instance imports its own arguments the
  same way once the daemon is reachable. Other URI schemes are ignored. Without a session bus the
  UI starts anyway, without single-instance handling and without wallpapers.

## Start-up

`lively-daemon --install-user` writes, for the current user:

| File | Purpose |
| --- | --- |
| `~/.local/share/dbus-1/services/io.github.lively_linux.Daemon.service` | D-Bus activation file: the session bus starts `lively-daemon` on demand. |
| `~/.local/share/applications/io.github.lively_linux.Lively.desktop` | Application launcher for the UI (`Exec=… %U`, video MIME types, icon from the build's `data/` folder). Only written when `lively` is in the same folder as the daemon. |

When the `Startup` setting is true it also rewrites the autostart entry (below) for this build.
It then asks the bus to reload its configuration, so activation works at once. `$XDG_DATA_HOME`
and `$XDG_CONFIG_HOME` replace `~/.local/share` and `~/.config` when set.
`lively-daemon --uninstall-user` removes both files and the autostart entry. Both commands are
idempotent and need no running daemon. `Exec=` is the apphost of the build that ran the command
(or `dotnet <dll>` with an absolute `dotnet` when there is no executable apphost): run it again
after building into another folder (Debug/Release, another checkout). A running daemon keeps
running the old build until it exits (`Quit`).

- **UI start:** when the daemon name has no owner, the UI calls `StartServiceByName` and waits up
  to 10 s for the name.
- **Activated daemon:** with dbus-broker (Fedora) it runs as a transient systemd user unit named
  like `dbus-:1.2-io.github.lively_linux.Daemon@0.service`. It is not a child of the UI: closing the
  UI or its terminal does not stop it. Its console output goes to the journal
  (`journalctl --user -f -u 'dbus-:*lively*'`); `daemon.log` is written as usual.
- **Environment:** an activated daemon gets the environment of the systemd user manager
  (`systemctl --user show-environment`), not that of the UI. It needs `WAYLAND_DISPLAY`,
  `XDG_SESSION_TYPE` and `XDG_CURRENT_DESKTOP` (Plasma exports them), and finds `ffmpeg`/`ffprobe`
  through that `PATH`. When they are missing:
  `dbus-update-activation-environment --systemd WAYLAND_DISPLAY XDG_SESSION_TYPE XDG_CURRENT_DESKTOP`.
- **Fallback spawn:** without an activation file (`org.freedesktop.DBus.Error.ServiceUnknown`), or
  when activation fails (for example a stale `Exec=`), the UI starts `lively-daemon` from its own
  directory. That daemon inherits the UI's process group and terminal, so Ctrl+C in that terminal
  stops both.
- **Login:** the daemon writes `~/.config/autostart/io.github.lively_linux.Daemon.desktop` when the
  `Startup` setting is true and removes it when false, at start and on every settings change.
  The entry starts `lively-daemon`, which restores the layout. There is no settings page yet; use
  `SetSettings` (example below).

## Examples with busctl

```bash
D="io.github.lively_linux.Daemon /io/github/lively_linux/Core io.github.lively_linux.Core1"
busctl --user introspect io.github.lively_linux.Daemon /io/github/lively_linux/Core
busctl --user call $D GetDisplays
# a(ss(iiii)db) 3 "DP-1" "Dell Inc. DELL E2726DS" 0 0 2560 1440 1 false "DP-3" ... true "DP-2" ...
busctl --user call $D SetWallpaper ss ~/.local/share/lively-wallpaper/library/wallpapers/sample DP-1
busctl --user call $D SetWallpaper ss ~/.local/share/lively-wallpaper/library/wallpapers/sample ""   # primary (DP-3 here)
busctl --user call $D GetWallpapers
# a(ss) 1 "DP-1" "/home/user/.local/share/lively-wallpaper/library/wallpapers/sample"
busctl --user call $D CloseWallpaper s DP-1
busctl --user call $D GetSettings
busctl --user call $D SetSettings s '{"Language":"de-DE"}'
busctl --user call $D SetSettings s '{"Startup":true,"WallpaperVolume":0}'   # autostart at login, muted
busctl --user call $D SetPaused b true
busctl --user call $D GetPaused
# b true
busctl --user call $D ImportVideo s ~/Videos/rain.mp4
# s "/home/user/.local/share/lively-wallpaper/library/wallpapers/rain"
busctl --user call $D ImportWeb s ~/pages/aurora.html
busctl --user call $D AddUrl s https://webglsamples.org/aquarium/aquarium.html
# s "/home/user/.local/share/lively-wallpaper/library/wallpapers/webglsamples-org-aquarium-aquarium-html"
busctl --user call $D Quit
busctl --user monitor io.github.lively_linux.Daemon          # watch calls and signals
busctl --user call org.freedesktop.DBus /org/freedesktop/DBus org.freedesktop.DBus \
    StartServiceByName su io.github.lively_linux.Daemon 0      # u 1 = started, u 2 = already running
busctl --user status io.github.lively_linux.Daemon           # shows the unit of an activated daemon
```

## Player protocol

Ported from upstream Lively so that wallpapers' JavaScript API and `LivelyProperties.json`
controls keep their message names.

### Launch

The core starts the player with `ProcessStartInfo.ArgumentList` (no shell), as the apphost next
to the daemon, else `dotnet <player>.dll`:

| Argument | Example |
| --- | --- |
| `--wallpaper-url <path or url>` | `/home/user/.../video.mp4` |
| `--wallpaper-type <WallpaperType name>` | `video` |
| `--wallpaper-display <output name>` | `DP-1` |
| `--wallpaper-geometry <W>x<H>` | `2560x1440` (logical size) |
| `--wallpaper-volume <0-100>` | `0` (the `WallpaperVolume` setting; 0 mutes) |
| backend arguments (Wayland only) | `--wallpaper-backend wayland --wallpaper-output DP-1 --wallpaper-desktop kde` |
| `--mpv-log <path>` (`lively-player-mpv`) | `~/.local/state/lively-wallpaper/logs/player-mpv-DP-1.log` |
| `--dummy-log <path>` (`lively-player-dummy`) | `~/.local/state/lively-wallpaper/logs/player-dummy-DP-1.log` |
| `--web-log <path>` (`lively-player-web`) | `~/.local/state/lively-wallpaper/logs/player-web-DP-1.log` |
| `--web-profile <dir>` (`lively-player-web`) | `~/.cache/lively-wallpaper/WebEngine/DP-1` (one per display) |

For `url` wallpapers `--wallpaper-url` is the address; for other types an absolute path.

Players must ignore `--wallpaper-*` arguments they do not know. For tests the dummy player also
takes `--dummy-load-delay <ms>`, `--dummy-hang` and `--dummy-fail`.

### Messages

- UTF-8 without BOM, one compact JSON object per line (`\n`).
- Core → player on **stdin**, player → core on **stdout**. **stderr** is free text that the core
  logs as warnings.
- `Type` comes first and is an **integer**: `{"Type":7}` is a suspend command,
  `{"Type":2,"Success":true}` reports a loaded wallpaper. Readers also accept the enum name.

| Type | Name | Direction | Fields |
| --- | --- | --- | --- |
| 0 | `msg_hwnd` | — | (Windows only, unused) |
| 1 | `msg_console` | player → core | `Message`, `Category` (0 log, 1 error, 2 console) |
| 2 | `msg_wploaded` | player → core | `Success` |
| 3 | `msg_screenshot` | player → core | `FileName`, `Success` |
| 4 | `cmd_reload` | core → player | |
| 5 | `cmd_close` | core → player | |
| 6 | `cmd_screenshot` | core → player | `Format`, `FilePath`, `Delay`, `Width` ([below](#screenshots)) |
| 7 | `cmd_suspend` | core → player | |
| 8 | `cmd_resume` | core → player | |
| 9 | `cmd_volume` | core → player | `Volume` (0-100) |
| 10 | `lsp_perfcntr` | core → player | (Phase 4b) |
| 11 | `lsp_nowplaying` | core → player | (Phase 4b) |
| 12 | `lp_slider` | core → player | `Name`, `Value`, `Step` |
| 13 | `lp_textbox` | core → player | `Name`, `Value` |
| 14 | `lp_dropdown` | core → player | `Name`, `Value` (int) |
| 15 | `lp_fdropdown` | core → player | `Name`, `Value` (string) |
| 16 | `lp_button` | core → player | `Name`, `IsDefault` |
| 17 | `lp_cpicker` | core → player | `Name`, `Value` |
| 18 | `lp_chekbox` | core → player | `Name`, `Value` (bool) |
| 19 | `lp_dropdown_scaler` | core → player | `Name`, `Value` (int) |

The core sends `cmd_close` (close), `cmd_suspend` and `cmd_resume` (`SetPaused`, and
`cmd_suspend` after a load while paused), `cmd_volume` (a changed `WallpaperVolume`) and
`cmd_screenshot` (the thumbnail of a web wallpaper). `cmd_reload` and a mute (`cmd_volume` 0,
remembering the previous volume) exist in `IWallpaperPlayer` but nothing in the core calls them yet.
`lively-player-mpv` handles `cmd_close`, `cmd_suspend`, `cmd_resume`, `cmd_volume` and
`cmd_reload` and logs the others; `lively-player-web` handles those and `cmd_screenshot`;
`lively-player-dummy` logs every command and exits on `cmd_close`. Players echo each command as
`msg_console` `received <name>`.

From the player the core reads `msg_wploaded` (the handshake), `msg_console` (logged; category 1 as
an error) and `msg_screenshot` (thumbnails).

### Screenshots

`cmd_screenshot` asks for a picture of what the wallpaper shows:

```
< {"Type":6,"Format":0,"FilePath":"/home/user/.local/share/lively-wallpaper/library/wallpapers/aurora/thumbnail.jpg","Delay":3000,"Width":480}
> {"Type":3,"FileName":"thumbnail.jpg","Success":true}
```

- `Format`: 0 `jpeg`, 1 `png`, 2 `bmp` (the name is accepted too).
- `FilePath`: absolute; the folder exists, the player creates no directories and replaces an
  existing file.
- `Delay`: milliseconds to wait before the capture.
- `Width`: scale the picture down to this many pixels wide, keeping the aspect ratio, never up.
  0 or missing means the native size. Not in upstream; left out of the JSON when 0.
- The answer `msg_screenshot` carries the file name only. `Success:false` means there is no
  picture (grab, encoding or write failed, invalid fields, or the request was replaced by a newer
  one). Not answering is allowed.

### Thumbnails of web wallpapers

When a `web`, `webaudio` or `url` wallpaper from the library's `wallpapers` folder has loaded and
its `LivelyInfo.json` names no thumbnail that exists, the core sends the request above (JPEG,
`<folder>/thumbnail.jpg`, 3000 ms, 480 pixels). While the wallpapers are paused it waits for the
resume. On `Success:true` with a non-empty file it sets `"Thumbnail": "thumbnail.jpg"` in
`LivelyInfo.json` (only that field; written to a temporary file and renamed) and sends
`WallpaperUpdated(folder)`. There is at most one request per folder (the same wallpaper on two
displays is asked once) and each player is asked once; an answer after the wallpaper was closed or
replaced is ignored. A failed or unanswered request is not repeated until the wallpaper starts
again.

### Lifetime

1. **Handshake:** the player sends `msg_wploaded {"Success":true}` once the wallpaper is visible
   (`lively-player-web`: for a `url` wallpaper as soon as its surface is shown, see below).
   The core waits 15 s. On a timeout, `Success:false` or an early exit, the core kills the process
   tree and `SetWallpaper` fails with `PlayerFailed`.
2. **Close:** the core sends `cmd_close`, closes stdin, waits 3 s, then kills the process tree.
3. **stdin EOF rule:** every player must exit when stdin reaches end of file. This is how players
   die with the daemon, even on `SIGKILL` (.NET cannot set `PR_SET_PDEATHSIG`). Players also exit
   cleanly on `SIGTERM` and `SIGINT`.
4. An unexpected exit removes the player, sends `WallpaperChanged(display, "")` and
   `Error(display, message)`, and keeps the layout entry, so the next restore tries again.

### Example transcript

```
$ lively-player-dummy --wallpaper-url /w/video.mp4 --wallpaper-type video \
      --wallpaper-display DP-1 --wallpaper-geometry 2560x1440 --wallpaper-volume 0
> {"Type":1,"Message":"started --wallpaper-url /w/video.mp4 ...","Category":0}
> {"Type":2,"Success":true}
< {"Type":7}
> {"Type":1,"Message":"received cmd_suspend","Category":0}
< {"Type":9,"Volume":40}
> {"Type":1,"Message":"received cmd_volume","Category":0}
< {"Type":5}
> {"Type":1,"Message":"received cmd_close","Category":0}
(exit 0)
```

`<` is stdin (core → player), `>` is stdout (player → core). `lively-player-mpv` answers the same
way (`started …`, `received cmd_suspend`, …).

## lively-player-mpv

A .NET program that calls the native libraries through P/Invoke; no C code and no mpv binary.

**Libraries**, loaded before anything touches the compositor, so a missing one is reported without a
surface appearing:

| Library | Package (Fedora / Debian, Ubuntu) |
| --- | --- |
| `libmpv.so.2` | `mpv-libs` / `libmpv2` |
| `libwayland-client.so.0` | `libwayland-client` / `libwayland-client0` |
| `libwayland-egl.so.1` | `libwayland-egl` / `libwayland-egl1` |
| `libEGL.so.1` | `libglvnd-egl`, `mesa-libEGL` / `libegl1` |

OpenGL ES functions come from `eglGetProcAddress`, else `libGLESv2.so.2`.

**Wayland objects:** `wl_compositor` (up to v4), `zwlr_layer_shell_v1` (up to v4; KWin advertises
v5) and every `wl_output` (up to v4, for the name and scale). The output is the one named by
`--wallpaper-output` (default: `--wallpaper-display`).

**Surface:** one `wl_surface` with a `zwlr_layer_surface_v1` on that output, layer `background`,
namespace `lively-wallpaper`, size 0×0 and anchored to all four edges (the compositor picks the
output size), exclusive zone -1 (does not move for panels), no keyboard. It is committed without a
buffer and maps with the first video frame. A configure size of 0 falls back to
`--wallpaper-geometry`.

**Scale:** the integer `wl_output.scale` of the output, read once at start. The buffer is the
logical size times the scale, with `wl_surface.set_buffer_scale`. There is no
`wp_fractional_scale_v1`: at a fractional scale (125 %, 150 %) the compositor resamples the
integer-scaled buffer.

**Rendering:** EGL on the Wayland display (OpenGL ES 3, else ES 2, else desktop OpenGL), swap
interval 0, and the mpv render API (`vo=libmpv`) drawing into the window's framebuffer. mpv
options: `hwdec=auto-safe`, `loop-file=inf`, `keep-open=no`, `input-default-bindings=no`, `osc=no`,
`terminal=no`, `msg-level=all=warn`, `audio-client-name=lively-wallpaper`, `volume=<v>`,
`mute=<v == 0>`.

**Event loop:** Wayland, EGL and mpv all run on the main thread, which sleeps in `poll()` on the
Wayland socket and an eventfd. A stdin reader thread, the `SIGTERM`/`SIGINT` handlers and mpv's
wakeup and render-update callbacks only queue work and signal the eventfd.

**Frames** are paced by `wl_surface.frame` callbacks. KWin sends none while the screen is locked or
the surface is not painted; a frame that waits longer than 1000 ms is drawn anyway (about one frame
per second) and this is logged once. While paused nothing is drawn, except the first frame and
frames that answer a configure.

**Loaded:** `msg_wploaded {"Success":true}` is sent after mpv reported the file loaded and its
first frame was swapped, also when `cmd_suspend` arrived earlier.

**Commands:** `cmd_close` exits; `cmd_suspend`/`cmd_resume` set mpv's `pause`; `cmd_volume` sets
`volume`, and 0 sets `mute=yes`; `cmd_reload` loads the file again.

**Exit codes:**

| Code | When |
| --- | --- |
| 0 | `cmd_close`, stdin EOF, `SIGTERM`, `SIGINT`, or the compositor closed the layer surface (`closed`). |
| 1 | Invalid arguments, a missing library, no `zwlr_layer_shell_v1`, unknown output, lost Wayland connection, EGL or mpv failure, a file mpv cannot play (or that ends before its first frame), or mpv shut down. The player first sends `msg_console` (category 1) with the reason and, if not sent yet, `msg_wploaded {"Success":false}`, and writes `lively-player-mpv: <reason>` to stderr. |

**Log** (`--mpv-log`, appended, timestamped): the arguments, the bound Wayland versions and outputs,
the chosen output and scale, configure events, the EGL version, vendor and context, the libmpv
client API version, mpv warnings and errors, file loaded and end-of-file events, the loaded line
with `hwdec-current` (`no` means software decoding), received commands, the frame-callback timeout,
and the exit reason and code.

**Limitations:**

- On KDE Plasma the layer-shell background surface shares its stacking layer with plasmashell's
  desktop, so **desktop icons and widgets are covered** while a wallpaper runs; they come back when
  it is closed. A Plasma wallpaper plugin would keep them and may be added later as a KDE backend.
- X11 is not supported: wallpapers run in `lively-player-dummy`.
- GNOME has no `wlr-layer-shell` (Phase 7: a GNOME Shell extension).
- Integer scale only (see above).
- Other `wlr-layer-shell` compositors (Sway, Hyprland, …) should work but are untested.

**By hand** (from `src/lively`, after `dotnet build`):

```bash
ffmpeg -y -f lavfi -i testsrc=duration=5:size=640x360:rate=30 -pix_fmt yuv420p /tmp/t.mp4
wayland-info | grep -E "zwlr_layer_shell_v1|kde_output_order_v1"
P="dotnet lively/bin/Debug/net10.0/lively-player-mpv.dll"
$P --wallpaper-url /tmp/t.mp4 --wallpaper-type video --wallpaper-display DP-1 --wallpaper-geometry 2560x1440 \
   --wallpaper-volume 0 --wallpaper-backend wayland --wallpaper-output DP-1 --wallpaper-desktop kde --mpv-log /tmp/mpv.log
# prints {"Type":2,"Success":true} and the video plays behind the windows on DP-1; then type:
{"Type":7}              # pause
{"Type":8}              # resume
{"Type":9,"Volume":50}
{"Type":5}              # exit 0; Ctrl+D and kill -TERM also exit 0
```

If the surface never appears: `WAYLAND_DEBUG=1 $P ... 2>&1 | grep -E "zwlr_layer|configure|commit"`,
or KWin's debug console (`busctl --user call org.kde.KWin /KWin org.kde.KWin showDebugConsole`,
Surfaces tab, namespace `lively-wallpaper`).

## lively-player-web

A C++/Qt 6 program (`src/lively/lively.Player.Web`, CMake) that shows a web page with QtWebEngine
(Chromium) on a LayerShellQt surface. `dotnet build` builds it through `lively.Player.Web.proj`
when the development packages are installed (see the [README](../README.md#building)) and copies
it next to the daemon, the UI and the tests.

**Packages:**

| Needed for | Fedora |
| --- | --- |
| Building | `gcc-c++ cmake qt6-qtbase-devel qt6-qtdeclarative-devel qt6-qtwebengine-devel layer-shell-qt-devel` |
| Running | `qt6-qtwebengine layer-shell-qt` |

Ubuntu 24.04 ships LayerShellQt for Qt 5 only, so the player cannot be built there.

**Arguments:** the `--wallpaper-*` set above (`--wallpaper-output`, else `--wallpaper-display`, is
required), `--web-log <path>` and `--web-profile <dir>`. `--wallpaper-url` is an absolute path, a
`file:` URL or an `http(s)` URL; `--wallpaper-type url` also marks the page as remote. Unknown
`--wallpaper-*` arguments are ignored, other unknown options are logged.

**Before Qt starts** the player checks the arguments and the page file, connects to
`WAYLAND_DISPLAY` and looks for `zwlr_layer_shell_v1`, so these failures are reported without a
surface appearing. It then forces the Wayland platform plugin.

**Surface:** a frameless Qt Quick window on the `QScreen` named by `--wallpaper-output`, made a
LayerShellQt layer surface: layer `background`, namespace `lively-wallpaper`, anchored to all four
edges, exclusive zone -1, keyboard interactivity none, not activated on show. The window is the
output size (`--wallpaper-geometry` when given). Pointer input reaches the page through the default
input region; keyboard input never does.

**Page:** a full-window `WebEngineView`. WebGL and accelerated 2D canvas are on, media plays without
a user gesture, local pages may load remote and `file:` URLs, the background is black. Refused, and
logged as `refused …`: new windows, downloads, file, color and authentication dialogs, full screen,
every permission request (camera, microphone, location, notifications, …), protocol handlers, file
system access, screen sharing, web authentication, client certificates and invalid server
certificates. The context menu and tooltips are not shown. `alert`, `confirm` and `prompt` are dismissed
(a "leave page?" dialog is accepted, so a reload is never blocked).

**Profile:** with `--web-profile <dir>` the QtWebEngine profile (cache, cookies, local storage) is
kept in that directory, which is created when missing; the core passes one directory per display,
`~/.cache/lively-wallpaper/WebEngine/<display>`, because two Chromium instances must not share one.
Without it the profile is off the record. Permission decisions are never stored. Qt Quick writes its
shader pipeline cache to `~/.cache/lively-wallpaper/lively-player-web`
(`QSG_RHI_DISABLE_DISK_CACHE=1` turns it off).

**Loaded:**

- A local page: `msg_wploaded {"Success":true}` once the page has loaded and the first frame was
  drawn. A failed load (for example a missing file) is an error: the player exits 1.
- A `url` wallpaper: `msg_wploaded {"Success":true}` as soon as the surface shows its first frame,
  whether or not the site has loaded. A failed load is reported on `msg_console` (category 1) and
  retried every 15 s, so a wallpaper restored at login before the network is up recovers.
- After every successful load the player probes WebGL in an isolated script world and reports
  `WebGL: <context>, <version>, renderer <renderer>` on `msg_console`, or `WebGL: no context`.

**Commands:**

- `cmd_close` exits 0; `cmd_reload` reloads the page.
- `cmd_suspend` grabs the current frame into a still image that covers the window, hides the view,
  sets its lifecycle state to `Frozen` (Chromium stops the page's scripts and rendering) and mutes
  it. `cmd_resume` makes the page active again and drops the still 250 ms later. A suspend that
  arrives before the load takes effect once loaded; a reload or retry during a suspend waits for
  the resume. Chromium pauses the `<video>` and `<audio>` elements of a frozen page and does not
  play them again, so a script that the player adds to every frame (also cross-origin iframes, in
  an isolated world the page cannot see) remembers on the page's `freeze` event which of its
  document's media elements were playing and plays them again on `resume`. Web Audio
  (`AudioContext`) runs again by itself.
- `cmd_volume` 0 mutes, any other value unmutes: QtWebEngine has no per-page volume level. The
  value from `--wallpaper-volume` applies from the start. Sound goes to the default output as a
  PulseAudio/PipeWire stream with `application.name` and node name `lively-player-web`, created
  by Chromium's audio process (`QtWebEngineProcess`) once the page plays sound while unmuted; a
  player muted from the start has no stream. Muting and `cmd_suspend` cork the stream.
- `cmd_screenshot` ([Screenshots](#screenshots)): `Delay` starts at the request when the page has
  loaded, else when it has; the capture is at least 250 ms after the load. While a page's load
  keeps failing (a `url` wallpaper retrying) the request waits, so an error or blank page is never
  captured. The picture is what the window shows (the
  still frame while suspended), written to a temporary file and renamed. One request is pending at
  a time: a newer one replaces it, and the replaced one is answered with `Success:false`.
- Every command is echoed as `msg_console` `received <name>`; a malformed line is answered with
  `invalid message: …` (category 1).

**Console:** messages of the page go to `msg_console` (category 2) as
`console <level>: <message> (<source>:<line>)` and to the log. They are limited to a burst of 20,
then 5 per second; the number dropped is reported once a second. Each is cut at 4096 characters.

**stdout** carries only protocol lines. At start the player moves its protocol output to a private
descriptor and points fd 1 at stderr, so whatever Qt, Chromium, its helper processes or the GL
drivers print cannot corrupt the protocol.

**Exit codes:**

| Code | When |
| --- | --- |
| 0 | `cmd_close`, stdin EOF, `SIGTERM`, `SIGINT`, or the compositor closed the layer surface. |
| 1 | Invalid arguments, a missing page file, no Wayland session, no `zwlr_layer_shell_v1`, unknown output (`unknown output X (outputs: DP-1, DP-2, DP-3)`), the profile directory cannot be created, a local page fails to load before it was shown, the output was removed, the web render process ended, or a fatal Qt error. The player first sends `msg_console` (category 1) with the reason and, if not sent yet, `msg_wploaded {"Success":false}`, and writes `lively-player-web: <reason>` to stderr. |

**Log** (`--web-log`, appended, timestamped): the arguments, the chosen output with size, position
and device pixel ratio, the profile, load events, the WebGL line, received commands, suspend and
resume, screenshots, refusals, page console messages, Qt warnings (`qt: …`) and the exit reason and
code.

**Limitations:**

- One Chromium instance (with its helper processes) per display, a few hundred MB each.
- Mute and unmute only, no volume level.
- Media elements outside the document (`new Audio()` never attached) or in shadow trees stay
  paused after `cmd_suspend` and `cmd_resume`.
- Sites with a cookie consent dialog (YouTube in the EU) keep their video paused until the choice
  is clicked in the wallpaper (mouse input works); the profile keeps the choice.
- No keyboard input, so pages that need a login cannot be signed into.
- Desktop icons and widgets are covered on KDE, as with `lively-player-mpv`.
- No `LivelyProperties.json` controls, JavaScript API bridge or audio data yet (Phase 4b).
- `mediump` precision: on Mesa/AMD, QtWebEngine's ANGLE uses the OpenGL ES backend and the driver
  runs `mediump` at 16-bit precision (`getShaderPrecisionFormat` reports 10 bits). Pages that rely
  on desktop Chrome treating `mediump` as `highp` can render wrongly; `highp` in the page's shader
  fixes them.
- While the session is locked KWin sends no frame callbacks: pages do not animate, and a player
  started then may not draw its first frame (and so not report loaded) until the session is
  unlocked.

**By hand** (from `src/lively`, after `dotnet build`):

```bash
P=lively/bin/Debug/net10.0/lively-player-web
$P --wallpaper-url ~/pages/aurora.html --wallpaper-type web --wallpaper-output DP-1 --web-log /tmp/web.log
# prints started …, {"Type":2,"Success":true} and WebGL: …; the page shows behind the windows on DP-1
{"Type":7}                                                          # suspend
{"Type":8}                                                          # resume
{"Type":9,"Volume":50}                                              # unmute (0 mutes)
{"Type":6,"Format":0,"FilePath":"/tmp/shot.jpg","Delay":500,"Width":480}
{"Type":5}                                                          # exit 0; Ctrl+D and kill -TERM also exit 0
```

The sound stream: `pactl list sink-inputs | grep -B20 'application.name = "lively-player-web"'`
shows its state (`Corked`, `Mute`); `pactl list sink-inputs short` does not name the application.

Built alone: `cmake -S lively.Player.Web -B lively.Player.Web/build -DCMAKE_BUILD_TYPE=Release &&
cmake --build lively.Player.Web/build -j` (the `build/` folder is ignored by git).
