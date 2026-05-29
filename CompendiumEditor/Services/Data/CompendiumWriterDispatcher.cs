// File: Services/Data/CompendiumWriterDispatcher.cs
using CompendiumEditor.Models;
using CompendiumEditor.Services.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CompendiumEditor.Services.Data;

/// <summary>
/// A routing service that dispatches save operations to the most appropriate 
/// specialized writer based on the repository path.
/// </summary>
public class CompendiumWriterDispatcher : ICompendiumWriter
{
    private readonly IEnumerable<ICategoryCompendiumWriter> _specializedWriters;
    private readonly GeneralCompendiumWriter _fallbackWriter;
    private readonly IDiagnosticLogger _logger;

    public CompendiumWriterDispatcher(
        IEnumerable<ICategoryCompendiumWriter> specializedWriters, 
        GeneralCompendiumWriter fallbackWriter,
        IDiagnosticLogger logger)
    {
        // Sort specialized writers (if any) to ensure priority, though currently only one per type
        _specializedWriters = specializedWriters;
        _fallbackWriter = fallbackWriter;
        _logger = logger;
    }

    public async Task SaveRecordModificationAsync(string repositoryPath, CompendiumRecord record, string cleanHtmlMarkup)
    {
        // Find a specialized writer that claims it can handle this path
        var writer = _specializedWriters.FirstOrDefault(w => w.CanHandle(repositoryPath));

        if (writer != null)
        {
            _logger.Log($"Routing Save to specialized writer: {writer.GetType().Name}", "DISPATCHER");
            await writer.SaveRecordModificationAsync(repositoryPath, record, cleanHtmlMarkup);
        }
        else
        {
            _logger.Log("No specialized writer found. Routing to General fallback.", "DISPATCHER");
            await _fallbackWriter.SaveRecordModificationAsync(repositoryPath, record, cleanHtmlMarkup);
        }
    }

    /// <summary>
    /// Backup restoration is currently folder-based and generic across all categories.
    /// </summary>
    public async Task RestoreLatestBackupAsync(string repositoryPath, string targetFileName)
    {
        string backupDir = Path.Combine(repositoryPath, ".backup");
        if (!Directory.Exists(backupDir))
        {
            _logger.Log($"No backup directory found at {backupDir}", "DISPATCHER:BACKUP WARNING");
            return;
        }

        string simpleName = Path.GetFileNameWithoutExtension(targetFileName);
        string fileExtension = Path.GetExtension(targetFileName);

        var directoryInfo = new DirectoryInfo(backupDir);
        var files = directoryInfo.GetFiles($"{simpleName}_*{fileExtension}");

        Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

        if (files.Length > 0)
        {
            string cleanOriginalDestination = Path.Combine(repositoryPath, targetFileName);
            _logger.Log($"Restoring latest backup: {files[0].Name} -> {targetFileName}", "DISPATCHER:BACKUP");
            File.Copy(files[0].FullName, cleanOriginalDestination, true);
        }
        else
        {
            _logger.Log($"No backup files matching {simpleName} found in {backupDir}", "DISPATCHER:BACKUP WARNING");
        }

        await Task.CompletedTask;
    }
}
