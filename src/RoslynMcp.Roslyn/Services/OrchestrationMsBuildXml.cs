using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RoslynMcp.Roslyn.Services;

internal static class OrchestrationMsBuildXml
{
    internal static XElement GetOrCreateItemGroup(XDocument document, string itemName)
    {
        var existingGroup = document.Root?.Elements("ItemGroup")
            .FirstOrDefault(group => group.Elements(itemName).Any());
        if (existingGroup is not null)
        {
            return existingGroup;
        }

        var itemGroup = new XElement("ItemGroup");
        var root = document.Root;
        if (root is null)
        {
            return itemGroup;
        }

        // FORMAT-BUG-003 fix: splice the new ItemGroup between existing elements with matching
        // indent + line-ending trivia so the serializer does not produce
        // `<ItemGroup><PackageReference .../></ItemGroup>` on a single line or glue
        // `</Project>` onto the preceding element. A single AddAfterSelf call with both nodes
        // preserves document order (`[lastElement][leading-whitespace][itemGroup][trailing-whitespace]`)
        // instead of the LIFO semantics of two sequential calls. The trailing XText keeps
        // `</Project>` on its own line after the new ItemGroup even when the caller just pruned
        // the only prior trailing whitespace (via `RemoveElementCleanly` on a lone ItemGroup).
        var lineEnding = DetectLineEnding(document);
        const string indent = "  ";
        if (root.Elements().Any())
        {
            var lastElement = root.Elements().Last();
            if (lastElement.NextNode is XText existingTrailing && string.IsNullOrWhiteSpace(existingTrailing.Value))
            {
                lastElement.AddAfterSelf(new XText(lineEnding + indent), itemGroup);
            }
            else
            {
                lastElement.AddAfterSelf(new XText(lineEnding + indent), itemGroup, new XText(lineEnding));
            }
        }
        else
        {
            root.Add(new XText(lineEnding + indent));
            root.Add(itemGroup);
            root.Add(new XText(lineEnding));
        }

        return itemGroup;
    }

    /// <summary>
    /// Adds <paramref name="child"/> to <paramref name="parent"/> with a leading newline + detected
    /// child indent so a fresh element does not end up concatenated onto the previous sibling or
    /// the parent's closing tag. Mirrors <c>ProjectMutationService.AddChildElementPreservingIndentation</c>
    /// for orchestrators that mutate csproj / Directory.Packages.props files.
    /// </summary>
    internal static void AddChildElementPreservingIndentation(XElement parent, XElement child)
    {
        var trailingWhitespace = parent.Nodes().OfType<XText>().LastOrDefault(node =>
            node.NextNode is null && string.IsNullOrWhiteSpace(node.Value));

        var childIndentation = DetectChildIndentation(parent);
        var lineEnding = DetectLineEnding(parent.Document);

        if (trailingWhitespace is not null)
        {
            trailingWhitespace.AddBeforeSelf(new XText(lineEnding + childIndentation));
            trailingWhitespace.AddBeforeSelf(child);
            return;
        }

        if (!parent.HasElements)
        {
            var parentIndentation = DetectParentIndentation(parent);
            parent.Add(new XText(lineEnding + childIndentation));
            parent.Add(child);
            parent.Add(new XText(lineEnding + parentIndentation));
            return;
        }

        var lastElement = parent.Elements().LastOrDefault();
        if (lastElement is not null)
        {
            lastElement.AddAfterSelf(new XText(lineEnding + childIndentation), child);
            return;
        }

        parent.Add(child);
    }

    /// <summary>
    /// Removes <paramref name="element"/> together with its adjacent whitespace trivia and prunes
    /// the parent ItemGroup if it becomes empty. Mirrors <c>ProjectMutationService.RemoveElementCleanly</c>
    /// so orchestrator edits do not leave orphan `<ItemGroup />` nodes or stray blank lines.
    /// </summary>
    internal static void RemoveElementCleanly(XElement element)
    {
        var parent = element.Parent;
        RemoveNodeAndAdjacentWhitespace(element);

        if (parent is not null
            && parent != parent.Document?.Root
            && !parent.Elements().Any())
        {
            foreach (var ws in parent.Nodes().OfType<XText>().ToList())
            {
                ws.Remove();
            }
            RemoveNodeAndAdjacentWhitespace(parent);
        }
    }

    /// <summary>
    /// Serializes an MSBuild XML document using an indenting <see cref="XmlWriter"/> so edits that
    /// insert new elements produce multi-line output matching the surrounding indentation. Replaces
    /// <c>XDocument.ToString(SaveOptions.DisableFormatting)</c> which strips formatting entirely and
    /// was the proximate cause of FORMAT-BUG-003 (inline ItemGroup XML in <c>migrate_package_preview</c>).
    /// </summary>
    internal static string FormatProjectXml(XDocument document, string originalContent)
    {
        var lineEnding = DetectLineEnding(document);
        var hadXmlDeclaration = originalContent.AsSpan().TrimStart().StartsWith("<?xml".AsSpan(), StringComparison.Ordinal);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = lineEnding,
            OmitXmlDeclaration = !hadXmlDeclaration,
            Encoding = utf8,
        };

        // Use a MemoryStream (not StringBuilder) so the XmlWriter advertises the stream's UTF-8
        // encoding in any emitted XML declaration. Writing to StringBuilder causes the writer to
        // emit `encoding="utf-16"` (StringBuilder's internal encoding), which then fails when the
        // resulting text is persisted as UTF-8 — the subsequent XmlReader throws
        // "There is no Unicode byte order mark. Cannot switch to Unicode." FORMAT-BUG-003 only
        // exposed the inline-ItemGroup half of this bug; the encoding mismatch is a second defect
        // that affects any csproj whose original content begins with an XML declaration.
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }

        return utf8.GetString(stream.ToArray());
    }

    private static void RemoveNodeAndAdjacentWhitespace(XNode node)
    {
        if (node.PreviousNode is XText leading && string.IsNullOrWhiteSpace(leading.Value))
        {
            leading.Remove();
        }
        else if (node.NextNode is XText trailing && string.IsNullOrWhiteSpace(trailing.Value))
        {
            trailing.Remove();
        }
        node.Remove();
    }

    private static string DetectChildIndentation(XElement parent)
    {
        var firstChild = parent.Elements().FirstOrDefault();
        if (firstChild?.PreviousNode is XText previousText)
        {
            var indentation = GetTrailingIndentation(previousText.Value);
            if (indentation is not null)
            {
                return indentation;
            }
        }

        return DetectParentIndentation(parent) + "  ";
    }

    private static string DetectParentIndentation(XElement element)
    {
        if (element.PreviousNode is XText previousText)
        {
            return GetTrailingIndentation(previousText.Value) ?? string.Empty;
        }

        return string.Empty;
    }

    private static string? GetTrailingIndentation(string whitespace)
    {
        if (string.IsNullOrWhiteSpace(whitespace))
        {
            var newlineIndex = whitespace.LastIndexOf('\n');
            if (newlineIndex >= 0)
            {
                return whitespace[(newlineIndex + 1)..];
            }
        }

        return null;
    }

    private static string DetectLineEnding(XDocument? document)
    {
        if (document is null)
        {
            return Environment.NewLine;
        }

        foreach (var textNode in document.DescendantNodes().OfType<XText>())
        {
            if (textNode.Value.Contains("\r\n", StringComparison.Ordinal))
            {
                return "\r\n";
            }

            if (textNode.Value.Contains("\n", StringComparison.Ordinal))
            {
                return "\n";
            }
        }

        return Environment.NewLine;
    }
}
