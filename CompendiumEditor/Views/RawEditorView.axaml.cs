using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CompendiumEditor.ViewModels;
using System;
using System.ComponentModel;

namespace CompendiumEditor.Views;

public partial class RawEditorView : UserControl
{
    private bool _isUpdatingEditorDirectly;

    public RawEditorView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;

            var editor = this.FindControl<AvaloniaEdit.TextEditor>("MarkupDocumentEditor");
            if (editor != null)
            {
                BindEditorEvents(editor, vm);
            }
        }
    }

    private void BindEditorEvents(AvaloniaEdit.TextEditor editor, MainWindowViewModel vm)
    {
        if (editor.Document == null) return;

        editor.Document.Changed -= OnEditorTextChanged;
        editor.Document.Changed += OnEditorTextChanged;

        // Sync initial state
        if (!string.IsNullOrEmpty(vm.RawHtmlContent) && editor.Text != vm.RawHtmlContent)
        {
            _isUpdatingEditorDirectly = true;
            editor.Text = vm.RawHtmlContent;
            _isUpdatingEditorDirectly = false;
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingEditorDirectly) return;

        var editor = this.FindControl<AvaloniaEdit.TextEditor>("MarkupDocumentEditor");
        if (editor != null && DataContext is MainWindowViewModel vm)
        {
            if (vm.RawHtmlContent != editor.Text)
            {
                vm.RawHtmlContent = editor.Text;
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.RawHtmlContent))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    var editor = this.FindControl<AvaloniaEdit.TextEditor>("MarkupDocumentEditor");
                    if (editor != null && !_isUpdatingEditorDirectly)
                    {
                        if (editor.Text != vm.RawHtmlContent)
                        {
                            _isUpdatingEditorDirectly = true;
                            editor.Text = vm.RawHtmlContent ?? string.Empty;
                            _isUpdatingEditorDirectly = false;
                        }
                    }
                }
            });
        }
    }
}
