using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Resolves Roslyn <see cref="ISymbol"/> instances from a <see cref="SymbolLocator"/> within a
/// loaded <see cref="Solution"/>.
/// </summary>
public static class SymbolResolver
{
    private const int MetadataNameMessageMaxLength = 200;
    private const int NearestQueryMaxLength = 256;

    private static readonly SymbolDisplayFormat QualifiedNameOnlyFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None);

    /// <summary>
    /// compilation-cache-adoption-read-side: liveness gate for the opt-in
    /// <see cref="ICompilationCache"/> routing on the per-project compilation fetches in
    /// <see cref="ResolveByMetadataNameAsync"/>, <see cref="FindClosestMatchesAsync"/>, and
    /// <see cref="SymbolHandleSerializer.FindAllByMetadataNameAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ICompilationCache"/> keys purely on <c>(workspaceId, ProjectId, workspaceVersion)</c>,
    /// so serving a FORKED (preview) solution from it would hand back a compilation built from the LIVE
    /// workspace content while the caller is reasoning about different document text. Caching is
    /// therefore permitted only when the supplied solution IS the workspace's current solution: every
    /// fork (e.g. <c>solution.WithDocumentText(...)</c>, or the never-applied solution
    /// <c>CrossProjectRefactoringService.PreviewDependencyInversionAsync</c> builds) is
    /// reference-distinct from <see cref="Workspace.CurrentSolution"/> and falls through to the raw
    /// per-project fetch.
    /// </para>
    /// <para>
    /// group-c-compilation-cache-gate-hardening: the check is exactly
    /// <c>solution == IWorkspaceManager.GetCurrentSolution(workspaceId)</c>, delegated to
    /// <see cref="ICompilationCache.IsLiveSolution"/> (which holds the workspace-manager reference a
    /// static helper cannot). The former self-referential form
    /// <c>ReferenceEquals(solution, solution.Workspace.CurrentSolution)</c> proved liveness only
    /// w.r.t. the solution's OWN <see cref="Solution.Workspace"/> back-reference; a LIVE solution
    /// belonging to workspace B passed together with <c>workspaceId: "A"</c> satisfied it and would
    /// have polluted A's cache slot. Keying on the caller-supplied id closes that identity gap.
    /// </para>
    /// <para>
    /// This must be re-evaluated on EVERY per-project compilation fetch, never hoisted above the
    /// project loop: <see cref="ICompilationCache"/> re-reads the workspace version on each call, so
    /// a reload landing mid-scan would otherwise let a pre-bump <see cref="Project"/> be compiled and
    /// STORED under the post-bump cache key, poisoning it for every later reader.
    /// <see cref="GetProjectCompilationAsync"/> is the single fetch-dispatch site that guarantees it.
    /// </para>
    /// </remarks>
    internal static bool CanUseCompilationCache(Solution solution, ICompilationCache? cache, string? workspaceId)
    {
        if (cache is null || string.IsNullOrEmpty(workspaceId))
        {
            return false;
        }

        return cache.IsLiveSolution(workspaceId, solution);
    }

    /// <summary>
    /// group-c-compilation-cache-gate-hardening: rejects the half-configured opt-in — a
    /// <paramref name="compilationCache"/> supplied without a <paramref name="workspaceId"/> to key
    /// it by. Previously this silently disabled caching, so a caller that meant to opt in got the
    /// uncached path with no signal. Called once per public helper BEFORE its project loop, so a
    /// solution with zero projects still fails loudly.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="compilationCache"/> is non-<see langword="null"/> and
    /// <paramref name="workspaceId"/> is <see langword="null"/> or empty.
    /// </exception>
    internal static void ValidateCacheParameters(ICompilationCache? compilationCache, string? workspaceId)
    {
        // The reverse combination (workspaceId without a cache) stays a silent no-cache by design:
        // every caller that threads a workspaceId through for other reasons must keep working.
        if (compilationCache is not null && string.IsNullOrEmpty(workspaceId))
        {
            throw new ArgumentException(
                "workspaceId is required when compilationCache is supplied — the compilation cache keys on it.",
                nameof(workspaceId));
        }
    }

    /// <summary>
    /// group-c-compilation-cache-gate-hardening: the single per-project compilation fetch-dispatch
    /// site shared by <see cref="ResolveByMetadataNameAsync"/>, <see cref="FindClosestMatchesAsync"/>,
    /// and <see cref="SymbolHandleSerializer.FindAllByMetadataNameAsync"/>. Evaluates
    /// <see cref="CanUseCompilationCache"/> FRESH on every call — the gate must never be hoisted
    /// above the caller's project loop.
    /// </summary>
    internal static async Task<Compilation?> GetProjectCompilationAsync(
        Solution solution,
        Project project,
        ICompilationCache? compilationCache,
        string? workspaceId,
        CancellationToken ct)
    {
        return CanUseCompilationCache(solution, compilationCache, workspaceId)
            ? await compilationCache!.GetCompilationAsync(workspaceId!, project, ct).ConfigureAwait(false)
            : await project.GetCompilationAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a symbol from the given <paramref name="locator"/> using whichever identification
    /// strategy is set (handle, metadata name, or source position).
    /// </summary>
    /// <param name="solution">The solution to search.</param>
    /// <param name="locator">The symbol locator describing how to find the symbol.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="strict">
    /// symbol-info-lenient-whitespace-resolution: when <see langword="true"/>, disables the
    /// preceding-token fallback in <see cref="ResolveAtPositionAsync"/> — a caret on whitespace
    /// next to an identifier will return <see langword="null"/> instead of silently resolving to
    /// the adjacent symbol. Default <see langword="false"/> preserves the existing lenient
    /// behavior for every tool that was passing symbol locators before the strict-opt-in contract
    /// existed. <c>symbol_info</c> is the primary opt-in caller (default <c>allowAdjacent=false</c>
    /// flips the flag to <c>true</c>).
    /// </param>
    /// <returns>The resolved symbol, or <see langword="null"/> if it could not be found.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="locator"/> does not contain a valid identification strategy.</exception>
    public static async Task<ISymbol?> ResolveAsync(Solution solution, SymbolLocator locator, CancellationToken ct, bool strict = false)
    {
        locator.Validate();

        if (locator.HasHandle)
        {
            return await SymbolHandleSerializer.ResolveHandleAsync(solution, locator.SymbolHandle!, ct).ConfigureAwait(false);
        }

        if (locator.HasMetadataName)
        {
            return await ResolveByMetadataNameAsync(solution, locator.MetadataName!, ct).ConfigureAwait(false);
        }

        return await ResolveAtPositionAsync(solution, locator.FilePath!, locator.Line!.Value, locator.Column!.Value, ct, strict).ConfigureAwait(false);
    }

    /// <summary>
    /// Same as <see cref="ResolveAsync"/> but throws <see cref="KeyNotFoundException"/> when the
    /// locator does not resolve to a symbol. Used by reader tools (`find_references`,
    /// `find_consumers`, `find_type_usages`, etc.) so callers see a structured NotFound envelope
    /// from <see cref="RoslynMcp.Host.Stdio.Tools.ToolErrorHandler"/> instead of an empty result
    /// that can't be distinguished from a legitimate "valid symbol, zero references" outcome.
    /// </summary>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown when the locator decodes correctly but no symbol can be found in the solution.</exception>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="locator"/> does not contain a valid identification strategy.</exception>
    public static async Task<ISymbol> ResolveOrThrowAsync(Solution solution, SymbolLocator locator, CancellationToken ct)
    {
        var symbol = await ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is not null) return symbol;

        if (locator.HasMetadataName)
        {
            throw await CreateSymbolNotFoundExceptionAsync(solution, locator, ct).ConfigureAwait(false);
        }

        var detail = locator.HasHandle
            ? "the supplied symbol handle"
            : locator.HasMetadataName
                ? $"metadata name '{locator.MetadataName}'"
                : $"position {locator.FilePath}:{locator.Line}:{locator.Column}";

        throw new KeyNotFoundException(
            $"No symbol could be resolved for {detail}. The handle may be from a previous workspace " +
            "version, the symbol may have been removed, or the position may not contain a symbol identifier.");
    }

    public static async Task<SymbolNotFoundException> CreateSymbolNotFoundExceptionAsync(
        Solution solution,
        SymbolLocator locator,
        CancellationToken ct)
    {
        var metadataName = locator.MetadataName ?? string.Empty;
        var truncatedName = TruncateForMessage(metadataName, MetadataNameMessageMaxLength);
        var closestMatches = locator.HasMetadataName
            ? await FindClosestMatchesAsync(solution, metadataName, maxResults: 5, ct).ConfigureAwait(false)
            : Array.Empty<SymbolClosestMatchDto>();

        var message = $"No symbol could be resolved for metadata name '{truncatedName}'. " +
            "The metadata name may be misspelled, from a previous workspace version, or removed from the current solution.";

        return new SymbolNotFoundException(message, closestMatches);
    }

    /// <param name="compilationCache">
    /// compilation-cache-adoption-read-side: optional shared compilation cache. When supplied together
    /// with <paramref name="workspaceId"/> AND <paramref name="solution"/> is the live workspace
    /// solution, the per-project compilation fetch is routed through it so independent callers share
    /// warm compilations. Omitted (the default) or a forked solution takes the raw fetch — see
    /// <see cref="CanUseCompilationCache"/>.
    /// </param>
    /// <param name="workspaceId">
    /// The workspace session identifier, required to key the cache. Mandatory whenever
    /// <paramref name="compilationCache"/> is supplied — the half-configured combination throws
    /// <see cref="System.ArgumentException"/> instead of silently taking the uncached path.
    /// </param>
    /// <exception cref="System.ArgumentException">
    /// Thrown when <paramref name="compilationCache"/> is supplied without a <paramref name="workspaceId"/>.
    /// </exception>
    public static async Task<IReadOnlyList<SymbolClosestMatchDto>> FindClosestMatchesAsync(
        Solution solution,
        string query,
        int maxResults,
        CancellationToken ct,
        ICompilationCache? compilationCache = null,
        string? workspaceId = null)
    {
        ValidateCacheParameters(compilationCache, workspaceId);

        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0)
            return Array.Empty<SymbolClosestMatchDto>();

        var normalizedQuery = NormalizeForDistance(query);
        var matches = new List<(int Score, string MetadataName, string Kind, string? LocationHint)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in solution.Projects)
        {
            if (ct.IsCancellationRequested) break;

            var compilation = await GetProjectCompilationAsync(
                solution, project, compilationCache, workspaceId, ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var symbol in EnumerateNamedTypesAndMembers(compilation.GlobalNamespace))
            {
                if (ct.IsCancellationRequested) break;
                if (symbol.IsImplicitlyDeclared) continue;

                var metadataName = symbol.ToDisplayString(QualifiedNameOnlyFormat);
                if (string.IsNullOrWhiteSpace(metadataName) || !seen.Add(metadataName))
                    continue;

                var score = ScoreCandidate(normalizedQuery, metadataName, symbol.Name);
                if (score >= 90)
                    continue;

                matches.Add((score, metadataName, GetSymbolKind(symbol), FormatLocationHint(symbol)));
            }
        }

        return matches
            .OrderBy(m => m.Score)
            .ThenBy(m => m.MetadataName, StringComparer.Ordinal)
            .Take(maxResults)
            .Select(m => new SymbolClosestMatchDto(m.MetadataName, m.Kind, m.LocationHint))
            .ToList();
    }

    /// <summary>
    /// Resolves the symbol at the given 1-based <paramref name="line"/> and <paramref name="column"/> position
    /// in the specified source file.
    /// </summary>
    /// <param name="strict">
    /// symbol-info-lenient-whitespace-resolution: when <see langword="true"/>, the lenient
    /// preceding-token fallback is skipped — a caret on whitespace adjacent to an identifier
    /// returns <see langword="null"/> instead of resolving to that identifier.
    /// </param>
    /// <returns>The resolved symbol, or <see langword="null"/> if no symbol exists at that position.</returns>
    public static async Task<ISymbol?> ResolveAtPositionAsync(
        Solution solution, string filePath, int line, int column, CancellationToken ct, bool strict = false)
    {
        var document = FindDocument(solution, filePath);
        if (document is null) return null;

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel is null) return null;

        var syntaxTree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (syntaxTree is null) return null;

        var text = syntaxTree.GetText(ct);
        if (line < 1 || line > text.Lines.Count)
        {
            throw new ArgumentException(
                $"Line {line} is out of range. The file has {text.Lines.Count} line(s).",
                nameof(line));
        }

        var position = text.Lines[line - 1].Start + (column - 1);
        var root = await syntaxTree.GetRootAsync(ct).ConfigureAwait(false);

        // Try the token at the exact position first, then also try FindToken with
        // findInsideTrivia to handle mid-identifier cursor positions (AUDIT-05).
        var token = root.FindToken(position);

        // symbol-info-lenient-whitespace-resolution: in strict mode, reject the match when the
        // caret sits on trivia (whitespace / comment / end-of-line) that is LEADING to the found
        // token. Pre-fix, a caret on a blank line right before `ILibraryManager` would silently
        // resolve to that identifier. Trailing trivia inside the token is still accepted because
        // Roslyn's FindToken classifies trailing space as part of the token's FullSpan but
        // callers typically land between-tokens in leading-trivia territory.
        if (strict)
        {
            var tokenStart = token.SpanStart;
            if (position < tokenStart)
            {
                // Caret sits inside the token's leading trivia (between the previous token's
                // trailing trivia boundary and this token's Start). Reject — don't fall through
                // to the preceding-token fallback either.
                return null;
            }
        }

        var result = ResolveFromToken(token, semanticModel, ct);
        if (result is not null) return result;

        // symbol-info-lenient-whitespace-resolution: the preceding-token fallback is purely
        // lenient — gate it on !strict so opt-in strict mode can't drift back into adjacent
        // resolution via the "cursor on '(' after method name" shape.
        if (!strict && position > 0)
        {
            var precedingToken = root.FindToken(position - 1);
            if (precedingToken != token)
            {
                result = ResolveFromToken(precedingToken, semanticModel, ct);
                if (result is not null) return result;
            }
        }

        return null;
    }

    /// <summary>
    /// rename-preview-caret-resolution: Walking <c>token.Parent</c> with
    /// <see cref="SemanticModel.GetSymbolInfo"/> before <see cref="SemanticModel.GetDeclaredSymbol"/>
    /// on every node lets a caret on a nested identifier (e.g. tuple element) climb to an enclosing
    /// member or type. Prefer symbol-info on the immediate parent so return-type / usage tokens resolve
    /// to types; prefer declared-symbol when deeper so locals and parameters win over outer scopes.
    /// </summary>
    private static ISymbol? ResolveFromToken(SyntaxToken token, SemanticModel semanticModel, CancellationToken ct)
    {
        SyntaxNode? node = token.Parent;
        var depth = 0;
        const int maxDepth = 24;

        while (node is not null && depth < maxDepth)
        {
            depth++;
            var declared = semanticModel.GetDeclaredSymbol(node, ct);
            var symbolInfo = semanticModel.GetSymbolInfo(node, ct);

            if (depth == 1)
            {
                if (symbolInfo.Symbol is not null)
                    return symbolInfo.Symbol;
                if (symbolInfo.CandidateReason == CandidateReason.MemberGroup && symbolInfo.CandidateSymbols.Length > 0)
                    return symbolInfo.CandidateSymbols[0];
                if (declared is not null)
                    return declared;
            }
            else
            {
                if (declared is not null)
                    return declared;
                if (symbolInfo.Symbol is not null)
                    return symbolInfo.Symbol;
                if (symbolInfo.CandidateReason == CandidateReason.MemberGroup && symbolInfo.CandidateSymbols.Length > 0)
                    return symbolInfo.CandidateSymbols[0];
            }

            node = node.Parent;
        }

        return null;
    }

    /// <summary>
    /// Searches all projects in <paramref name="solution"/> for a symbol matching the given
    /// metadata name. Resolves TYPES via <see cref="Compilation.GetTypeByMetadataName"/> first
    /// (fast path). If the input is dotted like <c>Namespace.Type.Member</c> and no type with
    /// that full name exists, splits on the last dot and resolves the final segment as a member
    /// of the type before it — so <c>SampleLib.AnimalService.GetAllAnimals</c> resolves to the
    /// method even though no type has that exact name. Returns the first match; generic method
    /// overloads return the first arity match.
    /// </summary>
    /// <param name="compilationCache">
    /// compilation-cache-adoption-read-side: optional shared compilation cache. When supplied together
    /// with <paramref name="workspaceId"/> AND <paramref name="solution"/> is the live workspace
    /// solution, the per-project compilation fetch is routed through it. Omitted (the default) or a
    /// forked solution takes the raw fetch — see <see cref="CanUseCompilationCache"/>.
    /// </param>
    /// <param name="workspaceId">
    /// The workspace session identifier, required to key the cache. Mandatory whenever
    /// <paramref name="compilationCache"/> is supplied — the half-configured combination throws
    /// <see cref="System.ArgumentException"/> instead of silently taking the uncached path.
    /// </param>
    /// <exception cref="System.ArgumentException">
    /// Thrown when <paramref name="compilationCache"/> is supplied without a <paramref name="workspaceId"/>.
    /// </exception>
    /// <returns>The first matching symbol, or <see langword="null"/> if not found.</returns>
    public static async Task<ISymbol?> ResolveByMetadataNameAsync(
        Solution solution,
        string metadataName,
        CancellationToken ct,
        ICompilationCache? compilationCache = null,
        string? workspaceId = null)
    {
        ValidateCacheParameters(compilationCache, workspaceId);

        foreach (var project in solution.Projects)
        {
            var compilation = await GetProjectCompilationAsync(
                solution, project, compilationCache, workspaceId, ct).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            // Fast path: full metadata name resolves directly to a type.
            var typeSymbol = compilation.GetTypeByMetadataName(metadataName);
            if (typeSymbol is not null)
            {
                return typeSymbol;
            }

            // Fallback: resolve as a member by splitting at the last '.'
            // (Namespace.Type.Member — the containing type is everything before the last dot).
            var lastDot = metadataName.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == metadataName.Length - 1) continue;

            var containingTypeName = metadataName[..lastDot];
            var memberName = metadataName[(lastDot + 1)..];
            var containingType = compilation.GetTypeByMetadataName(containingTypeName);
            if (containingType is null) continue;

            var member = containingType.GetMembers(memberName).FirstOrDefault();
            if (member is not null)
            {
                return member;
            }
        }

        return null;
    }

    private static IEnumerable<ISymbol> EnumerateNamedTypesAndMembers(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol child)
            {
                foreach (var nested in EnumerateNamedTypesAndMembers(child))
                    yield return nested;
            }
            else if (member is INamedTypeSymbol type)
            {
                foreach (var symbol in EnumerateTypeAndMembers(type))
                    yield return symbol;
            }
        }
    }

    private static IEnumerable<ISymbol> EnumerateTypeAndMembers(INamedTypeSymbol type)
    {
        yield return type;

        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var symbol in EnumerateTypeAndMembers(nested))
                yield return symbol;
        }

        foreach (var member in type.GetMembers())
        {
            if (!member.IsImplicitlyDeclared)
                yield return member;
        }
    }

    private static int ScoreCandidate(string normalizedQuery, string metadataName, string simpleName)
    {
        var normalizedMetadataName = NormalizeForDistance(metadataName);
        var normalizedSimpleName = NormalizeForDistance(simpleName);

        if (string.Equals(normalizedMetadataName, normalizedQuery, StringComparison.Ordinal))
            return 0;
        if (normalizedMetadataName.Contains(normalizedQuery, StringComparison.Ordinal))
            return 1;
        if (normalizedQuery.Contains(normalizedMetadataName, StringComparison.Ordinal))
            return 2;
        if (normalizedSimpleName.Contains(normalizedQuery, StringComparison.Ordinal)
            || normalizedQuery.Contains(normalizedSimpleName, StringComparison.Ordinal))
            return 3;

        return Math.Min(
            BoundedLevenshtein(normalizedQuery, normalizedMetadataName),
            BoundedLevenshtein(normalizedQuery, normalizedSimpleName) + 10);
    }

    private static int BoundedLevenshtein(string left, string right)
    {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            var rowBest = current[0];

            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
                rowBest = Math.Min(rowBest, current[j]);
            }

            if (rowBest > 90)
                return rowBest;

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static string NormalizeForDistance(string value)
    {
        var normalized = value.Length <= NearestQueryMaxLength
            ? value
            : value[..NearestQueryMaxLength];
        return normalized.ToUpperInvariant();
    }

    private static string GetSymbolKind(ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol namedType)
            return namedType.TypeKind.ToString();
        return symbol.Kind.ToString();
    }

    private static string? FormatLocationHint(ISymbol symbol)
    {
        var sourceLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        if (sourceLocation is null) return null;

        var span = sourceLocation.GetLineSpan();
        return $"{span.Path}:{span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1}";
    }

    private static string TruncateForMessage(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        if (value.Length <= maxLength) return value;
        return value[..maxLength] + "...";
    }

    /// <summary>
    /// Finds the first document in <paramref name="solution"/> whose file path matches <paramref name="filePath"/>
    /// using the current platform's filesystem identity semantics.
    /// </summary>
    /// <returns>The matching document, or <see langword="null"/> if not found.</returns>
    public static Document? FindDocument(Solution solution, string filePath)
    {
        var normalizedPath = PhysicalPathResolver.Resolve(filePath);
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (document.FilePath is not null &&
                    FileSystemPath.Comparer.Equals(PhysicalPathResolver.Resolve(document.FilePath), normalizedPath))
                {
                    return document;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// FLAG-006: When the caret lands on a member's type token (e.g. the return-type token of a
    /// method declaration), <see cref="ResolveAtPositionAsync"/> resolves the *type* symbol rather
    /// than the enclosing member. This helper walks the syntax tree upward from the caret token
    /// looking for an enclosing member declaration (method, local function, or property) and
    /// returns its declared symbol so callers can promote a type-token resolution to the more
    /// useful declaring-member resolution.
    /// </summary>
    /// <returns>
    /// The enclosing <see cref="IMethodSymbol"/> or <see cref="IPropertySymbol"/>, or
    /// <see langword="null"/> if no enclosing member is found.
    /// </returns>
    public static async Task<ISymbol?> TryResolveEnclosingMemberAsync(
        Solution solution, string filePath, int line, int column, CancellationToken ct)
    {
        var document = FindDocument(solution, filePath);
        if (document is null) return null;

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (semanticModel is null || tree is null) return null;

        var text = tree.GetText(ct);
        if (line < 1 || line > text.Lines.Count) return null;

        var position = text.Lines[line - 1].Start + (column - 1);
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

        var node = root.FindToken(position).Parent;
        while (node is not null)
        {
            if (node is MethodDeclarationSyntax or LocalFunctionStatementSyntax or PropertyDeclarationSyntax)
            {
                var declared = semanticModel.GetDeclaredSymbol(node, ct);
                if (declared is IMethodSymbol or IPropertySymbol)
                    return declared;
            }
            node = node.Parent;
        }

        return null;
    }

    public static async Task<string?> GetPreviewTextAsync(Document document, Location location, CancellationToken ct)
    {
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var lineSpan = location.GetLineSpan();
        if (!lineSpan.IsValid) return null;

        var lineNumber = lineSpan.StartLinePosition.Line;
        if (lineNumber >= text.Lines.Count) return null;

        return text.Lines[lineNumber].ToString().Trim();
    }
}
