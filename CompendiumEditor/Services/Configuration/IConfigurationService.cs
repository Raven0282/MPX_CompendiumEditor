// File: Services/Configuration/IConfigurationService.cs
using System.ComponentModel;

namespace CompendiumEditor.Services.Configuration;

public interface IConfigurationService : INotifyPropertyChanged
{
    string ThemeMode { get; set; }        // "Light" or "Dark"
    string DisplayRenderMode { get; set; } // "Classic" or "Modern"
    string LocalRepositoryPath { get; set; }
    string LastRepositoryPath { get; set; }
    void LoadSettings();
    void SaveSettings();
}