// File: Controls/NativeHtmlPreviewer.cs
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System;
using System.Text.RegularExpressions;

namespace CompendiumEditor.Controls;

/// <summary>
/// A lightweight, custom layout block control parsing structured D&D HTML fragments into inline native Run elements.
/// </summary>
public class NativeHtmlPreviewer : SelectableTextBlock
{
    public static readonly StyledProperty<string> HtmlSourceProperty =
        AvaloniaProperty.Register<NativeHtmlPreviewer, string>(nameof(HtmlSource), string.Empty);

    public string HtmlSource
    {
        get => GetValue(HtmlSourceProperty);
        set => SetValue(HtmlSourceProperty, value);
    }

    static NativeHtmlPreviewer()
    {
        HtmlSourceProperty.Changed.AddClassHandler<NativeHtmlPreviewer>((x, e) => x.OnHtmlSourceChanged(e));
    }

    private void OnHtmlSourceChanged(AvaloniaPropertyChangedEventArgs e)
    {
        Inlines?.Clear();
        string rawHtml = e.GetNewValue<string>();

        if (string.IsNullOrWhiteSpace(rawHtml))
            return;

        try
        {
            // Tokenize layout fragments by splitting content blocks on tags
            string[] tokens = Regex.Split(rawHtml, @"(<[^>]+>)");
            bool isBold = false;
            bool isHeader = false;

            foreach (string token in tokens)
            {
                if (string.IsNullOrEmpty(token)) continue;

                string normalizedToken = token.ToLowerInvariant();
                switch (normalizedToken)
                {
                    case "<h1>" or "<h1 class=player>":
                        isHeader = true;
                        continue;
                    case "</h1>":
                        isHeader = false;
                        Inlines?.Add(new LineBreak());
                        continue;
                    case "<b>":
                        isBold = true;
                        continue;
                    case "</b>":
                        isBold = false;
                        continue;
                    case "<br>" or "<br/>" or "<br />":
                        Inlines?.Add(new LineBreak());
                        continue;
                    case "<p class=flavor>" or "<p class=publishedIn>" or "<p>":
                        continue;
                    case "</p>":
                        Inlines?.Add(new LineBreak());
                        Inlines?.Add(new LineBreak());
                        continue;
                }

                // Append text node runs configured against parsed block rules
                var run = new Run { Text = token };

                if (isHeader)
                {
                    run.FontSize = 20;
                    run.FontWeight = FontWeight.Bold;
                    run.Foreground = Brushes.Crimson;
                }
                else if (isBold)
                {
                    run.FontWeight = FontWeight.Bold;
                }

                Inlines?.Add(run);
            }
        }
        catch
        {
            // Fallback gracefully to basic raw text insertion if parsing crashes
            Text = rawHtml;
        }
    }
}