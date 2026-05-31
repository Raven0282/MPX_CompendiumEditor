// File: Services/Data/RaceCompendiumWriter.cs
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
/// Specialized writer for the Race category, handling racial traits and 6-column listing matrices.
/// </summary>
public class RaceCompendiumWriter : BaseCompendiumWriter
{
    public RaceCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath)
    {
        return repositoryPath.Contains($"{Path.DirectorySeparatorChar}race", StringComparison.OrdinalIgnoreCase) || 
               repositoryPath.EndsWith("race", StringComparison.OrdinalIgnoreCase);
    }

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // Name: <h1 class=player>Gnome</h1>
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);
        if (!nameMatch.Success)
        {
            throw new CompendiumValidationException("Could not extract Race Name from <h1> tag.", activeTargetShardPath, id);
        }

        string cleanName = Regex.Replace(nameMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

        // Size: <b>Size</b>: Small
        string size = "Medium";
        var sizeMatch = Regex.Match(html, @"<b>Size</b>:\s*(.*?)(?:<br>|$)", RegexOptions.IgnoreCase);
        if (sizeMatch.Success) size = Regex.Replace(sizeMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

        // Origin: <b>Fey Origin</b>: ...
        string origin = "Natural";
        var originMatch = Regex.Match(html, @"<b>(Fey|Immortal|Elemental|Shadow|Aberrant|Natural)\s+Origin</b>", RegexOptions.IgnoreCase);
        if (originMatch.Success) 
        {
            origin = originMatch.Groups[1].Value;
        }
        else if (html.Contains(" considered an immortal creature", StringComparison.OrdinalIgnoreCase))
        {
            origin = "Immortal";
        }

        // DescriptionAttribute (Ability Scores): <b>Ability scores</b>: +2 Intelligence, +2 Charisma or +2 Dexterity
        string abilityScores = "";
        var abilityMatch = Regex.Match(html, @"<b>Ability scores</b>:\s*(.*?)(?:<br>|$)", RegexOptions.IgnoreCase);
        if (abilityMatch.Success)
        {
            string rawAbilities = Regex.Replace(abilityMatch.Groups[1].Value, @"<[^>]+>", "").Trim();
            // Simplify to short codes: "Str, Dex, Wis"
            var codes = new List<string>();
            if (rawAbilities.Contains("Strength", StringComparison.OrdinalIgnoreCase)) codes.Add("Str");
            if (rawAbilities.Contains("Constitution", StringComparison.OrdinalIgnoreCase)) codes.Add("Con");
            if (rawAbilities.Contains("Dexterity", StringComparison.OrdinalIgnoreCase)) codes.Add("Dex");
            if (rawAbilities.Contains("Intelligence", StringComparison.OrdinalIgnoreCase)) codes.Add("Int");
            if (rawAbilities.Contains("Wisdom", StringComparison.OrdinalIgnoreCase)) codes.Add("Wis");
            if (rawAbilities.Contains("Charisma", StringComparison.OrdinalIgnoreCase)) codes.Add("Cha");
            
            abilityScores = string.Join(", ", codes);
            if (rawAbilities.Contains(" or ", StringComparison.OrdinalIgnoreCase))
            {
                // Re-process for the "or" format if simple join isn't expressive enough
                // But the legacy data uses "Wis, Cha or Int"
                int lastIdx = abilityScores.LastIndexOf(", ");
                if (lastIdx != -1)
                {
                    abilityScores = abilityScores.Substring(0, lastIdx) + " or " + abilityScores.Substring(lastIdx + 2);
                }
            }
        }

        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new RaceMetadata
        {
            Name = cleanName,
            Tier = origin, // Used for Origin column
            Prerequisite = abilityScores, // Used for DescriptionAttribute column
            BenefitText = size, // Used for Size column
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source",
            Origin = origin,
            AbilityScores = abilityScores,
            Size = size
        };
    }

    protected override async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta, bool isAppend)
    {
        string path = Path.Combine(repositoryPath, "_listing.js");
        if (!File.Exists(path)) return;

        var rMeta = (RaceMetadata)meta;
        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonArray dataMatrix = _extractor.ExtractArrayPayload(rawText);

        // Race Matrix: ["ID", "Name", "Origin", "DescriptionAttribute", "Size", "SourceBook"]
        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count >= 6 && row[0]?.ToString() == id)
            {
                row[1] = JsonValue.Create(rMeta.Name);
                row[2] = JsonValue.Create(rMeta.Origin);
                row[3] = JsonValue.Create(rMeta.AbilityScores);
                row[4] = JsonValue.Create(rMeta.Size);
                row[5] = JsonValue.Create(rMeta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            _logger.Log($"Appending new race row to _listing.js matrix for ID: {id}", "WRITER:RACE");
            var newRow = new JsonArray
            {
                JsonValue.Create(id),
                JsonValue.Create(rMeta.Name),
                JsonValue.Create(rMeta.Origin),
                JsonValue.Create(rMeta.AbilityScores),
                JsonValue.Create(rMeta.Size),
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

    private class RaceMetadata : ExtractedMetadata
    {
        public required string Origin { get; set; }
        public required string AbilityScores { get; set; }
        public required string Size { get; set; }
    }
}
