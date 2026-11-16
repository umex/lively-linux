using Avalonia.Controls;
using Avalonia.Interactivity;
using lively.Common.Helpers;

namespace lively.Views.Pages
{
    public partial class AboutPage : UserControl
    {
        public AboutPage()
        {
            InitializeComponent();
        }

        private void OnSrcButtonClick(object? sender, RoutedEventArgs e)
        {
            LinkHandler.OpenBrowser("https://github.com/rocksdanister/lively");
        }
    }
}
