using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class UnusedCodeAnalyzer : IUnusedCodeAnalyzer
{
    private static readonly HashSet<string> FrameworkInvokedAttributeNames =
    [
        "Fact",
        "Theory",
        "Test",
        "TestMethod",
        "TestCase",
        "DataMember",
        "JsonProperty",
        "JsonRequired",
        "JsonInclude",
        "Required",
        "Key",
        "Column",
        "Table",
        "HttpGet",
        "HttpPost",
        "HttpPut",
        "HttpDelete",
        "HttpPatch",
        "Route",
        "ApiController",
        "Controller",
        "EventHandler",
        "MessageHandler",
        "Subscribe"
    ];

    private readonly IWorkspaceManager _workspace;
    private readonly ICompilationCache _compilationCache;
    private readonly ILogger<UnusedCodeAnalyzer> _logger;

    public UnusedCodeAnalyzer(IWorkspaceManager workspace, ICompilationCache compilationCache, ILogger<UnusedCodeAnalyzer> logger)
    {
        _workspace = workspace;
        _compilationCache = compilationCache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UnusedSymbolDto>> FindUnusedSymbolsAsync(
        string workspaceId,
        UnusedSymbolsAnalysisOptions options,
        CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<UnusedSymbolDto>();
        var processedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        var projects = ProjectFilterHelper.FilterProjects(solution, options.ProjectFilter);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested || results.Count >= options.Limit) break;

            if (options.ExcludeTestProjects && IsTestProjectName(project.Name))
                continue;

            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var candidates = await CollectUnusedCandidatesAsync(
                compilation, options, processedSymbols, ct).ConfigureAwait(false);
            if (candidates.Count == 0) continue;

            await ScanCandidatesAndAppendResultsAsync(
                workspaceId, project, solution, candidates, options.Limit, results, ct).ConfigureAwait(false);
        }

        return results;
    }

    /// <summary>
    /// Phase 1 (sequential, cheap): walks every non-generated syntax tree in the
    /// project, visits each <see cref="MemberDeclarationSyntax"/>, and returns the
    /// subset that (a) has a declared symbol, (b) is not implicit/namespace,
    /// (c) is not yet in <paramref name="processedSymbols"/>, and (d) does not
    /// match the skip-filter in <see cref="ShouldSkipSymbolForUnusedAnalysis"/>.
    /// The HashSet de-dup MUST run on a single thread, so this phase is not
    /// parallelized.
    /// </summary>
    private static async Task<List<ISymbol>> CollectUnusedCandidatesAsync(
        Compilation compilation,
        UnusedSymbolsAnalysisOptions options,
        HashSet<ISymbol> processedSymbols,
        CancellationToken ct)
    {
        var candidates = new List<ISymbol>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            if (ct.IsCancellationRequested) break;
            if (PathFilter.IsGeneratedOrContentFile(tree.FilePath)) continue;

            var semanticModel = compilation.GetSemanticModel(tree);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

            foreach (var decl in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                if (ct.IsCancellationRequested) break;

                var symbol = semanticModel.GetDeclaredSymbol(decl, ct);
                if (!IsCandidateSymbol(symbol, processedSymbols)) continue;
                if (ShouldSkipSymbolForUnusedAnalysis(
                        symbol!, options.IncludePublic, options.ExcludeEnums,
                        options.ExcludeRecordProperties, options.ExcludeTests,
                        options.ExcludeConventionInvoked))
                    continue;

                candidates.Add(symbol!);
            }
        }

        return candidates;
    }

    /// <summary>
    /// Returns true when the declared <paramref name="symbol"/> is eligible for
    /// unused-analysis (non-null, non-implicit, non-namespace, and not already seen
    /// in <paramref name="processedSymbols"/>). Adds the symbol to the set as a
    /// side-effect when it's accepted so the caller does not need a separate
    /// <c>Add</c>.
    /// </summary>
    private static bool IsCandidateSymbol(ISymbol? symbol, HashSet<ISymbol> processedSymbols)
    {
        if (symbol is null) return false;
        if (symbol is INamespaceSymbol) return false;
        if (symbol.IsImplicitlyDeclared) return false;
        return processedSymbols.Add(symbol);
    }

    /// <summary>
    /// Phase 2 (parallel, expensive): fans out a reference scan across every
    /// candidate symbol using a bounded <see cref="SemaphoreSlim"/>, then appends
    /// the zero-reference survivors to <paramref name="results"/> in declaration
    /// order (project → tree → decl). Honors <paramref name="limit"/> once results
    /// are collected. Roslyn's <see cref="Solution"/> is immutable, so
    /// <see cref="SymbolFinder"/> is safe under concurrency.
    /// </summary>
    private async Task ScanCandidatesAndAppendResultsAsync(
        string workspaceId,
        Project project,
        Solution solution,
        List<ISymbol> candidates,
        int limit,
        List<UnusedSymbolDto> results,
        CancellationToken ct)
    {
        var parallelism = Math.Clamp(Environment.ProcessorCount, 4, 16);
        using var semaphore = new SemaphoreSlim(parallelism, parallelism);

        var tasks = candidates.Select(symbol =>
            TryBuildUnusedDtoAsync(workspaceId, symbol, project, solution, semaphore, ct));

        // Preserve original ordering (project → tree → declaration order) by awaiting
        // in input order, not completion order.
        var unusedDtos = await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var dto in unusedDtos)
        {
            if (dto is null) continue;
            results.Add(dto);
            if (results.Count >= limit) break;
        }
    }

    /// <summary>
    /// Per-candidate reference count + cross-compilation fallback, gated by
    /// <paramref name="semaphore"/>. Returns an <see cref="UnusedSymbolDto"/> when
    /// the final reference count is zero, otherwise <see langword="null"/>.
    ///
    /// <para>Fallback note: cross-compilation re-resolution is required for
    /// extension methods and overloaded methods whose callers bind via implicit
    /// conversion — the project-local symbol from <c>GetDeclaredSymbol</c> may not
    /// match the reduced/converted form other compilations see.</para>
    /// </summary>
    private async Task<UnusedSymbolDto?> TryBuildUnusedDtoAsync(
        string workspaceId,
        ISymbol symbol,
        Project declaringProject,
        Solution solution,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var refs = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
            var refCount = refs.Sum(r => r.Locations.Count());

            if (refCount == 0 && NeedsCrossCompilationCheck(symbol))
            {
                refCount = await CountCrossCompilationReferencesAsync(
                    workspaceId, symbol, declaringProject, solution, ct).ConfigureAwait(false);
            }

            return refCount == 0 ? BuildUnusedDto(symbol) : null;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static UnusedSymbolDto? BuildUnusedDto(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null) return null;
        var lineSpan = location.GetLineSpan();

        return new UnusedSymbolDto(
            symbol.Name,
            symbol.Kind.ToString(),
            lineSpan.Path,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1,
            symbol.ContainingType?.Name,
            SymbolHandleSerializer.CreateHandle(symbol),
            ComputeUnusedConfidence(symbol));
    }

    /// <summary>
    /// Returns true when the symbol should not be analyzed for unused status (filters noise: public API, tests, etc.).
    /// </summary>
    private static bool IsTestProjectName(string projectName) =>
        projectName.Contains(".Tests", StringComparison.OrdinalIgnoreCase) ||
        projectName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldSkipSymbolForUnusedAnalysis(
        ISymbol symbol,
        bool includePublic,
        bool excludeEnums,
        bool excludeRecordProperties,
        bool excludeTests,
        bool excludeConventionInvoked)
    {
        return IsTestFixtureFiltered(symbol, excludeTests)
            || IsConventionInvokedFiltered(symbol, excludeConventionInvoked)
            || IsPublicFiltered(symbol, includePublic)
            || IsEnumMemberFiltered(symbol, excludeEnums)
            || IsRecordPropertyFiltered(symbol, excludeRecordProperties)
            || IsExcludedMethodKind(symbol)
            || IsInterfaceImplementation(symbol)
            || IsOverrideMember(symbol)
            || IsProgramEntryPoint(symbol)
            || HasFrameworkInvokedAttribute(symbol)
            || IsContainedInTestFixture(symbol, excludeTests)
            || IsExtensionMethodHostType(symbol);
    }

    private static bool IsConventionInvokedFiltered(ISymbol symbol, bool excludeConventionInvoked)
    {
        if (!excludeConventionInvoked) return false;
        if (symbol is INamedTypeSymbol type && IsConventionInvokedType(type)) return true;
        return IsConventionInvokedMember(symbol);
    }

    /// <summary>
    /// Detects types that are invoked by frameworks via reflection or convention rather than
    /// direct C# call sites. Match by name shape and base-type chain so the analyzer doesn't
    /// require the corresponding NuGet packages.
    /// </summary>
    private static bool IsConventionInvokedType(INamedTypeSymbol type)
    {
        // 1. EF Core migration snapshot — naming convention is enough.
        if (type.Name.EndsWith("ModelSnapshot", StringComparison.Ordinal)) return true;

        // 2. Walk base-type chain by simple name. Stop at System.Object to bound the walk.
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.SpecialType == SpecialType.System_Object) break;
            var baseName = current.Name;
            if (baseName is "AbstractValidator" or "Hub" or "PageModel" or "Migration" or "ModelSnapshot")
                return true;
        }

        // 3. ASP.NET middleware shape: public InvokeAsync(HttpContext) or Invoke(HttpContext).
        //    Match the parameter type by simple name only so we don't pull Microsoft.AspNetCore.Http.
        //    ASP.NET HealthCheck response-writer shape: method taking (HttpContext, HealthReport).
        //    HealthCheck writers are registered via Action<HttpContext, HealthReport> delegates
        //    (`ResponseWriter = MyType.WriteAsync`), so direct C# call sites don't exist even
        //    though the method is live.
        foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.DeclaredAccessibility != Accessibility.Public) continue;

            if (member.Name is "InvokeAsync" or "Invoke"
                && member.Parameters.Length >= 1
                && member.Parameters[0].Type.Name == "HttpContext")
                return true;

            if (member.Parameters.Length == 2
                && member.Parameters[0].Type.Name == "HttpContext"
                && member.Parameters[1].Type.Name == "HealthReport")
                return true;
        }

        // 4. Attribute-based convention markers applied to the type declaration itself.
        //    Covered families:
        //      - MCP server catalogs: [McpServerToolType] / [McpServerPromptType] / [McpServerResourceType].
        //        The ModelContextProtocol host discovers these types via reflection; no C# call
        //        site exists for the class declaration.
        //      - xUnit [CollectionDefinition("name")] holder types. xUnit looks them up by attribute;
        //        the class itself is never constructed by user code.
        foreach (var attribute in type.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            if (name is null) continue;

            var normalized = name.EndsWith("Attribute", StringComparison.Ordinal)
                ? name[..^"Attribute".Length]
                : name;

            if (normalized is "McpServerToolType"
                or "McpServerPromptType"
                or "McpServerResourceType"
                or "CollectionDefinition")
                return true;
        }

        // 5. Reuse the existing test-fixture detection so xUnit/MSTest/NUnit fixture types are
        //    uniformly excluded under the convention-invoked umbrella.
        return IsLikelyTestFixtureType(type);
    }

    private static bool IsConventionInvokedMember(ISymbol symbol)
    {
        if (symbol.ContainingType is { } containingType && IsConventionInvokedType(containingType))
            return true;

        foreach (var attribute in symbol.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            if (name is "DbContextAttribute" or "MigrationAttribute") return true;
        }

        return false;
    }

    private static bool IsTestFixtureFiltered(ISymbol symbol, bool excludeTests) =>
        excludeTests && symbol is INamedTypeSymbol namedType && IsLikelyTestFixtureType(namedType);

    private static bool IsPublicFiltered(ISymbol symbol, bool includePublic) =>
        !includePublic && symbol.DeclaredAccessibility == Accessibility.Public;

    private static bool IsEnumMemberFiltered(ISymbol symbol, bool excludeEnums) =>
        excludeEnums && symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum };

    private static bool IsRecordPropertyFiltered(ISymbol symbol, bool excludeRecordProperties) =>
        excludeRecordProperties && symbol is IPropertySymbol { ContainingType.IsRecord: true };

    private static bool IsExcludedMethodKind(ISymbol symbol) =>
        symbol is IMethodSymbol method &&
        method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor
            or MethodKind.Destructor or MethodKind.UserDefinedOperator
            or MethodKind.Conversion;

    private static bool IsInterfaceImplementation(ISymbol symbol)
    {
        if (symbol.ContainingType is null) return false;

        foreach (var iface in symbol.ContainingType.AllInterfaces)
        {
            foreach (var ifaceMember in iface.GetMembers())
            {
                var impl = symbol.ContainingType.FindImplementationForInterfaceMember(ifaceMember);
                if (SymbolEqualityComparer.Default.Equals(impl, symbol))
                    return true;
            }
        }
        return false;
    }

    private static bool IsOverrideMember(ISymbol symbol) =>
        symbol is IMethodSymbol { IsOverride: true } or IPropertySymbol { IsOverride: true };

    private static bool IsProgramEntryPoint(ISymbol symbol) =>
        symbol is IMethodSymbol { Name: "Main", IsStatic: true };

    private static bool IsContainedInTestFixture(ISymbol symbol, bool excludeTests) =>
        excludeTests &&
        symbol.ContainingType is not null &&
        IsLikelyTestFixtureType(symbol.ContainingType);

    private static bool IsExtensionMethodHostType(ISymbol symbol) =>
        symbol is INamedTypeSymbol { IsStatic: true } staticType &&
        staticType.GetMembers().OfType<IMethodSymbol>().Any(m => m.IsExtensionMethod);

    /// <summary>
    /// Determines whether a symbol is susceptible to cross-compilation identity
    /// mismatches that cause <see cref="SymbolFinder.FindReferencesAsync"/> to miss
    /// references when the symbol comes from the declaring project's compilation.
    /// </summary>
    private static bool NeedsCrossCompilationCheck(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method)
        {
            // Extension methods: call sites bind to ReducedExtensionMethod which
            // may not map back to the original definition across compilations.
            if (method.IsExtensionMethod)
                return true;

            // Overloaded methods: when callers resolve via implicit conversion
            // (e.g. List<T> → IEnumerable<T>), the bound symbol in the caller's
            // compilation may have a different identity than the declaring symbol.
            if (method.ContainingType is not null &&
                method.ContainingType.GetMembers(method.Name)
                    .OfType<IMethodSymbol>()
                    .Count(m => m.MethodKind == MethodKind.Ordinary) > 1)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Re-resolves a symbol through dependent projects' compilations and checks for
    /// references from each. Returns the total reference count found, or 0 if none.
    /// </summary>
    private async Task<int> CountCrossCompilationReferencesAsync(
        string workspaceId, ISymbol symbol, Project declaringProject, Solution solution, CancellationToken ct)
    {
        var containingType = symbol.ContainingType;
        if (containingType is null) return 0;

        var metadataName = containingType.ToDisplayString();

        foreach (var project in solution.Projects)
        {
            if (ct.IsCancellationRequested) break;

            // Skip the declaring project — we already checked it.
            if (project.Id == declaringProject.Id) continue;

            // Only check projects that could reference the declaring project.
            if (!project.ProjectReferences.Any(r => r.ProjectId == declaringProject.Id))
                continue;

            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var resolvedType = compilation.GetTypeByMetadataName(metadataName);
            if (resolvedType is null) continue;

            // Find the equivalent member in this compilation's type.
            var resolvedMember = FindMatchingMember(resolvedType, symbol);
            if (resolvedMember is null) continue;

            var refs = await SymbolFinder.FindReferencesAsync(resolvedMember, solution, ct).ConfigureAwait(false);
            var count = refs.Sum(r => r.Locations.Count());
            if (count > 0) return count;
        }

        return 0;
    }

    /// <summary>
    /// Finds a member in <paramref name="type"/> that matches <paramref name="original"/>
    /// by name, kind, and parameter signature.
    /// </summary>
    private static ISymbol? FindMatchingMember(INamedTypeSymbol type, ISymbol original)
    {
        var candidates = type.GetMembers(original.Name);

        if (original is not IMethodSymbol originalMethod)
            return candidates.FirstOrDefault(c => c.Kind == original.Kind);

        foreach (var candidate in candidates.OfType<IMethodSymbol>())
        {
            if (candidate.Parameters.Length != originalMethod.Parameters.Length)
                continue;

            var match = true;
            for (var i = 0; i < candidate.Parameters.Length; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(
                        candidate.Parameters[i].Type.OriginalDefinition,
                        originalMethod.Parameters[i].Type.OriginalDefinition))
                {
                    match = false;
                    break;
                }
            }

            if (match) return candidate;
        }

        return null;
    }

    private static string ComputeUnusedConfidence(ISymbol symbol)
    {
        if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
            return "low";

        if (symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum })
            return "low";

        if (symbol is IPropertySymbol p &&
            (p.ContainingType?.IsRecord == true || HasJsonSerializationHint(p)))
            return "low";

        if (symbol.DeclaredAccessibility == Accessibility.Public)
            return "medium";

        return "high";
    }

    private static bool HasJsonSerializationHint(IPropertySymbol p)
    {
        foreach (var attribute in p.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            if (name is null) continue;
            if (name is "JsonPropertyNameAttribute" or "JsonPropertyAttribute" or "JsonIncludeAttribute"
                or "DataMemberAttribute")
                return true;
        }

        return false;
    }

    /// <summary>
    /// BUG-N6: xUnit/NUnit/MSTest types are invoked by the test host via reflection; treat as
    /// non-dead-code candidates when <paramref name="excludeTests"/> is true.
    /// </summary>
    private static bool IsLikelyTestFixtureType(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            var n = attribute.AttributeClass?.Name;
            if (n is "TestFixtureAttribute" or "TestClassAttribute" or "CollectionAttribute")
                return true;
        }

        if (type.Name.EndsWith("Tests", StringComparison.Ordinal) ||
            type.Name.EndsWith("Test", StringComparison.Ordinal))
            return true;

        return false;
    }

    private static bool HasFrameworkInvokedAttribute(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var normalized = name.EndsWith("Attribute", StringComparison.Ordinal)
                ? name[..^"Attribute".Length]
                : name;

            if (FrameworkInvokedAttributeNames.Contains(normalized))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Surfaces private/internal static helper methods that delegate in ≤ 2 body
    /// statements to a symbol declared in a non-source (BCL/NuGet) assembly — the
    /// "reinvented <c>string.IsNullOrWhiteSpace</c>" pattern. See
    /// <see cref="IUnusedCodeAnalyzer.FindDuplicateHelpersAsync"/> for the contract.
    /// </summary>
    public async Task<IReadOnlyList<DuplicateHelperDto>> FindDuplicateHelpersAsync(
        string workspaceId,
        DuplicateHelperAnalysisOptions options,
        CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<DuplicateHelperDto>();

        var projects = ProjectFilterHelper.FilterProjects(solution, options.ProjectFilter);
        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested || results.Count >= options.Limit) break;

            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested || results.Count >= options.Limit) break;
                if (PathFilter.IsGeneratedOrContentFile(tree.FilePath)) continue;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (ct.IsCancellationRequested || results.Count >= options.Limit) break;

                    var hit = TryClassifyHelperAsDuplicate(methodDecl, semanticModel, project, options, ct);
                    if (hit is not null) results.Add(hit);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Finds source-declared fields whose observed source usage is incomplete:
    /// never read, never written, or never either. See
    /// <see cref="IUnusedCodeAnalyzer.FindDeadFieldsAsync"/> for the contract.
    /// </summary>
    public async Task<IReadOnlyList<DeadFieldDto>> FindDeadFieldsAsync(
        string workspaceId,
        DeadFieldsAnalysisOptions options,
        CancellationToken ct)
    {
        ValidateDeadFieldUsageKindFilter(options.UsageKindFilter);

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<DeadFieldDto>();
        var processedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        var projects = ProjectFilterHelper.FilterProjects(solution, options.ProjectFilter);
        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested || results.Count >= options.Limit) break;

            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested || results.Count >= options.Limit) break;
                if (PathFilter.IsGeneratedOrContentFile(tree.FilePath)) continue;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                {
                    if (ct.IsCancellationRequested || results.Count >= options.Limit) break;

                    if (semanticModel.GetDeclaredSymbol(declarator, ct) is not IFieldSymbol field)
                        continue;
                    if (!IsCandidateSymbol(field, processedSymbols))
                        continue;
                    if (ShouldSkipFieldForDeadFieldAnalysis(field, options.IncludePublic))
                        continue;

                    var dto = await TryBuildDeadFieldDtoAsync(
                        field,
                        declarator,
                        solution,
                        project.Name,
                        options.UsageKindFilter,
                        ct).ConfigureAwait(false);
                    if (dto is null) continue;

                    results.Add(dto);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Inspects a method declaration and, when its host is a static helper class,
    /// its effective accessibility is non-public, and its body is a single
    /// ≤ 2-statement delegation to a non-source-declared method, returns a
    /// <see cref="DuplicateHelperDto"/>. Returns <see langword="null"/> otherwise.
    /// </summary>
    private static DuplicateHelperDto? TryClassifyHelperAsDuplicate(
        MethodDeclarationSyntax methodDecl,
        SemanticModel semanticModel,
        Project project,
        DuplicateHelperAnalysisOptions analysisOptions,
        CancellationToken ct)
    {
        if (semanticModel.GetDeclaredSymbol(methodDecl, ct) is not IMethodSymbol methodSymbol)
            return null;

        // Only methods on static helper classes — the idiomatic "StringHelper" /
        // "StringExtensions" shape. Filters out random static utilities that are not
        // re-wrappers of library primitives.
        if (methodSymbol.ContainingType is not { IsStatic: true } hostType)
            return null;

        // Effective accessibility: min(method, type). Public method on an internal
        // static class still surfaces here because the containing type clamps the
        // exposure. Public-on-public is intentionally excluded (library-API surface,
        // not a reinvented helper).
        var effective = MinAccessibility(methodSymbol.DeclaredAccessibility, hostType.DeclaredAccessibility);
        if (effective == Accessibility.Public) return null;

        // Skip the method kinds that cannot be a single-delegation shape.
        if (methodSymbol.MethodKind is not MethodKind.Ordinary) return null;

        // Extract the delegation target. Accepts:
        //   (a) expression-bodied method:  => Target(...);
        //   (b) single-statement body:     { return Target(...); } OR { Target(...); }
        //   (c) two-statement body:        { if (s is null) throw ...; return Target(...); }
        //                                  (any first statement that is NOT an invocation)
        var (invocation, statementCount) = ExtractDelegationInvocation(methodDecl);
        if (invocation is null) return null;

        // Resolve the target symbol. It must live in a NON-source assembly (BCL or
        // NuGet). A target that resolves to another project in the same solution is
        // an internal refactoring artifact, not a library-reinvention.
        if (semanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol targetSymbol)
            return null;

        // Unwrap reduced extension methods — we want the original library-defined
        // form so an assembly check reflects the true canonical target.
        targetSymbol = targetSymbol.ReducedFrom ?? targetSymbol;

        var targetAssembly = targetSymbol.ContainingAssembly;
        if (targetAssembly is null) return null;

        // Skip self-delegation: the helper calls into a method defined in the same
        // solution (could be a legitimate internal re-export, not a reinvented BCL).
        var projectAssemblyNames = new HashSet<string>(
            project.Solution.Projects.Select(p => p.AssemblyName),
            StringComparer.Ordinal);
        if (projectAssemblyNames.Contains(targetAssembly.Name)) return null;

        // Skip delegations to the helper's own containing type (recursion / self-call).
        if (SymbolEqualityComparer.Default.Equals(targetSymbol.ContainingType, hostType))
            return null;

        if (IsFrameworkGlueWrapperTarget(targetSymbol, analysisOptions.ExcludeFrameworkWrappers))
            return null;

        var lineSpan = methodDecl.Identifier.GetLocation().GetLineSpan();
        var confidence = statementCount <= 1 ? "high" : "medium";

        return new DuplicateHelperDto(
            SymbolName: methodSymbol.Name,
            ContainingType: hostType.Name,
            FilePath: lineSpan.Path,
            Line: lineSpan.StartLinePosition.Line + 1,
            Column: lineSpan.StartLinePosition.Character + 1,
            ProjectName: project.Name,
            CanonicalTarget: targetSymbol.ToDisplayString(),
            CanonicalTargetAssembly: targetAssembly.Name,
            Confidence: confidence);
    }

    /// <summary>
    /// Thin forwarders to ASP.NET Core HTTP and System.Net.Http API entry points are
    /// usually intentional (minimal APIs, test extensions), not reinvented primitive helpers.
    /// </summary>
    private static bool IsFrameworkGlueWrapperTarget(IMethodSymbol targetSymbol, bool excludeFrameworkWrappers)
    {
        if (!excludeFrameworkWrappers) return false;

        for (var ns = targetSymbol.ContainingNamespace;
             ns is { IsGlobalNamespace: false };
             ns = ns.ContainingNamespace)
        {
            var display = ns.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (display.StartsWith("global::", StringComparison.Ordinal))
                display = display["global::".Length..];

            if (display.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
                || string.Equals(display, "Microsoft.AspNetCore", StringComparison.Ordinal))
                return true;

            // System.Net.Http, System.Net.Http.Headers, System.Net.Http.Json, etc.
            if (display.StartsWith("System.Net.Http", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts the single delegation invocation from a method declaration, if the
    /// body shape qualifies as "≤ 2 statements ending in a single method call".
    /// Returns the invocation plus the statement count (1 for expression-bodied or
    /// single-statement, 2 for two-statement-with-guard). Returns null otherwise.
    /// </summary>
    private static (InvocationExpressionSyntax? Invocation, int StatementCount) ExtractDelegationInvocation(
        MethodDeclarationSyntax methodDecl)
    {
        // Expression-bodied: public static bool X(string s) => string.IsNullOrWhiteSpace(s);
        if (methodDecl.ExpressionBody is { Expression: InvocationExpressionSyntax inv })
            return (inv, 1);

        if (methodDecl.Body is not { } block) return (null, 0);

        var statements = block.Statements;
        if (statements.Count == 0 || statements.Count > 2) return (null, 0);

        // Find the "delegation" statement — the last one, which must be a return of
        // an invocation or a bare invocation (void-returning helpers). Roslyn parses
        // `return X(...);` as ReturnStatementSyntax with Expression == InvocationExpressionSyntax.
        var last = statements[^1];
        InvocationExpressionSyntax? delegationInv = last switch
        {
            ReturnStatementSyntax { Expression: InvocationExpressionSyntax retInv } => retInv,
            ExpressionStatementSyntax { Expression: InvocationExpressionSyntax exprInv } => exprInv,
            _ => null
        };

        if (delegationInv is null) return (null, 0);

        // Two-statement shape: the first statement may NOT be another invocation
        // (else it's a helper that does work and then delegates — not a pure
        // re-wrapper). It's typically a null-guard / throw: `if (s is null) throw ...;`.
        if (statements.Count == 2)
        {
            var first = statements[0];
            if (first is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax })
                return (null, 0);
        }

        return (delegationInv, statements.Count);
    }

    /// <summary>
    /// Returns the more-restrictive of two accessibility values. Accessibility is
    /// ordered Public > Protected-or-Internal > Internal == Protected >
    /// Private-Protected > Private, so we take the smaller enum value.
    /// </summary>
    private static Accessibility MinAccessibility(Accessibility a, Accessibility b)
    {
        // Higher enum value = more visible (Public=6, Internal=4, Private=1).
        return ((int)a < (int)b) ? a : b;
    }

    private static void ValidateDeadFieldUsageKindFilter(string? usageKindFilter)
    {
        if (usageKindFilter is null) return;

        if (!IsAcceptedDeadFieldUsageKind(usageKindFilter))
        {
            throw new ArgumentException(
                $"Unsupported usageKind '{usageKindFilter}'. Expected never-read, never-written, or never-either.",
                nameof(usageKindFilter));
        }
    }

    private static bool IsAcceptedDeadFieldUsageKind(string usageKind) =>
        usageKind is "never-read" or "never-written" or "never-either";

    private static bool ShouldSkipFieldForDeadFieldAnalysis(IFieldSymbol field, bool includePublic)
    {
        if (field.IsImplicitlyDeclared) return true;
        if (field.AssociatedSymbol is not null) return true;
        if (field.ContainingType?.TypeKind == TypeKind.Enum) return true;
        if (field.IsConst) return true;
        if (field.Name.Length == 0) return true;

        var effectiveAccessibility = field.ContainingType is null
            ? field.DeclaredAccessibility
            : MinAccessibility(field.DeclaredAccessibility, field.ContainingType.DeclaredAccessibility);

        if (!includePublic && effectiveAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)
            return true;

        return false;
    }

    private async Task<DeadFieldDto?> TryBuildDeadFieldDtoAsync(
        IFieldSymbol field,
        VariableDeclaratorSyntax declarator,
        Solution solution,
        string projectName,
        string? usageKindFilter,
        CancellationToken ct)
    {
        var locations = await SymbolFinder.FindReferencesAsync(field, solution, ct).ConfigureAwait(false);
        var classification = ClassifyDeadFieldReferences(locations);
        var readCount = classification.ReadCount;
        var writeCount = classification.WriteCount;

        if (declarator.Initializer is not null)
        {
            writeCount++;
        }

        var usageKind = DetermineDeadFieldUsageKind(readCount, writeCount);
        if (usageKind is null) return null;
        if (usageKindFilter is not null && !string.Equals(usageKind, usageKindFilter, StringComparison.Ordinal))
            return null;

        // A field is safely removable only when there are zero residual source references
        // (only the declaration initializer, if any, survives). Any ref — ctor-write,
        // read, or direct write — will make `remove_dead_code_preview` refuse with
        // "still has references", so callers should skip chaining the removal workflow
        // on those fields. The `RemovalBlockedBy` list enumerates the blocking sites.
        var removalBlockedBy = classification.BlockingSites.Count > 0
            ? classification.BlockingSites
            : null;
        var safelyRemovable = removalBlockedBy is null;

        return BuildDeadFieldDto(field, projectName, usageKind, readCount, writeCount, removalBlockedBy, safelyRemovable);
    }

    private readonly record struct DeadFieldReferenceClassification(
        int ReadCount,
        int WriteCount,
        IReadOnlyList<string> BlockingSites);

    /// <summary>
    /// Classifies every non-implicit, in-source reference to the field into read /
    /// write buckets AND records a <c>Kind@Path:Line:Col</c> marker for each one so
    /// callers can see exactly what would block a subsequent
    /// <c>remove_dead_code_preview</c>. Constructor writes are tagged specifically
    /// because the <c>find_dead_fields</c> → <c>remove_dead_code_preview</c> chain
    /// breaks most often on DI-captured fields assigned only in the constructor.
    /// </summary>
    private static DeadFieldReferenceClassification ClassifyDeadFieldReferences(IEnumerable<ReferencedSymbol> referencedSymbols)
    {
        var readCount = 0;
        var writeCount = 0;
        var blockingSites = new List<string>();

        foreach (var location in referencedSymbols.SelectMany(symbol => symbol.Locations))
        {
            if (location.IsImplicit) continue;
            if (!location.Location.IsInSource) continue;

            var kind = ClassifyDeadFieldReferenceLocation(location);
            switch (kind)
            {
                case "Write":
                    writeCount++;
                    break;
                case "ReadWrite":
                    readCount++;
                    writeCount++;
                    break;
                default:
                    readCount++;
                    break;
            }

            blockingSites.Add(FormatBlockingSite(location, kind));
        }

        return new DeadFieldReferenceClassification(readCount, writeCount, blockingSites);
    }

    /// <summary>
    /// Formats a reference site as <c>Kind@AbsolutePath:Line:Column</c>. When the
    /// reference lives inside a constructor body the kind is promoted to
    /// <c>ConstructorWrite</c> (for <c>Write</c>/<c>ReadWrite</c>) so callers can
    /// quickly tell the DI-captured-field pattern apart from ordinary reads.
    /// </summary>
    private static string FormatBlockingSite(ReferenceLocation refLocation, string kind)
    {
        var sourceTree = refLocation.Location.SourceTree;
        var span = refLocation.Location.GetLineSpan();
        var filePath = span.Path;
        var line = span.StartLinePosition.Line + 1;
        var column = span.StartLinePosition.Character + 1;

        var resolvedKind = kind;
        if ((kind == "Write" || kind == "ReadWrite") && sourceTree is not null)
        {
            var node = sourceTree.GetRoot().FindNode(refLocation.Location.SourceSpan);
            if (node.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is not null)
            {
                resolvedKind = "ConstructorWrite";
            }
        }

        return $"{resolvedKind}@{filePath}:{line}:{column}";
    }

    private static string ClassifyDeadFieldReferenceLocation(ReferenceLocation refLocation)
    {
        var syntaxNode = refLocation.Location.SourceTree?
            .GetRoot()
            .FindNode(refLocation.Location.SourceSpan);

        if (syntaxNode is null)
        {
            return "Read";
        }

        return syntaxNode.Parent switch
        {
            AssignmentExpressionSyntax assignment when assignment.Left == syntaxNode
                => assignment.Kind() is SyntaxKind.AddAssignmentExpression
                    or SyntaxKind.SubtractAssignmentExpression
                    or SyntaxKind.MultiplyAssignmentExpression
                    or SyntaxKind.DivideAssignmentExpression
                    ? "ReadWrite"
                    : "Write",
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Parent is AssignmentExpressionSyntax memberAssignment
                     && memberAssignment.Left == memberAccess
                => "Write",
            PrefixUnaryExpressionSyntax prefix
                when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression)
                => "ReadWrite",
            PostfixUnaryExpressionSyntax postfix
                when postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression)
                => "ReadWrite",
            ArgumentSyntax arg when arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                => "ReadWrite",
            ArgumentSyntax arg when arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)
                => "Write",
            _ => "Read"
        };
    }

    private static string? DetermineDeadFieldUsageKind(int readCount, int writeCount)
    {
        if (readCount == 0 && writeCount == 0) return "never-either";
        if (readCount == 0) return "never-read";
        if (writeCount == 0) return "never-written";
        return null;
    }

    private static DeadFieldDto? BuildDeadFieldDto(
        IFieldSymbol field,
        string projectName,
        string usageKind,
        int readCount,
        int writeCount,
        IReadOnlyList<string>? removalBlockedBy,
        bool safelyRemovable)
    {
        var location = field.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null) return null;

        var lineSpan = location.GetLineSpan();
        return new DeadFieldDto(
            SymbolName: field.Name,
            ContainingType: field.ContainingType?.Name,
            FilePath: lineSpan.Path,
            Line: lineSpan.StartLinePosition.Line + 1,
            Column: lineSpan.StartLinePosition.Character + 1,
            ProjectName: projectName,
            UsageKind: usageKind,
            ReadReferenceCount: readCount,
            WriteReferenceCount: writeCount,
            SymbolHandle: SymbolHandleSerializer.CreateHandle(field),
            Confidence: ComputeDeadFieldConfidence(field),
            RemovalBlockedBy: removalBlockedBy,
            SafelyRemovable: safelyRemovable);
    }

    private static string ComputeDeadFieldConfidence(IFieldSymbol field)
    {
        var effectiveAccessibility = field.ContainingType is null
            ? field.DeclaredAccessibility
            : MinAccessibility(field.DeclaredAccessibility, field.ContainingType.DeclaredAccessibility);

        return effectiveAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal
            ? "medium"
            : "high";
    }

    /// <summary>
    /// Finds method-local variables whose only write is not followed by any read. See
    /// <see cref="IUnusedCodeAnalyzer.FindDeadLocalsAsync"/> for the contract.
    /// </summary>
    public async Task<IReadOnlyList<DeadLocalDto>> FindDeadLocalsAsync(
        string workspaceId,
        DeadLocalsAnalysisOptions options,
        CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<DeadLocalDto>();

        var projects = ProjectFilterHelper.FilterProjects(solution, options.ProjectFilter);
        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested || results.Count >= options.Limit) break;

            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested || results.Count >= options.Limit) break;
                if (PathFilter.IsGeneratedOrContentFile(tree.FilePath)) continue;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                CollectDeadLocalsInTree(root, semanticModel, project.Name, options.Limit, results, ct);
            }
        }

        return results;
    }

    /// <summary>
    /// Walks a syntax tree's method-like bodies (methods, constructors, accessors,
    /// local functions) and appends a <see cref="DeadLocalDto"/> for each local that
    /// is written-but-not-read inside the body. Each body runs through
    /// <see cref="SemanticModel.AnalyzeDataFlow(SyntaxNode)"/> exactly once.
    /// </summary>
    private static void CollectDeadLocalsInTree(
        SyntaxNode root,
        SemanticModel semanticModel,
        string projectName,
        int limit,
        List<DeadLocalDto> results,
        CancellationToken ct)
    {
        foreach (var bodyOwner in EnumerateMethodLikeBodyOwners(root))
        {
            if (ct.IsCancellationRequested || results.Count >= limit) break;

            var (body, methodName, containingTypeName) = ResolveBodyAndOwnerNames(bodyOwner, semanticModel, ct);
            if (body is null) continue;

            DataFlowAnalysis? flow;
            try
            {
                // SemanticModel.AnalyzeDataFlow throws ArgumentException for bodies
                // it can't analyze (zero-statement blocks, unreachable code, etc.).
                // Treat those as "no dead locals" rather than aborting the scan.
                flow = semanticModel.AnalyzeDataFlow(body);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (flow is null || !flow.Succeeded) continue;

            // Build a lookup of locals that are written inside the body. We then check
            // each one against ReadInside; absence ⇒ dead-local candidate.
            var readInside = new HashSet<ISymbol>(flow.ReadInside, SymbolEqualityComparer.Default);

            foreach (var symbol in flow.WrittenInside)
            {
                if (ct.IsCancellationRequested || results.Count >= limit) break;

                if (symbol is not ILocalSymbol local) continue;
                if (readInside.Contains(symbol)) continue;
                if (ShouldSkipLocalForDeadLocalAnalysis(local)) continue;

                var dto = BuildDeadLocalDto(local, methodName, containingTypeName, projectName);
                if (dto is null) continue;

                results.Add(dto);
            }
        }
    }

    /// <summary>
    /// Enumerates every node in the tree that owns a method-like body whose locals we
    /// want to scan. Includes ordinary methods, constructors, accessors (property /
    /// indexer / event), conversion / operator declarations, and local functions.
    /// Lambdas and anonymous-method expressions are intentionally excluded — their
    /// captured locals overlap with the enclosing method's data-flow scope, and
    /// running data-flow on the same locals from two scopes produces double-counted
    /// hits.
    /// </summary>
    private static IEnumerable<SyntaxNode> EnumerateMethodLikeBodyOwners(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case BaseMethodDeclarationSyntax:
                case AccessorDeclarationSyntax:
                case LocalFunctionStatementSyntax:
                    yield return node;
                    break;
            }
        }
    }

    /// <summary>
    /// Returns the analyzable body (block or expression body), plus a friendly method
    /// name and containing type name for the DTO. Returns <c>(null, ...)</c> when the
    /// node has no body to analyze (abstract methods, partial declarations without
    /// implementation, expression-bodied member accessors with null bodies).
    /// </summary>
    private static (SyntaxNode? Body, string MethodName, string? ContainingTypeName) ResolveBodyAndOwnerNames(
        SyntaxNode bodyOwner,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        SyntaxNode? body = bodyOwner switch
        {
            BaseMethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody?.Expression,
            AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax lf => (SyntaxNode?)lf.Body ?? lf.ExpressionBody?.Expression,
            _ => null
        };
        if (body is null) return (null, string.Empty, null);

        var declaredSymbol = semanticModel.GetDeclaredSymbol(bodyOwner, ct);
        var methodName = declaredSymbol?.Name ?? bodyOwner switch
        {
            MethodDeclarationSyntax md => md.Identifier.ValueText,
            ConstructorDeclarationSyntax cd => cd.Identifier.ValueText,
            DestructorDeclarationSyntax dd => "~" + dd.Identifier.ValueText,
            LocalFunctionStatementSyntax lfn => lfn.Identifier.ValueText,
            AccessorDeclarationSyntax ad => ad.Keyword.ValueText,
            _ => "<anonymous>"
        };

        var containingTypeName = declaredSymbol?.ContainingType?.Name;
        return (body, methodName, containingTypeName);
    }

    /// <summary>
    /// True for locals we deliberately do NOT flag as dead even when they are
    /// written-but-not-read. The exclusions cover language shapes where a name is
    /// required by syntax even when the value is unused, plus shapes where IDE0059
    /// separately suggests a different rewrite (e.g. <c>out _</c> discards).
    /// </summary>
    private static bool ShouldSkipLocalForDeadLocalAnalysis(ILocalSymbol local)
    {
        // Discards (`_`) intentionally never read. Two shapes: explicit `_` name on
        // the synthesized local, or compiler-marked discard symbol.
        if (local.Name is "_") return true;

        // Implicit / compiler-generated locals (e.g. iterator state machine internals,
        // pattern-matching scratch slots) are not user-declared.
        if (local.IsImplicitlyDeclared) return true;

        // foreach (var x in ...) — the "write" is the loop step, not user intent.
        if (local.IsForEach) return true;

        // using var x = ... — the local exists to scope a Dispose call; "unread" is
        // expected in the resource-management idiom.
        if (local.IsUsing) return true;

        // const locals are compile-time named constants whose only "write" is the
        // initializer. Removing them changes API shape (visible name in nameof, etc.).
        if (local.IsConst) return true;

        // Inspect the declaration syntax to filter the call-site / catch / pattern shapes.
        foreach (var declRef in local.DeclaringSyntaxReferences)
        {
            var node = declRef.GetSyntax();

            // out var x in Foo(out var x). Bare declaration of an out parameter at
            // the call site — required to invoke the API. IDE0059 separately
            // suggests collapsing to `out _`.
            if (node is SingleVariableDesignationSyntax { Parent: DeclarationExpressionSyntax { Parent: ArgumentSyntax } })
                return true;

            // Pattern-matching designations: `if (x is Foo y)`, `switch (x) { case Foo y: ... }`.
            // The local is required by the pattern shape; "unread" patterns are
            // legitimately a type-test idiom (e.g. caller only cares about the success).
            if (node is SingleVariableDesignationSyntax { Parent: DeclarationPatternSyntax }
                or SingleVariableDesignationSyntax { Parent: VarPatternSyntax }
                or SingleVariableDesignationSyntax { Parent: RecursivePatternSyntax })
                return true;

            // Tuple deconstruction designations: `var (_, b) = Foo();` — the named
            // half is reachable for the user but the "unused" half is positionally
            // required by the deconstruction shape, so flagging produces noise.
            if (node is SingleVariableDesignationSyntax { Parent: ParenthesizedVariableDesignationSyntax })
                return true;

            // catch (Exception ex) — the local is required by the catch syntax.
            if (node is CatchDeclarationSyntax) return true;
        }

        return false;
    }

    /// <summary>
    /// Builds a <see cref="DeadLocalDto"/> for a flagged dead local. Returns <c>null</c>
    /// if the local has no source location (compiler-generated, source-generator-emitted).
    /// </summary>
    private static DeadLocalDto? BuildDeadLocalDto(
        ILocalSymbol local,
        string methodName,
        string? containingTypeName,
        string projectName)
    {
        var location = local.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null) return null;

        var lineSpan = location.GetLineSpan();
        return new DeadLocalDto(
            SymbolName: local.Name,
            ContainingMethod: methodName,
            ContainingType: containingTypeName,
            FilePath: lineSpan.Path,
            Line: lineSpan.StartLinePosition.Line + 1,
            Column: lineSpan.StartLinePosition.Character + 1,
            ProjectName: projectName);
    }
}
