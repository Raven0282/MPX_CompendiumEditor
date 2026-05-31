// File: Views/MainWindow.axaml.cs
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CompendiumEditor.ViewModels;
using System;
using System.ComponentModel;

namespace CompendiumEditor.Views
{
    public partial class MainWindow : Window
    {
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
                vm.PropertyChanged -= OnViewModelPropertyChanged;
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedRecord))
            {
                var vm = (MainWindowViewModel)DataContext!;
                if (vm.SelectedRecord != null)
                {
                    var grid = this.FindControl<DataGrid>("RecordsDataGrid");
                    grid?.ScrollIntoView(vm.SelectedRecord, null);
                }
            }
        }
    }
}
