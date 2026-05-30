// File: Services/Data/ItemCompendiumWriter.cs
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
/// Specialized writer for the Item category, handling complex scaled item tables and 8-column listing matrices.
/// </summary>
public class ItemCompendiumWriter : BaseCompendiumWriter
{
    public ItemCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath)
    {
        return repositoryPath.Contains($"{Path.DirectorySeparatorChar}item", StringComparison.OrdinalIgnoreCase) || 
               repositoryPath.EndsWith("item", StringComparison.OrdinalIgnoreCase);
    }

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // Name: <h1 class=mihead>Awe of the Dragon's Altar<span class=milevel...
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)<span", RegexOptions.IgnoreCase);
        if (!nameMatch.Success) nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);

        if (!nameMatch.Success)
        {
            throw new CompendiumValidationException("Could not extract Item Name from <h1> tag.", activeTargetShardPath, id);
        }

        string cleanName = Regex.Replace(nameMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

        // Level and Rarity: <span class=milevel id=headerlevel>Level 2+ Uncommon</span>
        string level = "1";
        string rarity = "Common";
        var levelSpanMatch = Regex.Match(html, @"id=headerlevel>(.*?)</span>", RegexOptions.IgnoreCase);
        if (levelSpanMatch.Success)
        {
            string spanContent = levelSpanMatch.Groups[1].Value;
            var levelNumMatch = Regex.Match(spanContent, @"Level ([\d\+]+)", RegexOptions.IgnoreCase);
            if (levelNumMatch.Success) level = levelNumMatch.Groups[1].Value;

            if (spanContent.Contains("Uncommon", StringComparison.OrdinalIgnoreCase)) rarity = "Uncommon";
            else if (spanContent.Contains("Rare", StringComparison.OrdinalIgnoreCase)) rarity = "Rare";
            else if (spanContent.Contains("Legendary", StringComparison.OrdinalIgnoreCase)) rarity = "Legendary";
        }

        // Category, Type, Cost: 
        // Simple: <p class=mistat><b>Grandmaster Training</b>        1,800 gp</p>
        // Table-based (Scaled): Use the first entry or the header
        string category = "Adventuring Gear";
        string type = "Item";
        string cost = "0 gp";

        var statMatch = Regex.Match(html, @"<p class=mistat><b>(.*?)</b>(.*?)</p>", RegexOptions.IgnoreCase);
        if (statMatch.Success)
        {
            type = statMatch.Groups[1].Value.Trim();
            string afterBold = statMatch.Groups[2].Value.Trim();
            if (afterBold.Contains("gp")) cost = afterBold;
            
            // Heuristic for Category: If it's a Boon, Reward, or Training, it's an Alternative Reward
            if (type.Contains("Boon") || type.Contains("Training") || type.Contains("Gift")) category = "Alternative Reward";
            else if (type.Contains("Alchemical")) category = "Alchemical Item";
            else if (type.Contains("Armor")) category = "Armor";
            else if (type.Contains("Weapon")) category = "Weapon";
            else if (type.Contains("Slot")) category = "Magic Item";
        }

        // Scaled Cost Check (Table)
        if (cost == "0 gp" || level.Contains("+"))
        {
            var tableMatch = Regex.Match(html, @"<td class=mic3>([\d,]+ gp)</td>", RegexOptions.IgnoreCase);
            if (tableMatch.Success)
            {
                cost = tableMatch.Groups[1].Value;
                if (level.EndsWith("+") && !cost.EndsWith("+ gp")) cost = cost.Replace(" gp", "+ gp");
            }
        }

        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new ItemMetadata
        {
            Name = cleanName,
            Tier = level, // Used for Level column
            Prerequisite = category, // Used for Category column
            BenefitText = type, // Used for Type column
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source",
            ItemCategory = category,
            ItemType = type,
            Cost = cost,
            Rarity = rarity
        };
    }

    protected override async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta, bool isAppend)
    {
        string path = Path.Combine(repositoryPath, "_listing.js");
        if (!File.Exists(path)) return;

        var iMeta = (ItemMetadata)meta;
        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonArray dataMatrix = _extractor.ExtractArrayPayload(rawText);

        // Item Matrix: ["ID", "Name", "Category", "Type", "Level", "Cost", "Rarity", "SourceBook"]
        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count >= 8 && row[0]?.ToString() == id)
            {
                row[1] = JsonValue.Create(iMeta.Name);
                row[2] = JsonValue.Create(iMeta.ItemCategory);
                row[3] = JsonValue.Create(iMeta.ItemType);
                row[4] = JsonValue.Create(iMeta.Tier); // Level
                row[5] = JsonValue.Create(iMeta.Cost);
                row[6] = JsonValue.Create(iMeta.Rarity);
                row[7] = JsonValue.Create(iMeta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            _logger.Log($"Appending new item row to _listing.js matrix for ID: {id}", "WRITER:ITEM");
            var newRow = new JsonArray
            {
                JsonValue.Create(id),
                JsonValue.Create(iMeta.Name),
                JsonValue.Create(iMeta.ItemCategory),
                JsonValue.Create(iMeta.ItemType),
                JsonValue.Create(iMeta.Tier),
                JsonValue.Create(iMeta.Cost),
                JsonValue.Create(iMeta.Rarity),
                JsonValue.Create(iMeta.SourceBook)
            };
            dataMatrix.Add(newRow);
            found = true;
        }

        if (found)
        {
            ReadOnlySpan<char> sourceSpan = rawText.AsSpan();
            int finalCloseParenthesis = sourceSpan.LastIndexOf(')');
            int matrixEndIndex = -1;
            for (int i = finalCloseParenthesis - 1; i >= 0; i--) { if (sourceSpan[i] == ']') { matrixEndIndex = i; break; } }
            int bracketDepth = 0, matrixStartIndex = -1;
            for (int i = matrixEndIndex; i >= 0; i--) { if (sourceSpan[i] == ']') bracketDepth++; if (sourceSpan[i] == '[') bracketDepth--; if (bracketDepth == 0) { matrixStartIndex = i; break; } }
            
            string header = rawText.Substring(0, matrixStartIndex);
            string footer = rawText.Substring(matrixEndIndex + 1);
            string newMatrixJson = dataMatrix.ToJsonString(GetModernOptions());
            
            await File.WriteAllTextAsync(path, header + newMatrixJson + footer, new System.Text.UTF8Encoding(false));
        }
    }

    private class ItemMetadata : ExtractedMetadata
    {
        public required string ItemCategory { get; set; }
        public required string ItemType { get; set; }
        public required string Cost { get; set; }
        public required string Rarity { get; set; }
    }
}
