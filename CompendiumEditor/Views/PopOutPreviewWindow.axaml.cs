using Avalonia;
using Avalonia.Controls;
using CompendiumEditor.ViewModels;
using System;

namespace CompendiumEditor.Views;

public partial class PopOutPreviewWindow : Window
{
    public PopOutPreviewWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsPreviewPoppedOut = false;
        }
        base.OnClosing(e);
    }
}
