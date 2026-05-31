// File: Services/Data/TrapCompendiumWriter.cs
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
/// Specialized writer for the Trap/Hazard category, handling diverse stat-blocks and 6-column listing matrices.
/// </summary>
public class TrapCompendiumWriter : BaseCompendiumWriter
{
    public TrapCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath)
    {
        return repositoryPath.Contains($"{Path.DirectorySeparatorChar}trap", StringComparison.OrdinalIgnoreCase) || 
               repositoryPath.EndsWith("trap", StringComparison.OrdinalIgnoreCase);
    }

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // Name: <h1 class=trap>Collapsing Ceiling<br> or <h1 class=thHead>Necrotic Energy Field<br>
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)<br>", RegexOptions.IgnoreCase);
        if (!nameMatch.Success) nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);

        if (!nameMatch.Success)
        {
            throw new CompendiumValidationException("Could not extract Trap/Hazard Name from <h1> tag.", activeTargetShardPath, id);
        }

        string cleanName = Regex.Replace(nameMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

        // Type: <span class=type>Hazard</span> or <span class=thSubHead>Object</span>
        string trapType = "Hazard";
        var typeMatch = Regex.Match(html, @"<span class=(?:type|thSubHead)>(.*?)</span>", RegexOptions.IgnoreCase);
        if (typeMatch.Success) trapType = typeMatch.Groups[1].Value.Trim();

        // Level and GroupRole: <span class=level>Level 2 Lurker<br> or <span class=thLevel>Level 5 Hazard<span...
        string level = "";
        string groupRole = "Standard";

        var levelMatch = Regex.Match(html, @"class=(?:level|thLevel)>Level ([\d\-]+)\s*(.*?)(?:<br>|<span)", RegexOptions.IgnoreCase);
        if (levelMatch.Success)
        {
            level = levelMatch.Groups[1].Value.Trim();
            string roleInfo = levelMatch.Groups[2].Value.Trim();
            
            if (roleInfo.Contains("Elite", StringComparison.OrdinalIgnoreCase)) groupRole = "Elite";
            else if (roleInfo.Contains("Solo", StringComparison.OrdinalIgnoreCase)) groupRole = "Solo";
            else if (string.IsNullOrWhiteSpace(roleInfo)) groupRole = "None";
            else groupRole = "Standard";
        }

        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new TrapMetadata
        {
            Name = cleanName,
            Tier = level, // Used for Level column
            Prerequisite = trapType, // Used for Type column
            BenefitText = groupRole, // Used for GroupRole column
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source",
            TrapType = trapType,
            GroupRole = groupRole
        };
    }

    protected override async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta, bool isAppend)
    {
        string path = Path.Combine(repositoryPath, "_listing.js");
        if (!File.Exists(path)) return;

        var tMeta = (TrapMetadata)meta;
        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonArray dataMatrix = _extractor.ExtractArrayPayload(rawText);

        // Trap Matrix: ["ID", "Name", "Type", "GroupRole", "Level", "SourceBook"]
        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count >= 6 && row[0]?.ToString() == id)
            {
                row[1] = JsonValue.Create(tMeta.Name);
                row[2] = JsonValue.Create(tMeta.TrapType);
                row[3] = JsonValue.Create(tMeta.GroupRole);
                row[4] = JsonValue.Create(tMeta.Tier); // Level
                row[5] = JsonValue.Create(tMeta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            _logger.Log($"Appending new trap row to _listing.js matrix for ID: {id}", "WRITER:TRAP");
            var newRow = new JsonArray
            {
                JsonValue.Create(id),
                JsonValue.Create(tMeta.Name),
                JsonValue.Create(tMeta.TrapType),
                JsonValue.Create(tMeta.GroupRole),
                JsonValue.Create(tMeta.Tier),
                JsonValue.Create(tMeta.SourceBook)
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

    private class TrapMetadata : ExtractedMetadata
    {
        public required string TrapType { get; set; }
        public required string GroupRole { get; set; }
    }
}
