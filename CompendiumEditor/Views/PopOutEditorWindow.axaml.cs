using Avalonia.Controls;
using CompendiumEditor.ViewModels;

namespace CompendiumEditor.Views;

public partial class PopOutEditorWindow : Window
{
    public PopOutEditorWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsEditorPoppedOut = false;
        }
        base.OnClosing(e);
    }
}
