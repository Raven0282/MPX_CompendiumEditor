// File: Services/Data/MonsterCompendiumWriter.cs
using CompendiumEditor.Exceptions;
using CompendiumEditor.Services.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;

namespace CompendiumEditor.Services.Data;

/// <summary>
/// Specialized writer for the "Monster" category. 
/// Handles 8-column listing matrices and complex stat-block search indices.
/// </summary>
public class MonsterCompendiumWriter : BaseCompendiumWriter
{
    public MonsterCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath) => 
        repositoryPath.Contains("\\monster\\", StringComparison.OrdinalIgnoreCase) || 
        repositoryPath.Contains("/monster/", StringComparison.OrdinalIgnoreCase);

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // 1. Extract Name
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)<br>", RegexOptions.IgnoreCase);
        string name = nameMatch.Success ? nameMatch.Groups[1].Value : "Unknown Monster";

        // 2. Extract Type/Size/CreatureType
        // Format: <span class=type>Small natural beast (reptile)</span>
        var typeMatch = Regex.Match(html, @"<span class=type>(.*?)</span>", RegexOptions.IgnoreCase);
        string fullType = typeMatch.Success ? typeMatch.Groups[1].Value : string.Empty;
        
        string size = "Medium";
        if (fullType.StartsWith("Tiny", StringComparison.OrdinalIgnoreCase)) size = "Tiny";
        else if (fullType.StartsWith("Small", StringComparison.OrdinalIgnoreCase)) size = "Small";
        else if (fullType.StartsWith("Large", StringComparison.OrdinalIgnoreCase)) size = "Large";
        else if (fullType.StartsWith("Huge", StringComparison.OrdinalIgnoreCase)) size = "Huge";
        else if (fullType.StartsWith("Gargantuan", StringComparison.OrdinalIgnoreCase)) size = "Gargantuan";

        string creatureType = fullType; // Default fallback

        // 3. Extract Level and Roles
        // Format: <span class=level>Level 1 Elite Brute<span class=xp> XP 200</span></span>
        var levelBlockMatch = Regex.Match(html, @"<span class=level>Level (?<lvl>\d+)\s*(?<roles>.*?)<span", RegexOptions.IgnoreCase);
        string level = levelBlockMatch.Success ? levelBlockMatch.Groups["lvl"].Value : "0";
        string rolesRaw = levelBlockMatch.Success ? levelBlockMatch.Groups["roles"].Value.Trim() : string.Empty;

        // Split roles (e.g. "Elite Brute" or "Minion Skirmisher")
        string groupRole = "Standard";
        string combatRole = rolesRaw;

        if (rolesRaw.Contains("Elite", StringComparison.OrdinalIgnoreCase)) { groupRole = "Elite"; combatRole = rolesRaw.Replace("Elite", "", StringComparison.OrdinalIgnoreCase).Trim(); }
        else if (rolesRaw.Contains("Solo", StringComparison.OrdinalIgnoreCase)) { groupRole = "Solo"; combatRole = rolesRaw.Replace("Solo", "", StringComparison.OrdinalIgnoreCase).Trim(); }
        else if (rolesRaw.Contains("Minion", StringComparison.OrdinalIgnoreCase)) { groupRole = "Minion"; combatRole = rolesRaw.Replace("Minion", "", StringComparison.OrdinalIgnoreCase).Trim(); }

        // 4. Source
        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        // Monster specific: We store Level in Tier, CombatRole in Prereq, etc. for the internal model
        // but the UpdateListingFileAsync will map them correctly to the 8-column matrix.
        return new MonsterMetadata
        {
            Name = Regex.Replace(name, @"<[^>]+>", "").Trim(),
            Tier = level, // Internal mapping: Level
            Prerequisite = combatRole, // Internal mapping: CombatRole
            BenefitText = groupRole, // Internal mapping: GroupRole
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source",
            Size = size,
            CreatureType = Regex.Replace(fullType, @"<[^>]+>", "").Trim()
        };
    }

    protected override async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta, bool isAppend)
    {
        string path = Path.Combine(repositoryPath, "_listing.js");
        if (!File.Exists(path)) return;

        var mMeta = (MonsterMetadata)meta;
        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonArray dataMatrix = _extractor.ExtractArrayPayload(rawText);

        // Monster Matrix: ["ID", "Name", "Level", "CombatRole", "GroupRole", "Size", "CreatureType", "SourceBook"]
        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count >= 8 && row[0]?.ToString() == id)
            {
                row[1] = JsonValue.Create(mMeta.Name);
                row[2] = JsonValue.Create(mMeta.Tier); // Level
                row[3] = JsonValue.Create(mMeta.Prerequisite); // CombatRole
                row[4] = JsonValue.Create(mMeta.BenefitText); // GroupRole
                row[5] = JsonValue.Create(mMeta.Size);
                row[6] = JsonValue.Create(mMeta.CreatureType);
                row[7] = JsonValue.Create(mMeta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            _logger.Log($"Appending new monster row to _listing.js matrix for ID: {id}", "WRITER:MONSTER");
            var newRow = new JsonArray
            {
                JsonValue.Create(id),
                JsonValue.Create(mMeta.Name),
                JsonValue.Create(mMeta.Tier),
                JsonValue.Create(mMeta.Prerequisite),
                JsonValue.Create(mMeta.BenefitText),
                JsonValue.Create(mMeta.Size),
                JsonValue.Create(mMeta.CreatureType),
                JsonValue.Create(mMeta.SourceBook)
            };
            dataMatrix.Add(newRow);
            found = true;
        }

        if (!found) _logger.Log($"ID {id} not found in Monster listing matrix!", "WRITER:MONSTER_LISTING WARNING");

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

        // REVERSION: Monster Index MUST be the dense stat block, not a summary.
        // We reconstruct this by stripping all HTML tags from the markup.
        string indexText = StripHtml(htmlMarkup);
        root[id] = JsonValue.Create(indexText);

        string newJson = root.ToJsonString(GetModernOptions());
        newJson = FormatForLegacy(newJson, rawText);
        await SplicedWriteAsync(path, rawText, newJson, '{', '}');
    }

    private class MonsterMetadata : ExtractedMetadata
    {
        public required string Size { get; set; }
        public required string CreatureType { get; set; }
    }
}
