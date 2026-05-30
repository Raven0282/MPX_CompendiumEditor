// File: Services/Data/CompanionCompendiumWriter.cs
using CompendiumEditor.Exceptions;
using CompendiumEditor.Services.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CompendiumEditor.Services.Data;

/// <summary>
/// Specialized writer for the "Companion" category. 
/// Handles 6-column listing matrices (ID, Name, Type, Size, CreatureType, SourceBook)
/// and flat-text search indices. Supports Familiar and Companion/Summoned markup.
/// </summary>
public class CompanionCompendiumWriter : BaseCompendiumWriter
{
    public CompanionCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath) => 
        repositoryPath.Contains("\\companion\\", StringComparison.OrdinalIgnoreCase) || 
        repositoryPath.Contains("/companion/", StringComparison.OrdinalIgnoreCase);

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // 1. Extract Name
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)<", RegexOptions.IgnoreCase);
        string name = nameMatch.Success ? nameMatch.Groups[1].Value : "Unknown Companion";

        // 2. Extract Metadata
        // Format: <span class=type>Small elemental humanoid (construct)</span>
        var typeSpanMatch = Regex.Match(html, @"<span class=type>(.*?)</span>", RegexOptions.IgnoreCase);
        string fullType = typeSpanMatch.Success ? typeSpanMatch.Groups[1].Value : string.Empty;

        string size = "Medium";
        if (fullType.StartsWith("Tiny", StringComparison.OrdinalIgnoreCase)) size = "Tiny";
        else if (fullType.StartsWith("Small", StringComparison.OrdinalIgnoreCase)) size = "Small";
        else if (fullType.StartsWith("Large", StringComparison.OrdinalIgnoreCase)) size = "Large";
        else if (fullType.StartsWith("Huge", StringComparison.OrdinalIgnoreCase)) size = "Huge";
        else if (fullType.StartsWith("Gargantuan", StringComparison.OrdinalIgnoreCase)) size = "Gargantuan";

        string creatureType = Regex.Replace(fullType, @"^(Tiny|Small|Medium|Large|Huge|Gargantuan)\s*", "", RegexOptions.IgnoreCase);

        // Category / Type
        // Format: <span class=level>Elemental Companion</span> or <span class=level>Familiar</span>
        var levelSpanMatch = Regex.Match(html, @"<span class=level>(.*?)</span>", RegexOptions.IgnoreCase);
        string type = levelSpanMatch.Success ? levelSpanMatch.Groups[1].Value : "Companion";

        // 3. Source
        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new CompanionMetadata
        {
            Name = Regex.Replace(name, @"<[^>]+>", "").Trim(),
            Tier = type, // Mapped to 'Type' in listing
            Prerequisite = size, // Mapped to 'Size' in listing
            BenefitText = creatureType, // Mapped to 'CreatureType' in listing
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source",
            Size = size,
            CreatureType = creatureType
        };
    }

    protected override async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta, bool isAppend)
    {
        string path = Path.Combine(repositoryPath, "_listing.js");
        if (!File.Exists(path)) return;

        var cMeta = (CompanionMetadata)meta;
        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonArray dataMatrix = _extractor.ExtractArrayPayload(rawText);

        // Companion Matrix: ["ID", "Name", "Type", "Size", "CreatureType", "SourceBook"]
        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count >= 6 && row[0]?.ToString() == id)
            {
                row[1] = JsonValue.Create(cMeta.Name);
                row[2] = JsonValue.Create(cMeta.Tier); // Type
                row[3] = JsonValue.Create(cMeta.Size);
                row[4] = JsonValue.Create(cMeta.CreatureType);
                row[5] = JsonValue.Create(cMeta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            _logger.Log($"Appending new companion row to _listing.js matrix for ID: {id}", "WRITER:COMPANION");
            var newRow = new JsonArray
            {
                JsonValue.Create(id),
                JsonValue.Create(cMeta.Name),
                JsonValue.Create(cMeta.Tier),
                JsonValue.Create(cMeta.Size),
                JsonValue.Create(cMeta.CreatureType),
                JsonValue.Create(cMeta.SourceBook)
            };
            dataMatrix.Add(newRow);
            found = true;
        }

        if (!found) _logger.Log($"ID {id} not found in Companion listing matrix!", "WRITER:COMPANION_LISTING WARNING");

        // Standard Spliced Write
        ReadOnlySpan<char> sourceSpan = rawText.AsSpan();
        int finalCloseParenthesis = sourceSpan.LastIndexOf(')');
        int matrixEndIndex = -1;
        for (int i = finalCloseParenthesis - 1; i >= 0; i--) { if (sourceSpan[i] == ']') { matrixEndIndex = i; break; } }
        int bracketDepth = 0, matrixStartIndex = -1;
        for (int i = matrixEndIndex; i >= 0; i--) { if (sourceSpan[i] == ']') bracketDepth++; if (sourceSpan[i] == '[') bracketDepth--; if (bracketDepth == 0) { matrixStartIndex = i; break; } }

        string header = rawText.Substring(0, matrixStartIndex);
        string footer = rawText.Substring(matrixEndIndex + 1);
        string newMatrixJson = dataMatrix.ToJsonString(GetModernOptions());

        await File.WriteAllTextAsync(path, header + newMatrixJson + footer);
    }

    protected override async Task UpdateIndexFileAsync(string repositoryPath, string id, ExtractedMetadata meta, string htmlMarkup)
    {
        string path = Path.Combine(repositoryPath, "_index.js");
        if (!File.Exists(path)) return;

        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonNode root = _extractor.ExtractObjectPayload(rawText);

        // REVERSION: Companion Index MUST be the dense stat block.
        string indexText = StripHtml(htmlMarkup);
        root[id] = JsonValue.Create(indexText);

        string newJson = root.ToJsonString(GetModernOptions());
        newJson = FormatForLegacy(newJson, rawText);
        await SplicedWriteAsync(path, rawText, newJson, '{', '}');
    }

    private class CompanionMetadata : ExtractedMetadata
    {
        public required string Size { get; set; }
        public required string CreatureType { get; set; }
    }
}
