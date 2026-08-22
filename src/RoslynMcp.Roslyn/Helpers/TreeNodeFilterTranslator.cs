using System.Text.RegularExpressions;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// tunit-treenode-filter-translation: translates the tool-generated VSTest-style
/// <c>FullyQualifiedName&lt;=|~&gt;{namespace}.{class}.{method}</c> filter — the shape
/// <c>TestDiscoveryService.SynthesizeDotnetTestFilter</c> emits — into MTP's
/// <c>--treenode-filter</c> tree-path syntax for an MTP-only project (TUnit).
/// <para>
/// Verified against a real TUnit project rather than assumed from docs: a plain test method's
/// tree-node path is exactly 4 segments, <c>/{Assembly}/{Namespace}/{ClassName}/{Method}</c>
/// (confirmed via <c>--treenode-filter</c> probes with wildcarded segments).
/// </para>
/// <para>
/// tunit-treenode-filter-or-requires-tunit-fix: OR-ing more than one atom within a single
/// namespace+class (<c>/A/B/C/(Method1|Method2)</c>) is valid MTP grammar — Microsoft.Testing.Platform's
/// <c>TreeNodeFilter</c> matches it correctly, confirmed by the MTP maintainer's own reflection
/// probe (https://github.com/microsoft/testfx/issues/7300#issuecomment-4564789043). Reproduced
/// against a real production TUnit project, that same shape silently matched ZERO tests. Root
/// cause: an (until recently) open bug in TUnit's OWN client-side pre-filter
/// (https://github.com/thomhurst/TUnit/issues/6026), which discards every test descriptor
/// before MTP's real filter ever runs whenever a parenthesized segment lacks a literal
/// <c>*</c>/<c>?</c> character. Fixed in TUnit.Engine 1.46.0
/// (https://github.com/thomhurst/TUnit/pull/6027 — confirmed via GitHub compare that the fix
/// commit lands between the v1.45.8 tag pinned by the project this bug was found against and
/// v1.46.0, and directly re-verified: the exact real-world method names that returned zero
/// matches on 1.45.8 match correctly on 1.46.0). So this only translates an OR-of-methods
/// filter when the target project's resolved TUnit.Engine version is known and at or above that
/// fix — otherwise it throws rather than risk the same silent zero-match on an older, unfixed
/// TUnit release.
/// </para>
/// <para>
/// OR-ing across different namespace/class combinations remains unsupported regardless of
/// TUnit version — that's a separate, genuine MTP grammar limit (OR over full paths, not within
/// one path segment, crashes the MTP test host: testfx#7415), not the TUnit pre-filter bug.
/// </para>
/// </summary>
internal static partial class TreeNodeFilterTranslator
{
    /// <summary>
    /// First TUnit.Engine version containing the fix for thomhurst/TUnit#6026. See the type doc
    /// comment for how this was pinned down and verified.
    /// </summary>
    internal static readonly Version MinimumTUnitEngineVersionWithOrFilterFix = new(1, 46, 0);

    [GeneratedRegex(@"^\s*FullyQualifiedName\s*(=|~)\s*(.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FullyQualifiedNameAtomRegex();

    /// <summary>
    /// Translates <paramref name="vsTestFilter"/> into an MTP <c>--treenode-filter</c> expression.
    /// </summary>
    /// <param name="resolvedTUnitEngineVersion">
    /// The target project's resolved <c>TUnit.Engine</c> version (see
    /// <see cref="ProjectMetadataParser.TryGetResolvedPackageVersion"/>), or <see langword="null"/>
    /// when it couldn't be determined (project not restored, or the assets file was unreadable).
    /// Gates whether a multi-method filter is safe to translate — see the type doc comment.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The filter names more than one test but the resolved TUnit.Engine version isn't known to
    /// have the OR pre-filter fix, spans more than one namespace+class combination, uses
    /// AND/grouping/negation, names a property other than <c>FullyQualifiedName</c>, or doesn't
    /// split into at least a class and method.
    /// </exception>
    public static string Translate(string vsTestFilter, Version? resolvedTUnitEngineVersion)
    {
        if (vsTestFilter.Contains('&') || vsTestFilter.Contains('(') || vsTestFilter.Contains(')'))
        {
            throw new InvalidOperationException(
                $"Filter '{vsTestFilter}' uses AND ('&') or parenthesized grouping, which this MTP filter " +
                "translation doesn't support. Only one or more FullyQualifiedName~ or FullyQualifiedName= " +
                "atoms joined with '|' are translated.");
        }

        var atoms = vsTestFilter.Split('|');
        var orFilterFixed = resolvedTUnitEngineVersion is not null
            && resolvedTUnitEngineVersion >= MinimumTUnitEngineVersionWithOrFilterFix;
        if (atoms.Length > 1 && !orFilterFixed)
        {
            throw new InvalidOperationException(
                $"Filter '{vsTestFilter}' names more than one test ('|'). MTP's --treenode-filter can OR " +
                "literal values within a single path segment, and MTP itself matches that correctly, but " +
                "TUnit's own pre-filter had a bug (fixed in TUnit.Engine 1.46.0 — " +
                "github.com/thomhurst/TUnit/issues/6026) that silently rejects every test for that exact " +
                "shape on older versions. " +
                (resolvedTUnitEngineVersion is null
                    ? "This project's resolved TUnit.Engine version could not be determined (has it been restored?)."
                    : $"This project resolves TUnit.Engine {resolvedTUnitEngineVersion}.") +
                " Call test_run once per test for now, or upgrade TUnit.Engine to 1.46.0 or later.");
        }

        var groups = new List<FilterGroup>();
        foreach (var atom in atoms)
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
