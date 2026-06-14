using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CompendiumEditor.Views
{
    public partial class SplashScreen : Window
    {
        public SplashScreen()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
