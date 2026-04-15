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

    public async Task<TestReferenceMapDto> BuildAsync(string workspaceId, string? projectName, CancellationToken ct)
    {
        var status = _workspace.GetStatus(workspaceId);
        var solution = _workspace.GetCurrentSolution(workspaceId);

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

        var productiveSymbols = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var allProductive = new HashSet<string>(StringComparer.Ordinal);
        var scannedTestProjects = new List<string>();
        var notes = new List<string>();

        // First pass: collect every public/internal productive symbol declaration across
        // non-test referenced projects so we can compute uncovered = productive - covered.
        foreach (var project in solution.Projects)
        {
            if (testProjectIds.Contains(project.Id)) continue;
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;
            CollectProductiveSymbols(compilation.GlobalNamespace, allProductive);
        }

        // Second pass: walk each test project's test methods and record references to
        // productive symbols.
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
        }

        if (scannedTestProjects.Count == 0)
        {
            notes.Add("No test projects detected (IsTestProject=true). Coverage defaults to 0%.");
        }
        notes.Add(
            "Static reference analysis misses reflection, DI-constructed calls, and mocks. " +
            "Runtime coverage from test_coverage remains the authoritative view for those.");

        // Item 9: detect NSubstitute mock-drift across the same test projects.
        var mockDrift = await DetectMockDriftAsync(solution, scopedProjects, testProjectIds, ct).ConfigureAwait(false);

        var covered = productiveSymbols
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new CoveredSymbolDto(kv.Key, kv.Value.OrderBy(x => x, StringComparer.Ordinal).ToArray()))
            .ToArray();

        var uncovered = allProductive
            .Where(sym => !productiveSymbols.ContainsKey(sym))
            .OrderBy(sym => sym, StringComparer.Ordinal)
            .ToArray();

        var denominator = covered.Length + uncovered.Length;
        var percent = denominator == 0 ? 0 : Math.Round(covered.Length * 100.0 / denominator, 1);

        return new TestReferenceMapDto(covered, uncovered, percent, scannedTestProjects, notes, mockDrift);
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
                var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (semanticModel is null || root is null) continue;

                foreach (var classDecl in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>())
                {
                    var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
                    if (classSymbol is null) continue;

                    // 1. Find Substitute.For<T>() invocations within this class — yields the mocked
                    //    interface symbols this test class works with.
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

                    if (mockedInterfaces.Count == 0) continue;

                    // 2. For each mocked interface, find every method called somewhere in the
                    //    *solution* that we'd expect a stub for. Approximation: any method on the
                    //    interface called outside the test project counts as "called by production".
                    foreach (var iface in mockedInterfaces)
                    {
                        var methodsOnInterface = iface.GetMembers().OfType<IMethodSymbol>()
                            .Where(m => m.MethodKind == MethodKind.Ordinary)
                            .ToArray();
                        if (methodsOnInterface.Length == 0) continue;

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
                }
            }
        }

        return warnings;
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
