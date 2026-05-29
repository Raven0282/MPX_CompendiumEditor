// File: Services/Data/ArmorCompendiumWriter.cs
using CompendiumEditor.Exceptions;
using CompendiumEditor.Services.Logging;
using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CompendiumEditor.Services.Data;

/// <summary>
/// Specialized writer for the "Armor" category. 
/// Handles 7-column listing matrices (ID, Name, Type, Level, Cost, Rarity, SourceBook)
/// and flat-text search indices. Supports both mundane and magic armor markup.
/// </summary>
public class ArmorCompendiumWriter : BaseCompendiumWriter
{
    public ArmorCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath) => 
        repositoryPath.Contains("\\armor\\", StringComparison.OrdinalIgnoreCase) || 
        repositoryPath.Contains("/armor/", StringComparison.OrdinalIgnoreCase);

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // 1. Extract Name (Mundane uses player, Magic uses mihead)
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);
        string name = nameMatch.Success ? nameMatch.Groups[1].Value : "Unknown Armor";
        
        // Strip nested tags from name (like milevel span in magic items)
        name = Regex.Replace(name, @"<span[^>]*>.*?</span>", "", RegexOptions.IgnoreCase);

        // 2. Extract Metadata
        string type = "Cloth";
        if (html.Contains("Leather", StringComparison.OrdinalIgnoreCase)) type = "Leather";
        else if (html.Contains("Hide", StringComparison.OrdinalIgnoreCase)) type = "Hide";
        else if (html.Contains("Chainmail", StringComparison.OrdinalIgnoreCase)) type = "Chainmail";
        else if (html.Contains("Scale", StringComparison.OrdinalIgnoreCase)) type = "Scale";
        else if (html.Contains("Plate", StringComparison.OrdinalIgnoreCase)) type = "Plate";
        else if (html.Contains("Shield", StringComparison.OrdinalIgnoreCase)) type = "Shield";

        var costMatch = Regex.Match(html, @"<b>Cost</b>:\s*(?<val>.*?)<br>", RegexOptions.IgnoreCase);
        var levelMatch = Regex.Match(html, @"<span class=milevel[^>]*>(?<val>.*?)</span>", RegexOptions.IgnoreCase);
        
        string level = "";
        if (levelMatch.Success)
        {
            // Extract numeric level from "Level 2+ Uncommon"
            var lvlNumMatch = Regex.Match(levelMatch.Groups["val"].Value, @"Level (?<num>\d+)");
            level = lvlNumMatch.Success ? lvlNumMatch.Groups["num"].Value : levelMatch.Groups["val"].Value;
        }

        string rarity = html.Contains("Uncommon", StringComparison.OrdinalIgnoreCase) ? "Uncommon" :
                        html.Contains("Rare", StringComparison.OrdinalIgnoreCase) ? "Rare" : "Mundane";

        // 3. Source
        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new ExtractedMetadata
        {
            Name = Regex.Replace(name, @"<[^>]+>", "").Trim(),
            Tier = type, // Mapped to 'Type' in listing
            Prerequisite = level, // Mapped to 'Level' in listing
            BenefitText = costMatch.Success ? Regex.Replace(costMatch.Groups["val"].Value, @"<[^>]+>", "").Trim() : "", // Cost
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

        // REVERSION: Armor Index MUST be the flat dense text block.
        string indexText = StripHtml(htmlMarkup);
        root[id] = JsonValue.Create(indexText);

        string newJson = root.ToJsonString(GetModernOptions());
        newJson = FormatForLegacy(newJson, rawText);
        await SplicedWriteAsync(path, rawText, newJson, '{', '}');
    }
}
