// File: Models/CompendiumRecord.cs
namespace CompendiumEditor.Models;

/// <summary>
/// Represents a high-performance memory model mapping metadata grid arrays.
/// </summary>
public class CompendiumRecord
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Tier { get; init; }
    public required string Prerequisite { get; init; }
    public required string BenefitText { get; init; }
    public required string SourceBook { get; init; }
}
