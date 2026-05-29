// File: Services/Data/ClassCompendiumWriter.cs
using CompendiumEditor.Exceptions;
using CompendiumEditor.Services.Logging;
using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CompendiumEditor.Services.Data;

/// <summary>
/// Specialized writer for the "Class" category. 
/// Handles 6-column listing matrices (ID, Name, RoleName, PowerSourceText, KeyAbilities, SourceBook)
/// and flat-text search indices.
/// </summary>
public class ClassCompendiumWriter : BaseCompendiumWriter
{
    public ClassCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath) => 
        repositoryPath.Contains("\\class\\", StringComparison.OrdinalIgnoreCase) || 
        repositoryPath.Contains("/class/", StringComparison.OrdinalIgnoreCase);

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // 1. Extract Name
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);
        string name = nameMatch.Success ? nameMatch.Groups[1].Value : "Unknown Class";

        // 2. Extract Metadata from blockquote
        var roleMatch = Regex.Match(html, @"<b>Role:\s*</b>(?<val>.*?)<br>", RegexOptions.IgnoreCase);
        var sourceMatch = Regex.Match(html, @"<b>Power Source:\s*</b>(?<val>.*?)<br>", RegexOptions.IgnoreCase);
        var abilitiesMatch = Regex.Match(html, @"<b>Key Abilities:\s*</b>(?<val>.*?)<br>", RegexOptions.IgnoreCase);

        // 3. Published In
        var pubMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new ExtractedMetadata
        {
            Name = Regex.Replace(name, @"<[^>]+>", "").Trim(),
            Tier = roleMatch.Success ? Regex.Replace(roleMatch.Groups["val"].Value, @"<[^>]+>", "").Trim() : "Unknown Role",
            Prerequisite = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups["val"].Value, @"<[^>]+>", "").Trim() : "Unknown Source",
            BenefitText = abilitiesMatch.Success ? Regex.Replace(abilitiesMatch.Groups["val"].Value, @"<[^>]+>", "").Trim() : "Unknown Abilities",
            SourceBook = pubMatch.Success ? Regex.Replace(pubMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source"
        };
    }

    protected override async Task UpdateIndexFileAsync(string repositoryPath, string id, ExtractedMetadata meta, string htmlMarkup)
    {
        string path = Path.Combine(repositoryPath, "_index.js");
        if (!File.Exists(path)) return;

        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonNode root = _extractor.ExtractObjectPayload(rawText);

        // REVERSION: Class Index MUST be the flat dense text block.
        string indexText = StripHtml(htmlMarkup);
        root[id] = JsonValue.Create(indexText);

        string newJson = root.ToJsonString(GetModernOptions());
        newJson = FormatForLegacy(newJson, rawText);
        await SplicedWriteAsync(path, rawText, newJson, '{', '}');
    }
}
