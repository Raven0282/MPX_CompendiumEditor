// File: Services/Data/ClassCompendiumWriter.cs
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

    protected override async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta, bool isAppend)
    {
        string path = Path.Combine(repositoryPath, "_listing.js");
        if (!File.Exists(path)) return;

        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonArray dataMatrix = _extractor.ExtractArrayPayload(rawText);

        ReadOnlySpan<char> sourceSpan = rawText.AsSpan();
        int headerStart = sourceSpan.IndexOf('[');
        int headerEnd = sourceSpan.Slice(headerStart).IndexOf(']') + headerStart;
        string headerJson = sourceSpan.Slice(headerStart, headerEnd - headerStart + 1).ToString();
        var headers = JsonSerializer.Deserialize<List<string>>(headerJson) ?? new List<string>();

        int idxName = headers.IndexOf("Name");
        int idxRole = headers.IndexOf("RoleName");
        int idxPower = headers.IndexOf("PowerSourceText");
        int idxAbilities = headers.IndexOf("KeyAbilities");
        int idxSource = headers.IndexOf("SourceBook");

        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count > 0 && row[0]?.ToString() == id)
            {
                if (idxName != -1 && row.Count > idxName) row[idxName] = JsonValue.Create(meta.Name);
                if (idxRole != -1 && row.Count > idxRole) row[idxRole] = JsonValue.Create(meta.Tier);
                if (idxPower != -1 && row.Count > idxPower) row[idxPower] = JsonValue.Create(meta.Prerequisite);
                if (idxAbilities != -1 && row.Count > idxAbilities) row[idxAbilities] = JsonValue.Create(meta.BenefitText);
                if (idxSource != -1 && row.Count > idxSource) row[idxSource] = JsonValue.Create(meta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            var newRow = new JsonArray();
            for (int i = 0; i < headers.Count; i++)
            {
                if (i == 0) newRow.Add(JsonValue.Create(id));
                else if (i == idxName) newRow.Add(JsonValue.Create(meta.Name));
                else if (i == idxRole) newRow.Add(JsonValue.Create(meta.Tier));
                else if (i == idxPower) newRow.Add(JsonValue.Create(meta.Prerequisite));
                else if (i == idxAbilities) newRow.Add(JsonValue.Create(meta.BenefitText));
                else if (i == idxSource) newRow.Add(JsonValue.Create(meta.SourceBook));
                else newRow.Add(JsonValue.Create(""));
            }
            dataMatrix.Add(newRow);
            found = true;
        }

        if (found)
        {
            int matrixEndIndex = -1;
            int finalCloseParenthesis = sourceSpan.LastIndexOf(')');
            for (int i = finalCloseParenthesis - 1; i >= 0; i--) { if (sourceSpan[i] == ']') { matrixEndIndex = i; break; } }
            int bracketDepth = 0, matrixStartIndex = -1;
            for (int i = matrixEndIndex; i >= 0; i--) { if (sourceSpan[i] == ']') bracketDepth++; if (sourceSpan[i] == '[') bracketDepth--; if (bracketDepth == 0) { matrixStartIndex = i; break; } }
            string header = rawText.Substring(0, matrixStartIndex);
            string footer = rawText.Substring(matrixEndIndex + 1);
            string newMatrixJson = dataMatrix.ToJsonString(GetModernOptions());
            await File.WriteAllTextAsync(path, header + newMatrixJson + footer);
        }
    }
}
