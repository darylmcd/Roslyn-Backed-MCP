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
        var projectFilter = options.ProjectFilter;
        var includePublic = options.IncludePublic;
        var limit = options.Limit;
        var excludeEnums = options.ExcludeEnums;
        var excludeRecordProperties = options.ExcludeRecordProperties;
        var excludeTestProjects = options.ExcludeTestProjects;
        var excludeTests = options.ExcludeTests;
        var excludeConventionInvoked = options.ExcludeConventionInvoked;

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<UnusedSymbolDto>();
        var processedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested || results.Count >= limit) break;

            if (excludeTestProjects && IsTestProjectName(project.Name))
                continue;

            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;

            // Phase 1 (sequential, cheap): walk syntax and collect candidates that survive
            // the pre-filter. The HashSet de-dup must run on a single thread.
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
                    if (symbol is null) continue;
                    if (symbol is INamespaceSymbol) continue;
                    if (symbol.IsImplicitlyDeclared) continue;
                    if (!processedSymbols.Add(symbol)) continue;

                    if (ShouldSkipSymbolForUnusedAnalysis(
                            symbol, includePublic, excludeEnums, excludeRecordProperties, excludeTests, excludeConventionInvoked))
                        continue;

                    candidates.Add(symbol);
                }
            }

            if (candidates.Count == 0) continue;

            // Phase 2 (parallel, expensive): each candidate fans out to a SymbolFinder lookup
            // (and possibly a cross-compilation fallback). Roslyn's Solution is immutable, so
            // SymbolFinder is safe under concurrency. Bound parallelism by CPU count.
            var parallelism = Math.Clamp(Environment.ProcessorCount, 4, 16);
            using var semaphore = new SemaphoreSlim(parallelism, parallelism);
            var capturedProject = project;
            var capturedSolution = solution;

            var tasks = candidates.Select(async symbol =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var refs = await SymbolFinder.FindReferencesAsync(symbol, capturedSolution, ct).ConfigureAwait(false);
                    var refCount = refs.Sum(r => r.Locations.Count());

                    // Fallback: cross-compilation re-resolution for symbols prone to
                    // identity mismatches (extension methods, overloaded methods resolved
                    // via implicit conversion). The project-local symbol from
                    // GetDeclaredSymbol may not match the reduced/converted form that
                    // callers bind to in other compilations.
                    if (refCount == 0 && NeedsCrossCompilationCheck(symbol))
                    {
                        refCount = await CountCrossCompilationReferencesAsync(
                            workspaceId, symbol, capturedProject, capturedSolution, ct).ConfigureAwait(false);
                    }

                    return refCount == 0 ? BuildUnusedDto(symbol) : null;
                }
                finally
                {
                    semaphore.Release();
                }
            });

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

        return results;
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
        foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.DeclaredAccessibility != Accessibility.Public) continue;
            if (member.Name is not ("InvokeAsync" or "Invoke")) continue;
            if (member.Parameters.Length < 1) continue;
            if (member.Parameters[0].Type.Name == "HttpContext") return true;
        }

        // 4. Reuse the existing test-fixture detection so xUnit/MSTest/NUnit fixture types are
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

                    var hit = TryClassifyHelperAsDuplicate(methodDecl, semanticModel, project, ct);
                    if (hit is not null) results.Add(hit);
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
}
