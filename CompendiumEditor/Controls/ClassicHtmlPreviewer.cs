// File: Controls/ClassicHtmlPreviewer.cs
using Avalonia;
using Avalonia.Controls;
using System;
using TheArtOfDev.HtmlRenderer.Avalonia;
using static System.Net.Mime.MediaTypeNames;

namespace CompendiumEditor.Controls;

/// <summary>
/// A control wrapper encapsulating the high-fidelity HTML Renderer engine for historical compendium stylesheets.
/// </summary>
public class ClassicHtmlPreviewer : HtmlLabel
{
    public static readonly StyledProperty<string> HtmlSourceProperty =
        AvaloniaProperty.Register<ClassicHtmlPreviewer, string>(nameof(HtmlSource), string.Empty);

    public string HtmlSource
    {
        get => GetValue(HtmlSourceProperty);
        set => SetValue(HtmlSourceProperty, value);
    }

    static ClassicHtmlPreviewer()
    {
        HtmlSourceProperty.Changed.AddClassHandler<ClassicHtmlPreviewer>((control, e) => control.OnHtmlSourceChanged(e));
    }

    private void OnHtmlSourceChanged(AvaloniaPropertyChangedEventArgs e)
    {
        string rawHtml = e.GetNewValue<string>();

        if (string.IsNullOrWhiteSpace(rawHtml))
        {
            Text = string.Empty;
            return;
        }

        try
        {
            // Inject localized inline stylesheet variables matching the compendium layout profile
            // This guarantees uniform presentation colors regardless of global window margins
            string scopedStyleHeader = @"
                <style>
                    body { 
                        font-family: 'Segoe UI', Helvetica, Arial, sans-serif; 
                        font-size: 13px; 
                        margin: 0; 
                        padding: 0;
                        word-wrap: break-word;
                        max-width: 200px
                        color: #333;
                    }
                    h1.player { 
                        font-size: 18px; 
                        color: #B11226; 
                        font-weight: bold; 
                        margin-bottom: 4px; 
                        padding-bottom: 2px;
                        border-bottom: 1px solid #B11226;
                    }
                    p.flavor { 
                        font-style: italic; 
                        margin-top: 2px; 
                        margin-bottom: 6px; 
                    }
                    p.publishedIn { 
                        font-size: 11px; 
                        font-style: italic; 
                        color: #666; 
                        margin-top: 10px; 
                    }
                </style>";

            // Update the underlying HTML core engine canvas document securely
            Text = $"{scopedStyleHeader}<body>{rawHtml}</body>";
        }
        catch
        {
            // Fallback securely to raw text output visualization if the layout blocks are corrupted
            Text = rawHtml;
        }
    }
}