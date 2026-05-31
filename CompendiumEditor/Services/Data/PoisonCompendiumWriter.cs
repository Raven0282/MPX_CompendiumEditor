// File: Services/Data/PoisonCompendiumWriter.cs
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
/// Specialized writer for the Poison category, handling mundane and magical poison formats and 5-column matrices.
/// </summary>
public class PoisonCompendiumWriter : BaseCompendiumWriter
{
    public PoisonCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath)
    {
        return repositoryPath.Contains($"{Path.DirectorySeparatorChar}poison", StringComparison.OrdinalIgnoreCase) || 
               repositoryPath.EndsWith("poison", StringComparison.OrdinalIgnoreCase);
    }

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // Name: <h1 class=poison>Stormclaw Scorpion Venom<br>
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)<span", RegexOptions.IgnoreCase);
        if (!nameMatch.Success) nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)<br>", RegexOptions.IgnoreCase);
        if (!nameMatch.Success) nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);

        if (!nameMatch.Success)
        {
            throw new CompendiumValidationException("Could not extract Poison Name from <h1> tag.", activeTargetShardPath, id);
        }

        string cleanName = Regex.Replace(nameMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

        // Level: <span class=level>Level 5 Poison</span> or id=headerlevel>Level 15 Uncommon</span>
        string level = "";
        var levelMatch = Regex.Match(html, @"class=(?:level|milevel)(?:\s+id=headerlevel)?>Level\s+(\d+)", RegexOptions.IgnoreCase);
        if (levelMatch.Success)
        {
            level = levelMatch.Groups[1].Value;
        }

        // Cost: <p><b>Poison</b> 250 gp<br> or similar
        string cost = "";
        var costMatch = Regex.Match(html, @"<b>Poison</b>\s*([\d,]+)\s*(?:gp|GP)", RegexOptions.IgnoreCase);
        if (costMatch.Success)
        {
            cost = costMatch.Groups[1].Value.Trim() + " GP";
        }
        else
        {
            // Try alternate price format from magic item style
            var priceMatch = Regex.Match(html, @"<b>Price</b>:\s*([\d,]+)\s*(?:gp|GP)", RegexOptions.IgnoreCase);
            if (priceMatch.Success) cost = priceMatch.Groups[1].Value.Trim() + " GP";
        }

        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new PoisonMetadata
        {
            Name = cleanName,
            Tier = level, // Used for Level column
            Prerequisite = cost, // Used for Cost column
            BenefitText = "", 
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source",
            Cost = cost
        };
    }

    protected override async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta, bool isAppend)
    {
        string path = Path.Combine(repositoryPath, "_listing.js");
        if (!File.Exists(path)) return;

        var pMeta = (PoisonMetadata)meta;
        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonArray dataMatrix = _extractor.ExtractArrayPayload(rawText);

        // Poison Matrix: ["ID", "Name", "Level", "Cost", "SourceBook"]
        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count >= 5 && row[0]?.ToString() == id)
            {
                row[1] = JsonValue.Create(pMeta.Name);
                row[2] = JsonValue.Create(pMeta.Tier); // Level
                row[3] = JsonValue.Create(pMeta.Cost);
                row[4] = JsonValue.Create(pMeta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            _logger.Log($"Appending new poison row to _listing.js matrix for ID: {id}", "WRITER:POISON");
            var newRow = new JsonArray
            {
                JsonValue.Create(id),
                JsonValue.Create(pMeta.Name),
                JsonValue.Create(pMeta.Tier),
                JsonValue.Create(pMeta.Cost),
                JsonValue.Create(pMeta.SourceBook)
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

    private class PoisonMetadata : ExtractedMetadata
    {
        public required string Cost { get; set; }
    }
}
