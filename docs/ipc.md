# Processes and IPC

Lively Linux runs as three kinds of processes:

```
 lively (Avalonia UI)                 lively-daemon (core)                  players
 ───────────────────                  ────────────────────                  ───────
 io.github.lively_linux.Lively        io.github.lively_linux.Daemon
 org.freedesktop.Application  ◄─┐     /io/github/lively_linux/Core
                                │     io.github.lively_linux.Core1
 CoreClient ── D-Bus calls ─────┼───► CoreDBusServer ── WallpaperService ──► lively-player-dummy (DP-1)
            ◄─ D-Bus signals ───┼──── (WallpaperChanged, DisplaysChanged,   ├─► lively-player-dummy (DP-2)
                                │      Error)                               │   JSON lines on stdin/stdout
 second `lively` ── Activate ───┘                                           └── one process per display
```

- **lively** is a pure client. On start it takes the UI bus name (single instance), starts
  `lively-daemon` if nobody owns the daemon bus name, then talks to it over the session bus.
- **lively-daemon** hosts `lively.Core`: displays, settings, the wallpaper layout and the player
  processes. It keeps running when the UI is closed.
- **Players** render one wallpaper on one display. In Phase 2 every wallpaper type runs in
  `lively-player-dummy`, which speaks the protocol but renders nothing.
  On Wayland, videos and GIFs run in `lively-player-mpv` (libmpv on a `wlr-layer-shell` background surface).

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
| `SetSettings` | `s` → | Replaces the settings with the given JSON object and saves them. A changed `WallpaperVolume` applies to the running wallpapers at once. |
| `SetPaused` | `b` → | Pauses (`true`) or resumes (`false`) every wallpaper. Wallpapers started while paused start paused. Not persisted. |
| `GetPaused` | → `b` | Whether the wallpapers are paused. |
| `ImportVideo` | `s` (path) → `s` (folder) | Copies the video at the absolute `path` into a new folder under `library/wallpapers` (with a thumbnail and `LivelyInfo.json`) and returns that folder. Needs `ffmpeg` and `ffprobe` on the daemon's PATH. |
| `Quit` | → | Closes the wallpapers (the layout is kept) and exits the daemon. |
| signal `WallpaperChanged` | `ss` (display, folder) | A wallpaper started, or closed when `folder` is `""`. Sent for every change, also for those made by other clients, restores and crashed players. |
| signal `DisplaysChanged` | | The display list changed; call `GetDisplays`. |
| signal `Error` | `ss` (display, message) | A player failed to load or exited unexpectedly. |
| signal `PausedChanged` | `b` (paused) | The paused state changed (sent only on a change). |

Rules:

- **`""` means the primary display** for `SetWallpaper` and `CloseWallpaper`.
- The primary display is the output at (0, 0), else the first by (y, x, id). Wayland has no
  primary concept, so this may differ from the desktop's own setting (on KDE, `kscreen-doctor`
  "priority 1"). KDE's `kde_primary_output_v1` will replace the heuristic later.
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
| `io.github.lively_linux.Error.ImportFailed` | `ImportVideo` got a relative or missing path, the file has no video stream, or ffmpeg/ffprobe is missing or failed. Nothing is left in the library. |
| `io.github.lively_linux.Error.Failed` | Anything else, for example a call that arrives while the daemon shuts down. |

### Settings JSON

`SettingsModel` serialized by Newtonsoft.Json, compact:

```json
{"AppVersion":"1.0.0.0","Language":"en-US","Startup":false,"IsFirstRun":true,"WallpaperVolume":0}
```

`SetSettings` replaces the whole object; missing properties get their defaults.

### Concurrency

Method calls run on the thread pool, so a `SetWallpaper` waiting for its player does not block
other calls. Wallpaper changes are serialized in the daemon: a second `SetWallpaper` waits for the
first. The player load timeout (15 s) stays below the usual D-Bus client call timeout
(25 s in libdbus, sd-bus and busctl).

## Single instance

- **Daemon:** owning `io.github.lively_linux.Daemon` is the lock. A second `lively-daemon` exits
  with code 2. Exit codes: 0 normal, 1 invalid arguments or session bus lost, 2 already running,
  3 no session bus.
- **UI:** owning `io.github.lively_linux.Lively` is the lock. A second `lively` calls
  `org.freedesktop.Application.Activate` on the first (forwarding `XDG_ACTIVATION_TOKEN` as
  `activation-token` and `DESKTOP_STARTUP_ID` as `desktop-startup-id` in `platform_data`) and exits
  with code 0. The first instance shows and activates its window. With file arguments
  (`lively ~/Videos/rain.mp4`) the second instance calls `Open` with their `file://` URIs instead,
  and the first instance imports them as video wallpapers. Without a session bus the UI
  starts anyway, without single-instance handling and without wallpapers.
- When the daemon name has no owner, the UI asks the bus to start it (`StartServiceByName`) and
  waits up to 10 s for the name. `lively-daemon --install-user` writes the activation file
  `~/.local/share/dbus-1/services/io.github.lively_linux.Daemon.service` (plus the application
  launcher); an activated daemon runs as a transient systemd user unit and logs to the journal.
  `--uninstall-user` removes the files again.
- Without an activation file (`ServiceUnknown`), or when activation fails, the UI starts
  `lively-daemon` from its own directory. That daemon inherits the UI's process group and
  terminal, so Ctrl+C in that terminal stops both.
- The daemon writes `~/.config/autostart/io.github.lively_linux.Daemon.desktop` when the `Startup`
  setting is true and removes it when false.

## Examples with busctl

```bash
D="io.github.lively_linux.Daemon /io/github/lively_linux/Core io.github.lively_linux.Core1"
busctl --user introspect io.github.lively_linux.Daemon /io/github/lively_linux/Core
busctl --user call $D GetDisplays
# a(ss(iiii)db) 3 "DP-1" "Dell Inc. DELL E2726DS" 0 0 2560 1440 1 true "DP-3" ... "DP-2" ...
busctl --user call $D SetWallpaper ss ~/.local/share/lively-wallpaper/library/wallpapers/sample DP-1
busctl --user call $D SetWallpaper ss ~/.local/share/lively-wallpaper/library/wallpapers/sample ""   # primary
busctl --user call $D GetWallpapers
# a(ss) 1 "DP-1" "/home/user/.local/share/lively-wallpaper/library/wallpapers/sample"
busctl --user call $D CloseWallpaper s DP-1
busctl --user call $D GetSettings
busctl --user call $D SetSettings s '{"Language":"de-DE"}'
busctl --user call $D SetPaused b true
busctl --user call $D GetPaused
# b true
busctl --user call $D ImportVideo s ~/Videos/rain.mp4
# s "/home/user/.local/share/lively-wallpaper/library/wallpapers/rain"
busctl --user call $D Quit
busctl --user monitor io.github.lively_linux.Daemon          # watch calls and signals
```

## Player protocol

Ported from upstream Lively so that wallpapers' JavaScript API and `LivelyProperties.json`
controls keep their message names.

### Launch

The core starts the player with `ProcessStartInfo.ArgumentList` (no shell):

| Argument | Example |
| --- | --- |
| `--wallpaper-url <path or url>` | `/home/user/.../video.mp4` |
| `--wallpaper-type <WallpaperType name>` | `video` |
| `--wallpaper-display <output name>` | `DP-1` |
| `--wallpaper-geometry <W>x<H>` | `2560x1440` |
| `--wallpaper-volume <0-100>` | `0` (the `WallpaperVolume` setting) |
| backend arguments | `--wallpaper-backend wayland --wallpaper-output DP-1 --wallpaper-desktop kde` |
| `--mpv-log <path>` (`lively-player-mpv` only) | `~/.local/state/lively-wallpaper/logs/player-mpv-DP-1.log` |

Players must ignore `--wallpaper-*` arguments they do not know. The dummy player additionally
takes `--dummy-log <path>` (the core passes `~/.local/state/lively-wallpaper/logs/player-dummy-<display>.log`),
and, for tests, `--dummy-load-delay <ms>`, `--dummy-hang` and `--dummy-fail`.

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
| 6 | `cmd_screenshot` | core → player | `Format`, `FilePath`, `Delay` |
| 7 | `cmd_suspend` | core → player | |
| 8 | `cmd_resume` | core → player | |
| 9 | `cmd_volume` | core → player | `Volume` (0-100) |
| 10 | `lsp_perfcntr` | core → player | (Phase 4) |
| 11 | `lsp_nowplaying` | core → player | (Phase 4) |
| 12 | `lp_slider` | core → player | `Name`, `Value`, `Step` |
| 13 | `lp_textbox` | core → player | `Name`, `Value` |
| 14 | `lp_dropdown` | core → player | `Name`, `Value` (int) |
| 15 | `lp_fdropdown` | core → player | `Name`, `Value` (string) |
| 16 | `lp_button` | core → player | `Name`, `IsDefault` |
| 17 | `lp_cpicker` | core → player | `Name`, `Value` |
| 18 | `lp_chekbox` | core → player | `Name`, `Value` (bool) |
| 19 | `lp_dropdown_scaler` | core → player | `Name`, `Value` (int) |

Phase 2 sends `cmd_close`, `cmd_suspend`, `cmd_resume`, `cmd_volume` (mute sends 0 and remembers
the previous value) and `cmd_reload`.

### Lifetime

1. **Handshake:** the player sends `msg_wploaded {"Success":true}` once the wallpaper is visible.
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
      --wallpaper-display DP-1 --wallpaper-geometry 2560x1440 --wallpaper-volume 100
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

`<` is stdin (core → player), `>` is stdout (player → core).
