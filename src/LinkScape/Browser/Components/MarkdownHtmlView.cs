using System.Net;
using System.Text;

namespace Browser.Components;

internal sealed record MarkdownHtmlViewProps(
    string Markdown,
    bool IsError = false,
    double MinHeight = 160,
    double MaxHeight = 360);

internal sealed class MarkdownHtmlView : Component<MarkdownHtmlViewProps>
{
    private Microsoft.UI.Xaml.Controls.WebView2? _webView;
    private string? _lastHtml;

    public override Element Render()
    {
        var renderHeight = GetRenderHeight();

        return Border(null)
            .Set(host =>
            {
                EnsureWebView(host, renderHeight);
                NavigateIfNeeded(BuildHtml(Props.Markdown, Props.IsError));
            })
            .Height(renderHeight)
            .MinHeight(Props.MinHeight)
            .MaxHeight(Props.MaxHeight)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch);
    }

    private double GetRenderHeight()
    {
        var lineCount = Math.Max(4, (Props.Markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Length);
        return Math.Clamp(84 + lineCount * 18, Props.MinHeight, Props.MaxHeight);
    }

    private void EnsureWebView(Microsoft.UI.Xaml.Controls.Border host, double renderHeight)
    {
        if (_webView is not null)
        {
            _webView.Height = renderHeight;
            _webView.MinHeight = renderHeight;
            _webView.MaxHeight = renderHeight;

            if (!ReferenceEquals(host.Child, _webView))
            {
                host.Child = _webView;
            }

            return;
        }

        _webView = new Microsoft.UI.Xaml.Controls.WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Height = renderHeight,
            MinHeight = Props.MinHeight,
            MaxHeight = Props.MaxHeight,
            DefaultBackgroundColor = Microsoft.UI.Colors.Black
        };

        host.Child = _webView;
    }

    private async void NavigateIfNeeded(string html)
    {
        if (_webView is null || string.Equals(_lastHtml, html, StringComparison.Ordinal))
        {
            return;
        }

        _lastHtml = html;

        try
        {
            if (_webView.CoreWebView2 is null)
            {
                await _webView.EnsureCoreWebView2Async();
            }

            _webView.NavigateToString(html);
        }
        catch
        {
        }
    }

    private static string BuildHtml(string markdown, bool isError)
    {
        var body = RenderMarkdownSubset(markdown);
        var accent = isError ? "#fca5a5" : "#bfdbfe";
        var border = isError ? "#ef4444" : "#475569";

        return $$"""
            <!doctype html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>
                html, body { margin: 0; padding: 0; background: #0f172a; color: #f8fafc; font-family: Segoe UI, sans-serif; font-size: 12px; line-height: 1.42; }
                body { overflow-wrap: anywhere; }
                .card { box-sizing: border-box; min-height: 100vh; padding: 10px 12px; background: #111827; border: 1px solid {{border}}; border-radius: 12px; }
                h2 { color: {{accent}}; font-size: 15px; margin: 0 0 8px; }
                h3 { color: #fed7aa; font-size: 13px; margin: 12px 0 6px; }
                p { margin: 0 0 8px; }
                ul { margin: 4px 0 10px 18px; padding: 0; }
                li { margin: 3px 0; }
                table { border-collapse: collapse; width: 100%; margin: 8px 0 10px; background: rgba(30, 41, 59, 0.72); border-radius: 10px; overflow: hidden; }
                th, td { padding: 7px 8px; border-bottom: 1px solid #334155; text-align: left; vertical-align: top; }
                th { color: #bfdbfe; background: rgba(15, 23, 42, 0.9); }
                strong { color: #ffffff; }
                code { color: #fde68a; }
            </style>
            </head>
            <body><div class="card">{{body}}</div></body>
            </html>
            """;
    }

    private static string RenderMarkdownSubset(string markdown)
    {
        var lines = (markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var html = new StringBuilder();
        var paragraph = new StringBuilder();
        var inList = false;
        var inTable = false;

        void FlushParagraph()
        {
            if (paragraph.Length == 0)
            {
                return;
            }

            html.Append("<p>").Append(Inline(paragraph.ToString().Trim())).AppendLine("</p>");
            paragraph.Clear();
        }

        void CloseList()
        {
            if (!inList)
            {
                return;
            }

            html.AppendLine("</ul>");
            inList = false;
        }

        void CloseTable()
        {
            if (!inTable)
            {
                return;
            }

            html.AppendLine("</tbody></table>");
            inTable = false;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                FlushParagraph();
                CloseList();
                CloseTable();
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                CloseTable();
                html.Append("<h2>").Append(Inline(trimmed[3..])).AppendLine("</h2>");
                continue;
            }

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                CloseTable();
                html.Append("<h3>").Append(Inline(trimmed[4..])).AppendLine("</h3>");
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("• ", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseTable();

                if (!inList)
                {
                    html.AppendLine("<ul>");
                    inList = true;
                }

                html.Append("<li>").Append(Inline(trimmed[2..].Trim())).AppendLine("</li>");
                continue;
            }

            if (trimmed.Contains('|', StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();

                if (trimmed.Replace("|", string.Empty, StringComparison.Ordinal).All(ch => ch == '-' || ch == ':' || char.IsWhiteSpace(ch)))
                {
                    continue;
                }

                var cells = trimmed.Trim('|').Split('|').Select(cell => Inline(cell.Trim())).ToArray();

                if (!inTable)
                {
                    html.AppendLine("<table><tbody>");
                    inTable = true;
                }

                html.Append("<tr>");
                foreach (var cell in cells)
                {
                    html.Append("<td>").Append(cell).Append("</td>");
                }

                html.AppendLine("</tr>");
                continue;
            }

            CloseList();
            CloseTable();
            paragraph.Append(' ').Append(trimmed.TrimEnd(' ', '\\'));
        }

        FlushParagraph();
        CloseList();
        CloseTable();

        return html.ToString();
    }

    private static string Inline(string value)
    {
        var encoded = WebUtility.HtmlEncode(value ?? string.Empty);
        var parts = encoded.Split("**");

        if (parts.Length > 1)
        {
            var builder = new StringBuilder(parts[0]);

            for (var i = 1; i < parts.Length; i++)
            {
                builder.Append(i % 2 == 1 ? "<strong>" : "</strong>");
                builder.Append(parts[i]);
            }

            if (parts.Length % 2 == 0)
            {
                builder.Append("</strong>");
            }

            encoded = builder.ToString();
        }

        return encoded.Replace("  ", "<br>", StringComparison.Ordinal);
    }
}
