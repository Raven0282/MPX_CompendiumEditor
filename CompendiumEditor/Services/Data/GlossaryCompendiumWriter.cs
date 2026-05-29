// File: Services/Data/GlossaryCompendiumWriter.cs
using CompendiumEditor.Exceptions;
using CompendiumEditor.Services.Logging;
using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CompendiumEditor.Services.Data;

/// <summary>
/// Specialized writer for the "Glossary" category. 
/// Handles 5-column listing matrices (ID, Name, Category, Type, SourceBook)
/// and flat-text search indices.
/// </summary>
public class GlossaryCompendiumWriter : BaseCompendiumWriter
{
    public GlossaryCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath) => 
        repositoryPath.Contains("\\glossary\\", StringComparison.OrdinalIgnoreCase) || 
        repositoryPath.Contains("/glossary/", StringComparison.OrdinalIgnoreCase);

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // 1. Extract Name
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);
        string name = nameMatch.Success ? nameMatch.Groups[1].Value : "Unknown Entry";

        // 2. Extract Category/Type
        // Glossary files often don't have explicit Category/Type markers in the HTML.
        // We look for common hints or default to "Rules" / "General".
        string category = "Rules";
        if (html.Contains("Monsters", StringComparison.OrdinalIgnoreCase)) category = "Monsters";

        string type = "Keyword";
        if (html.Contains("Type", StringComparison.OrdinalIgnoreCase)) type = "Type";

        // 3. Source
        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new ExtractedMetadata
        {
            Name = Regex.Replace(name, @"<[^>]+>", "").Trim(),
            Tier = category, // Mapped to 'Category' in listing
            Prerequisite = type, // Mapped to 'Type' in listing
            BenefitText = string.Empty,
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source"
        };
    }

    protected override async Task UpdateIndexFileAsync(string repositoryPath, string id, ExtractedMetadata meta, string htmlMarkup)
    {
        string path = Path.Combine(repositoryPath, "_index.js");
        if (!File.Exists(path)) return;

        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonNode root = _extractor.ExtractObjectPayload(rawText);

        // REVERSION: Glossary Index MUST be the flat dense text block.
        string indexText = StripHtml(htmlMarkup);
        root[id] = JsonValue.Create(indexText);

        string newJson = root.ToJsonString(GetModernOptions());
        newJson = FormatForLegacy(newJson, rawText);
        await SplicedWriteAsync(path, rawText, newJson, '{', '}');
    }
}
