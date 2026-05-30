// File: Services/Data/ICompendiumWriter.cs
using System.Threading.Tasks;
using CompendiumEditor.Models;

namespace CompendiumEditor.Services.Data;

public interface ICompendiumWriter
{
    Task SaveRecordModificationAsync(string repositoryPath, CompendiumRecord record, string cleanHtmlMarkup);
    Task AppendRecordAsync(string repositoryPath, CompendiumRecord record, string cleanHtmlMarkup);
    Task RestoreLatestBackupAsync(string repositoryPath, string targetFileName); // <-- INSERTED LINE
}