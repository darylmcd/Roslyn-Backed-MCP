using System.Text.RegularExpressions;
using RoslynMcp.Core.Services;

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
/// OR-ing exact method-qualified paths across different namespace/class combinations remains
/// unsupported — MTP's <c>--treenode-filter</c> can't disambiguate an independent OR per segment
/// from a cross-product (which of ClassA/ClassB does MethodX belong to?), and attempting it
/// crashes the MTP test host (testfx#7415). This is a limit of what THIS translator represents,
/// not a blanket claim about TUnit's grammar: TUnit's own filter syntax does support OR-ing bare
/// class names within one class segment when no method needs to be pinned down (e.g.
/// <c>/*/*/(LoginTests)|(SignupTests)/*</c>, all methods of either class — see
/// https://tunit.dev/docs/execution/test-filters/#or-filter-across-classes). This translator only
/// ever accepts <c>FullyQualifiedName</c> atoms, which are always method-qualified, so that
/// class-level shape isn't representable as input here regardless of version.
/// </para>
/// <para>
/// tunit-treenode-filter-requires-known-test: <c>FullyQualifiedName~</c> is VSTest's "contains"
/// operator, not "equals" — a value like <c>My.Tests.WidgetTests</c> is exactly as consistent
/// with "class WidgetTests in namespace My.Tests" (no method — a class-level filter this
/// translator can't represent) as with "method WidgetTests on class Tests in namespace My" (the
/// shape <see cref="RoslynMcp.Roslyn.Services.TestDiscoveryService.SynthesizeDotnetTestFilter"/>
/// always emits), and no amount of dot-counting resolves that ambiguity from the string alone.
/// <see cref="Translate"/> accepts an optional set of the target project's actually-discovered
/// test names; when supplied, an atom that doesn't name one of them is declined rather than
/// silently mistranslated. Every filter <c>SynthesizeDotnetTestFilter</c> produces names a real,
/// complete test by construction, so this never rejects that round trip.
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
    /// <param name="knownFullyQualifiedTestNames">
    /// The target project's actually-discovered test names (typically from
    /// <see cref="RoslynMcp.Core.Services.ITestDiscoveryService"/>), or <see langword="null"/> to
    /// skip this check. When supplied, every parsed atom must name one of these exactly — see the
    /// type doc comment's <c>tunit-treenode-filter-requires-known-test</c> section for why this is
    /// necessary to safely translate <c>~</c> atoms at all.
    /// </param>
    /// <exception cref="PublicInvalidOperationException">
    /// The filter names more than one test but the resolved TUnit.Engine version isn't known to
    /// have the OR pre-filter fix, spans more than one namespace+class combination, uses
    /// AND/grouping/negation, names a property other than <c>FullyQualifiedName</c>, doesn't split
    /// into at least a class and method, or (when <paramref name="knownFullyQualifiedTestNames"/>
    /// is supplied) doesn't match a real discovered test.
    /// </exception>
    public static string Translate(
        string vsTestFilter,
        Version? resolvedTUnitEngineVersion,
        IReadOnlyCollection<string>? knownFullyQualifiedTestNames = null)
    {
        if (vsTestFilter.Contains('&') || vsTestFilter.Contains('(') || vsTestFilter.Contains(')'))
        {
            throw new PublicInvalidOperationException(
                $"Filter '{vsTestFilter}' uses AND ('&') or parenthesized grouping, which this MTP filter " +
                "translation doesn't support. Only one or more FullyQualifiedName~ or FullyQualifiedName= " +
                "atoms joined with '|' are translated.");
        }

        var atoms = vsTestFilter.Split('|');
        var knownNames = knownFullyQualifiedTestNames is null
            ? null
            : new HashSet<string>(knownFullyQualifiedTestNames, StringComparer.Ordinal);

        var groups = new List<FilterGroup>();
        foreach (var atom in atoms)
        {
            var (@namespace, className, method) = ParseAtom(atom, vsTestFilter);

            if (knownNames is not null)
            {
                var candidateFqn = @namespace is null ? $"{className}.{method}" : $"{@namespace}.{className}.{method}";
                if (!knownNames.Contains(candidateFqn))
                {
                    throw new PublicInvalidOperationException(
                        $"Filter '{vsTestFilter}' contains atom '{atom}', which this translator parses as the " +
                        $"test '{candidateFqn}' — but no discovered test in this project has that fully-qualified " +
                        "name. A 'FullyQualifiedName~' filter is a contains match, not an exact one, so a value " +
                        "that names a class or namespace rather than one complete test (e.g. 'Foo' meaning " +
                        "\"every test in class Foo\") can't be safely translated to MTP's --treenode-filter this " +
                        "way. Run test_discover to confirm the exact test name, or call test_run once per test.");
                }
            }

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
            throw new PublicInvalidOperationException(
                $"Filter '{vsTestFilter}' names tests across {groups.Count} different namespace/class " +
                "combinations. Microsoft.Testing.Platform's --treenode-filter can OR alternatives within a " +
                "single path segment (e.g. TUnit supports OR-ing whole class names, like " +
                "'/*/*/(LoginTests)|(SignupTests)/*'), but OR-ing full method-qualified paths together across " +
                "classes is an unsafe cross-product and crashes the MTP test host — and this translator, whose " +
                "input is always method-qualified (FullyQualifiedName), has no way to drop down to the " +
                "class-only shape that would be safe here. Run test_run once per namespace/class group instead.");
        }

        var orFilterFixed = resolvedTUnitEngineVersion is not null
            && resolvedTUnitEngineVersion >= MinimumTUnitEngineVersionWithOrFilterFix;
        if (atoms.Length > 1 && !orFilterFixed)
        {
            throw new PublicInvalidOperationException(
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
            throw new PublicInvalidOperationException(
                $"Filter '{fullFilter}' contains atom '{atom}' that isn't a supported " +
                "'FullyQualifiedName=<value>' or 'FullyQualifiedName~<value>' expression. Only those two " +
                "operators against the FullyQualifiedName property are translated to MTP's --treenode-filter.");
        }

        // BuildFullyQualifiedTestName (TestDiscoveryService) builds "{namespace}.{class}.{method}";
        // nested types stay inside the class segment as "Outer+Inner",
        // joining a possibly multi-part namespace with dots — invert by taking the last two
        // dot-separated parts as class/method and treating everything before them as the namespace.
        var parts = match.Groups[2].Value.Split('.');
        if (parts.Length < 2)
        {
            throw new PublicInvalidOperationException(
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
