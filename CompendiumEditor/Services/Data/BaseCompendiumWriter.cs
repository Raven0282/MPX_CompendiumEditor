// File: Services/Data/BaseCompendiumWriter.cs
using CompendiumEditor.Exceptions;
using CompendiumEditor.Models;
using CompendiumEditor.Services.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CompendiumEditor.Services.Data;

/// <summary>
/// Abstract base class providing core multi-file synchronization and JSONP manipulation 
/// logic for specialized compendium writers.
/// </summary>
public abstract class BaseCompendiumWriter : ICategoryCompendiumWriter
{
    protected readonly ICompendiumExtractor _extractor;
    protected readonly IDiagnosticLogger _logger;

    protected BaseCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger)
    {
        _extractor = extractor;
        _logger = logger;
    }

    public abstract bool CanHandle(string repositoryPath);

    /// <summary>
    /// Core synchronization pipeline. Can be overridden if a category requires a unique sequence.
    /// </summary>
    public virtual async Task SaveRecordModificationAsync(string repositoryPath, CompendiumRecord record, string cleanHtmlMarkup)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
            throw new DirectoryNotFoundException("Target working repository space could not be verified.");

        _logger.Log($"[{GetType().Name}] Starting Save for ID: {record.Id}", "WRITER");

        // 1. Update the local shard
        string isolatedShardPath = await UpdateDataShardAsync(repositoryPath, record.Id, cleanHtmlMarkup, isAppend: false);

        // 2. Extract metadata
        var metadata = ExtractMetadataFromHtml(record.Id, cleanHtmlMarkup, isolatedShardPath);
        
        // OVERRIDE: Ensure the SourceBook from the UI dialog/record is preserved 
        // for the listing index, rather than being overwritten by re-extraction from template HTML.
        metadata.SourceBook = record.SourceBook;

        // 3. Update listing matrix
        await UpdateListingFileAsync(repositoryPath, record.Id, metadata, isAppend: false);

        // 4. Update local search index
        await UpdateIndexFileAsync(repositoryPath, record.Id, metadata, cleanHtmlMarkup);

        // 5. Synchronize Upward (Top-Level)
        await SynchronizeUpwardAsync(repositoryPath, record.Id, cleanHtmlMarkup, metadata);

        _logger.Log($"[{GetType().Name}] Save successful for ID: {record.Id}", "WRITER SUCCESS");
    }

    public virtual async Task AppendRecordAsync(string repositoryPath, CompendiumRecord record, string cleanHtmlMarkup)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
            throw new DirectoryNotFoundException("Target working repository space could not be verified.");

        _logger.Log($"[{GetType().Name}] Starting Append for ID: {record.Id}", "WRITER");

        // 1. Append to the local shard (may create it)
        string isolatedShardPath = await UpdateDataShardAsync(repositoryPath, record.Id, cleanHtmlMarkup, isAppend: true);

        // 2. Extract metadata
        var metadata = ExtractMetadataFromHtml(record.Id, cleanHtmlMarkup, isolatedShardPath);
        
        // OVERRIDE: Ensure the SourceBook from the UI dialog/record is preserved 
        // for the listing index, rather than being overwritten by re-extraction from template HTML.
        metadata.SourceBook = record.SourceBook;

        // 3. Append to listing matrix
        await UpdateListingFileAsync(repositoryPath, record.Id, metadata, isAppend: true);

        // 4. Append to local search index (UpdateIndexFileAsync handles new keys naturally)
        await UpdateIndexFileAsync(repositoryPath, record.Id, metadata, cleanHtmlMarkup);

        // 5. Synchronize Upward (Top-Level)
        await SynchronizeUpwardAsync(repositoryPath, record.Id, cleanHtmlMarkup, metadata);

        _logger.Log($"[{GetType().Name}] Append successful for ID: {record.Id}", "WRITER SUCCESS");
    }

    private async Task SynchronizeUpwardAsync(string repositoryPath, string id, string htmlMarkup, ExtractedMetadata metadata)
    {
        string? topLevelPath = FindTopLevelDirectory(repositoryPath);
        if (!string.IsNullOrEmpty(topLevelPath))
        {
            _logger.Log($"Resolved Top-Level directory: {topLevelPath}", "WRITER:SYNC");
            await UpdateTopLevelShardAsync(topLevelPath, id, htmlMarkup);
            await UpdateTopLevelIndexAsync(topLevelPath, id, metadata);
            await UpdateTopLevelCatalogAsync(topLevelPath, id, metadata);
        }
        else
        {
            _logger.Log("No top-level directory found (searched upward for catalog.js). Skipping global sync.", "WRITER:SYNC WARNING");
        }
    }

    /// <summary>
    /// Searches upward from the starting path to find the directory containing 'catalog.js'.
    /// This is more robust than assuming exactly one directory level up.
    /// </summary>
    private string? FindTopLevelDirectory(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        
        // If the user selected a CATEGORY folder (e.g. \monster), 
        // the top level is almost certainly the immediate parent.
        // We look for 'catalog.js' as the marker for the top level.
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "catalog.js")))
            {
                return current.FullName;
            }
            
            // If we are IN the category folder and catalog.js isn't here, 
            // the top level is likely the parent.
            if (current.Parent != null && File.Exists(Path.Combine(current.Parent.FullName, "catalog.js")))
            {
                return current.Parent.FullName;
            }

            current = current.Parent;
            // Safety: Don't go all the way to the root
            if (current?.Parent == null) break; 
        }

        return null;
    }

    protected abstract ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath);

    #region Shared Infrastructure Logic

    /// <summary>
    /// Converts HTML markup to plain text, preserving the dense stat-block format 
    /// required for legacy search indices.
    /// </summary>
    protected string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        
        // Convert breaks/headers to spaces to prevent word mashing
        string text = Regex.Replace(html, @"<(br|p|h1|h2|tr|td)[^>]*>", " ", RegexOptions.IgnoreCase);
        // Strip remaining tags
        text = Regex.Replace(text, @"<[^>]*>", "");
        // Decode common entities and normalize whitespace
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    protected JsonSerializerOptions GetModernOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentSize = 4,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    protected string FormatForLegacy(string json, string originalFragment)
    {
        // 1. Handle Unquoted Keys
        bool usesUnquotedKeys = !originalFragment.Contains("\":") && originalFragment.Contains(":");
        string processed = json;
        if (usesUnquotedKeys)
        {
            processed = Regex.Replace(processed, @"^\s*""(?<key>[a-zA-Z0-9_]+)""\s*:", "    ${key}:", RegexOptions.Multiline);
        }

        // 2. Restore Literal Unicode Characters (⚔, ✦, etc.)
        // System.Text.Json escapes non-ASCII even with UnsafeRelaxedJsonEscaping.
        // We regex find \uXXXX and convert back to literal UTF-8 characters.
        processed = Regex.Replace(processed, @"\\u(?<val>[a-fA-F0-9]{4})", m =>
        {
            int code = int.Parse(m.Groups["val"].Value, System.Globalization.NumberStyles.HexNumber);
            return ((char)code).ToString();
        });

        return processed;
    }

    protected int ExtractNumericId(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        var match = Regex.Match(id, @"\d+");
        if (match.Success && int.TryParse(match.Value, out int result))
            return result;
        return 0;
    }

    protected async Task<string> UpdateDataShardAsync(string repositoryPath, string id, string htmlMarkup, bool isAppend)
    {
        int n = ExtractNumericId(id) % 20;
        string targetFile = Path.Combine(repositoryPath, $"data{n}.js");
        
        if (!File.Exists(targetFile) && !isAppend)
        {
            string[] shards = Directory.GetFiles(repositoryPath, "data*.js");
            foreach (string file in shards)
            {
                string text = await File.ReadAllTextAsync(file);
                if (text.Contains($"\"{id}\":") || text.Contains($"'{id}':"))
                {
                    targetFile = file;
                    break;
                }
            }
        }

        if (!File.Exists(targetFile))
        {
            if (isAppend)
            {
                _logger.Log($"Target shard data{n}.js not found. Requesting creation via logic flow.", "WRITER:APPEND");
                // For now, we auto-create the shard with valid JSONP wrapper if it's missing during an append
                // In a future turn, we can add the explicit UI prompt if desired.
                string initialContent = $"od.reader.jsonp_batch_data(\"{n}\", {{ }})";
                await File.WriteAllTextAsync(targetFile, initialContent, new System.Text.UTF8Encoding(false));
            }
            else
            {
                throw new FileNotFoundException($"Could not locate the explicit data shard mapping the ID '{id}'.");
            }
        }

        string content = await File.ReadAllTextAsync(targetFile);
        await CreateBackupSnapshotAsync(repositoryPath, targetFile);

        JsonNode root = _extractor.ExtractObjectPayload(content);
        root[id] = JsonValue.Create(htmlMarkup);

        string newJson = root.ToJsonString(GetModernOptions());
        newJson = FormatForLegacy(newJson, content);

        await SplicedWriteAsync(targetFile, content, newJson, '{', '}');
        return targetFile;
    }

    protected virtual async Task UpdateListingFileAsync(string repositoryPath, string id, ExtractedMetadata meta, bool isAppend)
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
        int idxTier = headers.IndexOf("Tier");
        if (idxTier == -1) idxTier = headers.IndexOf("Category");
        int idxPrereq = headers.IndexOf("Prerequisite");
        if (idxPrereq == -1) idxPrereq = headers.IndexOf("Type");
        int idxSource = headers.IndexOf("SourceBook");

        bool found = false;
        foreach (var node in dataMatrix)
        {
            if (node is JsonArray row && row.Count > 0 && row[0]?.ToString() == id)
            {
                if (idxName != -1 && row.Count > idxName) row[idxName] = JsonValue.Create(meta.Name);
                if (idxTier != -1 && row.Count > idxTier) row[idxTier] = JsonValue.Create(meta.Tier);
                if (idxPrereq != -1 && row.Count > idxPrereq) row[idxPrereq] = JsonValue.Create(meta.Prerequisite);
                if (idxSource != -1 && row.Count > idxSource) row[idxSource] = JsonValue.Create(meta.SourceBook);
                found = true;
                break;
            }
        }

        if (!found && isAppend)
        {
            _logger.Log($"Appending new record row to _listing.js matrix for ID: {id}", "WRITER:LISTING");
            var newRow = new JsonArray();
            for (int i = 0; i < headers.Count; i++)
            {
                if (i == 0) newRow.Add(JsonValue.Create(id));
                else if (i == idxName) newRow.Add(JsonValue.Create(meta.Name));
                else if (i == idxTier) newRow.Add(JsonValue.Create(meta.Tier));
                else if (i == idxPrereq) newRow.Add(JsonValue.Create(meta.Prerequisite));
                else if (i == idxSource) newRow.Add(JsonValue.Create(meta.SourceBook));
                else newRow.Add(JsonValue.Create(""));
            }
            dataMatrix.Add(newRow);
        }

        int matrixEndIndex = -1;
        int finalCloseParenthesis = sourceSpan.LastIndexOf(')');
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

        string header = rawText.Substring(0, matrixStartIndex);
        string footer = rawText.Substring(matrixEndIndex + 1);
        string newMatrixJson = dataMatrix.ToJsonString(GetModernOptions());

        await File.WriteAllTextAsync(path, header + newMatrixJson + footer);
    }

    protected virtual async Task UpdateIndexFileAsync(string repositoryPath, string id, ExtractedMetadata meta, string htmlMarkup)
    {
        string path = Path.Combine(repositoryPath, "_index.js");
        if (!File.Exists(path)) return;

        await CreateBackupSnapshotAsync(repositoryPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        JsonNode root = _extractor.ExtractObjectPayload(rawText);

        string indexText = string.IsNullOrEmpty(meta.BenefitText) ? meta.Name : $"{meta.Name} {meta.Tier} Tier Prerequisite : {meta.Prerequisite} Benefit : {meta.BenefitText} {meta.SourceBook}.";
        root[id] = JsonValue.Create(indexText);

        string newJson = root.ToJsonString(GetModernOptions());
        newJson = FormatForLegacy(newJson, rawText);
        await SplicedWriteAsync(path, rawText, newJson, '{', '}');
    }

    protected async Task UpdateTopLevelShardAsync(string parentPath, string id, string htmlMarkup)
    {
        int n = ExtractNumericId(id) % 20;
        string path = Path.Combine(parentPath, $"data{n}.js");
        if (!File.Exists(path)) return;

        await CreateBackupSnapshotAsync(parentPath, path);
        string content = await File.ReadAllTextAsync(path);
        JsonNode root = _extractor.ExtractObjectPayload(content);
        root[id] = JsonValue.Create(htmlMarkup);

        string newJson = root.ToJsonString(GetModernOptions());
        newJson = FormatForLegacy(newJson, content);
        await SplicedWriteAsync(path, content, newJson, '{', '}');
    }

    protected async Task UpdateTopLevelIndexAsync(string parentPath, string id, ExtractedMetadata meta)
    {
        string path = Path.Combine(parentPath, "index.js");
        if (!File.Exists(path))
        {
            _logger.Log($"Top-level index NOT found at: {path}", "WRITER:TOP_INDEX ERROR");
            return;
        }

        _logger.Log($"Updating top-level index: {path}", "WRITER:TOP_INDEX");
        await CreateBackupSnapshotAsync(parentPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        
        _logger.Log($"Extracting payload from index.js ({rawText.Length} bytes)...", "WRITER:TOP_INDEX");
        JsonNode root = _extractor.ExtractObjectPayload(rawText);
        var obj = root.AsObject();
        _logger.Log($"Index parsed. Current entries: {obj.Count}", "WRITER:TOP_INDEX");

        // Top-level index is Name -> ID. 
        // REVERSION: We must preserve the ORIGINAL case of the key if it already exists.
        var keysToRemove = new List<string>();
        string? existingCaseKey = null;

        foreach (var kvp in obj)
        {
            if (kvp.Value?.ToString() == id) 
            {
                keysToRemove.Add(kvp.Key);
                // Check if this existing key matches the new record name (case-insensitive)
                if (string.Equals(kvp.Key, meta.Name, StringComparison.OrdinalIgnoreCase))
                {
                    existingCaseKey = kvp.Key;
                    _logger.Log($"Found existing case key to preserve: '{existingCaseKey}'", "WRITER:TOP_INDEX");
                }
                else
                {
                    _logger.Log($"Removing stale index key: '{kvp.Key}' -> {id}", "WRITER:TOP_INDEX");
                }
            }
        }
        foreach (var key in keysToRemove) obj.Remove(key);

        // Add mapping: Use existing case if found, otherwise use meta.Name as-is (preserving its case)
        string finalKey = existingCaseKey ?? meta.Name;
        obj[finalKey] = JsonValue.Create(id);
        _logger.Log($"Mapped index entry: '{finalKey}' -> {id}", "WRITER:TOP_INDEX");

        string newJson = root.ToJsonString(GetModernOptions());
        newJson = FormatForLegacy(newJson, rawText);
        
        _logger.Log($"Committing spliced write to {path}...", "WRITER:TOP_INDEX");
        await SplicedWriteAsync(path, rawText, newJson, '{', '}');
        _logger.Log("Top-level index synchronization complete.", "WRITER:TOP_INDEX SUCCESS");
    }

    protected async Task UpdateTopLevelCatalogAsync(string parentPath, string id, ExtractedMetadata meta)
    {
        string path = Path.Combine(parentPath, "catalog.js");
        if (!File.Exists(path)) return;

        _logger.Log($"Updating top-level catalog counts: {path}", "WRITER:TOP_CATALOG");
        await CreateBackupSnapshotAsync(parentPath, path);
        string rawText = await File.ReadAllTextAsync(path);
        
        JsonNode root = _extractor.ExtractObjectPayload(rawText);
        var obj = root.AsObject();

        // 1. Identify category from ID (e.g. "feat123" -> "feat")
        string category = Regex.Replace(id, @"\d+", "").ToLowerInvariant();

        // 2. Get current actual count from the category's _listing.js
        // parentPath is the root, we need to find the category subfolder.
        // Usually repositoryPath (passed in public methods) is parentPath/category.
        // We can infer the local listing path by looking at the category name.
        string categoryDir = Path.Combine(parentPath, category);
        string listingPath = Path.Combine(categoryDir, "_listing.js");

        if (File.Exists(listingPath))
        {
            try 
            {
                string listingContent = await File.ReadAllTextAsync(listingPath);
                JsonArray matrix = _extractor.ExtractArrayPayload(listingContent);
                int actualCount = matrix.Count;

                _logger.Log($"Syncing {category} count to {actualCount} in catalog.js", "WRITER:TOP_CATALOG");
                obj[category] = JsonValue.Create(actualCount);

                string newJson = root.ToJsonString(GetModernOptions());
                newJson = FormatForLegacy(newJson, rawText);
                await SplicedWriteAsync(path, rawText, newJson, '{', '}');
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "WRITER:TOP_CATALOG_SYNC");
            }
        }
        else
        {
            _logger.Log($"Could not find _listing.js for {category} to verify count. Skipping catalog update.", "WRITER:TOP_CATALOG WARNING");
        }
    }

    protected async Task SplicedWriteAsync(string path, string originalText, string newJson, char openChar, char closeChar)
    {
        int openIndex = originalText.IndexOf(openChar);
        int closeIndex = originalText.LastIndexOf(closeChar);
        string header = originalText.Substring(0, openIndex);
        string footer = originalText.Substring(closeIndex + 1);
        
        // REVERSION: Force UTF-8 WITHOUT BOM to match legacy application standards
        var encoding = new System.Text.UTF8Encoding(false);
        await File.WriteAllTextAsync(path, header + newJson + footer, encoding);
    }

    protected async Task CreateBackupSnapshotAsync(string rootPath, string sourceFilePath)
    {
        string backupDir = Path.Combine(rootPath, ".backup");
        Directory.CreateDirectory(backupDir);
        string dest = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(sourceFilePath)}_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(sourceFilePath)}");
        File.Copy(sourceFilePath, dest, true);
        await Task.CompletedTask;
    }

    #endregion

    protected class ExtractedMetadata
    {
        public required string Name { get; set; }
        public required string Tier { get; set; }
        public required string Prerequisite { get; set; }
        public required string BenefitText { get; set; }
        public required string SourceBook { get; set; }
    }
}
