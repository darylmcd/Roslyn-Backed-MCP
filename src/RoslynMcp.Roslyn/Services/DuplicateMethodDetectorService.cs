using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Security.Cryptography;
using System.Text;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Detects near-duplicate method bodies by bucketing methods whose AST-normalized
/// structure hashes collide, then confirming the group with a length-scaled
/// similarity comparison. The normalization strips trivia, renames locals/parameters
/// to ordinal placeholders, and reduces each body to a canonical sequence of
/// <see cref="SyntaxKind"/> plus anonymized identifier slots — so overloads with
/// identical bodies cluster while overloads with different bodies do not.
/// </summary>
public sealed class DuplicateMethodDetectorService : IDuplicateMethodDetectorService
{
    private readonly IWorkspaceManager _workspace;

    public DuplicateMethodDetectorService(IWorkspaceManager workspace)
    {
        _workspace = workspace;
    }

    public async Task<IReadOnlyList<DuplicatedMethodGroupDto>> FindDuplicatedMethodsAsync(
        string workspaceId,
        DuplicateMethodAnalysisOptions options,
        CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);

        // Phase 1: bucket every qualifying method-body by its canonical sequence.
        // Identical canonical strings = exact structural duplicates.
        var buckets = await CollectMethodBucketsAsync(solution, options, ct).ConfigureAwait(false);

        // Phase 2: emit exact-structural matches as DTOs (similarity = 1.0 by construction).
        var results = EmitGroupsFromBuckets(buckets, options, ct);

        // Phase 3: rank descending by MemberCount, then LineCount. Stable for determinism.
        return RankGroups(results, options.Limit);
    }

    /// <summary>
    /// Walks every project document, syntactically extracting every method-body that
    /// passes the file-/length-filter gates, and buckets them by canonical AST string.
    /// Pure syntax — no compilation binding required — so projects with unresolved
    /// references still produce results.
    /// </summary>
    private async Task<Dictionary<string, List<MethodCandidate>>> CollectMethodBucketsAsync(
        Solution solution,
        DuplicateMethodAnalysisOptions options,
        CancellationToken ct)
    {
        var buckets = new Dictionary<string, List<MethodCandidate>>(StringComparer.Ordinal);
        var projects = ProjectFilterHelper.FilterProjects(solution, options.ProjectFilter);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested) break;
            await CollectProjectMethodsAsync(project, options, buckets, ct).ConfigureAwait(false);
        }

        return buckets;
    }

    /// <summary>
    /// Per-project document walk. Skips generated/content trees via <see cref="PathFilter"/>
    /// AND the filename-convention gate (generator outputs in src/ with <c>.g.cs</c> /
    /// <c>.Designer.cs</c> suffixes). Buckets are accumulated into the shared dictionary.
    /// </summary>
    private static async Task CollectProjectMethodsAsync(
        Project project,
        DuplicateMethodAnalysisOptions options,
        Dictionary<string, List<MethodCandidate>> buckets,
        CancellationToken ct)
    {
        foreach (var doc in project.Documents)
        {
            if (ct.IsCancellationRequested) break;
            if (PathFilter.IsGeneratedOrContentFile(doc.FilePath)) continue;
            if (IsLikelyGeneratedByName(doc.FilePath)) continue;

            var tree = await doc.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            if (tree is null) continue;

            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
            BucketDocumentMethods(doc, tree, root, project.Name, options, buckets, ct);
        }
    }

    /// <summary>
    /// Per-document loop: walk each <see cref="MethodDeclarationSyntax"/>, gate on body
    /// presence + line-count threshold + non-empty canonical hash, and append the
    /// resulting <see cref="MethodCandidate"/> to its bucket.
    /// </summary>
    private static void BucketDocumentMethods(
        Document doc,
        SyntaxTree tree,
        SyntaxNode root,
        string projectName,
        DuplicateMethodAnalysisOptions options,
        Dictionary<string, List<MethodCandidate>> buckets,
        CancellationToken ct)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (ct.IsCancellationRequested) break;

            var body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
            if (body is null) continue; // abstract, partial-no-body, interface declarations

            // xUnit [Theory] test methods share an identical dispatch shape (assert against
            // every [InlineData]/[MemberData] row) so they bucket together even though they
            // exercise different business logic. Exclude them by attribute-name suffix —
            // both `[Theory]` and the fully-qualified `[TheoryAttribute]` form match. Pure
            // syntactic check; no xUnit reference needed.
            if (HasTheoryAttribute(method)) continue;

            var lineSpan = method.GetLocation().GetLineSpan();
            var startLine = lineSpan.StartLinePosition.Line + 1;
            var endLine = lineSpan.EndLinePosition.Line + 1;
            var lineCount = endLine - startLine + 1;
            if (lineCount < options.MinLines) continue;

            var canonical = Canonicalize(body);
            if (string.IsNullOrEmpty(canonical)) continue;

            if (!buckets.TryGetValue(canonical, out var bucket))
            {
                bucket = new List<MethodCandidate>();
                buckets[canonical] = bucket;
            }

            bucket.Add(new MethodCandidate(
                FilePath: doc.FilePath ?? tree.FilePath ?? string.Empty,
                StartLine: startLine,
                EndLine: endLine,
                LineCount: lineCount,
                MethodName: method.Identifier.ValueText,
                ContainingType: ExtractContainingType(method),
                ProjectName: projectName,
                Canonical: canonical));
        }
    }

    /// <summary>
    /// Convert exact-structural buckets (>= 2 members) into <see cref="DuplicatedMethodGroupDto"/>
    /// instances. Similarity is 1.0 by construction since every member's canonical string
    /// is identical to the bucket key. Threshold &gt; 1.0 trivially excludes everything.
    /// </summary>
    private static List<DuplicatedMethodGroupDto> EmitGroupsFromBuckets(
        Dictionary<string, List<MethodCandidate>> buckets,
        DuplicateMethodAnalysisOptions options,
        CancellationToken ct)
    {
        var results = new List<DuplicatedMethodGroupDto>();
        foreach (var (canonical, members) in buckets)
        {
            if (ct.IsCancellationRequested) break;
            if (results.Count >= options.Limit) break;
            if (members.Count < 2) continue;
            if (options.SimilarityThreshold > 1.0) continue;

            // Default emission: exact-structural duplicate, similarity 1.0, no discriminator.
            // The symmetric-mapper classifier below downranks the score and tags the cluster
            // when the bucket is exactly a strict To*/From* complementary pair so callers can
            // skip these designed-symmetric pairs without losing the signal entirely.
            var similarity = 1.0;
            string? clusterKind = null;
            if (IsSymmetricMapperPair(members))
            {
                // Pair shares an AST shape because the mapper is symmetric by design — the
                // copy-paste signal is weak. Downrank to the mid-band; threshold-filtering
                // callers can drop these by raising the similarity floor.
                similarity = 0.5;
                clusterKind = "round-trip-mapper";
            }

            results.Add(new DuplicatedMethodGroupDto(
                NormalizedHash: ShortHash(canonical),
                MemberCount: members.Count,
                Similarity: similarity,
                LineCount: members[0].LineCount,
                Methods: members.Select(m => new DuplicatedMethodMemberDto(
                    FilePath: m.FilePath,
                    StartLine: m.StartLine,
                    EndLine: m.EndLine,
                    MethodName: m.MethodName,
                    ContainingType: m.ContainingType,
                    ProjectName: m.ProjectName)).ToList(),
                ClusterKind: clusterKind));
        }
        return results;
    }

    /// <summary>
    /// True when the bucket is exactly two members whose names are complementary
    /// <c>To*</c>/<c>From*</c> forms sharing the same stem — e.g. <c>ToDto</c>/<c>FromDto</c>,
    /// <c>ToWire</c>/<c>FromWire</c>. The heuristic is intentionally strict: three or more
    /// members never trip it (a 3+ bucket is structural duplication that happens to include
    /// a mapper, not a designed pair), and mismatched stems (<c>ToX</c>/<c>FromY</c>) are
    /// rejected because the structural collision is then coincidental, not designed.
    /// </summary>
    private static bool IsSymmetricMapperPair(IReadOnlyList<MethodCandidate> members)
    {
        if (members.Count != 2) return false;

        var first = members[0].MethodName;
        var second = members[1].MethodName;

        // Identify which member starts with "To" and which with "From"; reject when both
        // share the same prefix (e.g. two ToFoo overloads) or when neither does.
        var firstStem = ExtractMapperStem(first);
        var secondStem = ExtractMapperStem(second);
        if (firstStem is null || secondStem is null) return false;

        // Stems must agree AND prefixes must be complementary (one To, one From).
        return firstStem.Value.Stem == secondStem.Value.Stem
            && firstStem.Value.Prefix != secondStem.Value.Prefix;
    }

    /// <summary>
    /// Decompose a method name into its mapper-prefix (<c>"To"</c>|<c>"From"</c>) and the
    /// remaining stem when the name starts with one of those prefixes followed by an
    /// uppercase letter. Returns <see langword="null"/> when the name is neither.
    /// The uppercase-follows requirement avoids matching names like <c>Toggle</c> or
    /// <c>Format</c> where "To"/"From" are merely a coincidental letter run.
    /// </summary>
    private static (string Prefix, string Stem)? ExtractMapperStem(string name)
    {
        if (name.Length > 2 && name.StartsWith("To", StringComparison.Ordinal) && char.IsUpper(name[2]))
        {
            return ("To", name[2..]);
        }
        if (name.Length > 4 && name.StartsWith("From", StringComparison.Ordinal) && char.IsUpper(name[4]))
        {
            return ("From", name[4..]);
        }
        return null;
    }

    /// <summary>
    /// Syntactic check for an <c>[xUnit.Theory]</c> attribute on a method declaration —
    /// matches both the short <c>[Theory]</c> form and the fully-qualified
    /// <c>[TheoryAttribute]</c> form. Uses the last name segment of qualified-name attributes
    /// (so <c>[Xunit.TheoryAttribute]</c> matches too). No semantic-model lookup.
    /// </summary>
    private static bool HasTheoryAttribute(MethodDeclarationSyntax method)
    {
        foreach (var list in method.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                var name = attribute.Name switch
                {
                    QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
                    SimpleNameSyntax simple => simple.Identifier.ValueText,
                    _ => string.Empty,
                };
                if (string.IsNullOrEmpty(name)) continue;
                if (name.Equals("Theory", StringComparison.Ordinal)
                    || name.Equals("TheoryAttribute", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Sort descending by <c>MemberCount</c> (largest clusters first), then by <c>LineCount</c>
    /// (larger methods carry more signal than 10-line clusters). Orderings are stable
    /// so determinism within a ranking tier is preserved.
    /// </summary>
    private static List<DuplicatedMethodGroupDto> RankGroups(List<DuplicatedMethodGroupDto> results, int limit)
        => results
            .OrderByDescending(g => g.MemberCount)
            .ThenByDescending(g => g.LineCount)
            .Take(limit)
            .ToList();

    /// <summary>
    /// Excludes source-generator and designer files by filename convention — <c>PathFilter</c>
    /// covers obj/ and NuGet content, but generator outputs regularly sit in src/ with a
    /// <c>.g.cs</c> or <c>.Designer.cs</c> suffix.
    /// </summary>
    private static bool IsLikelyGeneratedByName(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        var name = Path.GetFileName(filePath);
        return name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the containing type's name for a method, walking up through partial-type
    /// nests and namespace scopes. Uses syntax-tree shape only (no semantic model) so it
    /// works on unbound trees.
    /// </summary>
    private static string? ExtractContainingType(MethodDeclarationSyntax method)
    {
        var node = method.Parent;
        while (node is not null)
        {
            if (node is TypeDeclarationSyntax type)
            {
                return type.Identifier.ValueText;
            }
            node = node.Parent;
        }
        return null;
    }

    /// <summary>
    /// Canonicalize a method body to a string representation that is stable across
    /// cosmetic differences. The strategy: walk the syntax tree, emit each node's
    /// <see cref="SyntaxKind"/>, and for identifier tokens (locals, parameters,
    /// member-access names) substitute ordinal placeholders assigned on first
    /// appearance. Trivia is stripped by only inspecting tokens/kinds. Literals are
    /// preserved as-is so two "string concat Hello + World" methods don't collapse
    /// into one bucket with a "format (a, b)" method.
    /// </summary>
    private static string Canonicalize(SyntaxNode body)
    {
        // Hard ceiling on the walked subtree to protect the detector against a
        // pathologically large auto-generated method body.
        const int MaxNodes = 50_000;

        var sb = new StringBuilder(capacity: 1024);
        var identifierMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var visited = 0;

        foreach (var node in body.DescendantNodesAndTokensAndSelf())
        {
            if (++visited > MaxNodes) return string.Empty; // bail out on outliers
            sb.Append((int)node.Kind());
            sb.Append('.');

            // Tokens carry the identifier text; nodes don't. Only normalize identifier
            // tokens — keep literal tokens (numeric/string) and keyword tokens by
            // value so bodies that differ on literal content don't collide.
            if (node.IsToken)
            {
                var token = node.AsToken();
                if (token.IsKind(SyntaxKind.IdentifierToken))
                {
                    var key = token.ValueText;
                    if (!identifierMap.TryGetValue(key, out var placeholder))
                    {
                        placeholder = "$" + identifierMap.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        identifierMap[key] = placeholder;
                    }
                    sb.Append(placeholder);
                }
                else if (token.IsKind(SyntaxKind.StringLiteralToken)
                      || token.IsKind(SyntaxKind.NumericLiteralToken)
                      || token.IsKind(SyntaxKind.CharacterLiteralToken))
                {
                    // Literals keep their value so 'return "foo"' vs 'return "bar"'
                    // bucket separately — a bucket containing both would cluster
                    // unrelated helpers that merely share scaffolding.
                    sb.Append(token.ValueText);
                }
            }
            sb.Append('|');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns a short, stable hash of a canonical string — used only as a DTO field
    /// so callers can correlate groups across invocations. Not security-sensitive.
    /// </summary>
    private static string ShortHash(string canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        // First 8 bytes as hex → 16 hex chars. Plenty of entropy for bucket IDs.
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++)
        {
            sb.Append(bytes[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Carries the per-method data needed for clustering and DTO emission.
    /// Kept private to the service since it's purely an implementation detail.
    /// </summary>
    private sealed record MethodCandidate(
        string FilePath,
        int StartLine,
        int EndLine,
        int LineCount,
        string MethodName,
        string? ContainingType,
        string ProjectName,
        string Canonical);
}
