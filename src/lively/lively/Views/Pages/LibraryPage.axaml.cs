using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace lively.Views.Pages
{
    public partial class LibraryPage : UserControl
    {
        private static readonly FilePickerFileType VideoFiles = new("Videos")
        {
            Patterns = ["*.mp4", "*.webm", "*.mkv", "*.mov", "*.m4v", "*.avi"],
            MimeTypes = ["video/*"],
        };

        public LibraryPage()
        {
            InitializeComponent();
            if (!Design.IsDesignMode)
            {
                DataContext = App.Services.GetRequiredService<LibraryViewModel>();
            }
        }

        private async void OnImportVideoClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not LibraryViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                return;
            }
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import video",
                AllowMultiple = true,
                FileTypeFilter = [VideoFiles, FilePickerFileTypes.All],
            });
            var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            // ImportVideosAsync reports its own errors in the status line.
            await vm.ImportVideosAsync(paths);
        }
    }
}
