// File: Services/Data/PreviewStylingService.cs
using System;
using System.IO;
using CompendiumEditor.Services.Logging;

namespace CompendiumEditor.Services.Data;

public class PreviewStylingService : IPreviewStylingService
{
    private readonly IDiagnosticLogger _logger;

    public PreviewStylingService(IDiagnosticLogger logger)
    {
        _logger = logger;
    }

    private const string BaseCss = @"
        body { 
            font-family: 'Segoe UI', Helvetica, Arial, sans-serif; 
            font-size: 13px; 
            margin: 0; 
            padding: 10px 10px 50px 10px;
            word-wrap: break-word;
            overflow-wrap: break-word;

            word-break: normal;
            line-height: 1.4;
        }
        h1 { font-size: 18px; font-weight: bold; margin: 0 0 4px 0; padding-bottom: 2px; border-bottom: 1px solid; }
        h1.player, h1.atwillpower, h1.encounterpower, h1.dailypower, h1.magicitem, h1.monster, h1.trap { 
            display: block; 
        }
        p { margin: 6px 0; white-space: pre-wrap; }
        p.flavor { font-style: italic; }
        p.publishedIn { font-size: 11px; font-style: italic; margin-top: 15px; border-top: 1px solid #ccc; padding-top: 5px; }
        b { font-weight: bold; }
        i { font-style: italic; }
        table { max-width: 90%; border-collapse: collapse; margin: 10px 0; table-layout: fixed; }
        th { text-align: left; font-weight: bold; border-bottom: 1px solid #999; padding: 2px; word-wrap: break-word; }
        td { padding: 2px; vertical-align: top; word-wrap: break-word; }
        .ritualstats { display: block; padding: 5px; margin-bottom: 10px; border: 1px solid #ccc; }
        .indent { padding-left: 15px; }
    ";

    private const string LightModeCss = @"
        body { color: #333; background-color: #fff; }
        h1 { color: #B11226; border-bottom-color: #B11226; }
        p.publishedIn { color: #666; border-top-color: #eee; }
        .ritualstats { background-color: #f9f9f9; border-color: #ddd; }
        tr:nth-child(even) { background-color: #f2f2f2; }
    ";

    private const string DarkModeCss = @"
        body { color: #E0E0E0; background-color: #1E1E1E; }
        h1 { color: #FF4D4D; border-bottom-color: #FF4D4D; }
        p.publishedIn { color: #AAA; border-top-color: #444; }
        .ritualstats { background-color: #2D2D2D; border-color: #444; }
        tr:nth-child(even) { background-color: #2D2D2D; }
        b { color: #FFF; }
    ";

    public string GetActiveStyles(bool isDarkMode, string? repositoryPath)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(BaseCss);
        sb.AppendLine(isDarkMode ? DarkModeCss : LightModeCss);

        // 1. Load Global Styles (AppData)
        string globalStylesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CompendiumEditor", "Styles");
        if (!Directory.Exists(globalStylesDir))
        {
            Directory.CreateDirectory(globalStylesDir);
        }

        string globalCssPath = Path.Combine(globalStylesDir, "_preview.css");
        if (File.Exists(globalCssPath))
        {
            try
            {
                _logger.Log($"Injecting global styles from {globalCssPath}", "STYLING:GLOBAL");
                string globalCss = File.ReadAllText(globalCssPath);
                sb.AppendLine("/* Global Styles */");
                sb.AppendLine(globalCss);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "STYLING:GLOBAL_CSS");
            }
        }

        // 2. Load Local Overrides (Repository)
        if (!string.IsNullOrEmpty(repositoryPath))
        {
            string userCssPath = Path.Combine(repositoryPath, "_preview.css");
            if (File.Exists(userCssPath))
            {
                try
                {
                    _logger.Log("Injecting repository-specific overrides from _preview.css", "STYLING:LOCAL");
                    string userCss = File.ReadAllText(userCssPath);
                    sb.AppendLine("/* Local Overrides */");
                    sb.AppendLine(userCss);
                }
                catch (Exception ex)
                {
                    _logger.LogException(ex, "STYLING:LOCAL_CSS");
                }
            }
        }

        return sb.ToString();
    }
}
