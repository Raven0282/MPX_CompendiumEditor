// File: Services/Configuration/ConfigurationService.cs
using System;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CompendiumEditor.Services.Configuration
{
    public partial class ConfigurationService : ObservableObject, IConfigurationService
    {
        private const string ConfigFileName = "config.json";
        private bool _isLoaded = false; // Guard flag to prevent cascading saves during initialization

        [ObservableProperty]
        private string _themeMode = "Dark";

        [ObservableProperty]
        private string _displayRenderMode = "Modern";

        [ObservableProperty]
        private string _localRepositoryPath = string.Empty;

        [ObservableProperty]
        private string _lastRepositoryPath = string.Empty;


        private string GetResolvedConfigPath()
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string localPath = Path.Combine(appDirectory, ConfigFileName);

            try
            {
                if (File.Exists(localPath))
                {
                    return localPath;
                }

                // Perform a safe directory write capability validation probe
                using (var fs = File.Create(Path.Combine(appDirectory, ".write_test"), 1, FileOptions.DeleteOnClose)) { }
                return localPath;
            }
            catch (UnauthorizedAccessException)
            {
                // Fallback securely to designated local user data stores if application space is read-only
                string fallbackDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CompendiumEditor");
                if (!Directory.Exists(fallbackDir))
                {
                    Directory.CreateDirectory(fallbackDir);
                }
                return Path.Combine(fallbackDir, ConfigFileName);
            }
        }

        public void LoadSettings()
        {
            string targetPath = GetResolvedConfigPath();
            if (!File.Exists(targetPath))
            {
                _isLoaded = true; // No file to load; mark as initialized using baseline defaults
                return;
            }

            try
            {
                string rawJson = File.ReadAllText(targetPath);
                var dataModel = JsonSerializer.Deserialize<ConfigurationModel>(rawJson);
                if (dataModel != null)
                {
                    ThemeMode = dataModel.ThemeMode;
                    DisplayRenderMode = dataModel.DisplayRenderMode;
                    LocalRepositoryPath = dataModel.LocalRepositoryPath;

                    // Notify UI components of property updates manually since fields were set directly
                    OnPropertyChanged(nameof(ThemeMode));
                    OnPropertyChanged(nameof(DisplayRenderMode));
                    OnPropertyChanged(nameof(LocalRepositoryPath));
                }
            }
            catch
            {
                // Fail gracefully to fallback defaults if file degradation is discovered
            }
            finally
            {
                _isLoaded = true; // Hydration complete, safe to allow subsequent save pipelines
            }
        }

        public void SaveSettings()
        {
            // Abort save actions if properties are shifting while inside the LoadSettings execution phase
            if (!_isLoaded) return;

            try
            {
                string targetPath = GetResolvedConfigPath();
                var options = new JsonSerializerOptions { WriteIndented = true };
                var rawJson = JsonSerializer.Serialize(new ConfigurationModel
                {
                    ThemeMode = ThemeMode,
                    DisplayRenderMode = DisplayRenderMode,
                    LocalRepositoryPath = LocalRepositoryPath
                }, options);

                // Safe write execution: WriteAllText natively handles file creation if it is missing
                File.WriteAllText(targetPath, rawJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write configuration stream securely: {ex.Message}");
            }
        }

        private class ConfigurationModel
        {
            public string ThemeMode { get; set; } = "Dark";
            public string DisplayRenderMode { get; set; } = "Modern";
            public string LocalRepositoryPath { get; set; } = string.Empty;
        }
    }
}