// File: Services/Data/ICategoryCompendiumWriter.cs
using System.Threading.Tasks;
using CompendiumEditor.Models;

namespace CompendiumEditor.Services.Data;

/// <summary>
/// Defines a specialized writer for a specific compendium category (e.g., Monster, Feat).
/// </summary>
public interface ICategoryCompendiumWriter
{
    /// <summary>
    /// Determines if this writer can handle the repository at the specified path.
    /// </summary>
    bool CanHandle(string repositoryPath);

    /// <summary>
    /// Executes the specialized save logic for this category.
    /// </summary>
    Task SaveRecordModificationAsync(string repositoryPath, CompendiumRecord record, string cleanHtmlMarkup);

    /// <summary>
    /// Appends a new record to the category data files.
    /// </summary>
    Task AppendRecordAsync(string repositoryPath, CompendiumRecord record, string cleanHtmlMarkup);
}
