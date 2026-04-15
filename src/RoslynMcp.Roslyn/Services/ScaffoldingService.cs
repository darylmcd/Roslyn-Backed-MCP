using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal static class MinimalSymbolDisplayExtensions
{
    /// <summary>
    /// Concise display form used when emitting scaffolded interface stubs. Keeps generic
    /// arguments readable without full namespace qualification.
    /// </summary>
    public static string ToMinimalDisplay(this ITypeSymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
}

public sealed class ScaffoldingService : IScaffoldingService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IFileOperationService _fileOperationService;
    private readonly Contracts.IPreviewStore _previewStore;

    public ScaffoldingService(
        IWorkspaceManager workspace,
        IFileOperationService fileOperationService,
        Contracts.IPreviewStore previewStore)
    {
        _workspace = workspace;
        _fileOperationService = fileOperationService;
        _previewStore = previewStore;
    }

    public async Task<RefactoringPreviewDto> PreviewScaffoldTypeAsync(string workspaceId, ScaffoldTypeDto request, CancellationToken ct)
    {
        IdentifierValidation.ThrowIfInvalidIdentifier(request.TypeName, "type name");
        var project = ResolveProject(workspaceId, request.ProjectName);
        var projectDirectory = Path.GetDirectoryName(project.FilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{project.FilePath}'.");
        var typeNamespace = string.IsNullOrWhiteSpace(request.Namespace) ? project.Name : request.Namespace!;
        var folderSegments = ResolveFolderSegmentsForNamespace(typeNamespace, project.Name);
        var filePath = Path.Combine([projectDirectory, .. folderSegments, $"{request.TypeName}.cs"]);

        var interfaceResolution = await ResolveInterfaceMembersAsync(workspaceId, request, ct).ConfigureAwait(false);
        var content = BuildTypeContent(typeNamespace, request, interfaceResolution);

        var preview = await _fileOperationService
            .PreviewCreateFileAsync(workspaceId, new CreateFileDto(project.Name, filePath, content), ct)
            .ConfigureAwait(false);

        if (interfaceResolution.Warnings.Count > 0)
        {
            return preview with { Warnings = interfaceResolution.Warnings };
        }
        return preview;
    }

    /// <summary>
    /// Picks the folder segments under the project root for a scaffolded file. When the
    /// namespace starts with the project name (the conventional case), strip that prefix and
    /// use the rest as folder names. Otherwise, use the full namespace path so that an
    /// explicit \"SomeOther.Sub\" namespace lands in \"SomeOther/Sub/\" instead of the project
    /// root. Previously the namespace-doesn't-start-with-project-name case fell through to
    /// the project root, which mismatched the expectation that scaffolded files live under
    /// a folder matching their namespace.
    /// </summary>
    private static IReadOnlyList<string> ResolveFolderSegmentsForNamespace(string typeNamespace, string projectName)
    {
        if (string.IsNullOrWhiteSpace(typeNamespace) || string.Equals(typeNamespace, projectName, StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        var workingNamespace = typeNamespace.StartsWith(projectName + ".", StringComparison.Ordinal)
            ? typeNamespace[(projectName.Length + 1)..]
            : typeNamespace;

        return workingNamespace.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Item 8: batch scaffold-test. Runs the single-type resolver once per target but reuses a
    /// single workspace solution snapshot across all targets, aggregating the document adds into
    /// one <see cref="RefactoringPreviewDto"/>/preview token. Callers redeem the token with
    /// <c>apply_composite_preview</c> (or the regular apply path) to commit the whole batch
    /// atomically.
    /// </summary>
    public async Task<RefactoringPreviewDto> PreviewScaffoldTestBatchAsync(
        string workspaceId, ScaffoldTestBatchDto request, CancellationToken ct)
    {
        if (request.Targets is null || request.Targets.Count == 0)
        {
            throw new InvalidOperationException("scaffold_test_batch_preview requires at least one target.");
        }

        var project = ResolveProject(workspaceId, request.TestProjectName);
        ValidateIsTestProject(project);
        var projectDirectory = Path.GetDirectoryName(project.FilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{project.FilePath}'.");
        var testNamespace = project.Name;
        var framework = ResolveTestFramework(request.TestFramework, project.FilePath);

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var testProject = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, request.TestProjectName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.FilePath, request.TestProjectName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Test project not loaded: {request.TestProjectName}");

        // Cache source-project compilations once to avoid N× GetCompilationAsync across targets
        // (the primary perf win over iterating PreviewScaffoldTestAsync).
        var projectsToSearch = new List<Project> { testProject };
        foreach (var projectRef in testProject.ProjectReferences)
        {
            var referenced = solution.GetProject(projectRef.ProjectId);
            if (referenced is not null) projectsToSearch.Add(referenced);
        }
        var cachedCompilations = new List<Compilation>();
        foreach (var p in projectsToSearch)
        {
            var compilation = await p.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is not null) cachedCompilations.Add(compilation);
        }

        var accumulator = solution;
        var warnings = new List<string>();
        var createdFiles = new List<string>();
        var skippedTargets = new List<string>();

        foreach (var target in request.Targets)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(target.TargetTypeName))
            {
                warnings.Add("Skipped empty target type name.");
                continue;
            }

            var testFilePath = Path.Combine(projectDirectory, $"{target.TargetTypeName}GeneratedTests.cs");
            if (SymbolResolver.FindDocument(accumulator, testFilePath) is not null || File.Exists(testFilePath))
            {
                skippedTargets.Add(target.TargetTypeName);
                warnings.Add($"Skipped '{target.TargetTypeName}': target file already exists at '{testFilePath}'.");
                continue;
            }

            var typeInfo = ResolveTargetTypeAndMethodFromCache(
                cachedCompilations, target.TargetTypeName, target.TargetMethodName);
            if (typeInfo.matchedType is null)
            {
                warnings.Add($"Target type '{target.TargetTypeName}' not found in referenced projects — skipped.");
                continue;
            }
            if (typeInfo.warnings is not null) warnings.AddRange(typeInfo.warnings);

            var dto = new ScaffoldTestDto(request.TestProjectName, target.TargetTypeName, target.TargetMethodName, request.TestFramework);
            var content = BuildTestContent(
                testNamespace, dto, typeInfo.targetNamespace, typeInfo.constructorArgs, framework,
                typeInfo.targetMethod, typeInfo.matchedType);

            var projInAccumulator = accumulator.GetProject(testProject.Id)
                ?? throw new InvalidOperationException("Test project disappeared from working solution snapshot.");
            var folders = Array.Empty<string>();
            var newDoc = projInAccumulator.AddDocument(
                Path.GetFileName(testFilePath),
                Microsoft.CodeAnalysis.Text.SourceText.From(content),
                folders,
                testFilePath);
            accumulator = newDoc.Project.Solution;
            createdFiles.Add(testFilePath);
        }

        if (createdFiles.Count == 0)
        {
            throw new InvalidOperationException(
                "scaffold_test_batch_preview produced no file creations. See Warnings for per-target reasons.");
        }

        var changes = await Helpers.SolutionDiffHelper.ComputeChangesAsync(solution, accumulator, ct).ConfigureAwait(false);
        var description = $"Scaffold {createdFiles.Count} test file(s) in project '{project.Name}'";
        var token = _previewStore.Store(workspaceId, accumulator, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, warnings.Count > 0 ? warnings : null);
    }

    /// <summary>
    /// Non-async variant of <see cref="ResolveTargetTypeAndMethodAsync"/> reading from a cached
    /// compilation list. Used by batch scaffold to avoid re-walking <c>GetCompilationAsync</c>
    /// per target.
    /// </summary>
    private static (string targetNamespace, string constructorArgs, IMethodSymbol? targetMethod, List<string>? warnings, INamedTypeSymbol? matchedType)
        ResolveTargetTypeAndMethodFromCache(
            IReadOnlyList<Compilation> compilations, string targetTypeName, string? targetMethodName)
    {
        INamedTypeSymbol? matchedType = null;
        foreach (var compilation in compilations)
        {
            var candidates = compilation.GetSymbolsWithName(targetTypeName, SymbolFilter.Type)
                .OfType<INamedTypeSymbol>()
                .Where(t => t.TypeKind is TypeKind.Class or TypeKind.Struct &&
                            string.Equals(t.Name, targetTypeName, StringComparison.Ordinal))
                .ToList();
            if (candidates.Count == 1) { matchedType = candidates[0]; break; }
            if (candidates.Count > 1)
            {
                return (string.Empty, string.Empty, null,
                    [$"Ambiguous type '{targetTypeName}' — multiple candidates; skipped."], null);
            }
        }

        if (matchedType is null)
            return (string.Empty, string.Empty, null, null, null);

        var ns = matchedType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : matchedType.ContainingNamespace.ToDisplayString();

        var constructorArgs = BuildConstructorArgs(matchedType);

        IMethodSymbol? targetMethod = null;
        List<string>? warnings = null;
        if (!string.IsNullOrWhiteSpace(targetMethodName))
        {
            targetMethod = matchedType.GetMembers(targetMethodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.MethodKind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation);
            if (targetMethod is null)
            {
                warnings ??= [];
                warnings.Add($"Target method '{targetMethodName}' was not found on type '{matchedType.Name}'.");
            }
        }

        return (ns, constructorArgs, targetMethod, warnings, matchedType);
    }

    public async Task<RefactoringPreviewDto> PreviewScaffoldTestAsync(string workspaceId, ScaffoldTestDto request, CancellationToken ct)
    {
        var project = ResolveProject(workspaceId, request.TestProjectName);
        ValidateIsTestProject(project);
        var projectDirectory = Path.GetDirectoryName(project.FilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{project.FilePath}'.");
        var testFilePath = Path.Combine(projectDirectory, $"{request.TargetTypeName}GeneratedTests.cs");
        var testNamespace = project.Name;

        var framework = ResolveTestFramework(request.TestFramework, project.FilePath);

        var typeInfo = await ResolveTargetTypeAndMethodAsync(
            workspaceId, request.TestProjectName, request.TargetTypeName, request.TargetMethodName, ct).ConfigureAwait(false);
        var content = BuildTestContent(
            testNamespace, request, typeInfo.targetNamespace, typeInfo.constructorArgs, framework, typeInfo.targetMethod, typeInfo.matchedType);
        var preview = await _fileOperationService.PreviewCreateFileAsync(workspaceId, new CreateFileDto(project.Name, testFilePath, content), ct).ConfigureAwait(false);

        if (typeInfo.warnings is null || typeInfo.warnings.Count == 0)
            return preview;

        return preview with { Warnings = typeInfo.warnings };
    }

    private static string ResolveTestFramework(string? requested, string? projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(requested) ||
            string.Equals(requested, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return DetectTestFrameworkFromProjectFile(projectFilePath);
        }

        if (string.Equals(requested, "mstest", StringComparison.OrdinalIgnoreCase)) return "mstest";
        if (string.Equals(requested, "xunit", StringComparison.OrdinalIgnoreCase)) return "xunit";
        if (string.Equals(requested, "nunit", StringComparison.OrdinalIgnoreCase)) return "nunit";

        throw new InvalidOperationException(
            $"Unsupported testFramework '{requested}'. Use mstest, xunit, nunit, or auto.");
    }

    private static string DetectTestFrameworkFromProjectFile(string? projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
            return "mstest";

        try
        {
            var doc = XDocument.Load(projectFilePath, LoadOptions.None);
            var includes = doc.Descendants("PackageReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i!.ToLowerInvariant())
                .ToList();

            if (includes.Any(i => i.Contains("xunit", StringComparison.Ordinal)))
                return "xunit";
            if (includes.Any(i => i.Contains("nunit", StringComparison.Ordinal)))
                return "nunit";
        }
        catch
        {
            // Fall through to default
        }

        return "mstest";
    }

    private async Task<(string targetNamespace, string constructorArgs, IMethodSymbol? targetMethod, List<string>? warnings, INamedTypeSymbol? matchedType)>
        ResolveTargetTypeAndMethodAsync(
            string workspaceId, string testProjectName, string targetTypeName, string? targetMethodName, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var testProject = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, testProjectName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.FilePath, testProjectName, StringComparison.OrdinalIgnoreCase));

        if (testProject is null)
            return (string.Empty, string.Empty, null, null, null);

        var projectsToSearch = new List<Project> { testProject };
        foreach (var projectRef in testProject.ProjectReferences)
        {
            var referencedProject = solution.GetProject(projectRef.ProjectId);
            if (referencedProject is not null)
                projectsToSearch.Add(referencedProject);
        }

        INamedTypeSymbol? matchedType = null;
        foreach (var project in projectsToSearch)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var candidates = compilation.GetSymbolsWithName(targetTypeName, SymbolFilter.Type, ct)
                .OfType<INamedTypeSymbol>()
                .Where(t => t.TypeKind is TypeKind.Class or TypeKind.Struct &&
                            string.Equals(t.Name, targetTypeName, StringComparison.Ordinal))
                .ToList();

            if (candidates.Count == 1)
            {
                matchedType = candidates[0];
                break;
            }

            if (candidates.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Ambiguous type name '{targetTypeName}' — found in multiple namespaces: " +
                    string.Join(", ", candidates.Select(c => c.ToDisplayString())) +
                    ". Use the fully qualified type name.");
            }
        }

        if (matchedType is null)
            return (string.Empty, string.Empty, null, null, null);

        var ns = matchedType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : matchedType.ContainingNamespace.ToDisplayString();

        var constructorArgs = BuildConstructorArgs(matchedType);

        IMethodSymbol? targetMethod = null;
        List<string>? warnings = null;
        if (!string.IsNullOrWhiteSpace(targetMethodName))
        {
            targetMethod = matchedType.GetMembers(targetMethodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.MethodKind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation);

            if (targetMethod is null)
            {
                warnings ??= [];
                warnings.Add($"Target method '{targetMethodName}' was not found on type '{matchedType.Name}'.");
            }
            else if (targetMethod.DeclaredAccessibility == Accessibility.Private)
            {
                warnings ??= [];
                warnings.Add(
                    $"Target method '{targetMethodName}' is private — the scaffold uses reflection to invoke it; " +
                    "prefer InternalsVisibleTo or testing via public API when possible.");
            }
        }

        return (ns, constructorArgs, targetMethod, warnings, matchedType);
    }

    private static string BuildConstructorArgs(INamedTypeSymbol type)
    {
        var constructors = type.Constructors
            .Where(c => !c.IsImplicitlyDeclared || c.Parameters.Length == 0)
            .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .OrderBy(c => c.Parameters.Length)
            .ToList();

        if (constructors.Count == 0)
            return string.Empty;

        var bestCtor = constructors[0];
        if (bestCtor.Parameters.Length == 0)
            return string.Empty;

        var args = bestCtor.Parameters.Select(p => $"{BuildArgExpression(p.Type)} /* {p.Name} */");
        return string.Join(", ", args);
    }

    /// <summary>
    /// Builds a default-constructible expression for a constructor parameter type. Empty
    /// collection interfaces (<c>IEnumerable&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, etc.) get
    /// <c>Array.Empty&lt;T&gt;()</c>, dictionaries get <c>new Dictionary&lt;K,V&gt;()</c>,
    /// and <c>string</c> gets <c>string.Empty</c>. Everything else falls back to
    /// <c>default(T)</c>. Previously every parameter was emitted as <c>default(T)</c>, which
    /// throws <c>NullReferenceException</c> on the first call when the parameter is a non-null
    /// collection interface — observed in the 2026-04-07 ITChatBot legacy-mutex audit.
    /// </summary>
    private static string BuildArgExpression(ITypeSymbol parameterType)
    {
        var displayName = parameterType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        if (parameterType.SpecialType == SpecialType.System_String)
        {
            return "string.Empty";
        }

        if (parameterType is INamedTypeSymbol named && named.IsGenericType)
        {
            var openGenericName = named.ConstructedFrom.ToDisplayString();

            if (openGenericName is "System.Collections.Generic.IEnumerable<T>"
                or "System.Collections.Generic.ICollection<T>"
                or "System.Collections.Generic.IReadOnlyCollection<T>"
                or "System.Collections.Generic.IList<T>"
                or "System.Collections.Generic.IReadOnlyList<T>")
            {
                var elementType = named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                return $"System.Array.Empty<{elementType}>()";
            }

            if (openGenericName is "System.Collections.Generic.IDictionary<TKey, TValue>"
                or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
            {
                var keyType = named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                var valueType = named.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                return $"new System.Collections.Generic.Dictionary<{keyType}, {valueType}>()";
            }
        }

        return $"default({displayName})";
    }

    private ProjectStatusDto ResolveProject(string workspaceId, string projectName)
    {
        return _workspace.GetStatus(workspaceId).Projects.FirstOrDefault(project =>
                   string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(project.FilePath, projectName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Project not found: {projectName}");
    }

    private static void ValidateIsTestProject(ProjectStatusDto project)
    {
        if (string.IsNullOrWhiteSpace(project.FilePath) || !File.Exists(project.FilePath))
            return; // Can't validate — allow and let framework detection handle it

        try
        {
            var doc = XDocument.Load(project.FilePath, LoadOptions.None);

            // Check <IsTestProject>true</IsTestProject>
            var isTestProject = doc.Descendants("IsTestProject")
                .Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
            if (isTestProject) return;

            // Check for test framework PackageReferences
            var includes = doc.Descendants("PackageReference")
                .Select(e => e.Attribute("Include")?.Value?.ToLowerInvariant())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .ToList();

            var hasTestFramework = includes.Any(i =>
                i!.Contains("mstest", StringComparison.Ordinal) ||
                i!.Contains("xunit", StringComparison.Ordinal) ||
                i!.Contains("nunit", StringComparison.Ordinal) ||
                i!.Contains("microsoft.net.test.sdk", StringComparison.Ordinal));
            if (hasTestFramework) return;

            throw new InvalidOperationException(
                $"Project '{project.Name}' does not appear to be a test project. " +
                "It has no <IsTestProject>true</IsTestProject> property and no test framework package references (MSTest, xUnit, NUnit). " +
                "Please specify a test project instead.");
        }
        catch (InvalidOperationException) { throw; }
        catch
        {
            // If we can't parse the project file, allow and let downstream handle it
        }
    }

    /// <summary>
    /// Resolution result for interface auto-implementation: the textual member stubs to inject
    /// into the class body, plus the <c>using</c> namespaces referenced by those stubs, plus
    /// any warnings emitted during resolution.
    /// </summary>
    private sealed record InterfaceResolutionResult(
        string MemberStubs,
        IReadOnlyCollection<string> RequiredUsings,
        IReadOnlyList<string> Warnings)
    {
        public static InterfaceResolutionResult Empty { get; } =
            new(string.Empty, Array.Empty<string>(), Array.Empty<string>());
    }

    /// <summary>
    /// Item 2: when <see cref="ScaffoldTypeDto.ImplementInterface"/> is true and the scaffolded
    /// type is a class (not an interface/record/enum), walk any interface candidates in
    /// <c>BaseType</c> and <c>Interfaces</c>, resolve each to an <see cref="INamedTypeSymbol"/>,
    /// and build textual stub declarations for all interface members. Falls back to
    /// <see cref="InterfaceResolutionResult.Empty"/> (with a warning) when the interface cannot
    /// be resolved so scaffold still succeeds.
    /// </summary>
    private async Task<InterfaceResolutionResult> ResolveInterfaceMembersAsync(
        string workspaceId, ScaffoldTypeDto request, CancellationToken ct)
    {
        if (!request.ImplementInterface)
            return InterfaceResolutionResult.Empty;

        var normalizedKind = request.TypeKind.ToLowerInvariant();
        if (normalizedKind is "interface" or "enum")
            return InterfaceResolutionResult.Empty;

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.BaseType))
            candidates.Add(request.BaseType!);
        if (request.Interfaces is not null)
            candidates.AddRange(request.Interfaces.Where(n => !string.IsNullOrWhiteSpace(n)));

        if (candidates.Count == 0)
            return InterfaceResolutionResult.Empty;

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var stubs = new System.Text.StringBuilder();
        var requiredUsings = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var emittedSignatures = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            INamedTypeSymbol? resolved = null;
            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                if (compilation is null) continue;
                resolved = compilation.GetTypeByMetadataName(candidate)
                    ?? compilation.GetSymbolsWithName(
                            StripGenericArity(candidate), Microsoft.CodeAnalysis.SymbolFilter.Type, ct)
                        .OfType<INamedTypeSymbol>()
                        .FirstOrDefault(t => t.ToDisplayString().Equals(candidate, StringComparison.Ordinal) ||
                                             t.Name.Equals(candidate, StringComparison.Ordinal));
                if (resolved is not null) break;
            }

            if (resolved is null)
            {
                warnings.Add(
                    $"Could not resolve '{candidate}' to a type symbol in workspace '{workspaceId}'. " +
                    "Scaffolded class will have an empty body — add interface members manually.");
                continue;
            }

            if (resolved.TypeKind != TypeKind.Interface)
                continue; // concrete base class — no stubs needed.

            // AllInterfaces includes inherited interfaces (IFoo : IBar implements IBar's members too).
            var interfacesToEmit = new List<INamedTypeSymbol> { resolved };
            interfacesToEmit.AddRange(resolved.AllInterfaces);

            foreach (var iface in interfacesToEmit)
            {
                foreach (var member in iface.GetMembers())
                {
                    // Skip static interface members (DIM entrypoints), property accessors (handled via property itself),
                    // and members with a default implementation (not required of implementors).
                    if (member.IsStatic) continue;
                    if (member is IMethodSymbol methodSym &&
                        methodSym.AssociatedSymbol is IPropertySymbol or IEventSymbol) continue;
                    if (!member.IsAbstract && HasDefaultInterfaceImplementation(member)) continue;

                    var signature = BuildMemberSignatureKey(member);
                    if (!emittedSignatures.Add(signature)) continue;

                    var stub = BuildInterfaceMemberStub(member, requiredUsings);
                    if (stub is null) continue;
                    stubs.AppendLine(stub);
                }
            }
        }

        return new InterfaceResolutionResult(stubs.ToString(), requiredUsings, warnings);
    }

    /// <summary>Rope generic arity (<c>`1</c>) from a display name like <c>IEnumerable`1</c>.</summary>
    private static string StripGenericArity(string name)
    {
        var tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    /// <summary>True when an interface method/property has a concrete default body (C# 8 DIM).</summary>
    private static bool HasDefaultInterfaceImplementation(ISymbol member)
    {
        foreach (var syntaxRef in member.DeclaringSyntaxReferences)
        {
            var node = syntaxRef.GetSyntax();
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax m && m.Body is not null)
                return true;
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax p &&
                p.AccessorList is not null &&
                p.AccessorList.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null))
                return true;
        }
        return false;
    }

    private static string BuildMemberSignatureKey(ISymbol member)
    {
        if (member is IMethodSymbol method)
        {
            var parms = string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString()));
            return $"M:{method.Name}({parms})<{method.TypeParameters.Length}>";
        }
        if (member is IPropertySymbol prop)
        {
            var parms = string.Join(",", prop.Parameters.Select(p => p.Type.ToDisplayString()));
            return $"P:{prop.Name}[{parms}]";
        }
        return $"{member.Kind}:{member.Name}";
    }

    /// <summary>
    /// Emit a textual method/property/event stub that throws <c>NotImplementedException</c>.
    /// Uses <c>MinimallyQualifiedFormat</c> so callers get readable type names plus recorded
    /// required <c>using</c> namespaces for later header assembly.
    /// </summary>
    private static string? BuildInterfaceMemberStub(ISymbol member, HashSet<string> requiredUsings)
    {
        requiredUsings.Add("System"); // NotImplementedException

        if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
        {
            CollectNamespaces(method.ReturnType, requiredUsings);
            foreach (var p in method.Parameters)
                CollectNamespaces(p.Type, requiredUsings);

            var typeParams = method.TypeParameters.Length == 0
                ? string.Empty
                : "<" + string.Join(", ", method.TypeParameters.Select(tp => tp.Name)) + ">";
            var parameters = string.Join(", ", method.Parameters.Select(FormatParameter));
            var constraints = BuildTypeParameterConstraints(method.TypeParameters);
            var returnType = method.ReturnsVoid ? "void" : method.ReturnType.ToMinimalDisplay();
            var body = method.ReturnsVoid
                ? "throw new NotImplementedException();"
                : "throw new NotImplementedException();";

            return
                $"    public {returnType} {method.Name}{typeParams}({parameters}){constraints}\n" +
                "    {\n" +
                $"        {body}\n" +
                "    }\n";
        }

        if (member is IPropertySymbol property && !property.IsIndexer)
        {
            CollectNamespaces(property.Type, requiredUsings);
            var type = property.Type.ToMinimalDisplay();
            var accessors = new List<string>();
            if (property.GetMethod is not null) accessors.Add("get => throw new NotImplementedException();");
            if (property.SetMethod is not null)
            {
                // Use set or init based on the interface declaration.
                var keyword = property.SetMethod.IsInitOnly ? "init" : "set";
                accessors.Add($"{keyword} => throw new NotImplementedException();");
            }
            var accessorBlock = string.Join(" ", accessors);
            return $"    public {type} {property.Name} {{ {accessorBlock} }}\n";
        }

        if (member is IEventSymbol evt)
        {
            CollectNamespaces(evt.Type, requiredUsings);
            var type = evt.Type.ToMinimalDisplay();
            return
                $"    public event {type}? {evt.Name}\n" +
                "    {\n" +
                "        add => throw new NotImplementedException();\n" +
                "        remove => throw new NotImplementedException();\n" +
                "    }\n";
        }

        return null;
    }

    private static string FormatParameter(IParameterSymbol parameter)
    {
        var modifier = parameter.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            _ => string.Empty
        };
        var typeText = parameter.Type.ToMinimalDisplay();
        return parameter.IsParams
            ? $"params {typeText} {parameter.Name}"
            : $"{modifier}{typeText} {parameter.Name}";
    }

    private static string BuildTypeParameterConstraints(ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        if (typeParameters.Length == 0) return string.Empty;
        var parts = new List<string>();
        foreach (var tp in typeParameters)
        {
            var clauses = new List<string>();
            if (tp.HasReferenceTypeConstraint) clauses.Add("class");
            if (tp.HasValueTypeConstraint) clauses.Add("struct");
            if (tp.HasUnmanagedTypeConstraint) clauses.Add("unmanaged");
            if (tp.HasNotNullConstraint) clauses.Add("notnull");
            foreach (var ct in tp.ConstraintTypes)
                clauses.Add(ct.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            if (tp.HasConstructorConstraint) clauses.Add("new()");
            if (clauses.Count == 0) continue;
            parts.Add($" where {tp.Name} : {string.Join(", ", clauses)}");
        }
        return string.Concat(parts);
    }

    private static void CollectNamespaces(ITypeSymbol type, HashSet<string> requiredUsings)
    {
        if (type is null) return;
        var ns = type.ContainingNamespace;
        if (ns is not null && !ns.IsGlobalNamespace)
        {
            var display = ns.ToDisplayString();
            if (!string.IsNullOrEmpty(display))
                requiredUsings.Add(display);
        }
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            foreach (var arg in named.TypeArguments)
                CollectNamespaces(arg, requiredUsings);
        }
        if (type is IArrayTypeSymbol array)
            CollectNamespaces(array.ElementType, requiredUsings);
    }

    private static string BuildTypeContent(string typeNamespace, ScaffoldTypeDto request, InterfaceResolutionResult interfaceResolution)
    {
        var inheritance = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.BaseType))
        {
            inheritance.Add(request.BaseType);
        }

        if (request.Interfaces is not null)
        {
            inheritance.AddRange(request.Interfaces.Where(@interface => !string.IsNullOrWhiteSpace(@interface)));
        }

        var inheritanceClause = inheritance.Count > 0 ? $" : {string.Join(", ", inheritance)}" : string.Empty;
        var normalizedKind = request.TypeKind.ToLowerInvariant();
        var typeKeyword = normalizedKind switch
        {
            "interface" => "interface",
            "record" => "record",
            "enum" => "enum",
            _ => "class"
        };

        // Modern .NET convention: default scaffolded classes to `internal sealed class` so
        // they don't expand the public API surface and aren't subclassable by accident.
        // Records/interfaces/enums stay `public` (interface and enum cannot be sealed; records
        // are typically intended as DTOs that get used widely).
        var modifier = normalizedKind == "interface" || normalizedKind == "record" || normalizedKind == "enum"
            ? "public"
            : "internal sealed";

        // Item 2: deduplicate usings against the implied namespace. Skip any using equal to the
        // scaffolded type's own namespace — we're already in it.
        var usingsBlock = new System.Text.StringBuilder();
        foreach (var ns in interfaceResolution.RequiredUsings
            .Where(n => !string.Equals(n, typeNamespace, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal))
        {
            usingsBlock.Append("using ").Append(ns).Append(";\n");
        }
        if (usingsBlock.Length > 0) usingsBlock.Append('\n');

        var body = string.IsNullOrEmpty(interfaceResolution.MemberStubs)
            ? string.Empty
            : interfaceResolution.MemberStubs;

        return $"{usingsBlock}namespace {typeNamespace};\n\n{modifier} {typeKeyword} {request.TypeName}{inheritanceClause}\n{{\n{body}}}\n";
    }

    private static string BuildTestContent(
        string testNamespace,
        ScaffoldTestDto request,
        string targetNamespace,
        string constructorArgs,
        string framework,
        IMethodSymbol? targetMethod,
        INamedTypeSymbol? matchedType)
    {
        var methodName = string.IsNullOrWhiteSpace(request.TargetMethodName)
            ? "Generated_Test"
            : $"{request.TargetMethodName}_Needs_Test";

        var usingDirective = string.IsNullOrWhiteSpace(targetNamespace)
            ? string.Empty
            : $"using {targetNamespace};\n";

        var useStaticScaffold = ShouldUseStaticTestScaffold(matchedType);
        var ctorCall = useStaticScaffold
            ? string.Empty
            : string.IsNullOrWhiteSpace(constructorArgs)
                ? $"new {request.TargetTypeName}()"
                : $"new {request.TargetTypeName}({constructorArgs})";

        var methodTargetBlock = BuildMethodTargetInvocationBlock(
            framework, request.TargetTypeName, request.TargetMethodName, targetMethod, useStaticScaffold);

        return framework switch
        {
            "xunit" => BuildXUnitTestContent(testNamespace, usingDirective, request.TargetTypeName, methodName, ctorCall, methodTargetBlock, useStaticScaffold),
            "nunit" => BuildNUnitTestContent(testNamespace, usingDirective, request.TargetTypeName, methodName, ctorCall, methodTargetBlock, useStaticScaffold),
            _ => BuildMSTestTestContent(testNamespace, usingDirective, request.TargetTypeName, methodName, ctorCall, methodTargetBlock, useStaticScaffold),
        };
    }

    /// <summary>
    /// BUG-N10: static classes, or instance classes whose only public API is static methods (utility types),
    /// should not scaffold <c>new T()</c> + instance assertions.
    /// </summary>
    private static bool ShouldUseStaticTestScaffold(INamedTypeSymbol? matchedType)
    {
        if (matchedType is null)
            return false;
        if (matchedType.IsStatic)
            return true;

        var ordinaryMethods = matchedType.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation)
            .ToList();

        var hasVisibleInstance = ordinaryMethods.Any(m =>
            !m.IsStatic &&
            m.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.Protected);

        var hasVisibleStatic = ordinaryMethods.Any(m =>
            m.IsStatic &&
            m.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal);

        return !hasVisibleInstance && hasVisibleStatic;
    }

    private static string BuildMethodTargetInvocationBlock(
        string framework,
        string targetTypeName,
        string? targetMethodName,
        IMethodSymbol? targetMethod,
        bool useStaticScaffold)
    {
        if (string.IsNullOrWhiteSpace(targetMethodName))
        {
            return "        // No target method specified.\n";
        }

        if (targetMethod is null)
        {
            return $"        // Target method '{targetMethodName}' was not resolved on {targetTypeName}.\n";
        }

        if (useStaticScaffold && targetMethod.IsStatic)
        {
            if (targetMethod.Parameters.Length == 0 && !targetMethod.ReturnsVoid)
                return $"        _ = {targetTypeName}.{targetMethodName}();\n";
            if (targetMethod.Parameters.Length == 0 && targetMethod.ReturnsVoid)
                return $"        {targetTypeName}.{targetMethodName}();\n";
            return $"        // Add arguments for static method '{targetMethodName}'.\n";
        }

        if (targetMethod.DeclaredAccessibility == Accessibility.Private)
        {
            if (useStaticScaffold && !targetMethod.IsStatic)
            {
                return "        // Private instance method — not reachable from a static-only scaffold; test via public API or InternalsVisibleTo.\n";
            }

            var assertNotNull = framework switch
            {
                "xunit" => "Assert.NotNull(__method);",
                "nunit" => "Assert.That(__method, Is.Not.Null);",
                _ => "Assert.IsNotNull(__method);",
            };
            var flags = targetMethod.IsStatic
                ? "System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic"
                : "System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic";
            var invokeTarget = targetMethod.IsStatic ? "null" : "subject";
            return
                "        // Private method — invoke via reflection (replace with InternalsVisibleTo or a public API test if preferred).\n" +
                $"        var __method = typeof({targetTypeName}).GetMethod(\n" +
                $"            \"{targetMethodName}\",\n" +
                $"            {flags});\n" +
                "        " + assertNotNull + "\n" +
                $"        __method!.Invoke({invokeTarget}, null);\n";
        }

        if (targetMethod.Parameters.Length == 0 && !targetMethod.ReturnsVoid)
        {
            return $"        _ = subject.{targetMethodName}();\n";
        }

        if (targetMethod.Parameters.Length == 0 && targetMethod.ReturnsVoid)
        {
            return $"        subject.{targetMethodName}();\n";
        }

        return
            $"        // Target method '{targetMethodName}' has parameters — add arguments or use a wrapper.\n" +
            $"        // Example: subject.{targetMethodName}(/* args */);\n";
    }

    private static string BuildMSTestTestContent(
        string testNamespace,
        string usingDirective,
        string targetTypeName,
        string methodName,
        string ctorCall,
        string methodBlock,
        bool isStaticType)
    {
        var instanceSetup = isStaticType
            ? string.Empty
            : "        var subject = " + ctorCall + ";\n\n";
        var tailAssert = isStaticType
            ? "        Assert.IsTrue(true);\n"
            : "        Assert.IsNotNull(subject);\n";
        return
            "using Microsoft.VisualStudio.TestTools.UnitTesting;\n" +
            usingDirective +
            "\nnamespace " + testNamespace + ";\n\n" +
            "[TestClass]\n" +
            "public class " + targetTypeName + "GeneratedTests\n" +
            "{\n" +
            "    [TestMethod]\n" +
            "    public void " + methodName + "()\n" +
            "    {\n" +
            instanceSetup +
            methodBlock +
            tailAssert +
            "    }\n" +
            "}\n";
    }

    private static string BuildXUnitTestContent(
        string testNamespace,
        string usingDirective,
        string targetTypeName,
        string methodName,
        string ctorCall,
        string methodBlock,
        bool isStaticType)
    {
        var instanceSetup = isStaticType
            ? string.Empty
            : "        var subject = " + ctorCall + ";\n\n";
        var tailAssert = isStaticType
            ? "        Assert.True(true);\n"
            : "        Assert.NotNull(subject);\n";
        return
            "using Xunit;\n" +
            usingDirective +
            "\nnamespace " + testNamespace + ";\n\n" +
            "public class " + targetTypeName + "GeneratedTests\n" +
            "{\n" +
            "    [Fact]\n" +
            "    public void " + methodName + "()\n" +
            "    {\n" +
            instanceSetup +
            methodBlock +
            tailAssert +
            "    }\n" +
            "}\n";
    }

    private static string BuildNUnitTestContent(
        string testNamespace,
        string usingDirective,
        string targetTypeName,
        string methodName,
        string ctorCall,
        string methodBlock,
        bool isStaticType)
    {
        var instanceSetup = isStaticType
            ? string.Empty
            : "        var subject = " + ctorCall + ";\n\n";
        var tailAssert = isStaticType
            ? "        Assert.That(true, Is.True);\n"
            : "        Assert.That(subject, Is.Not.Null);\n";
        return
            "using NUnit.Framework;\n" +
            usingDirective +
            "\nnamespace " + testNamespace + ";\n\n" +
            "[TestFixture]\n" +
            "public class " + targetTypeName + "GeneratedTests\n" +
            "{\n" +
            "    [Test]\n" +
            "    public void " + methodName + "()\n" +
            "    {\n" +
            instanceSetup +
            methodBlock +
            tailAssert +
            "    }\n" +
            "}\n";
    }
}
