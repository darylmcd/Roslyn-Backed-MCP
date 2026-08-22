using System.Text.RegularExpressions;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// tunit-treenode-filter-translation: translates the common tool-generated VSTest-style filter —
/// one or more <c>FullyQualifiedName&lt;=|~&gt;{namespace}.{class}.{method}</c> atoms OR'd with
/// <c>|</c>, exactly what <c>TestDiscoveryService.SynthesizeDotnetTestFilter</c> emits for
/// <c>test_related</c>/<c>test_related_files</c> — into MTP's <c>--treenode-filter</c> tree-path
/// syntax for an MTP-only project (TUnit).
/// <para>
/// Verified against a real TUnit project rather than assumed from docs: a plain test method's
/// tree-node path is exactly 4 segments, <c>/{Assembly}/{Namespace}/{ClassName}/{Method}</c>
/// (confirmed via <c>--treenode-filter</c> probes with wildcarded segments), and alternatives
/// can be OR'd with <c>(a|b)</c> only WITHIN one path segment — OR-ing full paths together
/// (<c>(/A/B/C/M1)|(/A/B/C/M2)</c>) is documented as unsupported and, confirmed by direct repro,
/// crashes the MTP test host at runtime rather than failing to parse. So a filter naming tests
/// across more than one namespace+class combination cannot be expressed as a single tree filter
/// at all.
/// </para>
/// <para>
/// Deliberately narrow rather than a full VSTest filter grammar translator: <c>&amp;</c> (AND),
/// parenthesized grouping, <c>!=</c>/<c>!~</c> (negation), and any property other than
/// <c>FullyQualifiedName</c> are rejected with an actionable error rather than guessed at,
/// since test_run's real-world filters are overwhelmingly the tool-generated shape above.
/// </para>
/// </summary>
internal static partial class TreeNodeFilterTranslator
{
    [GeneratedRegex(@"^\s*FullyQualifiedName\s*(=|~)\s*(.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FullyQualifiedNameAtomRegex();

    /// <summary>
    /// Translates <paramref name="vsTestFilter"/> into an MTP <c>--treenode-filter</c> expression.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The filter uses a shape this translator doesn't (yet) handle: AND/grouping/negation, a
    /// property other than <c>FullyQualifiedName</c>, an atom that doesn't split into at least a
    /// class and method, or atoms spanning more than one namespace+class (unsupported by MTP's
    /// tree filter grammar itself — verified by direct repro, not just documentation).
    /// </exception>
    public static string Translate(string vsTestFilter)
    {
        if (vsTestFilter.Contains('&') || vsTestFilter.Contains('(') || vsTestFilter.Contains(')'))
        {
            throw new InvalidOperationException(
                $"Filter '{vsTestFilter}' uses AND ('&') or parenthesized grouping, which this MTP filter " +
                "translation doesn't support yet. Only one or more FullyQualifiedName~ or FullyQualifiedName= " +
                "atoms joined with '|' are translated.");
        }

        var groups = new List<FilterGroup>();
        foreach (var atom in vsTestFilter.Split('|'))
        {
            var (@namespace, className, method) = ParseAtom(atom, vsTestFilter);
            var group = groups.Find(g =>
                string.Equals(g.Namespace, @namespace, StringComparison.Ordinal) &&
                string.Equals(g.ClassName, className, StringComparison.Ordinal));
            if (group is null)
            {
                group = new FilterGroup(@namespace, className);
                groups.Add(group);
            }

            group.Methods.Add(method);
        }

        if (groups.Count > 1)
        {
            throw new InvalidOperationException(
                $"Filter '{vsTestFilter}' names tests across {groups.Count} different namespace/class " +
                "combinations. Microsoft.Testing.Platform's --treenode-filter can only OR alternatives within " +
                "a single path segment (e.g. one class's methods) — OR-ing full test paths together is " +
                "unsupported and crashes the MTP test host. Run test_run once per namespace/class group instead.");
        }

        var only = groups[0];
        var namespaceSegment = only.Namespace ?? "*";
        var methodSegment = only.Methods.Count == 1
            ? only.Methods[0]
            : $"({string.Join("|", only.Methods)})";
        return $"/*/{namespaceSegment}/{only.ClassName}/{methodSegment}";
    }

    private static (string? Namespace, string ClassName, string Method) ParseAtom(string atom, string fullFilter)
    {
        var match = FullyQualifiedNameAtomRegex().Match(atom);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Filter '{fullFilter}' contains atom '{atom}' that isn't a supported " +
                "'FullyQualifiedName=<value>' or 'FullyQualifiedName~<value>' expression. Only those two " +
                "operators against the FullyQualifiedName property are translated to MTP's --treenode-filter.");
        }

        // BuildFullyQualifiedTestName (TestDiscoveryService) builds "{namespace}.{class}.{method}",
        // joining a possibly multi-part namespace with dots — invert by taking the last two
        // dot-separated parts as class/method and treating everything before them as the namespace.
        var parts = match.Groups[2].Value.Split('.');
        if (parts.Length < 2)
        {
            throw new InvalidOperationException(
                $"Filter '{fullFilter}' contains atom '{atom}' whose FullyQualifiedName value " +
                $"'{match.Groups[2].Value}' has no '.'-separated class/method — expected at least " +
                "'{ClassName}.{Method}'.");
        }

        var method = parts[^1];
        var className = parts[^2];
        var @namespace = parts.Length > 2 ? string.Join('.', parts[..^2]) : null;
        return (@namespace, className, method);
    }

    private sealed class FilterGroup(string? @namespace, string className)
    {
        public string? Namespace { get; } = @namespace;
        public string ClassName { get; } = className;
        public List<string> Methods { get; } = [];
    }
}
