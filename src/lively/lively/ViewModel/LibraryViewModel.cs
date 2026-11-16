using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using lively.Core.Library;
using lively.Models;

namespace lively
{
    public class LibraryViewModel : ObservableObject
    {
        private readonly List<string> wallpaperScanFolders;

        public LibraryViewModel() : this(LibraryScanner.DefaultScanFolders)
        {
        }

        /// <param name="wallpaperScanFolders">Parent folders whose subdirectories are wallpapers.</param>
        public LibraryViewModel(IReadOnlyList<string> wallpaperScanFolders)
        {
            this.wallpaperScanFolders = wallpaperScanFolders.ToList();
            foreach (var item in LibraryScanner.ScanFolders(this.wallpaperScanFolders))
            {
                LibraryItems.Add(item);
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

        #endregion //collections

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
