using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lively.Core.Library;
using lively.Helpers;
using lively.Ipc;
using lively.Models;
using Tmds.DBus.Protocol;

namespace lively
{
    public partial class LibraryViewModel : ObservableObject
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly List<string> wallpaperScanFolders;
        private readonly ICoreClient? core;
        private readonly Action<Action> post;
        // Display id -> wallpaper folder, as reported by the core.
        private readonly Dictionary<string, string> running = new(StringComparer.Ordinal);

        public LibraryViewModel() : this(LibraryScanner.DefaultScanFolders)
        {
        }

        /// <param name="wallpaperScanFolders">Parent folders whose subdirectories are wallpapers.</param>
        public LibraryViewModel(IReadOnlyList<string> wallpaperScanFolders) : this(null, wallpaperScanFolders)
        {
        }

        /// <param name="core">Client of lively-daemon, null when the core is not reachable.</param>
        /// <param name="wallpaperScanFolders">Parent folders whose subdirectories are wallpapers.</param>
        /// <param name="post">Runs core events on the UI thread; inline when null.</param>
        /// <param name="volumeDebounce">
        /// How long <see cref="WallpaperVolume"/> must stay unchanged before it is written to the core;
        /// <see cref="DefaultVolumeDebounce"/> when null.
        /// </param>
        public LibraryViewModel(ICoreClient? core, IReadOnlyList<string> wallpaperScanFolders, Action<Action>? post = null,
            TimeSpan? volumeDebounce = null)
        {
            this.core = core;
            this.post = post ?? (a => a());
            this.volumeDebounce = volumeDebounce ?? DefaultVolumeDebounce;
            this.wallpaperScanFolders = wallpaperScanFolders.ToList();
            foreach (var item in LibraryScanner.ScanFolders(this.wallpaperScanFolders))
            {
                LibraryItems.Add(item);
            }

            if (core != null)
            {
                core.WallpaperChanged += (_, e) => this.post(() => OnWallpaperChanged(e));
                core.Error += (_, e) => this.post(() => StatusMessage = $"Error on {e.DisplayId}: {e.Message}");
                core.DisplaysChanged += (_, _) => this.post(() => _ = ReloadDisplaysAsync());
                core.PausedChanged += (_, paused) => this.post(() => IsPaused = paused);
                core.WallpaperUpdated += (_, e) => this.post(() => OnWallpaperUpdated(e.FolderPath));
            }
        }

        #region collections

        private ObservableCollection<LibraryModel> _libraryItems = new ObservableCollection<LibraryModel>();
        public ObservableCollection<LibraryModel> LibraryItems
        {
            get { return _libraryItems; }
            set { SetProperty(ref _libraryItems, value); }
        }

        private LibraryModel? _selectedItem;
        public LibraryModel? SelectedItem
        {
            get { return _selectedItem; }
            set { SetProperty(ref _selectedItem, value); }
        }

        public ObservableCollection<DisplayMonitor> Displays { get; } = new ObservableCollection<DisplayMonitor>();

        private DisplayMonitor? _selectedDisplay;
        /// <summary>Target of Set/Close; null means the primary display.</summary>
        public DisplayMonitor? SelectedDisplay
        {
            get { return _selectedDisplay; }
            set { SetProperty(ref _selectedDisplay, value); }
        }

        /// <summary>Wallpaper folder per display id, as last reported by the core.</summary>
        public IReadOnlyDictionary<string, string> RunningWallpapers => running;

        #endregion //collections

        #region status

        private string? _statusMessage;
        public string? StatusMessage
        {
            get { return _statusMessage; }
            set { SetProperty(ref _statusMessage, value); }
        }

        private bool _isCoreAvailable;
        /// <summary>True once lively-daemon answered; wallpapers can only be set then.</summary>
        public bool IsCoreAvailable
        {
            get { return _isCoreAvailable; }
            private set { SetProperty(ref _isCoreAvailable, value); }
        }

        private bool _isPaused;
        /// <summary>True while the core has the wallpapers paused, as last reported by the core.</summary>
        public bool IsPaused
        {
            get { return _isPaused; }
            private set
            {
                if (SetProperty(ref _isPaused, value))
                {
                    OnPropertyChanged(nameof(PauseButtonText));
                }
            }
        }

        /// <summary>Label of the pause button: what pressing it does.</summary>
        public string PauseButtonText => IsPaused ? "Resume" : "Pause";

        #endregion //status

        #region volume

        /// <summary>Default wait after the last <see cref="WallpaperVolume"/> change before it is written.</summary>
        public static readonly TimeSpan DefaultVolumeDebounce = TimeSpan.FromMilliseconds(300);

        /// <summary>What <see cref="ToggleMuteCommand"/> restores when the volume was 0 from the start.</summary>
        public const int DefaultUnmuteVolume = 50;

        private readonly TimeSpan volumeDebounce;
        // One settings write at a time: each reads the settings, changes the volume and writes them back.
        private readonly SemaphoreSlim volumeWriteLock = new(1, 1);
        private CancellationTokenSource? volumeDelay;
        // Set while the volume is taken from the core, so that it is not written back.
        private bool loadingVolume;
        private int lastAudibleVolume = DefaultUnmuteVolume;

        private int _wallpaperVolume;
        /// <summary>
        /// The WallpaperVolume setting, 0-100; 0 mutes. Read from the core when connecting; a change is written
        /// to the core once it stayed unchanged for the debounce time, keeping the other settings.
        /// </summary>
        public int WallpaperVolume
        {
            get { return _wallpaperVolume; }
            set
            {
                if (!SetProperty(ref _wallpaperVolume, Math.Clamp(value, 0, 100)))
                {
                    return;
                }
                if (_wallpaperVolume > 0)
                {
                    lastAudibleVolume = _wallpaperVolume;
                }
                OnPropertyChanged(nameof(IsMuted));
                OnPropertyChanged(nameof(MuteButtonText));
                if (!loadingVolume)
                {
                    ScheduleVolumeWrite(volumeDebounce);
                }
            }
        }

        /// <summary>True when <see cref="WallpaperVolume"/> is 0.</summary>
        public bool IsMuted => WallpaperVolume == 0;

        /// <summary>Label of the mute button: what pressing it does.</summary>
        public string MuteButtonText => IsMuted ? "Unmute" : "Mute";

        /// <summary>The settings write in progress or waiting for the debounce; completed when there is none.</summary>
        public Task VolumeWriteTask { get; private set; } = Task.CompletedTask;

        /// <summary>Mutes, or restores the last volume above 0 (<see cref="DefaultUnmuteVolume"/> when none). Written at once.</summary>
        [RelayCommand]
        private Task ToggleMute()
        {
            loadingVolume = true;
            try
            {
                WallpaperVolume = IsMuted ? lastAudibleVolume : 0;
            }
            finally
            {
                loadingVolume = false;
            }
            ScheduleVolumeWrite(TimeSpan.Zero);
            return VolumeWriteTask;
        }

        private void ScheduleVolumeWrite(TimeSpan delay)
        {
            if (core == null || !IsCoreAvailable)
            {
                return;
            }
            volumeDelay?.Cancel();
            volumeDelay?.Dispose();
            volumeDelay = new CancellationTokenSource();
            VolumeWriteTask = WriteVolumeAsync(delay, volumeDelay.Token);
        }

        private async Task WriteVolumeAsync(TimeSpan delay, CancellationToken ct)
        {
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct);
                }
                await volumeWriteLock.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return; // a newer change writes
            }
            try
            {
                // The value at write time: a change made while waiting for the lock is included.
                var volume = WallpaperVolume;
                var settings = await core!.GetSettingsAsync();
                if (settings.WallpaperVolume == volume)
                {
                    return;
                }
                settings.WallpaperVolume = volume;
                await core.SetSettingsAsync(settings);
            }
            catch (Exception e)
            {
                Logger.Error("Changing the wallpaper volume failed: " + Describe(e));
                StatusMessage = "Could not change the volume: " + Describe(e);
            }
            finally
            {
                volumeWriteLock.Release();
            }
        }

        private void SetVolumeFromCore(int volume)
        {
            loadingVolume = true;
            try
            {
                WallpaperVolume = volume;
            }
            finally
            {
                loadingVolume = false;
            }
        }

        #endregion //volume

        #region core

        /// <summary>Subscribes to the core and loads displays and running wallpapers.</summary>
        public async Task InitializeAsync()
        {
            if (core == null)
            {
                SetCoreUnavailable("lively-daemon is not reachable (no D-Bus session bus); wallpapers cannot be set.");
                return;
            }
            try
            {
                await core.ConnectAsync();
                await ReloadDisplaysAsync();
                running.Clear();
                foreach (var w in await core.GetWallpapersAsync())
                {
                    running[w.DisplayId] = w.FolderPath;
                }
                OnPropertyChanged(nameof(RunningWallpapers));
                IsPaused = await core.GetPausedAsync();
                SetVolumeFromCore((await core.GetSettingsAsync()).WallpaperVolume);
                IsCoreAvailable = true;
                StatusMessage = running.Count == 0
                    ? "Ready. Right-click a wallpaper and choose Set Wallpaper."
                    : "Running: " + string.Join(", ", running.Select(x => $"\"{TitleOf(x.Value)}\" on {x.Key}"));
            }
            catch (Exception e)
            {
                Logger.Error("Connecting to lively-daemon failed: " + e);
                SetCoreUnavailable("lively-daemon is not reachable: " + Describe(e));
            }
        }

        public void SetCoreUnavailable(string message)
        {
            IsCoreAvailable = false;
            StatusMessage = message;
        }

        [RelayCommand]
        private async Task SetWallpaper(LibraryModel? item)
        {
            item ??= SelectedItem;
            if (item == null)
            {
                StatusMessage = "Select a wallpaper first.";
                return;
            }
            if (core == null)
            {
                StatusMessage = "lively-daemon is not reachable; wallpapers cannot be set.";
                return;
            }
            var displayId = SelectedDisplay?.Id ?? string.Empty;
            var target = displayId.Length == 0 ? "the primary display" : displayId;
            StatusMessage = $"Setting \"{item.Title}\" on {target}...";
            try
            {
                await core.SetWallpaperAsync(item.LivelyInfoFolderPath, displayId);
                StatusMessage = $"\"{item.Title}\" is running on {target}.";
            }
            catch (Exception e)
            {
                Logger.Error($"SetWallpaper {item.LivelyInfoFolderPath} on {target} failed: {e.Message}");
                StatusMessage = $"Could not set \"{item.Title}\": {Describe(e)}";
            }
        }

        [RelayCommand]
        private async Task CloseWallpaper()
        {
            if (core == null)
            {
                StatusMessage = "lively-daemon is not reachable.";
                return;
            }
            var displayId = SelectedDisplay?.Id ?? string.Empty;
            try
            {
                await core.CloseWallpaperAsync(displayId);
                StatusMessage = $"Wallpaper closed on {(displayId.Length == 0 ? "the primary display" : displayId)}.";
            }
            catch (Exception e)
            {
                StatusMessage = "Could not close the wallpaper: " + Describe(e);
            }
        }

        [RelayCommand]
        private async Task CloseAllWallpapers()
        {
            if (core == null)
            {
                StatusMessage = "lively-daemon is not reachable.";
                return;
            }
            try
            {
                await core.CloseAllWallpapersAsync();
                StatusMessage = "All wallpapers closed.";
            }
            catch (Exception e)
            {
                StatusMessage = "Could not close the wallpapers: " + Describe(e);
            }
        }

        [RelayCommand]
        private async Task TogglePause()
        {
            if (core == null)
            {
                StatusMessage = "lively-daemon is not reachable.";
                return;
            }
            var pause = !IsPaused;
            try
            {
                // IsPaused follows the PausedChanged signal.
                await core.SetPausedAsync(pause);
                StatusMessage = pause ? "Wallpapers paused." : "Wallpapers resumed.";
            }
            catch (Exception e)
            {
                StatusMessage = (pause ? "Could not pause the wallpapers: " : "Could not resume the wallpapers: ") + Describe(e);
            }
        }

        /// <summary>
        /// Imports the targets one after another through the core, adds each new wallpaper to the library and
        /// selects it. Folders and .html/.htm files are imported as web pages, http(s) addresses are added as
        /// URL wallpapers, anything else is imported as a video (the core reports what is wrong with it).
        /// A failed target is reported and the next one is tried.
        /// </summary>
        public async Task ImportFilesAsync(IReadOnlyList<string> paths)
        {
            if (paths.Count == 0)
            {
                return;
            }
            if (core == null)
            {
                StatusMessage = "lively-daemon is not reachable.";
                return;
            }
            var imported = 0;
            string? lastError = null;
            foreach (var path in paths)
            {
                var isAddress = FileUris.IsWebAddress(path);
                // Addresses are not shown or logged: they may hold a password, which the core refuses.
                var name = isAddress ? "the web address" : Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                StatusMessage = $"Importing {name}…";
                try
                {
                    var folder = isAddress ? await core.AddUrlAsync(path)
                        : IsWebPage(path) ? await core.ImportWebAsync(path)
                        : await core.ImportVideoAsync(path);
                    AddImported(folder);
                    imported++;
                }
                catch (Exception e)
                {
                    Logger.Error($"Import of {(isAddress ? "a web address" : path)} failed: {Describe(e)}");
                    lastError = $"Could not import {name}: {Describe(e)}";
                }
            }
            if (lastError == null)
            {
                StatusMessage = $"Imported {imported} wallpaper(s).";
            }
            else
            {
                StatusMessage = imported == 0 ? lastError : $"Imported {imported} of {paths.Count} wallpaper(s). {lastError}";
            }
        }

        private string _urlText = string.Empty;
        /// <summary>Web address typed for <see cref="AddUrlCommand"/>; cleared once it was added.</summary>
        public string UrlText
        {
            get { return _urlText; }
            set { SetProperty(ref _urlText, value ?? string.Empty); }
        }

        /// <summary>Raised after <see cref="AddUrlCommand"/> added a wallpaper; the view closes its input then.</summary>
        public event EventHandler? UrlAdded;

        /// <summary>Adds <see cref="UrlText"/> as a URL wallpaper, selects it and clears the text. Errors go to the status line.</summary>
        [RelayCommand]
        private async Task AddUrl()
        {
            if (core == null)
            {
                StatusMessage = "lively-daemon is not reachable.";
                return;
            }
            var url = UrlText.Trim();
            if (url.Length == 0)
            {
                StatusMessage = "Enter a web address starting with http:// or https://.";
                return;
            }
            StatusMessage = "Adding the web address…";
            try
            {
                var item = AddImported(await core.AddUrlAsync(url));
                UrlText = string.Empty;
                StatusMessage = $"Added \"{item.Title}\".";
                UrlAdded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception e)
            {
                // The core's message names the address without a password; the raw text is not logged.
                Logger.Error("AddUrl failed: " + Describe(e));
                StatusMessage = "Could not add the web address: " + Describe(e);
            }
        }

        /// <summary>Adds the new wallpaper folder to the library and selects it.</summary>
        private LibraryModel AddImported(string folder)
        {
            var item = AddWallpaper(folder)
                ?? throw new InvalidOperationException($"the imported folder {folder} is not readable.");
            SelectedItem = item;
            return item;
        }

        private static bool IsWebPage(string path)
        {
            var extension = Path.GetExtension(path);
            return Directory.Exists(path)
                || extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
        }

        private async Task ReloadDisplaysAsync()
        {
            if (core == null)
            {
                return;
            }
            IReadOnlyList<DisplayMonitor> displays;
            try
            {
                displays = await core.GetDisplaysAsync();
            }
            catch (Exception e)
            {
                Logger.Error("GetDisplays failed: " + e.Message);
                StatusMessage = "Could not read the displays: " + Describe(e);
                return;
            }
            var previous = SelectedDisplay?.Id;
            Displays.Clear();
            foreach (var d in displays)
            {
                Displays.Add(d);
            }
            SelectedDisplay = Displays.FirstOrDefault(x => x.Id == previous)
                ?? Displays.FirstOrDefault(x => x.IsPrimary)
                ?? Displays.FirstOrDefault();
        }

        private void OnWallpaperChanged(CoreWallpaperChangedEventArgs e)
        {
            // Also reports changes made by others (restore at daemon start, busctl, another client).
            if (e.FolderPath == null)
            {
                running.Remove(e.DisplayId);
                StatusMessage = $"Wallpaper closed on {e.DisplayId}.";
            }
            else
            {
                running[e.DisplayId] = e.FolderPath;
                StatusMessage = $"\"{TitleOf(e.FolderPath)}\" is running on {e.DisplayId}.";
            }
            OnPropertyChanged(nameof(RunningWallpapers));
        }

        /// <summary>Re-reads a wallpaper folder whose LivelyInfo.json changed and replaces its tile in place.</summary>
        private void OnWallpaperUpdated(string folder)
        {
            var key = Path.TrimEndingDirectorySeparator(folder);
            var index = -1;
            for (var i = 0; i < LibraryItems.Count; i++)
            {
                if (Path.TrimEndingDirectorySeparator(LibraryItems[i].LivelyInfoFolderPath) == key)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                return;
            }
            var item = LibraryScanner.ScanFolder(folder);
            if (item == null)
            {
                Logger.Warn($"Updated wallpaper {folder} is no longer readable; keeping its tile.");
                return;
            }
            var wasSelected = ReferenceEquals(SelectedItem, LibraryItems[index]);
            // A new item (with a new ImagePath) makes the tile load the new picture.
            LibraryItems[index] = item;
            if (wasSelected)
            {
                SelectedItem = item;
            }
        }

        private string TitleOf(string folder) =>
            LibraryItems.FirstOrDefault(x => x.LivelyInfoFolderPath == folder)?.Title ?? folder;

        private static string Describe(Exception e) => e is DBusErrorReplyException dbus ? dbus.ErrorMessage : e.Message;

        #endregion //core

        #region helpers

        /// <returns>The added item, null when the folder is not a readable wallpaper.</returns>
        public LibraryModel? AddWallpaper(string folderPath)
        {
            var libItem = LibraryScanner.ScanFolder(folderPath);
            if (libItem != null)
            {
                LibraryItems.Insert(LibraryScanner.FindInsertIndex(LibraryItems, libItem.Title), libItem);
            }
            return libItem;
        }

        public void SortLibraryItem(LibraryModel item)
        {
            LibraryItems.Remove(item);
            LibraryItems.Insert(LibraryScanner.FindInsertIndex(LibraryItems, item.Title), item);
        }

        #endregion //helpers
    }
}
