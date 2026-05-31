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

    public static readonly StyledProperty<string> CssStylesProperty =
        AvaloniaProperty.Register<ClassicHtmlPreviewer, string>(nameof(CssStyles), string.Empty);

    public string HtmlSource
    {
        get => GetValue(HtmlSourceProperty);
        set => SetValue(HtmlSourceProperty, value);
    }

    public string CssStyles
    {
        get => GetValue(CssStylesProperty);
        set => SetValue(CssStylesProperty, value);
    }

    static ClassicHtmlPreviewer()
    {
        HtmlSourceProperty.Changed.AddClassHandler<ClassicHtmlPreviewer>((control, e) => control.UpdateText());
        CssStylesProperty.Changed.AddClassHandler<ClassicHtmlPreviewer>((control, e) => control.UpdateText());
    }

    private void UpdateText()
    {
        string rawHtml = HtmlSource;
        string css = CssStyles;

        if (string.IsNullOrWhiteSpace(rawHtml))
        {
            Text = string.Empty;
            return;
        }

        try
        {
            // Update the underlying HTML core engine canvas document securely
            Text = $"<style>{css}</style><body>{rawHtml}</body>";
        }
        catch
        {
            // Fallback securely to raw text output visualization if the layout blocks are corrupted
            Text = rawHtml;
        }
    }
}