using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using lively.Core.Library;
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
        public LibraryViewModel(ICoreClient? core, IReadOnlyList<string> wallpaperScanFolders, Action<Action>? post = null)
        {
            this.core = core;
            this.post = post ?? (a => a());
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

        #endregion //status

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

        private string TitleOf(string folder) =>
            LibraryItems.FirstOrDefault(x => x.LivelyInfoFolderPath == folder)?.Title ?? folder;

        private static string Describe(Exception e) => e is DBusErrorReplyException dbus ? dbus.ErrorMessage : e.Message;

        #endregion //core

        #region helpers

        public void AddWallpaper(string folderPath)
        {
            var libItem = LibraryScanner.ScanFolder(folderPath);
            if (libItem != null)
            {
                LibraryItems.Insert(LibraryScanner.FindInsertIndex(LibraryItems, libItem.Title), libItem);
            }
        }

        public void SortLibraryItem(LibraryModel item)
        {
            LibraryItems.Remove(item);
            LibraryItems.Insert(LibraryScanner.FindInsertIndex(LibraryItems, item.Title), item);
        }

        #endregion //helpers
    }
}
