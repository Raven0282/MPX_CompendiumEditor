// File: Views/MainWindow.axaml.cs
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using CompendiumEditor.ViewModels;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace CompendiumEditor.Views
{
    public partial class MainWindow : Window
    {
        private bool _isUpdatingEditorDirectly;

        public MainWindow()
        {
            InitializeComponent();
        }

        public void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is MainWindowViewModel vm)
            {
                System.Diagnostics.Debug.WriteLine("[EDITOR DIAGNOSTIC] DataContext changed. Attaching handlers...");
                
                vm.PropertyChanged -= OnViewModelPropertyChanged;
                vm.PropertyChanged += OnViewModelPropertyChanged;

                // Robust lookup: Try to find the control immediately
                var editor = this.FindControl<AvaloniaEdit.TextEditor>("MarkupDocumentEditor");
                if (editor != null)
                {
                    System.Diagnostics.Debug.WriteLine("[EDITOR DIAGNOSTIC] MarkupDocumentEditor found via FindControl. Binding...");
                    BindEditorEvents(editor, vm);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[EDITOR DIAGNOSTIC] MarkupDocumentEditor not found yet. Attaching to Loaded event...");
                    this.Loaded += (s, args) =>
                    {
                        var ed = this.FindControl<AvaloniaEdit.TextEditor>("MarkupDocumentEditor");
                        if (ed != null)
                        {
                            System.Diagnostics.Debug.WriteLine("[EDITOR DIAGNOSTIC] MarkupDocumentEditor found on Window Loaded. Binding...");
                            BindEditorEvents(ed, vm);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[EDITOR DIAGNOSTIC ERROR] MarkupDocumentEditor STILL NOT FOUND after Load!");
                        }
                    };
                }
            }
        }

        private void BindEditorEvents(AvaloniaEdit.TextEditor editor, MainWindowViewModel vm)
        {
            if (editor.Document == null) return;

            // Ensure we don't double-subscribe
            editor.Document.Changed -= OnEditorTextChanged;
            editor.Document.Changed += OnEditorTextChanged;

            // Initial sync from VM to Editor
            if (!string.IsNullOrEmpty(vm.RawHtmlContent) && editor.Text != vm.RawHtmlContent)
            {
                System.Diagnostics.Debug.WriteLine($"[EDITOR DIAGNOSTIC] Performing initial sync. Length: {vm.RawHtmlContent.Length}");
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
                // Performance: Only update if changed to avoid redundant PropertyChanged cycles
                if (vm.RawHtmlContent != editor.Text)
                {
                    System.Diagnostics.Debug.WriteLine($"[EDITOR DIAGNOSTIC] UI -> VM Sync. Length: {editor.Text.Length}");
                    vm.RawHtmlContent = editor.Text;
                }
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.RawHtmlContent) && DataContext is MainWindowViewModel vm)
            {
                var editor = this.FindControl<AvaloniaEdit.TextEditor>("MarkupDocumentEditor");
                if (editor != null && !_isUpdatingEditorDirectly)
                {
                    if (editor.Text != vm.RawHtmlContent)
                    {
                        System.Diagnostics.Debug.WriteLine($"[EDITOR DIAGNOSTIC] VM -> UI Sync. Length: {vm.RawHtmlContent?.Length ?? 0}");
                        _isUpdatingEditorDirectly = true;
                        editor.Text = vm.RawHtmlContent ?? string.Empty;
                        _isUpdatingEditorDirectly = false;
                    }
                }
            }
        }
    }
}