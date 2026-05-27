// File: Services/Logging/DiagnosticLogger.cs
using System;
using System.IO;

namespace CompendiumEditor.Services.Logging;

public class DiagnosticLogger : IDiagnosticLogger
{
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public string CurrentLogPath => _logFilePath;

    public DiagnosticLogger()
    {
        string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        string fileName = $"session_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        _logFilePath = Path.Combine(logDir, fileName);

        // Header for a new session
        Log("--- COMPENDIUM EDITOR SESSION STARTED ---");
    }

    public void Log(string message, string category = "INFO")
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string formattedMessage = $"[{timestamp}] [{category}] {message}{Environment.NewLine}";

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, formattedMessage);
                // Also write to Debug for live viewing in IDE
                System.Diagnostics.Debug.Write(formattedMessage);
            }
            catch
            {
                // Fail silently to prevent logger from crashing the app
            }
        }
    }

    public void LogException(Exception ex, string context)
    {
        string message = $"EXCEPTION in {context}: {ex.Message}{Environment.NewLine}StackTrace: {ex.StackTrace}";
        Log(message, "ERROR");
    }
}