// File: ViewModels/MainWindowViewModel.cs
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CompendiumEditor.Exceptions;
using CompendiumEditor.Models;
using CompendiumEditor.Services.Configuration;
using CompendiumEditor.Services.Data;
using CompendiumEditor.Services.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace CompendiumEditor.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly IConfigurationService _configurationService;
        private readonly ICompendiumExtractor _compendiumExtractor;
        private readonly ICompendiumWriter _compendiumWriter;
        private readonly IPreviewStylingService _stylingService;
        private readonly IDiagnosticLogger _logger;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayRepositoryPath))]
        private string? _repositoryPath;

        public string DisplayRepositoryPath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(RepositoryPath)) return string.Empty;
                var segments = RepositoryPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length <= 4) return RepositoryPath;
                return "... " + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar, segments.Skip(segments.Length - 4));
            }
        }

        [ObservableProperty]
        private bool _isDataLoaded;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RecordCount))]
        private ObservableCollection<CompendiumRecord> _records = new();

        public int RecordCount => Records.Count;

        private List<CompendiumRecord> _allRecords = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private CompendiumRecord? _selectedRecord;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private string? _rawHtmlContent;

        [ObservableProperty]
        private string _injectedStyles = string.Empty;

        private string? _originalHtmlContent;

        [ObservableProperty]
        private bool _isClassicViewEnabled = true;

        [ObservableProperty]
        private bool _isDarkMode = true;

        [ObservableProperty]
        private bool _isAppendMode;

        [ObservableProperty]
        private bool _showValidationErrorAlert;

        [ObservableProperty]
        private string? _validationErrorTitle;

        [ObservableProperty]
        private string? _validationErrorMessage;

        public MainWindowViewModel(
            IConfigurationService configurationService,
            ICompendiumExtractor compendiumExtractor,
            ICompendiumWriter compendiumWriter,
            IPreviewStylingService stylingService,
            IDiagnosticLogger logger)
        {
            _configurationService = configurationService;
            _compendiumExtractor = compendiumExtractor;
            _compendiumWriter = compendiumWriter;
            _stylingService = stylingService;
            _logger = logger;

            RepositoryPath = _configurationService.LastRepositoryPath;
            IsDarkMode = _configurationService.ThemeMode != "Light";
            
            UpdateActiveStyles();

            if (!string.IsNullOrWhiteSpace(RepositoryPath))
            {
                _ = InitializeRepositoryAsync(RepositoryPath);
            }
        }

        public bool HasActiveSelection => SelectedRecord != null;

        public bool CanAppend => IsDataLoaded && HasActiveSelection;

        public bool IsDirty => SelectedRecord != null && RawHtmlContent != _originalHtmlContent;

        partial void OnSearchQueryChanged(string value)
        {
            ApplyFiltering();
        }

        partial void OnIsDarkModeChanged(bool value)
        {
            _configurationService.ThemeMode = value ? "Dark" : "Light";
            _configurationService.SaveSettings();
            
            if (Application.Current != null)
            {
                Application.Current.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
            }
            UpdateActiveStyles();
        }

        private void UpdateActiveStyles()
        {
            InjectedStyles = _stylingService.GetActiveStyles(IsDarkMode, RepositoryPath);
        }

        private void ApplyFiltering()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                Records = new ObservableCollection<CompendiumRecord>(_allRecords);
            }
            else
            {
                var filtered = _allRecords.Where(r => 
                    r.Id.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || 
                    r.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                    (r.SourceBook != null && r.SourceBook.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                ).ToList();
                Records = new ObservableCollection<CompendiumRecord>(filtered);
            }
        }

        partial void OnSelectedRecordChanged(CompendiumRecord? value)
        {
            if (value != null)
            {
                _logger.Log($"Selection changed to ID: {value.Id}", "VIEWMODEL");
                _ = LoadRecordDataAsync(value);
            }
            else
            {
                RawHtmlContent = string.Empty;
                _originalHtmlContent = string.Empty;
            }

            OnPropertyChanged(nameof(HasActiveSelection));
            OnPropertyChanged(nameof(CanAppend));
            OnPropertyChanged(nameof(IsDirty));
            AppendNewRecordCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        public async Task SelectRepositoryFolderAsync()
        {
            _logger.Log("SelectRepositoryFolderAsync triggered", "VIEWMODEL");
            
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var result = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Locate 4e Compendium Working Directory",
                    AllowMultiple = false
                });

                if (result != null && result.Count > 0)
                {
                    string path = result[0].Path.LocalPath;
                    _logger.Log($"Selected path: {path}", "VIEWMODEL");
                    RepositoryPath = path;
                    _configurationService.LastRepositoryPath = path;
                    _configurationService.SaveSettings();
                    UpdateActiveStyles();
                    await InitializeRepositoryAsync(path);
                }
            }
            else
            {
                _logger.Log("Failed to access ApplicationLifetime or MainWindow for FolderPicker", "ERROR");
            }
        }

        /// <summary>
        /// Explicitly commits working HTML markup edits back down to the local file asset.
        /// </summary>
        [RelayCommand(CanExecute = nameof(IsDirty))]
        public async Task SaveChangesAsync()
        {
            if (SelectedRecord == null || string.IsNullOrWhiteSpace(RepositoryPath)) return;

            _logger.Log($"SaveChangesAsync triggered for ID: {SelectedRecord.Id}", "VIEWMODEL");
            try
            {
                await _compendiumWriter.SaveRecordModificationAsync(RepositoryPath, SelectedRecord, RawHtmlContent ?? string.Empty);

                _logger.Log($"SaveChangesAsync completed for ID: {SelectedRecord.Id}", "VIEWMODEL SUCCESS");
                
                // Reset dirty state after successful save
                _originalHtmlContent = RawHtmlContent;
                OnPropertyChanged(nameof(IsDirty));
                SaveChangesCommand.NotifyCanExecuteChanged();
                
                ShowValidationErrorAlert = false;
            }
            catch (CompendiumValidationException valEx)
            {
                _logger.Log($"Validation Failure: {valEx.Message}", "VIEWMODEL");
                ValidationErrorTitle = "Compendium Formatting Policy Invalidation Rule Triggered";
                ValidationErrorMessage = valEx.Message;
                ShowValidationErrorAlert = true;
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "VIEWMODEL:SAVE");
                ValidationErrorTitle = "System Subsystem Operation Failure Alert";
                ValidationErrorMessage = $"An unexpected failure prevented saving records to disk securely. Details: {ex.Message}";
                ShowValidationErrorAlert = true;
            }
        }

        [RelayCommand(CanExecute = nameof(CanAppend))]
        public async Task AppendNewRecordAsync()
        {
            if (string.IsNullOrWhiteSpace(RepositoryPath)) return;

            string folderName = Path.GetFileName(RepositoryPath.TrimEnd(Path.DirectorySeparatorChar));
            string prefix = folderName.ToLowerInvariant();

            // Find max ID for this prefix
            int maxId = 0;
            foreach (var record in _allRecords)
            {
                if (record.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string suffix = record.Id.Substring(prefix.Length);
                    if (int.TryParse(suffix, out int val) && val > maxId)
                    {
                        maxId = val;
                    }
                }
            }

            string suggestedId = $"{prefix}{maxId + 1}";

            // Extract unique sourcebooks for the dropdown
            var sources = _allRecords
                .Select(r => r.SourceBook)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var dialog = new Views.NewRecordDialog
                {
                    GeneratedId = suggestedId,
                    ExistingSources = new ObservableCollection<string>(sources)
                };

                var result = await dialog.ShowDialog<Views.NewRecordResult>(desktop.MainWindow);

                if (result != null)
                {
                    _logger.Log($"Starting Append Mode for ID: {result.Id}", "VIEWMODEL");
                    
                    var newRecord = new CompendiumRecord
                    {
                        Id = result.Id,
                        Name = result.Name,
                        SourceBook = result.Source,
                        Tier = "Heroic",
                        Prerequisite = "None",
                        BenefitText = ""
                    };

                    // If we have a selection, use its HTML as a template
                    string templateHtml = RawHtmlContent ?? "<div>New entry content</div>";

                    IsAppendMode = true;
                    _allRecords.Insert(0, newRecord);
                    ApplyFiltering();
                    SelectedRecord = newRecord;
                    RawHtmlContent = templateHtml;
                    _originalHtmlContent = null; // Forces dirty state
                    
                    OnPropertyChanged(nameof(IsDirty));
                    SaveChangesCommand.NotifyCanExecuteChanged();
                    CommitNewRecordCommand.NotifyCanExecuteChanged();
                }
            }
        }

        [RelayCommand(CanExecute = nameof(IsAppendMode))]
        public async Task CommitNewRecordAsync()
        {
            if (SelectedRecord == null || string.IsNullOrWhiteSpace(RepositoryPath)) return;

            _logger.Log($"CommitNewRecordAsync triggered for ID: {SelectedRecord.Id}", "VIEWMODEL");
            try
            {
                await _compendiumWriter.AppendRecordAsync(RepositoryPath, SelectedRecord, RawHtmlContent ?? string.Empty);

                _logger.Log($"CommitNewRecordAsync completed for ID: {SelectedRecord.Id}", "VIEWMODEL SUCCESS");
                
                IsAppendMode = false;
                _originalHtmlContent = RawHtmlContent;
                
                OnPropertyChanged(nameof(IsDirty));
                SaveChangesCommand.NotifyCanExecuteChanged();
                CommitNewRecordCommand.NotifyCanExecuteChanged();
                
                ShowValidationErrorAlert = false;
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "VIEWMODEL:COMMIT");
                ValidationErrorTitle = "Append Operation Failure";
                ValidationErrorMessage = $"Failed to append new record: {ex.Message}";
                ShowValidationErrorAlert = true;
            }
        }

        [RelayCommand]
        public void DismissAlertAndKeepEditing()
        {
            ShowValidationErrorAlert = false;
        }

        [RelayCommand]
        public async Task RollbackToBackupAsync()
        {
            if (SelectedRecord == null || string.IsNullOrWhiteSpace(RepositoryPath)) return;

            try
            {
                _logger.Log($"Rolling back to latest backup for ID: {SelectedRecord.Id}", "VIEWMODEL");
                // We need to restore the data shard file
                await _compendiumWriter.RestoreLatestBackupAsync(RepositoryPath, "data*.js"); // Note: Writer handles finding the right one
                
                ShowValidationErrorAlert = false;
                // Reload the data
                await LoadRecordDataAsync(SelectedRecord);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "VIEWMODEL:ROLLBACK");
            }
        }

        private async Task InitializeRepositoryAsync(string path)
        {
            _logger.Log($"Starting repository initialization for path: {path}", "DIAGNOSTIC STAGE 1");
            IsDataLoaded = false;
            Records.Clear();

            try
            {
                string listingPath = Path.Combine(path, "_listing.js");
                if (!File.Exists(listingPath))
                {
                    _logger.Log($"Repository missing _listing.js at {path}", "ERROR");
                    return;
                }

                string rawContent = await File.ReadAllTextAsync(listingPath);
                _logger.Log($"Read _listing.js successfully. File length: {rawContent.Length} characters.", "DIAGNOSTIC STAGE 1 SUCCESS");

                _logger.Log("Sending raw file content canvas to CompendiumExtractor...", "DIAGNOSTIC STAGE 2");
                var matrix = _compendiumExtractor.ExtractArrayPayload(rawContent);

                _logger.Log($"Extractor safely unpacked json matrix. Root elements detected: {matrix.Count}", "DIAGNOSTIC STAGE 2 SUCCESS");

                var stagingList = new List<CompendiumRecord>();

                foreach (var item in matrix)
                {
                    // Ensure the row contains at least 2 elements (we need at least ID and Name to create a valid record)
                    if (item is JsonArray row && row.Count >= 2)
                    {
                        stagingList.Add(new CompendiumRecord
                        {
                            Id = row[0]?.ToString() ?? "unknown",
                            Name = row[1]?.ToString() ?? "Untitled",
                            SourceBook = row[row.Count -1]?.ToString() ?? "Unknown",

                            // For the middle fields, you can use conditional checks
                            // to avoid the "Index out of range" error:
                            Tier = row.Count >= 3 ? row[2]?.ToString() ?? "Heroic" : "Heroic",
                            Prerequisite = row.Count >= 4 ? row[3]?.ToString() ?? "None" : "None",
                            BenefitText = row.Count >= 5 ? row[4]?.ToString() ?? "No details" : "No details"
                        });
                    }
                }

                _logger.Log($"Staging pipeline finished. Attempting main thread dispatch marshaling for {stagingList.Count} items...", "DIAGNOSTIC STAGE 3 COMPLETE");

                foreach (var record in stagingList)
                {
                    Records.Add(record);
                }

                _allRecords = stagingList;
                IsDataLoaded = true;
                OnPropertyChanged(nameof(RecordCount));
                _logger.Log($"Collection population completed. ObservableCollection count: {Records.Count}. IsDataLoaded set to: {IsDataLoaded}", "DIAGNOSTIC STAGE 4 SUCCESS");
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "VIEWMODEL:INIT");
            }
        }

        private async Task LoadRecordDataAsync(CompendiumRecord record)
        {
            if (string.IsNullOrWhiteSpace(RepositoryPath)) return;

            string? loadedHtml = await Task.Run(async () =>
            {
                try
                {
                    _logger.Log($"Scanning shards for ID {record.Id}...", "PERFORMANCE");
                    string[] shards = Directory.GetFiles(RepositoryPath, "data*.js");

                    foreach (string file in shards)
                    {
                        string text = await File.ReadAllTextAsync(file);
                        if (text.Contains($"\"{record.Id}\":") || text.Contains($"'{record.Id}':") || text.Contains($"\"{record.Id}\"") || text.Contains($"'{record.Id}'"))
                        {
                            var root = _compendiumExtractor.ExtractObjectPayload(text);
                            if (root[record.Id] is JsonValue val)
                            {
                                return val.ToString();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogException(ex, "VIEWMODEL:LOAD_RECORD");
                }
                return null;
            });

            if (loadedHtml != null)
            {
                _originalHtmlContent = loadedHtml;
                RawHtmlContent = loadedHtml;
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }
}