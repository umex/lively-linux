using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace lively.Views.Pages
{
    public partial class LibraryPage : UserControl
    {
        public LibraryPage()
        {
            InitializeComponent();
            if (!Design.IsDesignMode)
            {
                DataContext = App.Services.GetRequiredService<LibraryViewModel>();
            }
        }
    }
}
