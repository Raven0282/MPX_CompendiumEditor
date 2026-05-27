// File: Services/Logging/IDiagnosticLogger.cs
using System;

namespace CompendiumEditor.Services.Logging;

/// <summary>
/// Interface for a persistent diagnostic logging service that records system events 
/// and data transformation states to local disk assets.
/// </summary>
public interface IDiagnosticLogger
{
    /// <summary>
    /// Writes a message to the active log file with a timestamp and category tag.
    /// </summary>
    void Log(string message, string category = "INFO");

    /// <summary>
    /// Records an exception with full stack trace details and context.
    /// </summary>
    void LogException(Exception ex, string context);

    /// <summary>
    /// Returns the absolute path to the current log file.
    /// </summary>
    string CurrentLogPath { get; }
}