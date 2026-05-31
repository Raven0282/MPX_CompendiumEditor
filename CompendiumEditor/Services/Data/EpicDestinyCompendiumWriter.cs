// File: Services/Data/EpicDestinyCompendiumWriter.cs
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
/// Specialized writer for the Epic Destiny category, handling 4-column listing matrices and prerequisite extraction.
/// </summary>
public class EpicDestinyCompendiumWriter : BaseCompendiumWriter
{
    public EpicDestinyCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath)
    {
        return repositoryPath.Contains($"{Path.DirectorySeparatorChar}epicdestiny", StringComparison.OrdinalIgnoreCase) || 
               repositoryPath.EndsWith("epicdestiny", StringComparison.OrdinalIgnoreCase);
    }

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // Name: <h1 class=player>Beastlord</h1>
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);
        if (!nameMatch.Success)
        {
            throw new CompendiumValidationException("Could not extract Epic Destiny Name from <h1> tag.", activeTargetShardPath, id);
        }

        string cleanName = Regex.Replace(nameMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

        // Prerequisite: <b>Prerequisite: </b>21st level, ranger, Beast Mastery class feature<br>
        string prereq = "";
        var prereqMatch = Regex.Match(html, @"<b>Prerequisite:\s*</b>(.*?)(?:<br>|$)", RegexOptions.IgnoreCase);
        if (prereqMatch.Success)
        {
            prereq = Regex.Replace(prereqMatch.Groups[1].Value, @"<[^>]+>", "").Trim();
            // Optional: Remove "21st level, " prefix if common to all to save space in listing, 
            // but for now we keep as per original data.
        }

        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new ExtractedMetadata
        {
            Name = cleanName,
            Tier = "", // Not used in epicdestiny listing matrix
            Prerequisite = prereq,
            BenefitText = "", // Not used in epicdestiny listing matrix
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

        // Epic Destiny Matrix: ["ID", "Name", "Prerequisite", "SourceBook"]
        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count >= 4 && row[0]?.ToString() == id)
            {
                row[1] = JsonValue.Create(meta.Name);
                row[2] = JsonValue.Create(meta.Prerequisite);
                row[3] = JsonValue.Create(meta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            _logger.Log($"Appending new epic destiny row to _listing.js matrix for ID: {id}", "WRITER:EPICDESTINY");
            var newRow = new JsonArray
            {
                JsonValue.Create(id),
                JsonValue.Create(meta.Name),
                JsonValue.Create(meta.Prerequisite),
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
