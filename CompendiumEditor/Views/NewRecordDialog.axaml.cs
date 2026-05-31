using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CompendiumEditor.Views;

public partial class NewRecordDialog : Window, INotifyPropertyChanged
{
    private string? _generatedId;
    public string? GeneratedId 
    { 
        get => _generatedId; 
        set { _generatedId = value; OnPropertyChanged(); } 
    }

    private string? _recordName;
    public string? RecordName 
    { 
        get => _recordName; 
        set { _recordName = value; OnPropertyChanged(); } 
    }

    private string? _sourceBook = "Custom";
    public string? SourceBook 
    { 
        get => _sourceBook; 
        set { _sourceBook = value; OnPropertyChanged(); } 
    }

    private IEnumerable<string> _existingSources = new List<string>();
    public IEnumerable<string> ExistingSources
    {
        get => _existingSources;
        set { _existingSources = value; OnPropertyChanged(); }
    }

    public NewRecordDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void Start_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RecordName)) return;
        
        Close(new NewRecordResult 
        { 
            Id = GeneratedId ?? throw new InvalidOperationException("ID not set"), 
            Name = RecordName, 
            Source = SourceBook ?? "Custom" 
        });
    }
}

public class NewRecordResult
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Source { get; set; }
}
