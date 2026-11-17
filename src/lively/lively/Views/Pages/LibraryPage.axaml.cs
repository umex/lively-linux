using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace lively.Views.Pages
{
    public partial class LibraryPage : UserControl
    {
        private static readonly string[] VideoPatterns = ["*.mp4", "*.webm", "*.mkv", "*.mov", "*.m4v", "*.avi"];
        private static readonly string[] WebPatterns = ["*.html", "*.htm"];

        private static readonly FilePickerFileType WallpaperFiles = new("Wallpapers")
        {
            Patterns = [.. VideoPatterns, .. WebPatterns],
            MimeTypes = ["video/*", "text/html"],
        };

        private static readonly FilePickerFileType VideoFiles = new("Videos")
        {
            Patterns = VideoPatterns,
            MimeTypes = ["video/*"],
        };

        private static readonly FilePickerFileType WebPageFiles = new("Web pages")
        {
            Patterns = WebPatterns,
            MimeTypes = ["text/html"],
        };

        private LibraryViewModel? subscribed;

        public LibraryPage()
        {
            InitializeComponent();
            if (!Design.IsDesignMode)
            {
                DataContext = App.Services.GetRequiredService<LibraryViewModel>();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Subscribe(DataContext as LibraryViewModel);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            Subscribe(null);
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (subscribed != null)
            {
                Subscribe(DataContext as LibraryViewModel);
            }
        }

        // The view model is a singleton that outlives the page: unsubscribe when the page leaves the tree.
        private void Subscribe(LibraryViewModel? vm)
        {
            if (subscribed != null)
            {
                subscribed.UrlAdded -= OnUrlAdded;
            }
            subscribed = vm;
            if (vm != null)
            {
                vm.UrlAdded += OnUrlAdded;
            }
        }

        private void OnUrlAdded(object? sender, EventArgs e) => AddUrlButton.Flyout?.Hide();

        private void OnAddUrlFlyoutOpened(object? sender, EventArgs e)
        {
            UrlBox.Focus();
            UrlBox.SelectAll();
        }

        private void OnUrlBoxKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is LibraryViewModel vm && vm.AddUrlCommand.CanExecute(null))
            {
                e.Handled = true;
                // AddUrlAsync reports its own errors in the status line.
                vm.AddUrlCommand.Execute(null);
            }
        }

        private async void OnImportFilesClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not LibraryViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                return;
            }
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import wallpapers",
                AllowMultiple = true,
                FileTypeFilter = [WallpaperFiles, VideoFiles, WebPageFiles, FilePickerFileTypes.All],
            });
            var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            // ImportFilesAsync reports its own errors in the status line.
            await vm.ImportFilesAsync(paths);
        }

        private async void OnImportFolderClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not LibraryViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                return;
            }
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Import web page folder",
                AllowMultiple = true,
            });
            var paths = folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            await vm.ImportFilesAsync(paths);
        }
    }
}
