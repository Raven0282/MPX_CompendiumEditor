// File: Exceptions/CompendiumValidationException.cs
using System;

namespace CompendiumEditor.Exceptions;

/// <summary>
/// Thrown when HTML fragment modification editing fails validation checks during save transactions.
/// </summary>
public class CompendiumValidationException : Exception
{
    public string CorruptedFilePath { get; }
    public string RecordId { get; }

    public CompendiumValidationException(string message, string corruptedFilePath, string recordId)
        : base(message)
    {
        CorruptedFilePath = corruptedFilePath;
        RecordId = recordId;
    }
}