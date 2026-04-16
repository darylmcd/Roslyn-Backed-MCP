using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Compares two csproj (or any MSBuild XML) files for <em>semantic</em> equivalence — that is,
/// equality of the parsed XML tree ignoring trivia that does not affect MSBuild evaluation.
/// </summary>
/// <remarks>
/// <para>
/// Closes <c>create-file-apply-csproj-side-effect-all-projects</c> (P2). When
/// <see cref="Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.TryApplyChanges(Microsoft.CodeAnalysis.Solution)"/>
/// runs, MSBuild's project-file writer can reserialize untouched csprojs in the workspace —
/// adding a UTF-8 BOM, converting LF line endings to CRLF, collapsing blank lines, and stripping
/// the trailing newline. These changes are <em>semantically</em> equivalent (the build graph is
/// identical) but they show up as noise in <c>git diff</c>, which pollutes PRs and confuses
/// source-control tooling.
/// </para>
/// <para>
/// The fix (in <c>RefactoringService.PersistDocumentSetChangesAsync</c>) is to snapshot every
/// csproj before <c>TryApplyChanges</c>, then — after the call — compare on-disk bytes against
/// the snapshot via this helper. If the diff is semantic (new <c>PackageReference</c>, new
/// <c>Compile Include</c>, property change), the new bytes are kept. If the diff is trivia-only
/// (whitespace, BOM, line endings), the snapshot is restored.
/// </para>
/// <para>
/// Comparison strategy: parse both sides as <see cref="XDocument"/> with
/// <see cref="LoadOptions.None"/> (so insignificant whitespace is normalized), then walk the
/// element tree comparing:
/// </para>
/// <list type="bullet">
///   <item><description>Element expanded name (namespace + local name).</description></item>
///   <item><description>Attributes: set equality keyed by expanded name; values compared ordinally.</description></item>
///   <item><description>Child elements: compared in order (MSBuild ordering is significant — <c>&lt;PackageReference&gt;</c> order inside an <c>&lt;ItemGroup&gt;</c> can affect transitive resolution, and <c>&lt;Target&gt;</c>s fire in definition order).</description></item>
///   <item><description>Leaf text content: whitespace-trimmed ordinal comparison (MSBuild property values are whitespace-trimmed at evaluation — e.g., <c>&lt;TargetFramework&gt; net10.0 &lt;/TargetFramework&gt;</c> and <c>&lt;TargetFramework&gt;net10.0&lt;/TargetFramework&gt;</c> are semantically identical).</description></item>
/// </list>
/// <para>
/// Leaf text content comparison is conservative: it only applies to elements that have no child
/// elements and whose text is a single scalar value. Element content that mixes text and children
/// (uncommon in MSBuild files but legal in XML) is compared ordinally on the combined content
/// string, which is stricter than needed but never produces a false positive.
/// </para>
/// <para>
/// This helper is pure (no I/O) and never throws for malformed XML — it returns <see langword="false"/>
/// instead, which signals the caller to keep the new bytes (the safe default: preserve whatever
/// MSBuild wrote rather than restore a snapshot that might have been intentionally changed).
/// </para>
/// </remarks>
internal static class CsprojSemanticEquality
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="originalContent"/> and
    /// <paramref name="currentContent"/> represent XML trees with identical element / attribute
    /// / text structure, ignoring whitespace trivia, BOM, and line endings. Returns
    /// <see langword="false"/> when the XML parse fails on either side (treats parse failure as
    /// "semantically different" — safer than silently restoring a snapshot whose current bytes
    /// can't be parsed).
    /// </summary>
    public static bool AreXmlEquivalent(string originalContent, string currentContent)
    {
        if (ReferenceEquals(originalContent, currentContent))
        {
            return true;
        }

        // Fast path: byte-identical strings. Avoids the XDocument parse cost on the common
        // "TryApplyChanges didn't touch this csproj at all" case.
        if (string.Equals(originalContent, currentContent, StringComparison.Ordinal))
        {
            return true;
        }

        XDocument originalDoc;
        XDocument currentDoc;
        try
        {
            // LoadOptions.None — whitespace between elements is discarded during parse, which
            // is exactly what we want: identical element trees compare equal even if one has
            // extra blank lines or different indentation.
            originalDoc = XDocument.Parse(StripLeadingBom(originalContent), LoadOptions.None);
            currentDoc = XDocument.Parse(StripLeadingBom(currentContent), LoadOptions.None);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }

        return AreElementsEquivalent(originalDoc.Root, currentDoc.Root);
    }

    /// <summary>
    /// Removes a leading UTF-8 BOM (<c>U+FEFF</c>) so <see cref="XDocument.Parse(string, LoadOptions)"/>
    /// doesn't raise an "unexpected character in prolog" <see cref="System.Xml.XmlException"/>. The
    /// BOM is purely a byte-stream encoding signal; whether present or absent it has no effect on
    /// the XML infoset, which is exactly the kind of trivia we want this helper to ignore.
    /// </summary>
    private static string StripLeadingBom(string content)
    {
        return !string.IsNullOrEmpty(content) && content[0] == '\uFEFF'
            ? content.Substring(1)
            : content;
    }

    private static bool AreElementsEquivalent(XElement? left, XElement? right)
    {
        if (left is null && right is null)
        {
            return true;
        }
        if (left is null || right is null)
        {
            return false;
        }

        if (!left.Name.Equals(right.Name))
        {
            return false;
        }

        if (!AreAttributesEquivalent(left, right))
        {
            return false;
        }

        var leftChildren = left.Elements().ToList();
        var rightChildren = right.Elements().ToList();

        if (leftChildren.Count != rightChildren.Count)
        {
            return false;
        }

        // Leaf compare: when both sides have no child elements, compare the scalar text value
        // trimmed (MSBuild whitespace-trims property values at evaluation). When both have
        // children, compare children in order; any text nodes between them are trivia at the
        // MSBuild level and were already normalized out by LoadOptions.None.
        if (leftChildren.Count == 0)
        {
            return string.Equals(
                (left.Value ?? string.Empty).Trim(),
                (right.Value ?? string.Empty).Trim(),
                StringComparison.Ordinal);
        }

        for (var i = 0; i < leftChildren.Count; i++)
        {
            if (!AreElementsEquivalent(leftChildren[i], rightChildren[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreAttributesEquivalent(XElement left, XElement right)
    {
        var leftAttrs = left.Attributes().ToList();
        var rightAttrs = right.Attributes().ToList();

        if (leftAttrs.Count != rightAttrs.Count)
        {
            return false;
        }

        // Attribute order is semantically meaningless in MSBuild (unlike child-element order).
        // Compare as a keyed set: both sides must have the same attribute names, and for each
        // name the values must match ordinally (attribute values in MSBuild — Include paths,
        // condition expressions, etc. — ARE case-sensitive on non-Windows and order-sensitive
        // inside multi-token values like `$(TF1);$(TF2)`, so ordinal is correct).
        var rightLookup = rightAttrs.ToDictionary(a => a.Name, a => a.Value);
        foreach (var attr in leftAttrs)
        {
            if (!rightLookup.TryGetValue(attr.Name, out var rightValue))
            {
                return false;
            }
            if (!string.Equals(attr.Value, rightValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Snapshots the current on-disk bytes of every csproj at the supplied paths. Used as the
    /// "before" half of the <c>TryApplyChanges</c> drift-guard pattern: capture bytes, call
    /// <c>TryApplyChanges</c>, then call <see cref="RestoreTriviaOnlyDriftAsync"/> to roll back
    /// any csprojs whose post-apply bytes differ from the snapshot only in XML trivia.
    /// </summary>
    /// <remarks>
    /// Read failures for individual csprojs are logged and skipped: they forfeit the
    /// trivia-restore guarantee for the affected file but do not propagate to the caller.
    /// Returns a path-keyed dictionary (ordinal-ignore-case) with one entry per readable csproj.
    /// </remarks>
    public static async Task<Dictionary<string, string>> SnapshotProjectsAsync(
        IEnumerable<string> csprojPaths,
        ILogger logger,
        CancellationToken ct)
    {
        var snapshots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in csprojPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || snapshots.ContainsKey(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                snapshots[path] = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A read failure here only forfeits the trivia-restore guarantee for this one
                // csproj; the caller's apply proceeds normally.
                logger.LogWarning(ex,
                    "CsprojSemanticEquality.SnapshotProjectsAsync: failed to read {Path}; this csproj may show trivia drift after TryApplyChanges.",
                    path);
            }
        }
        return snapshots;
    }

    /// <summary>
    /// For every path in <paramref name="snapshots"/>, re-reads the current on-disk bytes and —
    /// if the XML is semantically equivalent to the snapshot (i.e. only trivia like BOM, line
    /// endings, blank lines, or whitespace differs) — rewrites the file with the snapshot bytes.
    /// If the XML diff is semantic (new <c>PackageReference</c>, property change, etc.) the
    /// current bytes are preserved — MSBuild wrote them for a legitimate reason.
    /// </summary>
    /// <param name="snapshots">The path→content map returned by <see cref="SnapshotProjectsAsync"/>.</param>
    /// <param name="skipPaths">Paths to skip (already restored by a more specific pass, such as
    /// Item #5's SDK-style csproj guard). Pass an empty set when no earlier pass ran.</param>
    /// <param name="logger">Logger for debug/warning output.</param>
    /// <param name="operationTag">Short tag prefixed to log messages to identify the caller
    /// (e.g. <c>"csproj-reserialization-msbuildworkspace"</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task RestoreTriviaOnlyDriftAsync(
        IReadOnlyDictionary<string, string> snapshots,
        IReadOnlySet<string> skipPaths,
        ILogger logger,
        string operationTag,
        CancellationToken ct)
    {
        foreach (var (csprojPath, originalContent) in snapshots)
        {
            if (skipPaths.Contains(csprojPath))
            {
                continue;
            }

            try
            {
                if (!File.Exists(csprojPath))
                {
                    // MSBuildWorkspace doesn't delete csprojs during TryApplyChanges on document
                    // or analyzer-reference changes, but guard anyway — nothing to restore if
                    // the file is gone.
                    continue;
                }

                var currentContent = await File.ReadAllTextAsync(csprojPath, ct).ConfigureAwait(false);
                if (string.Equals(currentContent, originalContent, StringComparison.Ordinal))
                {
                    // Byte-identical; no restoration needed. Fast path for the common case where
                    // MSBuildWorkspace didn't touch this csproj at all.
                    continue;
                }

                if (AreXmlEquivalent(originalContent, currentContent))
                {
                    await File.WriteAllTextAsync(csprojPath, originalContent, ct).ConfigureAwait(false);
                    logger.LogDebug(
                        "{OperationTag}: restored {Path} after TryApplyChanges reserialized it with only trivia differences (BOM / line endings / blank lines).",
                        operationTag, csprojPath);
                }
                // else: semantic diff — MSBuild wrote a legitimate change. Keep the new bytes.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Restore failure here only forfeits the trivia-preservation guarantee; the
                // apply itself is not invalidated. Log and continue.
                logger.LogWarning(ex,
                    "{OperationTag}: failed to restore trivia-only csproj snapshot for {Path}; the file may show whitespace / BOM drift in source control.",
                    operationTag, csprojPath);
            }
        }
    }
}
