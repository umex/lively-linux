# Lively Linux — revival assessment and action plan

Assessed 2026-11-16 on Fedora 44, KDE Plasma 6.8.2 (Wayland), .NET SDK 10.0.113.

## Verdict

Nothing blocks a revival. The catch is scope: this repository is a UI skeleton, not a
half-finished port. Reviving it means building the wallpaper engine, which was never started.

## What is in the repository

- 3 projects, about 1,600 lines of C#/XAML. Last code commit: December 2021.
- Works: an Avalonia window with a library grid that reads `LivelyInfo.json` folders, and an About page.
- Missing: wallpaper rendering, players, settings, IPC, multi-monitor handling, import/export
  wiring, tray, autostart, tests, CI, packaging. The context menu entries are placeholders.

## What was checked

| Check | Result |
| --- | --- |
| Build of the original code (`net5.0`, Avalonia 0.10.10) on SDK 10 | Built with 0 errors but could not run: .NET 5 runtime is end-of-life and not installed. |
| Upgrade to `net10.0` + Avalonia 12.1.6 | Applied in Phase 1. Builds with 0 errors and the window starts (through XWayland). |
| License | GPL-3.0 here and upstream. No obstacle; the project must stay GPL-3.0. |
| Upstream | Moved to `lively-community/lively`, active, Windows-only (WPF core + WinUI 3). "Linux port" issue #220 is still open. The original `rocksdanister/lively-linux` repository no longer exists. |
| Compositor on this machine | KWin advertises `zwlr_layer_shell_v1` v5, `org_kde_plasma_window_management`, `ext_idle_notifier_v1`. |
| Native libraries | QtWebEngine, LayerShellQt and libvlc are installed. mpv/libmpv, gtk4-layer-shell and WebKitGTK 6.0 are in the Fedora repositories. |

Not checked at the time: whether a layer-shell surface coexists with Plasma's own desktop (icons,
widgets). That was spike A below; Phase 3 answered it (the surface covers them).

## Constraints to design around

1. **The Windows engine cannot be ported.** It relies on Win32 window tricks, WPF, WebView2,
   CefSharp and Media Foundation. What carries over: the wallpaper formats (`LivelyInfo.json`,
   `LivelyProperties.json`, the JavaScript API) and possibly the upstream `netstandard2.0`
   libraries (`Lively.Common`, `Lively.Models`, `Lively.Grpc.*`), after an audit for Win32 calls.
2. **Linux has no single wallpaper API.** `wlr-layer-shell` covers KDE, Hyprland, Sway, COSMIC
   and similar compositors. GNOME does not implement it and needs a Shell extension. X11 needs
   a desktop-type window. Each is a separate backend.
3. **On KDE, plasmashell owns the desktop surface.** A separate background surface is likely to
   either cover desktop icons or sit hidden behind Plasma's wallpaper. The native route is a
   Plasma wallpaper plugin (QML). Phase 3 confirmed the first: a `background` layer surface covers
   Plasma's desktop icons and widgets.
4. **Avalonia fits the manager UI, not the wallpaper surface.** Avalonia 12.1 has a Wayland
   backend, but no layer-shell support was found. Wallpapers should render in separate player
   processes, which is also how upstream is structured.
5. **Web wallpapers need an embedded browser.** Options: QtWebEngine (Chromium), WebKitGTK, or
   CEF. This is the largest compatibility risk, since many Lively wallpapers use WebGL and the
   audio API. Phase 4a chose QtWebEngine in a C++/Qt 6 player (see its notes).
6. **Wayland hides global state.** Pause rules (fullscreen, focused app) and audio capture need
   compositor-specific protocols, PipeWire, MPRIS and UPower rather than one API.
7. **Windows application wallpapers (`.exe`)** are out of scope.
8. **This checkout sits on an NTFS/exFAT (fuseblk) mount.** Every file shows mode `777` (owner
   root), so builds work and the apphosts (`lively`, `lively-daemon`, `lively-player-mpv`) run
   from the build folder. Permissions cannot be changed and symlinks do not work, so anything
   that depends on them (packaging) is safer on a Linux filesystem.

## Recommended direction

- Target KDE Plasma on Wayland first, then generic layer-shell compositors, then GNOME and X11.
- Keep C#/Avalonia for the manager app and a core service.
- One player program per kind of wallpaper, each on its own layer-shell surface: videos in a pure
  .NET player on libmpv (Phase 3), web pages in a C++/Qt 6 player on QtWebEngine and LayerShellQt
  (Phase 4a). This replaces the earlier idea of one Qt 6/QML player for both. A Plasma wallpaper
  plugin stays an option for keeping the desktop icons on KDE.
- Rejected for web wallpapers in Phase 4a: WebKitGTK driven from .NET (GTK 4, gtk4-layer-shell),
  CEF with offscreen rendering, and a PySide6 host. QtWebEngine was chosen for Chromium's
  compatibility with interactive websites and WebGL.

## Action plan

### Phase 0 — Spikes and decisions

- Spike A: QML + LayerShellQt window on the background layer playing a video. Record how it
  stacks against Plasma's desktop icons and widgets.
- Spike B: minimal Plasma wallpaper plugin showing a video and a `WebEngineView`.
- Decide: player technology, first desktop target, project name and relationship to upstream.

### Phase 1 — Modernize the foundation (done)

- Move to `net10.0` and Avalonia 12.1.x; update NLog, Octokit, SharpZipLib, Newtonsoft.Json.
- Remove the stale MyGet feed in `src/lively/lively/nuget.config`.
- Add `x:DataType` and compiled bindings; replace the hand-rolled `ObservableObject` with
  CommunityToolkit.Mvvm; add dependency injection (the existing TODO in `App.axaml.cs`).
- Fix the `SelectedItem` setter in `LibraryViewModel.cs`, which never stores its value.
- Move user data from `<app_folder>/userdata` to XDG directories.
- Add unit tests for models and zip helpers, a GitHub Actions build, and an accurate README.
- Found while testing and fixed: zip extraction accepted entries outside the destination
  folder, and zip creation dropped a character from entry names for folders ending in `/`.

### Phase 2 — Core architecture (done)

- Split into a core service (wallpaper state, monitor layout, settings) and the UI.
- IPC between UI, core and players: gRPC over a Unix socket (matches upstream) or D-Bus.
- Abstractions: `IWallpaperPlayer`, `IDesktopBackend`, `IDisplayManager`. Single-instance handling.

Notes on what was built (details in [docs/ipc.md](docs/ipc.md)):

- UI ↔ core uses the D-Bus session bus (`io.github.lively_linux.Core1`), with
  `Tmds.DBus.Protocol` and proxies/handlers generated by `Tmds.DBus.Generator` from the
  introspection XML. Single instance is bus-name ownership for both the daemon and the UI; a second
  UI activates the first through `org.freedesktop.Application`.
- `lively.Core` is hosted by `lively-daemon`; the Avalonia app is a pure client and starts the
  daemon when needed. Core ↔ player uses upstream's JSON-line messages on stdin/stdout. Only
  `lively-player-dummy` exists: it speaks the protocol and renders nothing.
- Displays come from Wayland `wl_output` (v4 names) through P/Invoke to libwayland-client, so any
  Wayland compositor works. Hot-plug is detected by polling every 5 s. Only the integer scale is
  known, and Wayland has no primary display: the output at (0, 0) is taken as primary, which may
  not match KDE's own primary.
- One wallpaper per display; span and duplicate arrangements are not implemented.
- Desktop backends detect the session (Wayland/X11, KDE/GNOME/wlroots-like) but do not place
  wallpapers yet.

### Phase 3 — MVP: video wallpapers on KDE Wayland (done)

- Player process with mpv; set and close a wallpaper from the library; one wallpaper per monitor.
- Replace the dummy player with the mpv player over the same protocol and launch arguments.
- Real placement in `IDesktopBackend` (layer-shell surface or Plasma wallpaper plugin).
- Start the daemon through a D-Bus activation file or a systemd user unit instead of from the UI.
- Import a video file (generate `LivelyInfo.json` and a thumbnail with ffmpeg).
- Restore wallpapers at login (XDG autostart); basic pause and resume.

Notes on what was built (details in [docs/ipc.md](docs/ipc.md)):

- Placement: `lively-player-mpv` shows the video on a `wlr-layer-shell` surface in the
  `background` layer, one per output (namespace `lively-wallpaper`, anchored to all edges,
  exclusive zone -1). On KDE this surface shares the stacking layer with plasmashell's desktop, so
  desktop icons and widgets are covered while a wallpaper runs. The user accepted this. A Plasma
  wallpaper plugin (QML) keeps the icons but ties playback to plasmashell and needs a separate
  QML player; it was rejected for now and stays an option for an icon-friendly KDE backend. Also
  rejected: a player written in C, and wrapping `mpvpaper`.
- The player is pure .NET: P/Invoke to libmpv (render API), libwayland-client, libwayland-egl and
  libEGL, with the `wl_interface` tables of the layer-shell protocol built by hand
  (`WaylandInterfaceTable.cs`, shared with `lively.Core`). Wayland, EGL and mpv run on one thread
  that sleeps in `poll()`; frames are paced by frame callbacks, with a 1 s timeout because KWin
  sends none while the screen is locked. It uses the Phase 2 protocol and launch arguments
  unchanged and exits 0 on close, stdin EOF, signals and `closed`, 1 on any failure (missing
  library included). No C toolchain is needed; libmpv (`mpv-libs`, Debian `libmpv2`) must be
  installed.
- Player choice is `IDesktopBackend.PlayerFor`: on Wayland, videos and GIFs go to
  `lively-player-mpv` (also when the compositor lacks layer-shell, so the player reports the
  reason); X11, unknown sessions and fake displays keep `lively-player-dummy`.
- Primary display: the enumerator binds `kde_output_order_v1` (first output = primary), else
  `kde_primary_output_v1`, else takes the output at (0, 0). KWin 6.8 advertises only the output
  order; the old (0, 0) rule picked the wrong output on the three-monitor test machine.
- Import: `ImportVideo` copies the file into `library/wallpapers/<slug>`, reads it with `ffprobe`,
  makes `thumbnail.jpg` with `ffmpeg` and writes `LivelyInfo.json` last; on failure the folder is
  removed. The UI has an **Import video…** button, imports its file arguments, and a second
  instance forwards them with `org.freedesktop.Application.Open`.
- Start-up: `lively-daemon --install-user` writes a D-Bus activation file and an application
  launcher that point at the build folder (re-run after building elsewhere); `--uninstall-user`
  removes them. The UI calls `StartServiceByName` and falls back to spawning the daemon from its
  own folder. The activated daemon runs as a transient systemd user unit (dbus-broker) with the
  user manager's environment and logs to the journal. The daemon keeps
  `~/.config/autostart/io.github.lively_linux.Daemon.desktop` in line with the `Startup`
  setting; no systemd unit of our own.
- Pause and volume: `SetPaused`/`GetPaused`/`PausedChanged` pause every wallpaper, not persisted.
  New setting `WallpaperVolume`, default 0 (muted), applied to running wallpapers when changed.
- Known limitations: desktop icons and widgets covered on KDE; only the integer `wl_output.scale`
  (no `wp_fractional_scale_v1`), read once when the player starts; no wallpaper on X11 (dummy
  player); GNOME has no layer-shell; other layer-shell compositors untested; no settings page yet
  (`Startup` and `WallpaperVolume` are set over D-Bus); the core does not send `cmd_reload` or
  mute yet. Not checked: whether a plasmashell restart stacks its desktop above the wallpaper.

### Phase 4a — Web wallpapers: websites (done)

- Web player for local HTML wallpapers and websites, one per monitor, with WebGL, mouse input,
  pause and volume.
- Import an HTML file or a page folder; add a website address; thumbnails for web wallpapers.

Notes on what was built (details in [docs/ipc.md](docs/ipc.md)):

- Engine: QtWebEngine (Chromium) in `lively-player-web`, a small C++/Qt 6 program built with CMake
  (`src/lively/lively.Player.Web`), the first part of the project that is not .NET. It was chosen
  over WebKitGTK from .NET, CEF offscreen rendering and a PySide6 host, for Chromium's
  compatibility with interactive websites and WebGL. `web`, `webaudio` and `url` wallpapers go to
  it on Wayland; `videostream` and `picture` stay on the dummy player.
- Surface: a full-window `WebEngineView` (QML) on a LayerShellQt `background` layer surface of its
  output, namespace `lively-wallpaper`, anchored to all edges, exclusive zone -1, keyboard
  interactivity none. Pointer input reaches the page; the keyboard never does.
- Page: WebGL and accelerated 2D canvas on, media plays without a user gesture, local pages may
  load remote and `file:` URLs, black background. Refused and logged: new windows, downloads, file,
  color and authentication dialogs, the context menu, full screen, every permission request,
  protocol handlers, client certificates and invalid server certificates. JavaScript dialogs are
  dismissed.
- Loaded: a local page sends `msg_wploaded` after its load and the first frame; a failed load
  fails the player. A `url` wallpaper reports loaded as soon as its surface is shown and retries a
  failed load every 15 s, so a wallpaper restored at login before the network is up recovers.
- Pause: `cmd_suspend` grabs the current frame into a still image, hides the view, sets its
  lifecycle state to `Frozen` and mutes it; `cmd_resume` reverses this. Volume: QtWebEngine has no
  per-page volume, so `cmd_volume` 0 mutes and any other value unmutes.
- Profiles: one QtWebEngine profile per display in `~/.cache/lively-wallpaper/WebEngine/<display>`
  (`--web-profile`), so two players never share a storage path. Cookies and local storage persist
  there. Qt Quick keeps its shader pipeline cache in `~/.cache/lively-wallpaper/lively-player-web`.
- Console and log: the page's console goes to `msg_console` and the player log, limited to a burst
  of 20 messages, then 5 per second, each cut at 4096 characters. After a load the player reports
  the WebGL context and GL renderer, for example
  `WebGL: webgl2, WebGL 2.0 (OpenGL ES 3.0 Chromium), renderer ANGLE (AMD, …)`. stdout carries only
  protocol lines: the player keeps the original descriptor for them and points fd 1 at stderr, so
  output of Chromium and the GL drivers cannot corrupt the protocol.
- Import: `ImportWeb` copies an `.html`/`.htm` file alone, or a page folder with its subfolders
  (entry `index.html`, else `index.htm`, else the only HTML file); the title comes from `<title>`.
  A folder that already is a Lively web wallpaper keeps its `LivelyInfo.json`. Symbolic links are
  not followed out of the folder. `AddUrl` adds an http(s) address as a `url` wallpaper; addresses
  with a user name or password are refused. The UI has **Import…** (videos and HTML files),
  **Import folder…** and **Add URL…**; file arguments and `Open` also take page files, folders and
  http(s) addresses.
- Thumbnails: when a web wallpaper from the library loads without a thumbnail (and the wallpapers
  are not paused), the core sends `cmd_screenshot` (JPEG, 480 pixels wide, 3 s after the load).
  On success it sets `Thumbnail` in `LivelyInfo.json` and sends the new `WallpaperUpdated` signal;
  the UI reloads that tile. One request per folder at a time; a failed one is tried again the next
  time the wallpaper starts.
- Build: `lively.Player.Web.proj` (Microsoft.Build.NoTargets) runs CMake during `dotnet build`, and
  the daemon, UI and tests copy the binary next to them. Without cmake, a C++ compiler or the Qt 6
  WebEngine and LayerShellQt development packages, the build warns (`LIVELYWEB01`) and continues;
  web wallpapers then fail with a message naming the packages. `-p:BuildWebPlayer=false` skips the
  step. CI compiles the player in a Fedora container; the Ubuntu job takes the warning path
  (Ubuntu 24.04 ships LayerShellQt for Qt 5 only).
- Known limitations:
  - One Chromium instance per monitor, a few hundred MB each.
  - Mute and unmute only, no volume level.
  - Mouse only; the wallpaper never takes keyboard focus, so pages that need a login cannot be
    signed into.
  - Desktop icons and widgets are still covered on KDE, as in Phase 3.
  - Lively wallpapers with `LivelyProperties.json` load but cannot be customized, and audio
    visualizers get no audio data, until Phase 4b.
  - `mediump` precision: on Mesa/AMD, QtWebEngine's ANGLE uses the OpenGL ES backend and the
    driver really runs `mediump` at reduced (16-bit) precision (`getShaderPrecisionFormat` reports
    10 bits). Pages that rely on desktop Chrome treating `mediump` as `highp` can render wrongly;
    changing the page's shader to `highp` fixes such a page. Tried without success: `QT_OPENGL=desktop`,
    `QSG_RHI_BACKEND=vulkan`, `AMD_DEBUG=nofp16`, driconf `radeonsi_mediump=false`,
    `--use-angle=gl`, `gl-egl` and `vulkan` (vulkan gives different artifacts), and
    `--disable-angle-features=enablePrecisionQualifiers`.
- Not verified: real pointer clicks reaching a page, removal of an output while a page runs,
  compositors without layer-shell, fractional scaling, and an audible audio level.

### Phase 4b — Web wallpapers: Lively wallpapers

- `LivelyProperties.json` customization panel.
- JavaScript API bridge (`livelyPropertyListener` and the other upstream entry points).
- Audio data for visualizers through PipeWire; now-playing through MPRIS.
- `videostream` wallpapers.

### Phase 4.5 — Span a web wallpaper across all monitors

Decided on 2026-11-17; not part of Phase 4a.

- One page rendered at the combined size of all monitors, with a window per monitor showing its
  slice, as an extra launch mode of `lively-player-web`.
- Mouse positions mapped back to the page.
- A "span" arrangement in the core layout and a way to choose it in the UI.
- Web wallpapers only.

### Phase 5 — Library and app features

- Import and export Lively `.zip` packages, delete, preview, drag and drop.
- Settings page, tray icon, localization.

### Phase 6 — Performance rules

- Pause on fullscreen, maximized or focused apps (KWin protocol first), on battery (UPower),
  and when idle or locked.

### Phase 7 — More desktops

- `lively-player-mpv` on other `wlr-layer-shell` compositors (Hyprland, Sway, COSMIC, niri):
  test and fix; GNOME Shell extension; X11.
- Option: a Plasma wallpaper plugin as an icon-friendly KDE backend (could also ship in Phase 8).

### Phase 8 — Packaging and release

- Flatpak or AppImage plus RPM/AUR; the Plasma plugin ships separately from any sandbox.
- Release CI, user documentation, and a note on upstream issue #220.
