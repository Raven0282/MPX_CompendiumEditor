// File: Services/Data/CompendiumWriter.cs
using CompendiumEditor.Exceptions;
using CompendiumEditor.Models;
using CompendiumEditor.Services.Logging;
using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace CompendiumEditor.Services.Data;

public class CompendiumWriter : ICompendiumWriter
{
    private readonly ICompendiumExtractor _extractor;
    private readonly IDiagnosticLogger _logger;

    public CompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger)
    {
        _extractor = extractor;
        _logger = logger;
    }

    private JsonSerializerOptions GetModernOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            // .NET 10: Use 4-space indentation to match the legacy compendium standard
            IndentSize = 4,
            // SECURITY/PERF: Use relaxed escaping to prevent & and < from becoming \u0026 and \u003C
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    /// <summary>
    /// REVERSION: If the original file used unquoted keys, we must strip the quotes 
    /// after serialization to ensure the legacy JS application remains compatible.
    /// </summary>
    private string FormatForLegacy(string json, string originalFragment)
    {
        // Check if the original fragment had unquoted keys at the start of lines
        // (Diagnostic check: if "feat123": was not found but feat123: was)
        bool usesUnquotedKeys = !originalFragment.Contains("\":") && originalFragment.Contains(":");

        if (usesUnquotedKeys)
        {
            _logger.Log("Detected unquoted keys in original fragment. Stripping quotes from output...", "WRITER:LEGACY");
            // Regex: Find quoted keys at the start of lines and remove the quotes
            return Regex.Replace(json, @"^\s*""(?<key>[a-zA-Z0-9_]+)""\s*:", "    ${key}:", RegexOptions.Multiline);
        }

        return json;
    }

    public async Task SaveRecordModificationAsync(string repositoryPath, CompendiumRecord record, string cleanHtmlMarkup)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
            throw new DirectoryNotFoundException("Target working repository space could not be verified.");

        _logger.Log($"Starting SaveRecordModificationAsync for ID: {record.Id}", "WRITER");

        // Step 1: Execute the Data Shard update first, which locates the file path and validates the HTML layout
        string isolatedShardPath = await UpdateDataShardAsync(repositoryPath, record.Id, cleanHtmlMarkup);

        // Step 2: Now parse out the valid clean metadata using that confirmed shard path context
        var metadata = ExtractMetadataFromHtml(record.Id, cleanHtmlMarkup, isolatedShardPath);

        // Step 3: Propagate downstream synchronizations safely to listing and indexing matrices
        await UpdateListingFileAsync(repositoryPath, record.Id, metadata);
        await UpdateIndexFileAsync(repositoryPath, record.Id, metadata);

        _logger.Log($"Finished SaveRecordModificationAsync for ID: {record.Id}", "WRITER SUCCESS");
    }

    private async Task<string> UpdateDataShardAsync(string repositoryPath, string id, string htmlMarkup)
    {
        _logger.Log($"UpdateDataShardAsync for ID: {id}", "WRITER");
        string[] shards = Directory.GetFiles(repositoryPath, "data*.js");
        string targetFile = string.Empty;
        string content = string.Empty;

        foreach (string file in shards)
        {
            string text = await File.ReadAllTextAsync(file);
            if (text.Contains($"\"{id}\":") || text.Contains($"'{id}':") || text.Contains($"\"{id}\"") || text.Contains($"'{id}'"))
            {
                targetFile = file;
                content = text;
                break;
            }
        }

        if (string.IsNullOrEmpty(targetFile))
            throw new FileNotFoundException($"Could not locate the explicit data shard mapping the ID '{id}'.");

        _logger.Log($"Found target shard: {targetFile}", "WRITER");

        // Force validation logic check early here before taking any action to ensure HTML safety rules pass
        ExtractMetadataFromHtml(id, htmlMarkup, targetFile);

        // Run transaction backup tasks
        await CreateBackupSnapshotAsync(repositoryPath, targetFile);

        _logger.Log($"Parsing shard content for {id}...", "WRITER");
        JsonNode root = _extractor.ExtractObjectPayload(content);
        
        _logger.Log($"Assigning new markup to ID {id}. Markup Length: {htmlMarkup.Length}", "WRITER");
        if (htmlMarkup.Length > 100)
            _logger.Log($"Markup Start: {htmlMarkup.Substring(0, 100)}", "WRITER");

        root[id] = JsonValue.Create(htmlMarkup);

        _logger.Log($"Writing updated shard content back to {targetFile}", "WRITER");
        
        string newJson = root.ToJsonString(GetModernOptions());
        newJson = FormatForLegacy(newJson, content);

        await SplicedWriteAsync(targetFile, content, newJson, '{', '}');

        return targetFile;
    }

    private async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta)
    {
        string path = Path.Combine(repositoryPath, "_listing.js");
        if (!File.Exists(path))
        {
            _logger.Log($"File not found: {path}", "WRITER:LISTING");
            return;
        }

        _logger.Log($"Updating listing file: {path}", "WRITER:LISTING");
        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);

        // Symmetrical Alignment: Pull out the matrix using the updated depth scanner
        _logger.Log($"Extracting array payload from _listing.js...", "WRITER:LISTING");
        JsonArray dataMatrix = _extractor.ExtractArrayPayload(rawText);

        // Find and mutate the explicit row match inside the matrix list array safely
        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count >= 5 && row[0]?.ToString() == id)
            {
                _logger.Log($"Found row for ID: {id}. Updating metadata...", "WRITER:LISTING");
                row[1] = JsonValue.Create(meta.Name);
                row[2] = JsonValue.Create(meta.Tier);
                row[3] = JsonValue.Create(meta.Prerequisite);
                // Maintain compliance with either 5 or 6 element schemas without out-of-bounds drops
                if (row.Count >= 6)
                {
                    row[5] = JsonValue.Create(meta.SourceBook);
                }
                else
                {
                    row[4] = JsonValue.Create(meta.SourceBook);
                }
                found = true;
                break;
            }
        }

        if (!found)
        {
            _logger.Log($"ID {id} not found in listing matrix!", "WRITER:LISTING WARNING");
        }

        // Symmetrical Target Bounds Isolation via backward index scanning tracking 
        ReadOnlySpan<char> sourceSpan = rawText.AsSpan();
        int finalCloseParenthesis = sourceSpan.LastIndexOf(')');

        int matrixEndIndex = -1;
        for (int i = finalCloseParenthesis - 1; i >= 0; i--)
        {
            if (sourceSpan[i] == ']') { matrixEndIndex = i; break; }
        }

        int bracketDepth = 0;
        int matrixStartIndex = -1;
        for (int i = matrixEndIndex; i >= 0; i--)
        {
            if (sourceSpan[i] == ']') bracketDepth++;
            if (sourceSpan[i] == '[') bracketDepth--;
            if (bracketDepth == 0) { matrixStartIndex = i; break; }
        }

        if (matrixStartIndex == -1 || matrixEndIndex == -1)
            throw new FormatException("Failed to safely align write splice boundaries for the listing matrix.");

        _logger.Log($"Splicing new matrix into file. Indices: {matrixStartIndex}-{matrixEndIndex}", "WRITER:LISTING");

        string header = rawText.Substring(0, matrixStartIndex);
        string footer = rawText.Substring(matrixEndIndex + 1);
        string newMatrixJson = dataMatrix.ToJsonString(GetModernOptions());

        await File.WriteAllTextAsync(path, header + newMatrixJson + footer);
        _logger.Log($"Listing file updated.", "WRITER:LISTING SUCCESS");
    }

    private async Task UpdateIndexFileAsync(string repositoryPath, string id, ExtractedMetadata meta)
    {
        string path = Path.Combine(repositoryPath, "_index.js");
        if (!File.Exists(path))
        {
            _logger.Log($"File not found: {path}", "WRITER:INDEX");
            return;
        }

        _logger.Log($"Updating index file: {path}", "WRITER:INDEX");
        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);

        _logger.Log($"Extracting object payload from _index.js...", "WRITER:INDEX");
        JsonNode root = _extractor.ExtractObjectPayload(rawText);

        // Construct a clean un-marked compound indexing search payload string
        string consolidatedIndexString = $"{meta.Name} {meta.Tier} Tier Prerequisite : {meta.Prerequisite} Benefit : {meta.BenefitText} {meta.SourceBook}.";
        root[id] = JsonValue.Create(consolidatedIndexString);

        _logger.Log($"Writing updated index for ID: {id}", "WRITER:INDEX");
        
        string newJson = root.ToJsonString(GetModernOptions());
        newJson = FormatForLegacy(newJson, rawText);

        await SplicedWriteAsync(path, rawText, newJson, '{', '}');
        _logger.Log($"Index file updated.", "WRITER:INDEX SUCCESS");
    }

    private async Task SplicedWriteAsync(string path, string originalText, string newJson, char openChar, char closeChar)
    {
        int openIndex = originalText.IndexOf(openChar);
        int closeIndex = originalText.LastIndexOf(closeChar);
        string header = originalText.Substring(0, openIndex);
        string footer = originalText.Substring(closeIndex + 1);
        await File.WriteAllTextAsync(path, header + newJson + footer);
    }

    private ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);
        if (!nameMatch.Success || string.IsNullOrWhiteSpace(nameMatch.Groups[1].Value))
        {
            throw new CompendiumValidationException(
                "Critical Malformed Record Markup: The item name heading block <h1>...</h1> tag sequence is missing or corrupted.",
                activeTargetShardPath, id);
        }

        string cleanName = nameMatch.Groups[1].Value;
        string tier = html.Contains("Epic Tier", StringComparison.OrdinalIgnoreCase) ? "Epic" :
                      html.Contains("Paragon", StringComparison.OrdinalIgnoreCase) ? "Paragon" : "Heroic";

        var prereqMatch = Regex.Match(html, @"<b>Prerequisite</b>:(.*?)<br>", RegexOptions.IgnoreCase);
        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        if (html.Contains("<b>Prerequisite</b>") && !prereqMatch.Success)
        {
            throw new CompendiumValidationException(
                "Malformed Markup: Found a Prerequisite marker, but it is missing the concluding trailing structural <br> tag.",
                activeTargetShardPath, id);
        }

        return new ExtractedMetadata
        {
            Name = Regex.Replace(cleanName, @"<[^>]+>", "").Trim(),
            Tier = tier,
            Prerequisite = prereqMatch.Success ? Regex.Replace(prereqMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : string.Empty,
            BenefitText = Regex.Match(html, @"<b>Benefit</b>:(.*?)<br>", RegexOptions.IgnoreCase).Success
                ? Regex.Replace(Regex.Match(html, @"<b>Benefit</b>:(.*?)<br>", RegexOptions.IgnoreCase).Groups[1].Value, @"<[^>]+>", "").Trim()
                : string.Empty,
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source"
        };
    }

    private async Task CreateBackupSnapshotAsync(string rootPath, string sourceFilePath)
    {
        string backupDir = Path.Combine(rootPath, ".backup");
        Directory.CreateDirectory(backupDir);
        string dest = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(sourceFilePath)}_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(sourceFilePath)}");
        File.Copy(sourceFilePath, dest, true);
        await Task.CompletedTask;
    }

    public async Task RestoreLatestBackupAsync(string repositoryPath, string targetFileName)
    {
        string backupDir = Path.Combine(repositoryPath, ".backup");
        if (!Directory.Exists(backupDir)) return;

        string simpleName = Path.GetFileNameWithoutExtension(targetFileName);
        string fileExtension = Path.GetExtension(targetFileName);

        var directoryInfo = new DirectoryInfo(backupDir);
        var files = directoryInfo.GetFiles($"{simpleName}_*{fileExtension}");

        Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

        if (files.Length > 0)
        {
            string cleanOriginalDestination = Path.Combine(repositoryPath, targetFileName);
            File.Copy(files[0].FullName, cleanOriginalDestination, true);
        }
        await Task.CompletedTask;
    }

    private class ExtractedMetadata
    {
        public required string Name { get; set; }
        public required string Tier { get; set; }
        public required string Prerequisite { get; set; }
        public required string BenefitText { get; set; }
        public required string SourceBook { get; set; }
    }
}