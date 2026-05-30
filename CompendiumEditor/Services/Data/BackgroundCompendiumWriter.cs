// File: Services/Data/BackgroundCompendiumWriter.cs
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
/// Specialized writer for the "Background" category. 
/// Handles 6-column listing matrices (ID, Name, Type, Campaign, Benefit, SourceBook)
/// and flat-text search indices.
/// </summary>
public class BackgroundCompendiumWriter : BaseCompendiumWriter
{
    public BackgroundCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath) => 
        repositoryPath.Contains("\\background\\", StringComparison.OrdinalIgnoreCase) || 
        repositoryPath.Contains("/background/", StringComparison.OrdinalIgnoreCase);

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // 1. Extract Name
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);
        string name = nameMatch.Success ? nameMatch.Groups[1].Value : "Unknown Background";

        // 2. Extract Metadata from flavortext/blockquote
        var typeMatch = Regex.Match(html, @"<b>Type:\s*</b>(?<val>.*?)<br>", RegexOptions.IgnoreCase);
        var campaignMatch = Regex.Match(html, @"<b>Campaign Setting:\s*</b>(?<val>.*?)<br>", RegexOptions.IgnoreCase);
        
        // Benefit in listing is often "Associated: ..." or a summary
        var skillsMatch = Regex.Match(html, @"<i>Associated Skills:\s*</i>(?<val>.*?)<br>", RegexOptions.IgnoreCase);
        string benefit = skillsMatch.Success ? $"Associated: {skillsMatch.Groups["val"].Value}" : string.Empty;
        
        if (string.IsNullOrEmpty(benefit))
        {
            var benefitMatch = Regex.Match(html, @"<b>Benefit:\s*</b>(?<val>.*?)<br>", RegexOptions.IgnoreCase);
            if (benefitMatch.Success) benefit = benefitMatch.Groups["val"].Value;
        }

        // 3. Published In
        var pubMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new ExtractedMetadata
        {
            Name = Regex.Replace(name, @"<[^>]+>", "").Trim(),
            Tier = typeMatch.Success ? Regex.Replace(typeMatch.Groups["val"].Value, @"<[^>]+>", "").Trim() : "General",
            Prerequisite = campaignMatch.Success ? Regex.Replace(campaignMatch.Groups["val"].Value, @"<[^>]+>", "").Trim() : "General",
            BenefitText = Regex.Replace(benefit, @"<[^>]+>", "").Trim(),
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

        // REVERSION: Background Index MUST be the flat dense text block.
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
        int idxType = headers.IndexOf("Type");
        int idxCampaign = headers.IndexOf("Campaign");
        int idxBenefit = headers.IndexOf("Benefit");
        int idxSource = headers.IndexOf("SourceBook");

        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count > 0 && row[0]?.ToString() == id)
            {
                if (idxName != -1 && row.Count > idxName) row[idxName] = JsonValue.Create(meta.Name);
                if (idxType != -1 && row.Count > idxType) row[idxType] = JsonValue.Create(meta.Tier);
                if (idxCampaign != -1 && row.Count > idxCampaign) row[idxCampaign] = JsonValue.Create(meta.Prerequisite);
                if (idxBenefit != -1 && row.Count > idxBenefit) row[idxBenefit] = JsonValue.Create(meta.BenefitText);
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
                else if (i == idxType) newRow.Add(JsonValue.Create(meta.Tier));
                else if (i == idxCampaign) newRow.Add(JsonValue.Create(meta.Prerequisite));
                else if (i == idxBenefit) newRow.Add(JsonValue.Create(meta.BenefitText));
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
