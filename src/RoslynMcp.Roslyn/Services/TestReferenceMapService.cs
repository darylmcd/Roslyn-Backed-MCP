using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Item 10 implementation. Walks each test project's methods and records every productive
/// symbol (method/property/constructor) they invoke via Roslyn's semantic model. Productive
/// symbols live in projects that are NOT test projects themselves.
/// </summary>
public sealed class TestReferenceMapService : ITestReferenceMapService
{
    private readonly IWorkspaceManager _workspace;

    public TestReferenceMapService(IWorkspaceManager workspace)
    {
        _workspace = workspace;
    }

    public async Task<TestReferenceMapDto> BuildAsync(
        string workspaceId,
        string? projectName,
        int offset,
        int limit,
        CancellationToken ct)
    {
        var status = _workspace.GetStatus(workspaceId);
        var solution = _workspace.GetCurrentSolution(workspaceId);

        // Phase 1: scope projects + classify test-vs-productive.
        var (scopedProjects, testProjectIds, productiveScopeProjects) =
            ScopeProjects(solution, status, projectName);

        // Phase 2: collect every public/internal productive-symbol declaration from the
        // productive-scope projects (all non-test projects by default, or the named project
        // when projectName identifies a productive project).
        var allProductive = await CollectProductiveSymbolsAsync(productiveScopeProjects, ct)
            .ConfigureAwait(false);

        // Phase 3: walk each test project's test methods and record references to
        // productive symbols.
        var (productiveSymbols, scannedTestProjects) = await RecordTestProjectReferencesAsync(
            solution, scopedProjects, testProjectIds, allProductive, ct).ConfigureAwait(false);

        var notes = new List<string>();
        if (scannedTestProjects.Count == 0)
        {
            notes.Add("No test projects detected (IsTestProject=true). Coverage defaults to 0%.");
        }
        notes.Add(
            "Static reference analysis misses reflection, DI-constructed calls, and mocks. " +
            "Runtime coverage from test_coverage remains the authoritative view for those.");

        // Item 9: detect NSubstitute mock-drift across the same test projects.
        var mockDrift = await DetectMockDriftAsync(solution, scopedProjects, testProjectIds, ct).ConfigureAwait(false);

        // Phase 4: compute ordered sets + stable pagination.
        var coveredAll = productiveSymbols
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new CoveredSymbolDto(kv.Key, kv.Value.OrderBy(x => x, StringComparer.Ordinal).ToArray()))
            .ToArray();

        var uncoveredAll = allProductive
            .Where(sym => !productiveSymbols.ContainsKey(sym))
            .OrderBy(sym => sym, StringComparer.Ordinal)
            .ToArray();

        return BuildPaginatedResult(
            coveredAll, uncoveredAll, offset, limit, scannedTestProjects, notes, mockDrift);
    }

    /// <summary>
    /// Resolve the project-scope triple used by the rest of <see cref="BuildAsync"/>:
    /// (a) the set of projects covered by <paramref name="projectName"/> (or every project when
    /// it is null/empty), (b) the test-project ids across the full solution, and (c) the
    /// productive-scope subset (dr-9-5-bug-pagination-001: a productive <paramref name="projectName"/>
    /// restricts the collected symbol set to that project alone). Throws when
    /// <paramref name="projectName"/> matches nothing.
    /// </summary>
    private static (List<Project> ScopedProjects, HashSet<ProjectId> TestProjectIds, IReadOnlyList<Project> ProductiveScopeProjects) ScopeProjects(
        Solution solution,
        WorkspaceStatusDto status,
        string? projectName)
    {
        var scopedProjects = string.IsNullOrWhiteSpace(projectName)
            ? solution.Projects.ToList()
            : solution.Projects.Where(p =>
                string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.FilePath, projectName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (scopedProjects.Count == 0)
        {
            throw new InvalidOperationException(
                $"test_reference_map: project '{projectName}' not found in workspace.");
        }

        // Map project ids to their test-or-not shape so we can classify quickly during the scan.
        var testProjectIds = new HashSet<ProjectId>();
        foreach (var project in solution.Projects)
        {
            var projStatus = status.Projects.FirstOrDefault(p =>
                string.Equals(p.FilePath, project.FilePath, StringComparison.OrdinalIgnoreCase));
            if (projStatus is not null && projStatus.IsTestProject)
                testProjectIds.Add(project.Id);
        }

        // dr-9-5-bug-pagination-001: when projectName matches a PRODUCTIVE project, restrict
        // the collected productive-symbol set to that project's symbols. Pre-fix, projectName
        // only influenced which test projects got scanned, so passing a productive project
        // returned the full unfiltered productive set.
        var productiveScopeProjects = !string.IsNullOrWhiteSpace(projectName)
            ? scopedProjects.Where(p => !testProjectIds.Contains(p.Id)).ToList()
            : (IReadOnlyList<Project>)solution.Projects.Where(p => !testProjectIds.Contains(p.Id)).ToList();

        return (scopedProjects, testProjectIds, productiveScopeProjects);
    }

    /// <summary>
    /// Walk the compilation global-namespace of each productive-scope project and collect the
    /// display string of every public/internal ordinary-method or constructor declared in
    /// source. Projects that cannot produce a compilation are skipped (matches the pre-refactor
    /// behavior).
    /// </summary>
    private static async Task<HashSet<string>> CollectProductiveSymbolsAsync(
        IReadOnlyList<Project> productiveScopeProjects,
        CancellationToken ct)
    {
        var allProductive = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in productiveScopeProjects)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;
            CollectProductiveSymbols(compilation.GlobalNamespace, allProductive);
        }
        return allProductive;
    }

    /// <summary>
    /// Walk every test project → document → method-declaration → invocation and record which
    /// productive symbols each test method references. A reference is counted when the target
    /// resolves to a source-declared method/property/constructor in a non-test project with
    /// public/internal/protected accessibility and appears in <paramref name="allProductive"/>.
    /// Returns the symbol→testMethodNames map plus the list of test projects actually scanned.
    /// </summary>
    private static async Task<(Dictionary<string, HashSet<string>> ProductiveSymbols, List<string> ScannedTestProjects)> RecordTestProjectReferencesAsync(
        Solution solution,
        IReadOnlyList<Project> scopedProjects,
        HashSet<ProjectId> testProjectIds,
        HashSet<string> allProductive,
        CancellationToken ct)
    {
        var productiveSymbols = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var scannedTestProjects = new List<string>();

        foreach (var testProject in scopedProjects.Where(p => testProjectIds.Contains(p.Id)))
        {
            ct.ThrowIfCancellationRequested();
            scannedTestProjects.Add(testProject.Name);
            var compilation = await testProject.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var document in testProject.Documents)
            {
                var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (semanticModel is null || root is null) continue;

                RecordDocumentReferences(
                    document: document,
                    root: root,
                    semanticModel: semanticModel,
                    solution: solution,
                    testProjectIds: testProjectIds,
                    allProductive: allProductive,
                    productiveSymbols: productiveSymbols,
                    ct: ct);
            }
        }

        return (productiveSymbols, scannedTestProjects);
    }

    /// <summary>
    /// Per-document leaf of <see cref="RecordTestProjectReferencesAsync"/>. Kept synchronous — the
    /// document's syntax root and semantic model are already materialized by the caller, so the
    /// inner loop is a pure walk.
    /// </summary>
    private static void RecordDocumentReferences(
        Document document,
        SyntaxNode root,
        SemanticModel semanticModel,
        Solution solution,
        HashSet<ProjectId> testProjectIds,
        HashSet<string> allProductive,
        Dictionary<string, HashSet<string>> productiveSymbols,
        CancellationToken ct)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var testMethodSymbol = semanticModel.GetDeclaredSymbol(method, ct) as IMethodSymbol;
            if (testMethodSymbol is null) continue;
            var testMethodName = testMethodSymbol.ToDisplayString();

            foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
                var target = symbolInfo.Symbol as IMethodSymbol;
                if (target is null) continue;
                if (target.ContainingAssembly is null) continue;
                if (target.Locations.All(l => l.Kind == LocationKind.MetadataFile)) continue;
                if (testProjectIds.Contains(target.ContainingAssembly.GetProjectIdFromSolution(solution))) continue;
                if (target.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.Protected or Accessibility.ProtectedOrInternal))
                    continue;

                var display = target.OriginalDefinition.ToDisplayString();
                if (!allProductive.Contains(display)) continue;

                if (!productiveSymbols.TryGetValue(display, out var methods))
                {
                    methods = new HashSet<string>(StringComparer.Ordinal);
                    productiveSymbols[display] = methods;
                }
                methods.Add(testMethodName);
            }
        }
    }

    /// <summary>
    /// dr-9-5-bug-pagination-001: page through the combined (covered-first, uncovered-next) list.
    /// The combined ordering is stable between calls so callers can resume at
    /// <c>offset + returnedCount</c>. <see cref="TestReferenceMapDto.CoveragePercent"/> stays pegged
    /// to the full counts so the verdict doesn't wobble based on page window.
    /// </summary>
    private static TestReferenceMapDto BuildPaginatedResult(
        CoveredSymbolDto[] coveredAll,
        string[] uncoveredAll,
        int offset,
        int limit,
        IReadOnlyList<string> scannedTestProjects,
        IReadOnlyList<string> notes,
        IReadOnlyList<MockDriftWarningDto> mockDrift)
    {
        var denominator = coveredAll.Length + uncoveredAll.Length;
        var percent = denominator == 0 ? 0 : Math.Round(coveredAll.Length * 100.0 / denominator, 1);

        var clampedOffset = Math.Clamp(offset, 0, denominator);
        var clampedLimit = Math.Clamp(limit, 1, 500);

        var coveredSkip = Math.Min(clampedOffset, coveredAll.Length);
        var coveredTakeCap = Math.Min(clampedLimit, coveredAll.Length - coveredSkip);
        var coveredPage = coveredTakeCap > 0
            ? coveredAll.Skip(coveredSkip).Take(coveredTakeCap).ToArray()
            : Array.Empty<CoveredSymbolDto>();

        var remainingLimit = clampedLimit - coveredPage.Length;
        var uncoveredSkip = Math.Max(0, clampedOffset - coveredAll.Length);
        var uncoveredTakeCap = remainingLimit > 0 ? Math.Min(remainingLimit, uncoveredAll.Length - uncoveredSkip) : 0;
        var uncoveredPage = uncoveredTakeCap > 0
            ? uncoveredAll.Skip(uncoveredSkip).Take(uncoveredTakeCap).ToArray()
            : Array.Empty<string>();

        var returned = coveredPage.Length + uncoveredPage.Length;
        var hasMore = clampedOffset + returned < denominator;

        return new TestReferenceMapDto(
            coveredPage,
            uncoveredPage,
            percent,
            scannedTestProjects,
            notes,
            mockDrift,
            Offset: clampedOffset,
            Limit: clampedLimit,
            TotalCoveredCount: coveredAll.Length,
            TotalUncoveredCount: uncoveredAll.Length,
            HasMore: hasMore);
    }

    /// <summary>
    /// Item 9 implementation. For every test class that uses
    /// <c>NSubstitute.Substitute.For&lt;TInterface&gt;()</c>, collect the interface and the test
    /// class. Then for each interface method called by production code that the test class
    /// references, check whether the test class also calls <c>.Returns(...)</c> /
    /// <c>.ReturnsForAnyArgs(...)</c> / <c>.Configure*(...)</c> on that method. If not, emit
    /// a <see cref="MockDriftWarningDto"/>.
    ///
    /// <para>
    /// Heuristic, not exhaustive: covers the canonical NSubstitute call shapes. Moq, FakeItEasy,
    /// and reflection-based stubbing are not detected.
    /// </para>
    /// </summary>
    private static async Task<IReadOnlyList<MockDriftWarningDto>> DetectMockDriftAsync(
        Solution solution,
        IReadOnlyList<Project> scopedProjects,
        HashSet<ProjectId> testProjectIds,
        CancellationToken ct)
    {
        var warnings = new List<MockDriftWarningDto>();

        foreach (var testProject in scopedProjects.Where(p => testProjectIds.Contains(p.Id)))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var document in testProject.Documents)
            {
                await ScanDocumentForMockDriftAsync(document, solution, testProjectIds, warnings, ct).ConfigureAwait(false);
            }
        }

        return warnings;
    }

    /// <summary>
    /// Per-document leaf of <see cref="DetectMockDriftAsync"/>. Materializes the syntax root +
    /// semantic model once and walks every class declaration. Skips silently when either is
    /// unavailable (binary-only references / source-generator-only documents).
    /// </summary>
    private static async Task ScanDocumentForMockDriftAsync(
        Document document,
        Solution solution,
        HashSet<ProjectId> testProjectIds,
        List<MockDriftWarningDto> warnings,
        CancellationToken ct)
    {
        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (semanticModel is null || root is null) return;

        foreach (var classDecl in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>())
        {
            await ScanClassForMockDriftAsync(classDecl, semanticModel, solution, testProjectIds, warnings, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Per-class leaf of <see cref="DetectMockDriftAsync"/>. Two phases:
    /// (1) collect the interface symbols this test class mocks via <c>Substitute.For&lt;T&gt;()</c>;
    /// (2) for each mocked interface, emit a warning for every interface method that production
    /// code calls but the test class never stubs. No-ops when the class declares no mocks.
    /// </summary>
    private static async Task ScanClassForMockDriftAsync(
        Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel,
        Solution solution,
        HashSet<ProjectId> testProjectIds,
        List<MockDriftWarningDto> warnings,
        CancellationToken ct)
    {
        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
        if (classSymbol is null) return;

        // Phase 1: find Substitute.For<T>() invocations within this class — yields the mocked
        // interface symbols this test class works with.
        var mockedInterfaces = CollectMockedInterfaces(classDecl, semanticModel, ct);
        if (mockedInterfaces.Count == 0) return;

        // Phase 2: for each mocked interface, find every method called somewhere in the
        // *solution* that we'd expect a stub for, and emit warnings for missing stubs.
        foreach (var iface in mockedInterfaces)
        {
            await EmitWarningsForMockedInterfaceAsync(
                iface, classDecl, classSymbol, semanticModel, solution, testProjectIds, warnings, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Phase-1 helper for <see cref="ScanClassForMockDriftAsync"/>. Walks every invocation
    /// inside the class body, matches the canonical NSubstitute shape
    /// <c>Substitute.For&lt;TInterface&gt;()</c>, and returns the bound interface symbols.
    /// </summary>
    private static HashSet<INamedTypeSymbol> CollectMockedInterfaces(
        Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var mockedInterfaces = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var invocation in classDecl.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax member) continue;
            if (member.Name is not Microsoft.CodeAnalysis.CSharp.Syntax.GenericNameSyntax generic) continue;
            if (!string.Equals(generic.Identifier.ValueText, "For", StringComparison.Ordinal)) continue;
            if (!member.Expression.ToString().EndsWith("Substitute", StringComparison.Ordinal)) continue;

            var typeArg = generic.TypeArgumentList.Arguments.FirstOrDefault();
            if (typeArg is null) continue;
            var typeInfo = semanticModel.GetTypeInfo(typeArg, ct);
            if (typeInfo.Type is INamedTypeSymbol named && named.TypeKind == TypeKind.Interface)
            {
                mockedInterfaces.Add(named);
            }
        }
        return mockedInterfaces;
    }

    /// <summary>
    /// Phase-2 helper for <see cref="ScanClassForMockDriftAsync"/>. For one mocked interface:
    /// enumerate its ordinary methods, look up which ones production code calls
    /// (via <see cref="FindProductionCallersAsync"/>) and which the test class stubs
    /// (via <see cref="CollectStubbedMethodNames"/>), then emit a
    /// <see cref="MockDriftWarningDto"/> for each method that production calls but the test
    /// fails to stub.
    /// </summary>
    private static async Task EmitWarningsForMockedInterfaceAsync(
        INamedTypeSymbol iface,
        Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax classDecl,
        INamedTypeSymbol classSymbol,
        SemanticModel semanticModel,
        Solution solution,
        HashSet<ProjectId> testProjectIds,
        List<MockDriftWarningDto> warnings,
        CancellationToken ct)
    {
        var methodsOnInterface = iface.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .ToArray();
        if (methodsOnInterface.Length == 0) return;

        // Find the production caller(s) for each method (cheap: scan the test class
        // imports + projects the test references, look for callsites; in practice
        // we just check non-test projects in the solution).
        var productionCallers = await FindProductionCallersAsync(solution, methodsOnInterface, testProjectIds, ct).ConfigureAwait(false);

        // Find stubbed methods within this test class. NSubstitute stubs look like
        // `mock.Method(...).Returns(value)` or `mock.Method(...).ReturnsForAnyArgs(value)`.
        var stubbedMethodNames = CollectStubbedMethodNames(classDecl, semanticModel, ct);

        foreach (var method in methodsOnInterface)
        {
            if (stubbedMethodNames.Contains(method.Name)) continue;
            if (!productionCallers.TryGetValue(method.Name, out var callerDisplay)) continue;
            warnings.Add(new MockDriftWarningDto(
                TestClassDisplay: classSymbol.ToDisplayString(),
                MockedInterfaceDisplay: iface.ToDisplayString(),
                MissingStubMethod: $"{iface.Name}.{method.Name}",
                ProductionCallerDisplay: callerDisplay));
        }
    }

    /// <summary>
    /// Map interface method name → first production callsite display string. Walks the
    /// non-test projects' invocation trees.
    /// </summary>
    private static async Task<Dictionary<string, string>> FindProductionCallersAsync(
        Solution solution,
        IReadOnlyList<IMethodSymbol> interfaceMethods,
        HashSet<ProjectId> testProjectIds,
        CancellationToken ct)
    {
        var byMethodName = new Dictionary<string, string>(StringComparer.Ordinal);
        var nameSet = new HashSet<string>(interfaceMethods.Select(m => m.Name), StringComparer.Ordinal);

        foreach (var project in solution.Projects)
        {
            if (testProjectIds.Contains(project.Id)) continue;
            ct.ThrowIfCancellationRequested();
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                if (root is null || semanticModel is null) continue;
                foreach (var invocation in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
                {
                    var nameToken = invocation.Expression switch
                    {
                        Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
                        Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax id => id.Identifier.ValueText,
                        _ => null,
                    };
                    if (nameToken is null || !nameSet.Contains(nameToken)) continue;
                    if (byMethodName.ContainsKey(nameToken)) continue;
                    var pos = invocation.GetLocation().GetLineSpan();
                    byMethodName[nameToken] = $"{document.FilePath}:{pos.StartLinePosition.Line + 1}";
                }
            }
        }

        return byMethodName;
    }

    /// <summary>
    /// Collect names of methods that the test class stubs via <c>.Returns(...)</c> /
    /// <c>.ReturnsForAnyArgs(...)</c> / <c>.Configure(...)</c> chained calls. Returns the
    /// set of method names the fixture has set expectations for.
    /// </summary>
    private static HashSet<string> CollectStubbedMethodNames(
        Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var stubbed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var invocation in classDecl.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (invocation.Expression is not Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax memberAccess) continue;
            var calledName = memberAccess.Name.Identifier.ValueText;
            if (calledName is not ("Returns" or "ReturnsForAnyArgs" or "ReturnsForAll" or "Configure")) continue;

            // The receiver is `mock.Method(...)`. Walk inward to find the inner invocation's name.
            var inner = memberAccess.Expression;
            if (inner is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax innerInv &&
                innerInv.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax innerMember)
            {
                stubbed.Add(innerMember.Name.Identifier.ValueText);
            }
            else if (inner is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax propAccess)
            {
                // Property stub: mock.MyProp.Returns(value)
                stubbed.Add(propAccess.Name.Identifier.ValueText);
            }
        }
        return stubbed;
    }

    /// <summary>Recurse a namespace collecting every method symbol declared in source.</summary>
    private static void CollectProductiveSymbols(INamespaceSymbol ns, HashSet<string> accumulator)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol childNs)
            {
                CollectProductiveSymbols(childNs, accumulator);
            }
            else if (member is INamedTypeSymbol type)
            {
                CollectProductiveSymbolsFromType(type, accumulator);
            }
        }
    }

    private static void CollectProductiveSymbolsFromType(INamedTypeSymbol type, HashSet<string> accumulator)
    {
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor)) continue;
            if (method.Locations.All(l => l.Kind == LocationKind.MetadataFile)) continue;
            if (method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.Protected or Accessibility.ProtectedOrInternal))
                continue;
            accumulator.Add(method.OriginalDefinition.ToDisplayString());
        }
        foreach (var nested in type.GetTypeMembers())
        {
            CollectProductiveSymbolsFromType(nested, accumulator);
        }
    }
}

internal static class AssemblyProjectIdExtensions
{
    /// <summary>
    /// Roslyn exposes symbols' <c>ContainingAssembly</c> but not the owning <c>ProjectId</c>.
    /// Map by assembly name + solution project set; returns <see cref="ProjectId"/> default when
    /// the symbol is from metadata (BCL / NuGet) so <c>.Contains(default)</c> safely returns false.
    /// </summary>
    public static ProjectId GetProjectIdFromSolution(this IAssemblySymbol assembly, Solution solution)
    {
        var name = assembly.Identity.Name;
        foreach (var p in solution.Projects)
        {
            if (string.Equals(p.AssemblyName, name, StringComparison.Ordinal))
                return p.Id;
        }
        return default!;
    }
}
