// File: Services/Data/IPreviewStylingService.cs
namespace CompendiumEditor.Services.Data;

public interface IPreviewStylingService
{
    string GetActiveStyles(bool isDarkMode, string? repositoryPath);
}
