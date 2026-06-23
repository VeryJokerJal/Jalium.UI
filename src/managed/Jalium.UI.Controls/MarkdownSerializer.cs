using System.Text;

namespace Jalium.UI.Controls;

/// <summary>
/// Serializes a parsed Markdown block tree into plain text, Markdown source, HTML, or RTF.
/// Used by the <see cref="Markdown"/> control to expose its content for copying and for
/// integration scenarios such as translation.
/// </summary>
internal static class MarkdownSerializer
{
    #region Plain text (no markers)

    public static string ToPlainText(IReadOnlyList<MarkdownBlock> blocks)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            AppendBlockPlainText(sb, blocks[i], 0);
            if (i < blocks.Count - 1)
            {
                sb.Append('\n');
            }
        }
        return sb.ToString().TrimEnd('\n');
    }

    private static void AppendBlockPlainText(StringBuilder sb, MarkdownBlock block, int indent)
    {
        switch (block)
        {
            case MarkdownHeadingBlock heading:
                sb.Append(InlinesToPlainText(heading.Inlines)).Append('\n');
                break;

            case MarkdownParagraphBlock paragraph:
                sb.Append(InlinesToPlainText(paragraph.Inlines)).Append('\n');
                break;

            case MarkdownListBlock list:
                for (var i = 0; i < list.Items.Count; i++)
                {
                    var marker = list.Ordered ? $"{list.StartIndex + i}. " : "• ";
                    sb.Append(' ', indent * 2).Append(marker);
                    var item = list.Items[i];
                    for (var b = 0; b < item.Blocks.Count; b++)
                    {
                        if (b > 0)
                        {
                            sb.Append(' ', indent * 2 + marker.Length);
                        }
                        AppendBlockPlainText(sb, item.Blocks[b], indent + 1);
                    }
                }
                break;

            case MarkdownQuoteBlock quote:
                foreach (var child in quote.Blocks)
                {
                    AppendBlockPlainText(sb, child, indent);
                }
                break;

            case MarkdownCodeBlock code:
                sb.Append(code.Text).Append('\n');
                break;

            case MarkdownRuleBlock:
                sb.Append("———").Append('\n');
                break;

            case MarkdownTableBlock table:
                foreach (var row in table.HeaderRows)
                {
                    AppendTableRowPlainText(sb, row);
                }
                foreach (var row in table.Rows)
                {
                    AppendTableRowPlainText(sb, row);
                }
                break;
        }
    }

    private static void AppendTableRowPlainText(StringBuilder sb, MarkdownTableRow row)
    {
        for (var c = 0; c < row.Cells.Count; c++)
        {
            if (c > 0) sb.Append('\t');
            sb.Append(InlinesToPlainText(row.Cells[c]));
        }
        sb.Append('\n');
    }

    private static string InlinesToPlainText(IReadOnlyList<MarkdownInline> inlines)
    {
        var sb = new StringBuilder();
        AppendInlinesPlainText(sb, inlines);
        return sb.ToString();
    }

    private static void AppendInlinesPlainText(StringBuilder sb, IReadOnlyList<MarkdownInline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline text: sb.Append(text.Text); break;
                case MarkdownStrongInline strong: AppendInlinesPlainText(sb, strong.Children); break;
                case MarkdownEmphasisInline emphasis: AppendInlinesPlainText(sb, emphasis.Children); break;
                case MarkdownCodeInline code: sb.Append(code.Text); break;
                case MarkdownLinkInline link: AppendInlinesPlainText(sb, link.Children); break;
                case MarkdownLineBreakInline: sb.Append('\n'); break;
            }
        }
    }

    #endregion

    #region Markdown source

    public static string ToMarkdown(IReadOnlyList<MarkdownBlock> blocks)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            AppendBlockMarkdown(sb, blocks[i]);
            if (i < blocks.Count - 1)
            {
                sb.Append('\n');
            }
        }
        return sb.ToString().TrimEnd('\n');
    }

    private static void AppendBlockMarkdown(StringBuilder sb, MarkdownBlock block)
    {
        switch (block)
        {
            case MarkdownHeadingBlock heading:
                sb.Append('#', heading.Level).Append(' ').Append(InlinesToMarkdown(heading.Inlines)).Append('\n');
                break;

            case MarkdownParagraphBlock paragraph:
                sb.Append(InlinesToMarkdown(paragraph.Inlines)).Append('\n');
                break;

            case MarkdownListBlock list:
                for (var i = 0; i < list.Items.Count; i++)
                {
                    var item = list.Items[i];
                    var marker = list.Ordered ? $"{list.StartIndex + i}. " : "- ";
                    if (item.TaskState is bool task)
                    {
                        marker += task ? "[x] " : "[ ] ";
                    }
                    sb.Append(marker);
                    for (var b = 0; b < item.Blocks.Count; b++)
                    {
                        AppendBlockMarkdown(sb, item.Blocks[b]);
                    }
                }
                break;

            case MarkdownQuoteBlock quote:
                {
                    var inner = new StringBuilder();
                    foreach (var child in quote.Blocks)
                    {
                        AppendBlockMarkdown(inner, child);
                    }
                    foreach (var line in inner.ToString().TrimEnd('\n').Split('\n'))
                    {
                        sb.Append("> ").Append(line).Append('\n');
                    }
                    break;
                }

            case MarkdownCodeBlock code:
                sb.Append("```").Append(code.Language ?? string.Empty).Append('\n')
                  .Append(code.Text).Append('\n').Append("```").Append('\n');
                break;

            case MarkdownRuleBlock:
                sb.Append("---").Append('\n');
                break;

            case MarkdownTableBlock table:
                foreach (var row in table.HeaderRows)
                {
                    AppendTableRowMarkdown(sb, row);
                    AppendTableSeparator(sb, row.Cells.Count);
                }
                foreach (var row in table.Rows)
                {
                    AppendTableRowMarkdown(sb, row);
                }
                break;
        }
    }

    private static void AppendTableRowMarkdown(StringBuilder sb, MarkdownTableRow row)
    {
        sb.Append("| ");
        for (var c = 0; c < row.Cells.Count; c++)
        {
            if (c > 0) sb.Append(" | ");
            sb.Append(InlinesToMarkdown(row.Cells[c]));
        }
        sb.Append(" |\n");
    }

    private static void AppendTableSeparator(StringBuilder sb, int columns)
    {
        sb.Append('|');
        for (var c = 0; c < columns; c++)
        {
            sb.Append(" --- |");
        }
        sb.Append('\n');
    }

    private static string InlinesToMarkdown(IReadOnlyList<MarkdownInline> inlines)
    {
        var sb = new StringBuilder();
        AppendInlinesMarkdown(sb, inlines);
        return sb.ToString();
    }

    private static void AppendInlinesMarkdown(StringBuilder sb, IReadOnlyList<MarkdownInline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline text: sb.Append(text.Text); break;
                case MarkdownStrongInline strong: sb.Append("**"); AppendInlinesMarkdown(sb, strong.Children); sb.Append("**"); break;
                case MarkdownEmphasisInline emphasis: sb.Append('*'); AppendInlinesMarkdown(sb, emphasis.Children); sb.Append('*'); break;
                case MarkdownCodeInline code: sb.Append('`').Append(code.Text).Append('`'); break;
                case MarkdownLinkInline link:
                    sb.Append('[');
                    AppendInlinesMarkdown(sb, link.Children);
                    sb.Append("](").Append(link.Target).Append(')');
                    break;
                case MarkdownLineBreakInline: sb.Append("  \n"); break;
            }
        }
    }

    #endregion

    #region HTML

    public static string ToHtmlDocument(IReadOnlyList<MarkdownBlock> blocks)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>\n");
        AppendBlocksHtml(sb, blocks);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    public static string ToHtmlFragment(IReadOnlyList<MarkdownBlock> blocks)
    {
        var sb = new StringBuilder();
        AppendBlocksHtml(sb, blocks);
        return sb.ToString();
    }

    private static void AppendBlocksHtml(StringBuilder sb, IReadOnlyList<MarkdownBlock> blocks)
    {
        foreach (var block in blocks)
        {
            AppendBlockHtml(sb, block);
        }
    }

    private static void AppendBlockHtml(StringBuilder sb, MarkdownBlock block)
    {
        switch (block)
        {
            case MarkdownHeadingBlock heading:
                {
                    var level = Math.Clamp(heading.Level, 1, 6);
                    sb.Append("<h").Append(level).Append('>');
                    AppendInlinesHtml(sb, heading.Inlines);
                    sb.Append("</h").Append(level).Append(">\n");
                    break;
                }

            case MarkdownParagraphBlock paragraph:
                sb.Append("<p>");
                AppendInlinesHtml(sb, paragraph.Inlines);
                sb.Append("</p>\n");
                break;

            case MarkdownListBlock list:
                {
                    var tag = list.Ordered ? "ol" : "ul";
                    sb.Append('<').Append(tag);
                    if (list.Ordered && list.StartIndex != 1)
                    {
                        sb.Append(" start=\"").Append(list.StartIndex).Append('"');
                    }
                    sb.Append(">\n");
                    foreach (var item in list.Items)
                    {
                        sb.Append("<li>");
                        for (var b = 0; b < item.Blocks.Count; b++)
                        {
                            // Inline the first paragraph so list items read naturally.
                            if (item.Blocks[b] is MarkdownParagraphBlock p)
                            {
                                AppendInlinesHtml(sb, p.Inlines);
                            }
                            else
                            {
                                AppendBlockHtml(sb, item.Blocks[b]);
                            }
                        }
                        sb.Append("</li>\n");
                    }
                    sb.Append("</").Append(tag).Append(">\n");
                    break;
                }

            case MarkdownQuoteBlock quote:
                sb.Append("<blockquote>\n");
                AppendBlocksHtml(sb, quote.Blocks);
                sb.Append("</blockquote>\n");
                break;

            case MarkdownCodeBlock code:
                sb.Append("<pre><code");
                if (!string.IsNullOrEmpty(code.Language))
                {
                    sb.Append(" class=\"language-").Append(HtmlEscape(code.Language!)).Append('"');
                }
                sb.Append('>').Append(HtmlEscape(code.Text)).Append("</code></pre>\n");
                break;

            case MarkdownRuleBlock:
                sb.Append("<hr>\n");
                break;

            case MarkdownTableBlock table:
                sb.Append("<table>\n");
                if (table.HeaderRows.Count > 0)
                {
                    sb.Append("<thead>\n");
                    foreach (var row in table.HeaderRows)
                    {
                        AppendTableRowHtml(sb, row, "th");
                    }
                    sb.Append("</thead>\n");
                }
                sb.Append("<tbody>\n");
                foreach (var row in table.Rows)
                {
                    AppendTableRowHtml(sb, row, "td");
                }
                sb.Append("</tbody>\n</table>\n");
                break;
        }
    }

    private static void AppendTableRowHtml(StringBuilder sb, MarkdownTableRow row, string cellTag)
    {
        sb.Append("<tr>");
        foreach (var cell in row.Cells)
        {
            sb.Append('<').Append(cellTag).Append('>');
            AppendInlinesHtml(sb, cell);
            sb.Append("</").Append(cellTag).Append('>');
        }
        sb.Append("</tr>\n");
    }

    private static void AppendInlinesHtml(StringBuilder sb, IReadOnlyList<MarkdownInline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline text: sb.Append(HtmlEscape(text.Text)); break;
                case MarkdownStrongInline strong: sb.Append("<strong>"); AppendInlinesHtml(sb, strong.Children); sb.Append("</strong>"); break;
                case MarkdownEmphasisInline emphasis: sb.Append("<em>"); AppendInlinesHtml(sb, emphasis.Children); sb.Append("</em>"); break;
                case MarkdownCodeInline code: sb.Append("<code>").Append(HtmlEscape(code.Text)).Append("</code>"); break;
                case MarkdownLinkInline link:
                    sb.Append("<a href=\"").Append(HtmlEscape(link.Target)).Append("\">");
                    AppendInlinesHtml(sb, link.Children);
                    sb.Append("</a>");
                    break;
                case MarkdownLineBreakInline: sb.Append("<br>"); break;
            }
        }
    }

    private static string HtmlEscape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    #endregion

    #region RTF

    public static string ToRtf(IReadOnlyList<MarkdownBlock> blocks)
    {
        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\ansicpg65001\deff0{\fonttbl{\f0\fnil Segoe UI;}{\f1\fmodern Consolas;}}");
        sb.Append(@"\f0\fs22").Append('\n');
        for (var i = 0; i < blocks.Count; i++)
        {
            AppendBlockRtf(sb, blocks[i]);
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendBlockRtf(StringBuilder sb, MarkdownBlock block)
    {
        switch (block)
        {
            case MarkdownHeadingBlock heading:
                {
                    var size = heading.Level switch { 1 => 36, 2 => 32, 3 => 28, 4 => 24, 5 => 22, _ => 20 };
                    sb.Append(@"{\b\fs").Append(size).Append(' ');
                    AppendInlinesRtf(sb, heading.Inlines);
                    sb.Append(@"}\par").Append('\n');
                    break;
                }

            case MarkdownParagraphBlock paragraph:
                AppendInlinesRtf(sb, paragraph.Inlines);
                sb.Append(@"\par").Append('\n');
                break;

            case MarkdownListBlock list:
                for (var i = 0; i < list.Items.Count; i++)
                {
                    var marker = list.Ordered ? $"{list.StartIndex + i}.\\tab " : "\\bullet\\tab ";
                    sb.Append(@"{\fi-360\li720 ").Append(marker);
                    foreach (var child in list.Items[i].Blocks)
                    {
                        if (child is MarkdownParagraphBlock p)
                        {
                            AppendInlinesRtf(sb, p.Inlines);
                        }
                        else
                        {
                            AppendBlockRtf(sb, child);
                        }
                    }
                    sb.Append(@"\par}").Append('\n');
                }
                break;

            case MarkdownQuoteBlock quote:
                sb.Append(@"{\li720\i ");
                foreach (var child in quote.Blocks)
                {
                    AppendBlockRtf(sb, child);
                }
                sb.Append('}');
                break;

            case MarkdownCodeBlock code:
                sb.Append(@"{\f1\fs20 ");
                foreach (var line in code.Text.Split('\n'))
                {
                    sb.Append(RtfEscape(line)).Append(@"\line ");
                }
                sb.Append(@"\par}").Append('\n');
                break;

            case MarkdownRuleBlock:
                sb.Append(@"\par").Append('\n');
                break;

            case MarkdownTableBlock table:
                foreach (var row in table.HeaderRows)
                {
                    AppendTableRowRtf(sb, row, bold: true);
                }
                foreach (var row in table.Rows)
                {
                    AppendTableRowRtf(sb, row, bold: false);
                }
                break;
        }
    }

    private static void AppendTableRowRtf(StringBuilder sb, MarkdownTableRow row, bool bold)
    {
        if (bold) sb.Append(@"{\b ");
        for (var c = 0; c < row.Cells.Count; c++)
        {
            if (c > 0) sb.Append(@"\tab ");
            AppendInlinesRtf(sb, row.Cells[c]);
        }
        if (bold) sb.Append('}');
        sb.Append(@"\par").Append('\n');
    }

    private static void AppendInlinesRtf(StringBuilder sb, IReadOnlyList<MarkdownInline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline text: sb.Append(RtfEscape(text.Text)); break;
                case MarkdownStrongInline strong: sb.Append(@"{\b "); AppendInlinesRtf(sb, strong.Children); sb.Append('}'); break;
                case MarkdownEmphasisInline emphasis: sb.Append(@"{\i "); AppendInlinesRtf(sb, emphasis.Children); sb.Append('}'); break;
                case MarkdownCodeInline code: sb.Append(@"{\f1 ").Append(RtfEscape(code.Text)).Append('}'); break;
                case MarkdownLinkInline link: AppendInlinesRtf(sb, link.Children); break;
                case MarkdownLineBreakInline: sb.Append(@"\line "); break;
            }
        }
    }

    private static string RtfEscape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '{': sb.Append(@"\{"); break;
                case '}': sb.Append(@"\}"); break;
                case '\n': sb.Append(@"\line "); break;
                case '\r': break;
                case '\t': sb.Append(@"\tab "); break;
                default:
                    if (c > 127)
                    {
                        // RTF expects a signed 16-bit decimal for \u, with one ASCII fallback char.
                        // Astral characters (emoji etc.) are a UTF-16 surrogate pair; RTF represents
                        // them as two consecutive \u escapes, which is exactly what emitting each
                        // code unit (high surrogate then low surrogate) produces here.
                        sb.Append(@"\u").Append((short)c).Append('?');
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    #endregion
}
