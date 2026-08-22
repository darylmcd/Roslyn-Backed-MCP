using System.Text.RegularExpressions;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// tunit-treenode-filter-translation: translates a single VSTest-style
/// <c>FullyQualifiedName&lt;=|~&gt;{namespace}.{class}.{method}</c> atom — the shape
/// <c>TestDiscoveryService.SynthesizeDotnetTestFilter</c> emits for one test — into MTP's
/// <c>--treenode-filter</c> tree-path syntax for an MTP-only project (TUnit).
/// <para>
/// Verified against a real TUnit project rather than assumed from docs: a plain test method's
/// tree-node path is exactly 4 segments, <c>/{Assembly}/{Namespace}/{ClassName}/{Method}</c>
/// (confirmed via <c>--treenode-filter</c> probes with wildcarded segments).
/// </para>
/// <para>
/// tunit-treenode-filter-or-of-literals-silently-zero-matches: OR-ing more than one atom is
/// deliberately NOT supported, even within a single path segment where MTP's own grammar
/// permits it (<c>/A/B/C/(Method1|Method2)</c>). Reproduced against a real production TUnit
/// project: a parenthesized group of two or more literal values matches ZERO tests — silently,
/// with no parse error — while the identical syntax worked against a different (scratch)
/// project on a different TUnit version. Root cause, per the MTP maintainer's own investigation
/// (https://github.com/microsoft/testfx/issues/7300#issuecomment-4564789043): MTP's
/// <c>TreeNodeFilter</c> itself matches this shape correctly — a direct reflection probe
/// confirmed <c>MatchesFilter</c> returns true for it. The zero-match is TUnit's OWN pre-filter
/// (<c>MetadataFilterMatcher.ExtractFilterHints</c>) rejecting every test descriptor before
/// MTP's real filter ever runs: it splits the filter on <c>/</c> and checks the last segment
/// for a literal <c>*</c>/<c>?</c> to decide whether to treat it as a wildcard hint — for
/// <c>(Method1|Method2)</c> that check sees the literal parentheses/pipe with no wildcard
/// character and wrongly concludes the user is filtering on a literal method named
/// <c>"(Method1|Method2)"</c>, so it discards everything up front. This is an open, unresolved
/// TUnit bug (https://github.com/thomhurst/TUnit/issues/6026), not an MTP defect and not tied to
/// any MTP platform version — so there is no version check that makes OR-translation safe.
/// Reflecting a project's resolved MTP version (doable via the same MSBuild-evaluation approach
/// <see cref="ProjectMetadataParser"/> already uses) would not help: the relevant version is
/// TUnit's own, the bug has no shipped fix to gate on yet, and the pre-filter heuristic that
/// causes it could differ release to release in ways this repo has no visibility into. Only a
/// bare literal value (no parens, no wildcard) reliably bypasses TUnit's broken pre-filter
/// entirely, which is why translation is restricted to exactly one atom.
/// </para>
/// </summary>
internal static partial class TreeNodeFilterTranslator
{
    [GeneratedRegex(@"^\s*FullyQualifiedName\s*(=|~)\s*(.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FullyQualifiedNameAtomRegex();

    /// <summary>
    /// Translates a single-atom <paramref name="vsTestFilter"/> into an MTP
    /// <c>--treenode-filter</c> expression.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The filter names more than one test (OR'd with <c>|</c> — unsupported, see the type doc
    /// comment), uses AND/grouping/negation, names a property other than
    /// <c>FullyQualifiedName</c>, or doesn't split into at least a class and method.
    /// </exception>
    public static string Translate(string vsTestFilter)
    {
        if (vsTestFilter.Contains('|'))
        {
            throw new InvalidOperationException(
                $"Filter '{vsTestFilter}' names more than one test ('|'). MTP's --treenode-filter can OR " +
                "literal values within a single path segment, and MTP itself matches that correctly, but " +
                "TUnit's own pre-filter has an open bug (github.com/thomhurst/TUnit/issues/6026) that " +
                "silently rejects every test for that exact shape before MTP's filter ever runs — " +
                "confirmed by direct repro against a real project. Only a single test can be translated " +
                "per test_run call for now; call it once per test.");
        }

        if (vsTestFilter.Contains('&') || vsTestFilter.Contains('(') || vsTestFilter.Contains(')'))
        {
            throw new InvalidOperationException(
                $"Filter '{vsTestFilter}' uses AND ('&') or parenthesized grouping, which this MTP filter " +
                "translation doesn't support. Only a single FullyQualifiedName~ or FullyQualifiedName= " +
                "atom is translated.");
        }

        var (@namespace, className, method) = ParseAtom(vsTestFilter, vsTestFilter);
        var namespaceSegment = @namespace ?? "*";
        return $"/*/{namespaceSegment}/{className}/{method}";
    }

    private static (string? Namespace, string ClassName, string Method) ParseAtom(string atom, string fullFilter)
    {
        var match = FullyQualifiedNameAtomRegex().Match(atom);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Filter '{fullFilter}' isn't a supported 'FullyQualifiedName=<value>' or " +
                "'FullyQualifiedName~<value>' expression. Only those two operators against the " +
                "FullyQualifiedName property are translated to MTP's --treenode-filter.");
        }

        // BuildFullyQualifiedTestName (TestDiscoveryService) builds "{namespace}.{class}.{method}",
        // joining a possibly multi-part namespace with dots — invert by taking the last two
        // dot-separated parts as class/method and treating everything before them as the namespace.
        var parts = match.Groups[2].Value.Split('.');
        if (parts.Length < 2)
        {
            throw new InvalidOperationException(
                $"Filter '{fullFilter}' has FullyQualifiedName value '{match.Groups[2].Value}' with no " +
                "'.'-separated class/method — expected at least '{ClassName}.{Method}'.");
        }

        var method = parts[^1];
        var className = parts[^2];
        var @namespace = parts.Length > 2 ? string.Join('.', parts[..^2]) : null;
        return (@namespace, className, method);
    }
}
