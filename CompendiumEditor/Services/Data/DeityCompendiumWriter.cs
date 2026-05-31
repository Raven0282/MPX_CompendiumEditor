// File: Services/Data/DeityCompendiumWriter.cs
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
/// Specialized writer for the Deity category, handling alignment and domain extraction for 5-column listing matrices.
/// </summary>
public class DeityCompendiumWriter : BaseCompendiumWriter
{
    public DeityCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath)
    {
        return repositoryPath.Contains($"{Path.DirectorySeparatorChar}deity", StringComparison.OrdinalIgnoreCase) || 
               repositoryPath.EndsWith("deity", StringComparison.OrdinalIgnoreCase);
    }

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // Name: <h1 class=player>Gruumsh<b>, The One-Eyed God</b></h1>
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);
        if (!nameMatch.Success)
        {
            throw new CompendiumValidationException("Could not extract Deity Name from <h1> tag.", activeTargetShardPath, id);
        }

        string cleanName = Regex.Replace(nameMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

        // Alignment: <b>Alignment: </b>Chaotic Evil<br>
        string alignment = "Unaligned";
        var alignmentMatch = Regex.Match(html, @"<b>Alignment:\s*</b>(.*?)(?:<br>|$)", RegexOptions.IgnoreCase);
        if (alignmentMatch.Success)
        {
            alignment = Regex.Replace(alignmentMatch.Groups[1].Value, @"<[^>]+>", "").Trim();
        }

        // Domain: <b>Domain: </b>Destruction, Strength<br>
        string domains = "";
        var domainMatch = Regex.Match(html, @"<b>Domain:?\s*</b>(.*?)(?:<br>|$)", RegexOptions.IgnoreCase);
        if (domainMatch.Success)
        {
            domains = Regex.Replace(domainMatch.Groups[1].Value, @"<[^>]+>", "").Trim();
        }

        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new DeityMetadata
        {
            Name = cleanName,
            Tier = domains, // Used for Domains column in the base mapping if needed
            Prerequisite = alignment, // Used for Alignment column in the base mapping if needed
            BenefitText = "", 
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source",
            Domains = domains,
            Alignment = alignment
        };
    }

    protected override async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta, bool isAppend)
    {
        string path = Path.Combine(repositoryPath, "_listing.js");
        if (!File.Exists(path)) return;

        var dMeta = (DeityMetadata)meta;
        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonArray dataMatrix = _extractor.ExtractArrayPayload(rawText);

        // Deity Matrix: ["ID", "Name", "Domains", "Alignment", "SourceBook"]
        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count >= 5 && row[0]?.ToString() == id)
            {
                row[1] = JsonValue.Create(dMeta.Name);
                row[2] = JsonValue.Create(dMeta.Domains);
                row[3] = JsonValue.Create(dMeta.Alignment);
                row[4] = JsonValue.Create(dMeta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            _logger.Log($"Appending new deity row to _listing.js matrix for ID: {id}", "WRITER:DEITY");
            var newRow = new JsonArray
            {
                JsonValue.Create(id),
                JsonValue.Create(dMeta.Name),
                JsonValue.Create(dMeta.Domains),
                JsonValue.Create(dMeta.Alignment),
                JsonValue.Create(dMeta.SourceBook)
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

    private class DeityMetadata : ExtractedMetadata
    {
        public required string Domains { get; set; }
        public required string Alignment { get; set; }
    }
}
