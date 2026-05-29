// File: Services/Data/BackgroundCompendiumWriter.cs
using CompendiumEditor.Exceptions;
using CompendiumEditor.Services.Logging;
using System;
using System.IO;
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
}
