// File: Services/Data/ImplementCompendiumWriter.cs
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
/// Specialized writer for the Implement category, handling mundane and magical implement formats and 7-column matrices.
/// </summary>
public class ImplementCompendiumWriter : BaseCompendiumWriter
{
    public ImplementCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath)
    {
        return repositoryPath.Contains($"{Path.DirectorySeparatorChar}implement", StringComparison.OrdinalIgnoreCase) || 
               repositoryPath.EndsWith("implement", StringComparison.OrdinalIgnoreCase);
    }

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // Name: <h1 class=player>Orb Implement</h1> or <h1 class=mihead>Magic Totem<span...
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)<span", RegexOptions.IgnoreCase);
        if (!nameMatch.Success) nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);

        if (!nameMatch.Success)
        {
            throw new CompendiumValidationException("Could not extract Implement Name from <h1> tag.", activeTargetShardPath, id);
        }

        string cleanName = Regex.Replace(nameMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

        // Level, Rarity, Type, Cost initialization
        string level = "";
        string rarity = "Mundane";
        string implementType = "Implement";
        string cost = "";

        // MAGIC IMPLEMENT DETECTION
        if (html.Contains("class=mihead", StringComparison.OrdinalIgnoreCase))
        {
            rarity = "Uncommon"; // Default for magic items
            var levelSpanMatch = Regex.Match(html, @"id=headerlevel>Level ([\d\+]+)\s*(.*?)</span>", RegexOptions.IgnoreCase);
            if (levelSpanMatch.Success)
            {
                level = levelSpanMatch.Groups[1].Value;
                string rarityInfo = levelSpanMatch.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(rarityInfo)) rarity = rarityInfo.Trim();
            }

            // Type for magic: <b>Implement: </b>Totem
            var typeMatch = Regex.Match(html, @"<b>Implement:\s*</b>(.*?)</p>", RegexOptions.IgnoreCase);
            if (typeMatch.Success) implementType = Regex.Replace(typeMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

            // Cost for magic: <td class=mic3>520 gp</td>
            var costMatch = Regex.Match(html, @"<td class=mic3>([\d,]+ gp)</td>", RegexOptions.IgnoreCase);
            if (costMatch.Success)
            {
                cost = costMatch.Groups[1].Value;
                if (level.Contains("+") && !cost.Contains("+")) cost = cost.Replace(" gp", "+ gp");
            }
        }
        else // MUNDANE IMPLEMENT
        {
            // Level column for mundane superior implements is usually "Superior"
            if (html.Contains("Superior", StringComparison.OrdinalIgnoreCase)) level = "Superior";

            // Type for mundane: <b>Group</b>: <br>Holy Symbol
            var groupMatch = Regex.Match(html, @"<b>Group</b>:\s*(?:<br>)?\s*(.*?)(?:\(|\.|<br>)", RegexOptions.IgnoreCase);
            if (groupMatch.Success) implementType = Regex.Replace(groupMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

            // Fallback for simple mundane like "implement260" Tome
            if (string.IsNullOrEmpty(implementType))
            {
                if (cleanName.Contains("Holy Symbol", StringComparison.OrdinalIgnoreCase)) implementType = "Holy Symbol";
                else if (cleanName.Contains("Ki Focus", StringComparison.OrdinalIgnoreCase)) implementType = "Ki Focus";
                else if (cleanName.Contains("Orb", StringComparison.OrdinalIgnoreCase)) implementType = "Orb";
                else if (cleanName.Contains("Rod", StringComparison.OrdinalIgnoreCase)) implementType = "Rod";
                else if (cleanName.Contains("Staff", StringComparison.OrdinalIgnoreCase)) implementType = "Staff";
                else if (cleanName.Contains("Tome", StringComparison.OrdinalIgnoreCase)) implementType = "Tome";
                else if (cleanName.Contains("Wand", StringComparison.OrdinalIgnoreCase)) implementType = "Wand";
                else if (cleanName.Contains("Totem", StringComparison.OrdinalIgnoreCase)) implementType = "Totem";
            }

            // Cost for mundane: Cost: 18 gp or Price: 7 gp
            var costMatch = Regex.Match(html, @"(?:Cost|Price):\s*(.*?)(?:<br>|$)", RegexOptions.IgnoreCase);
            if (costMatch.Success) cost = costMatch.Groups[1].Value.Trim();
        }

        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new ImplementMetadata
        {
            Name = cleanName,
            Tier = level, // Used for Level column
            Prerequisite = implementType, // Used for Type column
            BenefitText = cost, // Used for Cost column
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source",
            ImplementType = implementType,
            Cost = cost,
            Rarity = rarity
        };
    }

    protected override async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta, bool isAppend)
    {
        string path = Path.Combine(repositoryPath, "_listing.js");
        if (!File.Exists(path)) return;

        var iMeta = (ImplementMetadata)meta;
        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonArray dataMatrix = _extractor.ExtractArrayPayload(rawText);

        // Implement Matrix: ["ID", "Name", "Type", "Level", "Cost", "Rarity", "SourceBook"]
        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count >= 7 && row[0]?.ToString() == id)
            {
                row[1] = JsonValue.Create(iMeta.Name);
                row[2] = JsonValue.Create(iMeta.ImplementType);
                row[3] = JsonValue.Create(iMeta.Tier); // Level
                row[4] = JsonValue.Create(iMeta.Cost);
                row[5] = JsonValue.Create(iMeta.Rarity);
                row[6] = JsonValue.Create(iMeta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            _logger.Log($"Appending new implement row to _listing.js matrix for ID: {id}", "WRITER:IMPLEMENT");
            var newRow = new JsonArray
            {
                JsonValue.Create(id),
                JsonValue.Create(iMeta.Name),
                JsonValue.Create(iMeta.ImplementType),
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

    private class ImplementMetadata : ExtractedMetadata
    {
        public required string ImplementType { get; set; }
        public required string Cost { get; set; }
        public required string Rarity { get; set; }
    }
}
