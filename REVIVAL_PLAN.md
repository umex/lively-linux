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

Not checked: whether a layer-shell surface coexists with Plasma's own desktop (icons, widgets).
That is spike A below.

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
   Plasma wallpaper plugin (QML).
4. **Avalonia fits the manager UI, not the wallpaper surface.** Avalonia 12.1 has a Wayland
   backend, but no layer-shell support was found. Wallpapers should render in separate player
   processes, which is also how upstream is structured.
5. **Web wallpapers need an embedded browser.** Options: QtWebEngine (Chromium), WebKitGTK, or
   CEF. This is the largest compatibility risk, since many Lively wallpapers use WebGL and the
   audio API.
6. **Wayland hides global state.** Pause rules (fullscreen, focused app) and audio capture need
   compositor-specific protocols, PipeWire, MPRIS and UPower rather than one API.
7. **Windows application wallpapers (`.exe`)** are out of scope.
8. **This checkout sits on an NTFS/exFAT (fuseblk) mount.** Managed builds work, but Unix
   permissions and symlinks do not. Native player builds are safer on a Linux filesystem.

## Recommended direction

- Target KDE Plasma on Wayland first, then generic layer-shell compositors, then GNOME and X11.
- Keep C#/Avalonia for the manager app and a core service.
- Write one small Qt 6/QML player (video through mpv, web through QtWebEngine). The same QML can
  run on a layer-shell surface and inside a Plasma wallpaper plugin.
- Alternative: an all-.NET player with GTK 4, gtk4-layer-shell and WebKitGTK. One language, but
  weaker web compatibility and a library preload workaround.

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

### Phase 2 — Core architecture

- Split into a core service (wallpaper state, monitor layout, settings) and the UI.
- IPC between UI, core and players: gRPC over a Unix socket (matches upstream) or D-Bus.
- Abstractions: `IWallpaperPlayer`, `IDesktopBackend`, `IDisplayManager`. Single-instance handling.

### Phase 3 — MVP: video wallpapers on KDE Wayland

- Player process with mpv; set and close a wallpaper from the library; one wallpaper per monitor.
- Import a video file (generate `LivelyInfo.json` and a thumbnail with ffmpeg).
- Restore wallpapers at login (XDG autostart); basic pause and resume.

### Phase 4 — Web wallpapers

- Web player; `LivelyProperties.json` customization panel; JavaScript API bridge.
- Audio data through PipeWire, now-playing through MPRIS; URL and stream wallpapers.

### Phase 5 — Library and app features

- Import and export Lively `.zip` packages, delete, preview, drag and drop.
- Settings page, tray icon, localization.

### Phase 6 — Performance rules

- Pause on fullscreen, maximized or focused apps (KWin protocol first), on battery (UPower),
  and when idle or locked.

### Phase 7 — More desktops

- Generic layer-shell backend (Hyprland, Sway, COSMIC, niri), GNOME Shell extension, X11.

### Phase 8 — Packaging and release

- Flatpak or AppImage plus RPM/AUR; the Plasma plugin ships separately from any sandbox.
- Release CI, user documentation, and a note on upstream issue #220.
