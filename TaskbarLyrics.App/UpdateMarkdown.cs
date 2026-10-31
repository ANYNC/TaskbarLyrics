using System.Text;
using System.Text.RegularExpressions;
namespace TaskbarLyrics.App;

internal static class UpdateMarkdown
{
    public static string ToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";

        var sb = new StringBuilder();
        var lists = new Stack<bool>();
        bool paragraphOpen = false;
        bool itemOpen = false;
        bool codeOpen = false;

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = rawLine.TrimEnd();

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                CloseParagraph(sb, ref paragraphOpen);
                CloseLists(sb, lists, ref itemOpen, 0);
                if (codeOpen) { sb.Append("</code></pre>"); codeOpen = false; }
                else { sb.Append("<pre><code>"); codeOpen = true; }
                continue;
            }

            if (codeOpen)
            {
                sb.Append(Escape(rawLine)).Append('\n');
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                CloseParagraph(sb, ref paragraphOpen);
                CloseLists(sb, lists, ref itemOpen, 0);
                continue;
            }

            string trimmed = line.TrimStart();
            int indent = line.Length - trimmed.Length;

            if (IsHorizontalRule(trimmed))
            {
                CloseParagraph(sb, ref paragraphOpen);
                CloseLists(sb, lists, ref itemOpen, 0);
                sb.Append("<hr/>");
                continue;
            }

            int headingLevel = HeadingLevel(trimmed);
            if (headingLevel > 0)
            {
                CloseParagraph(sb, ref paragraphOpen);
                CloseLists(sb, lists, ref itemOpen, 0);
                sb.Append("<h").Append(headingLevel).Append('>').Append(Inline(trimmed.Substring(headingLevel).Trim())).Append("</h").Append(headingLevel).Append('>');
                continue;
            }

            if (TryListItem(trimmed, out bool ordered, out string itemText))
            {
                CloseParagraph(sb, ref paragraphOpen);
                int depth = Math.Min(indent / 2, 2) + 1;

                CloseLists(sb, lists, ref itemOpen, depth);
                if (lists.Count == depth)
                {
                    if (itemOpen) { sb.Append("</li>"); itemOpen = false; }
                    if (lists.Peek() != ordered) { sb.Append(lists.Pop() ? "</ol>" : "</ul>"); }
                }
                while (lists.Count < depth)
                {
                    sb.Append(ordered ? "<ol>" : "<ul>");
                    lists.Push(ordered);
                }

                sb.Append("<li>").Append(Inline(itemText));
                itemOpen = true;
                continue;
            }

            CloseLists(sb, lists, ref itemOpen, 0);
            if (paragraphOpen) sb.Append("<br/>");
            else { sb.Append("<p>"); paragraphOpen = true; }
            sb.Append(Inline(trimmed));
        }

        if (codeOpen) sb.Append("</code></pre>");
        CloseParagraph(sb, ref paragraphOpen);
        CloseLists(sb, lists, ref itemOpen, 0);
        return sb.ToString();
    }

    private static void CloseParagraph(StringBuilder sb, ref bool paragraphOpen)
    {
        if (!paragraphOpen) return;
        sb.Append("</p>");
        paragraphOpen = false;
    }

    private static void CloseLists(StringBuilder sb, Stack<bool> lists, ref bool itemOpen, int depth)
    {
        while (lists.Count > depth)
        {
            if (itemOpen) { sb.Append("</li>"); itemOpen = false; }
            sb.Append(lists.Pop() ? "</ol>" : "</ul>");
        }
    }

    private static bool IsHorizontalRule(string text)
        => Regex.IsMatch(text, @"^([-*_])\1{2,}$");

    private static int HeadingLevel(string text)
    {
        int level = 0;
        while (level < text.Length && level < 6 && text[level] == '#') level++;
        if (level == 0 || level >= text.Length || text[level] != ' ') return 0;
        return level;
    }

    private static bool TryListItem(string text, out bool ordered, out string content)
    {
        var match = Regex.Match(text, @"^([-*+]|\d+\.)\s+(.*)$");
        if (!match.Success)
        {
            ordered = false;
            content = "";
            return false;
        }

        ordered = char.IsDigit(match.Groups[1].Value[0]);
        content = match.Groups[2].Value;
        return true;
    }

    private static string Inline(string text)
    {
        var output = new StringBuilder();
        var offset = 0;
        foreach (Match match in Regex.Matches(text, @"!\[([^\]]*)\]\((https://[^\)\s]+)\)"))
        {
            output.Append(InlineText(text[offset..match.Index]));
            output.Append("<img src='").Append(Escape(match.Groups[2].Value))
                .Append("' alt='").Append(Escape(match.Groups[1].Value)).Append("'>");
            offset = match.Index + match.Length;
        }
        return output.Append(InlineText(text[offset..])).ToString();
    }

    private static string InlineText(string text)
    {
        string html = Escape(text);
        html = Regex.Replace(html, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        html = Regex.Replace(html, @"`([^`]+)`", "<code>$1</code>");
        html = Regex.Replace(html, @"\[([^\]]+)\]\((https?://[^\)\s]+)\)", "<a href='$2'>$1</a>");
        html = Regex.Replace(html, @"(?<![\*\w])\*([^\*\n]+)\*(?!\*)", "<em>$1</em>");
        return html;
    }

    private static string Escape(string text)
        => System.Net.WebUtility.HtmlEncode(text);
}
