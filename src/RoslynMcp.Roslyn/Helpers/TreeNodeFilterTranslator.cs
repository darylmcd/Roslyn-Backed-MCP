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
/// permits it (<c>/A/B/C/(Method1|Method2)</c>). Verified against a real production TUnit
/// project (Microsoft.Testing.Platform 2.2.3, via TUnit 1.45.8): a parenthesized group of two
/// or more literal values matches ZERO tests — silently, with no parse error — while the
/// identical syntax worked correctly against a different scratch project on MTP 2.3.3 (TUnit
/// 1.65.38). A bare, unparenthesized single literal matches correctly on both versions; so does
/// a lone wildcard <c>(*)</c>. Only OR-ing two or more literal values is affected. Since this
/// repo can't reliably determine a target project's resolved MTP version from its .csproj, and
/// a silent zero-match reads as a plausible "nothing matched" result rather than an error, the
/// only safe choice is to never emit that shape — even though it's real MTP grammar and does
/// work on some versions.
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
                $"Filter '{vsTestFilter}' names more than one test ('|'). MTP's --treenode-filter " +
                "can OR literal values within a single path segment in principle, but that shape was " +
                "confirmed to silently match zero tests on a real production Microsoft.Testing.Platform " +
                "2.2.3 project — a plausible-looking wrong result, not a loud error — while identical " +
                "syntax worked on a different MTP version. Only a single test can be translated per " +
                "test_run call for now; call it once per test.");
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
