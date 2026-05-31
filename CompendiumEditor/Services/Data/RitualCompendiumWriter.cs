// File: Services/Data/RitualCompendiumWriter.cs
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
/// Specialized writer for the Ritual category, handling 7-column listing matrices and ritual-specific stat extraction.
/// </summary>
public class RitualCompendiumWriter : BaseCompendiumWriter
{
    public RitualCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath)
    {
        return repositoryPath.Contains($"{Path.DirectorySeparatorChar}ritual", StringComparison.OrdinalIgnoreCase) || 
               repositoryPath.EndsWith("ritual", StringComparison.OrdinalIgnoreCase);
    }

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // Name: <h1 class=player>Animal Messenger</h1>
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);
        if (!nameMatch.Success)
        {
            throw new CompendiumValidationException("Could not extract Ritual Name from <h1> tag.", activeTargetShardPath, id);
        }

        string cleanName = Regex.Replace(nameMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

        // Stats: <span class=ritualstats><b>Component Cost</b>: 10 gp<br><b>Market Price</b>: 50 gp<br><b>Key Skill</b>: Nature</span>
        string componentCost = "";
        string marketPrice = "";
        string keySkill = "";
        string level = "1";

        var ritualStatsMatch = Regex.Match(html, @"<span class=ritualstats>(.*?)</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (ritualStatsMatch.Success)
        {
            string statsContent = ritualStatsMatch.Groups[1].Value;
            
            var costMatch = Regex.Match(statsContent, @"<b>Component Cost</b>:\s*(.*?)(?:<br>|$)", RegexOptions.IgnoreCase);
            if (costMatch.Success) componentCost = Regex.Replace(costMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

            var priceMatch = Regex.Match(statsContent, @"<b>Market Price</b>:\s*(.*?)(?:<br>|$)", RegexOptions.IgnoreCase);
            if (priceMatch.Success) marketPrice = Regex.Replace(priceMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

            var skillMatch = Regex.Match(statsContent, @"<b>Key Skill</b>:\s*(.*?)(?:<br>|$)", RegexOptions.IgnoreCase);
            if (skillMatch.Success) keySkill = Regex.Replace(skillMatch.Groups[1].Value, @"<[^>]+>", "").Trim();
        }

        // Level: <b>Level</b>: 1
        var levelMatch = Regex.Match(html, @"<b>Level</b>:\s*(\d+)", RegexOptions.IgnoreCase);
        if (levelMatch.Success) level = levelMatch.Groups[1].Value;

        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new RitualMetadata
        {
            Name = cleanName,
            Tier = level, // Used for Level column
            Prerequisite = componentCost, // Used for ComponentCost column
            BenefitText = marketPrice, // Used for Price column
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source",
            ComponentCost = componentCost,
            Price = marketPrice,
            KeySkill = keySkill
        };
    }

    protected override async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta, bool isAppend)
    {
        string path = Path.Combine(repositoryPath, "_listing.js");
        if (!File.Exists(path)) return;

        var rMeta = (RitualMetadata)meta;
        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonArray dataMatrix = _extractor.ExtractArrayPayload(rawText);

        // Ritual Matrix: ["ID", "Name", "Level", "ComponentCost", "Price", "KeySkillDescription", "SourceBook"]
        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count >= 7 && row[0]?.ToString() == id)
            {
                row[1] = JsonValue.Create(rMeta.Name);
                row[2] = JsonValue.Create(rMeta.Tier); // Level
                row[3] = JsonValue.Create(rMeta.ComponentCost);
                row[4] = JsonValue.Create(rMeta.Price);
                row[5] = JsonValue.Create(rMeta.KeySkill);
                row[6] = JsonValue.Create(rMeta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            _logger.Log($"Appending new ritual row to _listing.js matrix for ID: {id}", "WRITER:RITUAL");
            var newRow = new JsonArray
            {
                JsonValue.Create(id),
                JsonValue.Create(rMeta.Name),
                JsonValue.Create(rMeta.Tier),
                JsonValue.Create(rMeta.ComponentCost),
                JsonValue.Create(rMeta.Price),
                JsonValue.Create(rMeta.KeySkill),
                JsonValue.Create(rMeta.SourceBook)
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

    private class RitualMetadata : ExtractedMetadata
    {
        public required string ComponentCost { get; set; }
        public required string Price { get; set; }
        public required string KeySkill { get; set; }
    }
}
