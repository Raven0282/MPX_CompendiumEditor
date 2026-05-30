// File: Services/Data/PowerCompendiumWriter.cs
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
/// Specialized writer for the Power category, handling complex stat-blocks and 8-column listing matrices.
/// </summary>
public class PowerCompendiumWriter : BaseCompendiumWriter
{
    public PowerCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    public override bool CanHandle(string repositoryPath)
    {
        return repositoryPath.Contains($"{Path.DirectorySeparatorChar}power", StringComparison.OrdinalIgnoreCase) || 
               repositoryPath.EndsWith("power", StringComparison.OrdinalIgnoreCase);
    }

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        // Name: <h1 class=encounterpower>Adamantine Blast<span class=level>Adamant Instructor Attack 11</span></h1>
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)<span", RegexOptions.IgnoreCase);
        if (!nameMatch.Success) nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);

        if (!nameMatch.Success)
        {
            throw new CompendiumValidationException("Could not extract Power Name from <h1> tag.", activeTargetShardPath, id);
        }

        string cleanName = Regex.Replace(nameMatch.Groups[1].Value, @"<[^>]+>", "").Trim();

        // Level and Class: <span class=level>Adamant Instructor Attack 11</span>
        string className = "General";
        string level = "0";
        var levelSpanMatch = Regex.Match(html, @"<span class=level>(.*?)</span>", RegexOptions.IgnoreCase);
        if (levelSpanMatch.Success)
        {
            string spanContent = levelSpanMatch.Groups[1].Value;
            var levelNumMatch = Regex.Match(spanContent, @"\d+");
            if (levelNumMatch.Success)
            {
                level = levelNumMatch.Value;
                className = spanContent.Substring(0, levelNumMatch.Index).Trim();
            }
            else
            {
                className = spanContent.Trim();
            }
        }

        // Type, Action, Keywords: <p class=powerstat><b>Encounter</b>   ✦     <b>Divine</b>, <b>Thunder</b><br><b>Standard Action</b>      <b>Close</b> blast 5</p>
        string powerType = "At-Will";
        if (html.Contains("encounterpower", StringComparison.OrdinalIgnoreCase)) powerType = "Encounter";
        else if (html.Contains("dailypower", StringComparison.OrdinalIgnoreCase)) powerType = "Daily";

        string actionType = "Standard";
        if (html.Contains("Minor Action", StringComparison.OrdinalIgnoreCase)) actionType = "Minor";
        else if (html.Contains("Move Action", StringComparison.OrdinalIgnoreCase)) actionType = "Move";
        else if (html.Contains("Free Action", StringComparison.OrdinalIgnoreCase)) actionType = "Free";
        else if (html.Contains("No Action", StringComparison.OrdinalIgnoreCase)) actionType = "No Action";
        else if (html.Contains("Immediate Interrupt", StringComparison.OrdinalIgnoreCase)) actionType = "Interrupt";
        else if (html.Contains("Immediate Reaction", StringComparison.OrdinalIgnoreCase)) actionType = "Reaction";

        var keywords = new List<string>();
        var powerStatMatch = Regex.Match(html, @"<p class=powerstat>(.*?)</p>", RegexOptions.IgnoreCase);
        if (powerStatMatch.Success)
        {
            var boldMatches = Regex.Matches(powerStatMatch.Groups[1].Value, @"<b>(.*?)</b>", RegexOptions.IgnoreCase);
            foreach (Match m in boldMatches)
            {
                string val = m.Groups[1].Value.Trim();
                if (val != "At-Will" && val != "Encounter" && val != "Daily" && !val.Contains("Action"))
                {
                    keywords.Add(val);
                }
            }
        }

        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        return new PowerMetadata
        {
            Name = cleanName,
            Tier = level,
            Prerequisite = className, // Used for ClassName column
            BenefitText = powerType,  // Used for Type column
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source",
            ClassName = className,
            PowerType = powerType,
            ActionType = actionType,
            Keywords = string.Join(", ", keywords)
        };
    }

    protected override async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta, bool isAppend)
    {
        string path = Path.Combine(repositoryPath, "_listing.js");
        if (!File.Exists(path)) return;

        var pMeta = (PowerMetadata)meta;
        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonArray dataMatrix = _extractor.ExtractArrayPayload(rawText);

        // Power Matrix: ["ID", "Name", "ClassName", "Level", "Type", "Action", "Keywords", "SourceBook"]
        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count >= 8 && row[0]?.ToString() == id)
            {
                row[1] = JsonValue.Create(pMeta.Name);
                row[2] = JsonValue.Create(pMeta.ClassName);
                row[3] = JsonValue.Create(pMeta.Tier);
                row[4] = JsonValue.Create(pMeta.PowerType);
                row[5] = JsonValue.Create(pMeta.ActionType);
                row[6] = JsonValue.Create(pMeta.Keywords);
                row[7] = JsonValue.Create(pMeta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            _logger.Log($"Appending new power row to _listing.js matrix for ID: {id}", "WRITER:POWER");
            var newRow = new JsonArray
            {
                JsonValue.Create(id),
                JsonValue.Create(pMeta.Name),
                JsonValue.Create(pMeta.ClassName),
                JsonValue.Create(pMeta.Tier),
                JsonValue.Create(pMeta.PowerType),
                JsonValue.Create(pMeta.ActionType),
                JsonValue.Create(pMeta.Keywords),
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

    private class PowerMetadata : ExtractedMetadata
    {
        public required string ClassName { get; set; }
        public required string PowerType { get; set; }
        public required string ActionType { get; set; }
        public required string Keywords { get; set; }
    }
}
