namespace GHelper.Linux.Helpers;

// Tiny CommonMark subset parser.
// Supports H1/H2/H3, paragraphs, unordered list items (- or *),
// fenced code blocks (```), inline [text](url), **bold**, `code`,
// Markdown images ![alt](url), and HTML <img src="..." /> tags.
// Anything else is passed through as plain text so the renderer can
// still show it without crashing.

public enum ChangelogBlockKind
{
    Heading1,
    Heading2,
    Heading3,
    Paragraph,
    ListItem,
    CodeBlock,
    Image,
}

public enum ChangelogInlineKind
{
    Text,
    Link,
    Code,
    Bold,
    Image,
}

public sealed class ChangelogInline
{
    public ChangelogInlineKind Kind { get; init; }
    public string Text { get; init; } = "";
    public string Url { get; init; } = "";
}

public sealed class ChangelogBlock
{
    public ChangelogBlockKind Kind { get; init; }
    public List<ChangelogInline> Inlines { get; init; } = new();
    public string CodeLanguage { get; init; } = "";
    public string CodeText { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string ImageAlt { get; init; } = "";
}

public static class ChangelogParser
{
    public static List<ChangelogBlock> Parse(string markdown)
    {
        var blocks = new List<ChangelogBlock>();
        if (string.IsNullOrEmpty(markdown))
            return blocks;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var paragraph = new List<string>();
        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i];
            string trimmed = line.TrimEnd();

            if (trimmed.StartsWith("```"))
            {
                FlushParagraph(blocks, paragraph);
                string lang = trimmed.Length > 3 ? trimmed.Substring(3).Trim() : "";
                var codeLines = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimEnd().StartsWith("```"))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }
                if (i < lines.Length)
                    i++;
                blocks.Add(new ChangelogBlock
                {
                    Kind = ChangelogBlockKind.CodeBlock,
                    CodeLanguage = lang,
                    CodeText = string.Join("\n", codeLines),
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                FlushParagraph(blocks, paragraph);
                i++;
                continue;
            }

            // Standalone <img ... /> on its own line becomes a block image.
            if (TryParseImgTag(trimmed.TrimStart(), out var imgUrl, out var imgAlt))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(new ChangelogBlock
                {
                    Kind = ChangelogBlockKind.Image,
                    ImageUrl = imgUrl,
                    ImageAlt = imgAlt,
                });
                i++;
                continue;
            }

            if (trimmed.StartsWith("### "))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(new ChangelogBlock
                {
                    Kind = ChangelogBlockKind.Heading3,
                    Inlines = ParseInlines(trimmed.Substring(4).Trim()),
                });
                i++;
                continue;
            }

            if (trimmed.StartsWith("## "))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(new ChangelogBlock
                {
                    Kind = ChangelogBlockKind.Heading2,
                    Inlines = ParseInlines(trimmed.Substring(3).Trim()),
                });
                i++;
                continue;
            }

            if (trimmed.StartsWith("# "))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(new ChangelogBlock
                {
                    Kind = ChangelogBlockKind.Heading1,
                    Inlines = ParseInlines(trimmed.Substring(2).Trim()),
                });
                i++;
                continue;
            }

            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(new ChangelogBlock
                {
                    Kind = ChangelogBlockKind.ListItem,
                    Inlines = ParseInlines(trimmed.Substring(2).Trim()),
                });
                i++;
                continue;
            }

            // Standalone ![alt](url) on its own line becomes a block image.
            if (TryParseStandaloneMdImage(trimmed.Trim(), out var mdUrl, out var mdAlt))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(new ChangelogBlock
                {
                    Kind = ChangelogBlockKind.Image,
                    ImageUrl = mdUrl,
                    ImageAlt = mdAlt,
                });
                i++;
                continue;
            }

            paragraph.Add(trimmed);
            i++;
        }
        FlushParagraph(blocks, paragraph);
        return blocks;
    }

    private static void FlushParagraph(List<ChangelogBlock> blocks, List<string> paragraph)
    {
        if (paragraph.Count == 0)
            return;
        string text = string.Join(" ", paragraph).Trim();
        paragraph.Clear();
        if (text.Length == 0)
            return;
        blocks.Add(new ChangelogBlock
        {
            Kind = ChangelogBlockKind.Paragraph,
            Inlines = ParseInlines(text),
        });
    }

    // Inline scanner. Handles in priority order: ![img](url), `code`,
    // [text](url), **bold**, autolinked bare URLs.
    private static List<ChangelogInline> ParseInlines(string text)
    {
        var inlines = new List<ChangelogInline>();
        if (string.IsNullOrEmpty(text))
            return inlines;

        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            // Inline <img ... /> embedded in a paragraph.
            if (c == '<' && i + 4 < text.Length && text[i + 1] == 'i' && text[i + 2] == 'm' && text[i + 3] == 'g' && (text[i + 4] == ' ' || text[i + 4] == '\t' || text[i + 4] == '/'))
            {
                int end = text.IndexOf('>', i + 4);
                if (end > i)
                {
                    string tag = text.Substring(i, end - i + 1);
                    if (TryParseImgTag(tag, out var iurl, out var ialt))
                    {
                        FlushText(inlines, sb);
                        inlines.Add(new ChangelogInline
                        {
                            Kind = ChangelogInlineKind.Image,
                            Text = ialt,
                            Url = iurl,
                        });
                        i = end + 1;
                        continue;
                    }
                }
            }

            // Markdown image ![alt](url).
            if (c == '!' && i + 1 < text.Length && text[i + 1] == '[')
            {
                int closeBracket = text.IndexOf(']', i + 2);
                if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                {
                    int closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket)
                    {
                        string alt = text.Substring(i + 2, closeBracket - i - 2);
                        string url = text.Substring(closeBracket + 2, closeParen - closeBracket - 2);
                        FlushText(inlines, sb);
                        inlines.Add(new ChangelogInline
                        {
                            Kind = ChangelogInlineKind.Image,
                            Text = alt,
                            Url = url,
                        });
                        i = closeParen + 1;
                        continue;
                    }
                }
            }

            if (c == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    FlushText(inlines, sb);
                    inlines.Add(new ChangelogInline
                    {
                        Kind = ChangelogInlineKind.Code,
                        Text = text.Substring(i + 1, end - i - 1),
                    });
                    i = end + 1;
                    continue;
                }
            }

            if (c == '[')
            {
                int closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                {
                    int closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket)
                    {
                        string linkText = text.Substring(i + 1, closeBracket - i - 1);
                        string url = text.Substring(closeBracket + 2, closeParen - closeBracket - 2);
                        FlushText(inlines, sb);
                        inlines.Add(new ChangelogInline
                        {
                            Kind = ChangelogInlineKind.Link,
                            Text = linkText,
                            Url = url,
                        });
                        i = closeParen + 1;
                        continue;
                    }
                }
            }

            if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("**", i + 2);
                if (end > i + 1)
                {
                    FlushText(inlines, sb);
                    inlines.Add(new ChangelogInline
                    {
                        Kind = ChangelogInlineKind.Bold,
                        Text = text.Substring(i + 2, end - i - 2),
                    });
                    i = end + 2;
                    continue;
                }
            }

            // Auto-link bare http(s) URLs so they're clickable without [text](url) wrapping.
            if ((c == 'h' || c == 'H') && (text.Substring(i).StartsWith("http://") || text.Substring(i).StartsWith("https://")))
            {
                int end = i;
                while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != ')' && text[end] != ']')
                    end++;
                string url = text.Substring(i, end - i);
                FlushText(inlines, sb);
                inlines.Add(new ChangelogInline
                {
                    Kind = ChangelogInlineKind.Link,
                    Text = url,
                    Url = url,
                });
                i = end;
                continue;
            }

            sb.Append(c);
            i++;
        }
        FlushText(inlines, sb);
        return inlines;
    }

    private static void FlushText(List<ChangelogInline> inlines, System.Text.StringBuilder sb)
    {
        if (sb.Length == 0)
            return;
        inlines.Add(new ChangelogInline
        {
            Kind = ChangelogInlineKind.Text,
            Text = sb.ToString(),
        });
        sb.Clear();
    }

    // Parses a single HTML <img ... /> tag. Lenient on attribute order and
    // quote style (single or double). Returns true if a src was found.
    private static bool TryParseImgTag(string tag, out string url, out string alt)
    {
        url = "";
        alt = "";
        if (tag.Length < 5)
            return false;
        if (!tag.StartsWith("<img", StringComparison.OrdinalIgnoreCase))
            return false;
        int gt = tag.IndexOf('>');
        if (gt < 0)
            return false;
        string body = tag.Substring(4, gt - 4);
        url = ExtractAttr(body, "src");
        alt = ExtractAttr(body, "alt");
        return url.Length > 0;
    }

    private static string ExtractAttr(string tagBody, string name)
    {
        int idx = tagBody.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
        while (idx > 0)
        {
            char prev = tagBody[idx - 1];
            if (prev == ' ' || prev == '\t' || prev == '/')
                break;
            idx = tagBody.IndexOf(name + "=", idx + 1, StringComparison.OrdinalIgnoreCase);
        }
        if (idx < 0)
            return "";
        int start = idx + name.Length + 1;
        if (start >= tagBody.Length)
            return "";
        char q = tagBody[start];
        if (q == '"' || q == '\'')
        {
            int end = tagBody.IndexOf(q, start + 1);
            if (end < 0)
                return "";
            return tagBody.Substring(start + 1, end - start - 1);
        }
        int sp = tagBody.IndexOfAny(new[] { ' ', '\t', '/', '>' }, start);
        if (sp < 0)
            sp = tagBody.Length;
        return tagBody.Substring(start, sp - start);
    }

    // Parses a single ![alt](url) consuming the whole input string.
    private static bool TryParseStandaloneMdImage(string text, out string url, out string alt)
    {
        url = "";
        alt = "";
        if (text.Length < 5 || text[0] != '!' || text[1] != '[')
            return false;
        int closeBracket = text.IndexOf(']', 2);
        if (closeBracket < 0 || closeBracket + 1 >= text.Length || text[closeBracket + 1] != '(')
            return false;
        int closeParen = text.IndexOf(')', closeBracket + 2);
        if (closeParen < 0)
            return false;
        if (closeParen != text.Length - 1)
            return false;
        alt = text.Substring(2, closeBracket - 2);
        url = text.Substring(closeBracket + 2, closeParen - closeBracket - 2);
        return url.Length > 0;
    }
}
