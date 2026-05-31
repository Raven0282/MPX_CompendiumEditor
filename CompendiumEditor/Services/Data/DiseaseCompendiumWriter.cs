// File: Services/Data/DiseaseCompendiumWriter.cs
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
/// Specialized writer for the Disease category, handling 4-column listing matrices and level extraction.
/// </summary>
public class DiseaseCompendiumWriter : BaseCompendiumWriter
{
    public DiseaseCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath)
    {
        return repositoryPath.Contains($"{Path.DirectorySeparatorChar}disease", StringComparison.OrdinalIgnoreCase) || 
               repositoryPath.EndsWith("disease", StringComparison.OrdinalIgnoreCase);
    }

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // Name: <h1 class=atwillpower>Whispering Madness<span class=level>Level 4 Disease</span></h1>
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)<span", RegexOptions.IgnoreCase);
        if (!nameMatch.Success) nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);

        if (!nameMatch.Success)
        {
            throw new CompendiumValidationException("Could not extract Disease Name from <h1> tag.", activeTargetShardPath, id);
        }

        string cleanName = Regex.Replace(nameMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

        // Level: <span class=level>Level 4 Disease</span>
        string level = "";
        var levelMatch = Regex.Match(html, @"class=level>Level (\d+)", RegexOptions.IgnoreCase);
        if (levelMatch.Success)
        {
            level = levelMatch.Groups[1].Value;
        }

        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new ExtractedMetadata
        {
            Name = cleanName,
            Tier = level, // Used for Level column
            Prerequisite = "", 
            BenefitText = "", 
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source"
        };
    }

    protected override async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta, bool isAppend)
    {
        string path = Path.Combine(repositoryPath, "_listing.js");
        if (!File.Exists(path)) return;

        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonArray dataMatrix = _extractor.ExtractArrayPayload(rawText);

        // Disease Matrix: ["ID", "Name", "Level", "SourceBook"]
        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count >= 4 && row[0]?.ToString() == id)
            {
                row[1] = JsonValue.Create(meta.Name);
                row[2] = JsonValue.Create(meta.Tier); // Level
                row[3] = JsonValue.Create(meta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            _logger.Log($"Appending new disease row to _listing.js matrix for ID: {id}", "WRITER:DISEASE");
            var newRow = new JsonArray
            {
                JsonValue.Create(id),
                JsonValue.Create(meta.Name),
                JsonValue.Create(meta.Tier),
                JsonValue.Create(meta.SourceBook)
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
}
