// File: Services/Data/GeneralCompendiumWriter.cs
using CompendiumEditor.Exceptions;
using CompendiumEditor.Services.Logging;
using System;
using System.Text.RegularExpressions;

namespace CompendiumEditor.Services.Data;

/// <summary>
/// A general-purpose compendium writer that handles default categories (Feat, Glossary, etc.)
/// using flexible regex patterns.
/// </summary>
public class GeneralCompendiumWriter : BaseCompendiumWriter
{
    public GeneralCompendiumWriter(ICompendiumExtractor extractor, IDiagnosticLogger logger) 
        : base(extractor, logger)
    {
    }

    /// <summary>
    /// The General writer acts as the fallback for all repositories.
    /// </summary>
    public override bool CanHandle(string repositoryPath) => true;

    protected override ExtractedMetadata ExtractMetadataFromHtml(string id, string html, string activeTargetShardPath)
    {
        var nameMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);
        if (!nameMatch.Success || string.IsNullOrWhiteSpace(nameMatch.Groups[1].Value))
        {
            throw new CompendiumValidationException(
                "Critical Malformed Record Markup: The item name heading block <h1>...</h1> tag sequence is missing or corrupted.",
                activeTargetShardPath, id);
        }

        string cleanName = nameMatch.Groups[1].Value;
        
        string tier = "Heroic";
        if (html.Contains("Epic Tier", StringComparison.OrdinalIgnoreCase)) tier = "Epic";
        else if (html.Contains("Paragon", StringComparison.OrdinalIgnoreCase)) tier = "Paragon";
        else if (html.Contains("Rules", StringComparison.OrdinalIgnoreCase)) tier = "Rules";
        else if (html.Contains("Monsters", StringComparison.OrdinalIgnoreCase)) tier = "Monsters";

        var prereqMatch = Regex.Match(html, @"<b>Prerequisite</b>:(.*?)<br>", RegexOptions.IgnoreCase);
        var sourceMatch = Regex.Match(html, @"Published in (.*?)(?:, page|\.</p>)", RegexOptions.IgnoreCase);

        string prereq = string.Empty;
        if (prereqMatch.Success)
        {
            prereq = Regex.Replace(prereqMatch.Groups[1].Value, @"<[^>]+>", "").Trim();
        }

        return new ExtractedMetadata
        {
            Name = Regex.Replace(cleanName, @"<[^>]+>", "").Trim(),
            Tier = tier,
            Prerequisite = prereq,
            BenefitText = Regex.Match(html, @"<b>Benefit</b>:(.*?)<br>", RegexOptions.IgnoreCase).Success
                ? Regex.Replace(Regex.Match(html, @"<b>Benefit</b>:(.*?)<br>", RegexOptions.IgnoreCase).Groups[1].Value, @"<[^>]+>", "").Trim()
                : string.Empty,
            SourceBook = sourceMatch.Success ? Regex.Replace(sourceMatch.Groups[1].Value, @"<[^>]+>", "").Trim() : "Unknown Source"
        };
    }
}
