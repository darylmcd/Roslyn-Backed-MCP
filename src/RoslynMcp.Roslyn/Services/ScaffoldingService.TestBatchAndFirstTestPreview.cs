using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed partial class ScaffoldingService
{
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
        ValidateBatchScaffoldRequest(request);
        var context = CreateBatchScaffoldContext(workspaceId, request);
        var cachedCompilations = await LoadBatchCompilationsAsync(context.Solution, context.TestProject, ct).ConfigureAwait(false);
        var state = new BatchScaffoldState(context.Solution);

        foreach (var target in request.Targets)
        {
            ProcessBatchScaffoldTarget(target, request, context, cachedCompilations, state, ct);
        }

        return await CreateBatchScaffoldPreviewAsync(workspaceId, context.Project, state, ct).ConfigureAwait(false);
    }

    private static void ValidateBatchScaffoldRequest(ScaffoldTestBatchDto request)
    {
        if (request.Targets is null || request.Targets.Count == 0)
        {
            throw new InvalidOperationException("scaffold_test_batch_preview requires at least one target.");
        }
    }

    private BatchScaffoldContext CreateBatchScaffoldContext(string workspaceId, ScaffoldTestBatchDto request)
    {
        var project = ResolveProject(workspaceId, request.TestProjectName);
        ValidateIsTestProject(project);
        var projectDirectory = Path.GetDirectoryName(project.FilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{project.FilePath}'.");
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var testProject = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, request.TestProjectName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.FilePath, request.TestProjectName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Test project not loaded: {request.TestProjectName}");

        return new BatchScaffoldContext(
            Project: project,
            TestProject: testProject,
            Solution: solution,
            ProjectDirectory: projectDirectory,
            TestNamespace: project.Name,
            Framework: ResolveTestFramework(request.TestFramework, project.FilePath),
            NSubstituteAvailable: TestScaffoldRenderer.IsNSubstituteAvailable(testProject));
    }

    private static async Task<List<Compilation>> LoadBatchCompilationsAsync(
        Solution solution,
        Project testProject,
        CancellationToken ct)
    {
        // Cache source-project compilations once to avoid N× GetCompilationAsync across targets
        // (the primary perf win over iterating PreviewScaffoldTestAsync).
        var projectsToSearch = new List<Project> { testProject };
        foreach (var projectRef in testProject.ProjectReferences)
        {
            var referenced = solution.GetProject(projectRef.ProjectId);
            if (referenced is not null)
            {
                projectsToSearch.Add(referenced);
            }
        }

        var cachedCompilations = new List<Compilation>();
        foreach (var project in projectsToSearch)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is not null)
            {
                cachedCompilations.Add(compilation);
            }
        }

        return cachedCompilations;
    }

    private static void ProcessBatchScaffoldTarget(
        ScaffoldTestBatchTargetDto target,
        ScaffoldTestBatchDto request,
        BatchScaffoldContext context,
        IReadOnlyList<Compilation> cachedCompilations,
        BatchScaffoldState state,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(target.TargetTypeName))
        {
            state.Warnings.Add("Skipped empty target type name.");
            return;
        }

        // See PreviewScaffoldTestAsync — accept dotted FQN input, resolve via simple name,
        // and let the matched symbol supply the authoritative class identifier.
        var lookupName = TestScaffoldRenderer.StripToSimpleTypeName(target.TargetTypeName);
        var typeInfo = ResolveTargetTypeAndMethodFromCache(
            cachedCompilations,
            lookupName,
            target.TargetMethodName,
            context.NSubstituteAvailable);
        if (typeInfo.MatchedType is null)
        {
            state.Warnings.Add($"Target type '{target.TargetTypeName}' not found in referenced projects — skipped.");
            return;
        }

        var simpleTypeName = typeInfo.MatchedType.Name;
        var testFilePath = Path.Combine(context.ProjectDirectory, $"{simpleTypeName}GeneratedTests.cs");
        if (SymbolResolver.FindDocument(state.Accumulator, testFilePath) is not null || File.Exists(testFilePath))
        {
            state.Warnings.Add($"Skipped '{target.TargetTypeName}': target file already exists at '{testFilePath}'.");
            return;
        }

        if (typeInfo.Warnings is not null)
        {
            state.Warnings.AddRange(typeInfo.Warnings);
        }

        var dto = new ScaffoldTestDto(
            request.TestProjectName,
            target.TargetTypeName,
            target.TargetMethodName,
            request.TestFramework);

        // Batch scaffolding intentionally does NOT apply sibling-pattern inference — a batch
        // run typically targets a homogenous set of production types and callers want the
        // generic scaffold. Sibling inference is available via per-target scaffold_test_preview.
        var content = TestScaffoldRenderer.BuildTestContent(
            context.TestNamespace,
            dto,
            simpleTypeName,
            typeInfo.TargetNamespace,
            typeInfo.ConstructorArgs,
            context.Framework,
            typeInfo.TargetMethod,
            typeInfo.MatchedType,
            siblingPattern: null,
            isTargetInaccessible: typeInfo.IsTargetInaccessible);

        var testProject = state.Accumulator.GetProject(context.TestProject.Id)
            ?? throw new InvalidOperationException("Test project disappeared from working solution snapshot.");
        var newDocument = testProject.AddDocument(
            Path.GetFileName(testFilePath),
            Microsoft.CodeAnalysis.Text.SourceText.From(content),
            folders: [],
            filePath: testFilePath);

        state.Accumulator = newDocument.Project.Solution;
        state.CreatedFiles.Add(testFilePath);
    }

    private async Task<RefactoringPreviewDto> CreateBatchScaffoldPreviewAsync(
        string workspaceId,
        ProjectStatusDto project,
        BatchScaffoldState state,
        CancellationToken ct)
    {
        if (state.CreatedFiles.Count == 0)
        {
            throw new InvalidOperationException(
                "scaffold_test_batch_preview produced no file creations. See Warnings for per-target reasons.");
        }

        var changes = await Helpers.SolutionDiffHelper
            .ComputeChangesAsync(state.OriginalSolution, state.Accumulator, ct)
            .ConfigureAwait(false);
        var description = $"Scaffold {state.CreatedFiles.Count} test file(s) in project '{project.Name}'";
        var token = _previewStore.Store(workspaceId, state.Accumulator, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(
            token,
            description,
            changes,
            state.Warnings.Count > 0 ? state.Warnings : null);
    }

    /// <summary>
    /// Non-async variant of <see cref="ResolveTargetTypeAndMethodAsync"/> reading from a cached
    /// compilation list. Used by batch scaffold to avoid re-walking <c>GetCompilationAsync</c>
    /// per target.
    /// </summary>
    private static ResolvedTargetTypeInfo
        ResolveTargetTypeAndMethodFromCache(
            IReadOnlyList<Compilation> compilations,
            string targetTypeName,
            string? targetMethodName,
            bool nsubstituteAvailable = false)
    {
        INamedTypeSymbol? matchedType = null;
        foreach (var compilation in compilations)
        {
            var candidates = TestScaffoldRenderer.GetMatchingTargetTypeCandidates(compilation, targetTypeName, CancellationToken.None).ToList();
            if (candidates.Count == 1) { matchedType = candidates[0]; break; }
            if (candidates.Count > 1)
            {
                return TestScaffoldRenderer.CreateAmbiguousTargetTypeResult(targetTypeName);
            }
        }

        return TestScaffoldRenderer.CreateResolvedTargetTypeInfo(matchedType, targetMethodName, warnOnPrivateMethod: false, nsubstituteAvailable);
    }

    /// <summary>
    /// Previews scaffolding the FIRST <c>&lt;Service&gt;Tests.cs</c> file for a service that
    /// has no existing fixture in the destination test project. Resolves the service via
    /// fully-qualified <see cref="ScaffoldFirstTestFileDto.ServiceMetadataName"/> (so two services
    /// with the same simple name in different namespaces are unambiguous), infers the
    /// destination test project when not supplied, captures the boilerplate shape from up to
    /// three most-recently-modified <c>*Tests.cs</c> sibling fixtures, and emits a fixture with
    /// one smoke-test per public method on the service.
    /// </summary>
    public async Task<RefactoringPreviewDto> PreviewScaffoldFirstTestFileAsync(
        string workspaceId, ScaffoldFirstTestFileDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ServiceMetadataName))
            throw new InvalidOperationException("scaffold_first_test_file_preview requires a non-empty serviceMetadataName.");

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var (serviceSymbol, sourceProject) = await ResolveServiceByMetadataNameAsync(solution, request.ServiceMetadataName, ct).ConfigureAwait(false);
        if (serviceSymbol is null || sourceProject is null)
        {
            throw new InvalidOperationException(
                $"Service '{request.ServiceMetadataName}' was not found by metadata name in any loaded project. " +
                "Pass the fully-qualified type name (Namespace.TypeName).");
        }

        var simpleTypeName = serviceSymbol.Name;
        IdentifierValidation.ThrowIfInvalidIdentifier(simpleTypeName, "service type name");

        var testProject = ResolveDestinationTestProject(workspaceId, request.TestProjectName, sourceProject);
        ValidateIsTestProject(testProject);
        var projectDirectory = Path.GetDirectoryName(testProject.FilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{testProject.FilePath}'.");

        var testFilePath = Path.Combine(projectDirectory, $"{simpleTypeName}Tests.cs");
        if (File.Exists(testFilePath))
        {
            throw new InvalidOperationException(
                $"Destination file '{testFilePath}' already exists. " +
                "scaffold_first_test_file_preview is for brand-new fixtures only — use scaffold_test_preview to add tests to an existing fixture.");
        }

        var framework = ResolveTestFramework(request.TestFramework, testProject.FilePath);
        var testRoslynProject = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, testProject.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.FilePath, testProject.FilePath, StringComparison.OrdinalIgnoreCase));
        // Per scaffold-test-preview-sibling-inference-overbroad: pass the test project's
        // compilation to trim usings semantically.
        var testCompilation = testRoslynProject is null
            ? null
            : await testRoslynProject.GetCompilationAsync(ct).ConfigureAwait(false);
        var siblingInference = InferSiblingPatternFromRecent(projectDirectory, testFilePath, maxSiblings: 3, testCompilation);

        var serviceNamespace = serviceSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : serviceSymbol.ContainingNamespace.ToDisplayString();
        var nsubstituteAvailable = TestScaffoldRenderer.IsNSubstituteAvailable(testRoslynProject);
        var constructorArgs = TestScaffoldRenderer.BuildConstructorArgs(serviceSymbol, nsubstituteAvailable);
        var publicMethods = CollectPublicTestableMethods(serviceSymbol);

        var content = BuildFirstTestFileContent(
            testProject.Name,
            serviceNamespace,
            simpleTypeName,
            constructorArgs,
            publicMethods,
            framework,
            siblingInference.Pattern,
            serviceSymbol);

        var warnings = new List<string>();
        warnings.AddRange(siblingInference.Warnings);
        if (publicMethods.Count == 0)
        {
            warnings.Add(
                $"Service '{simpleTypeName}' has no public/internal instance methods to scaffold smoke tests for — emitted a single placeholder test.");
        }

        var preview = await _fileOperationService
            .PreviewCreateFileAsync(workspaceId, new CreateFileDto(testProject.Name, testFilePath, content), ct)
            .ConfigureAwait(false);

        return warnings.Count == 0 ? preview : preview with { Warnings = warnings };
    }

    private static async Task<(INamedTypeSymbol? Symbol, Project? Project)> ResolveServiceByMetadataNameAsync(
        Solution solution, string serviceMetadataName, CancellationToken ct)
    {
        // Parse the metadata name into "Namespace.Simple" pieces so we can fall back to
        // GetSymbolsWithName(simpleName) when GetTypeByMetadataName fails. The fast-path
        // (GetTypeByMetadataName) is the common case; the fallbacks handle transient
        // compilation-state gaps where the type-by-metadata-name lookup returns null for
        // a type that is still reachable via the slower symbol-enumeration path — observed
        // under fresh-load + immediate-query timing in MSBuild-backed test workspaces.
        var simpleName = serviceMetadataName.Contains('.', StringComparison.Ordinal)
            ? serviceMetadataName[(serviceMetadataName.LastIndexOf('.') + 1)..]
            : serviceMetadataName;

        // Walk every project, but accept ONLY when the symbol is declared in source within
        // that project (not merely visible via a project/metadata reference). Using
        // ContainingAssembly identity means we land on the owning project — critical for
        // ResolveDestinationTestProject which walks ProjectReferences from the OWNER.
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            // Fast path.
            var byMetadataName = compilation.GetTypeByMetadataName(serviceMetadataName);
            if (byMetadataName is { TypeKind: TypeKind.Class } &&
                SymbolEqualityComparer.Default.Equals(byMetadataName.ContainingAssembly, compilation.Assembly))
            {
                return (byMetadataName, project);
            }

            // Fallback 1: enumerate source-declared types with the simple name and pick the
            // one whose full display string matches. GetSymbolsWithName walks declared syntax
            // names, which can return results even in edge cases where the metadata-name
            // lookup has not yet published the type to the global cache.
            if (string.IsNullOrEmpty(simpleName)) continue;
            var candidates = compilation.GetSymbolsWithName(simpleName, SymbolFilter.Type, ct)
                .OfType<INamedTypeSymbol>()
                .Where(t => t.TypeKind == TypeKind.Class &&
                            SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, compilation.Assembly) &&
                            string.Equals(t.ToDisplayString(), serviceMetadataName, StringComparison.Ordinal))
                .ToList();
            if (candidates.Count == 1)
            {
                return (candidates[0], project);
            }

            // Fallback 2: walk the project's syntax trees directly for a matching class
            // declaration. Only used when both symbol-index paths miss — typically because
            // the compilation's symbol table has not yet caught up with a freshly-loaded
            // MSBuildWorkspace. Uses the SemanticModel on the tree that contains a
            // matching class declaration, which forces that tree's symbols to materialize.
            var syntaxFound = await FindByDeclaredClassSyntaxAsync(project, compilation, simpleName, serviceMetadataName, ct).ConfigureAwait(false);
            if (syntaxFound is not null)
            {
                return (syntaxFound, project);
            }
        }
        return (null, null);
    }

    private static async Task<INamedTypeSymbol?> FindByDeclaredClassSyntaxAsync(
        Project project, Compilation compilation, string simpleName, string fullMetadataName, CancellationToken ct)
    {
        foreach (var document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();
            var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            if (tree is null) continue;
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
            var classDecls = root.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                .Where(c => string.Equals(c.Identifier.ValueText, simpleName, StringComparison.Ordinal));
            foreach (var decl in classDecls)
            {
                var model = compilation.GetSemanticModel(tree);
                var symbol = model.GetDeclaredSymbol(decl, ct);
                if (symbol is INamedTypeSymbol named &&
                    string.Equals(named.ToDisplayString(), fullMetadataName, StringComparison.Ordinal))
                {
                    return named;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Picks the destination test project. When the caller supplies a name/path, we resolve
    /// directly. When omitted, we look for a project that (a) references the source production
    /// project and (b) has a project name ending in <c>.Tests</c> — the canonical convention
    /// used across this repo and most .NET solutions. Throws when no candidate is found.
    /// </summary>
    /// <remarks>
    /// When more than one candidate matches (a domain library referenced by several test
    /// projects), we apply a single conservative tiebreaker: prefer the candidate whose name
    /// is exactly <c>&lt;SourceProjectName&gt;.Tests</c> (case-sensitive). Variants like
    /// <c>MyLib.UnitTests</c> or <c>MyLib.IntegrationTests</c> are intentionally NOT matched —
    /// callers in those topologies still need to pass <c>testProjectName</c> explicitly. See
    /// initiative <c>scaffold-first-test-file-preview-single-target-heuristic</c>.
    /// </remarks>
    private ProjectStatusDto ResolveDestinationTestProject(string workspaceId, string? requestedName, Project sourceProject)
    {
        if (!string.IsNullOrWhiteSpace(requestedName))
            return ResolveProject(workspaceId, requestedName);

        var status = _workspace.GetStatus(workspaceId);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var sourceId = sourceProject.Id;

        var candidates = solution.Projects
            .Where(p => p.ProjectReferences.Any(r => r.ProjectId == sourceId))
            .Where(p => p.Name.EndsWith(".Tests", StringComparison.Ordinal))
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"Could not infer a destination test project for service in project '{sourceProject.Name}'. " +
                "No project ending in '.Tests' references this project. Pass testProjectName explicitly.");
        }

        Project picked;
        if (candidates.Count > 1)
        {
            // Suffix tiebreaker: when several test projects reference the same library, prefer
            // the one whose name follows the canonical `<Library>.Tests` convention. This
            // resolves the common topology where a domain library is referenced both by its
            // own unit-test project AND by a higher-level integration-test project — only the
            // unit-test project will follow the convention, so picking it is unambiguous.
            var expectedName = sourceProject.Name + ".Tests";
            var suffixMatches = candidates
                .Where(c => string.Equals(c.Name, expectedName, StringComparison.Ordinal))
                .ToList();

            if (suffixMatches.Count == 1)
            {
                picked = suffixMatches[0];
            }
            else
            {
                var names = string.Join(", ", candidates.Select(c => c.Name));
                throw new InvalidOperationException(
                    $"Multiple test projects reference '{sourceProject.Name}': {names}. " +
                    $"Pass testProjectName explicitly, or rename a candidate to '{expectedName}' to leverage the suffix tiebreaker.");
            }
        }
        else
        {
            picked = candidates[0];
        }

        return status.Projects.FirstOrDefault(p => string.Equals(p.Name, picked.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Inferred test project '{picked.Name}' is not in workspace status — cannot resolve file path.");
    }

    /// <summary>
    /// Returns public + internal instance methods declared on the service (excluding inherited
    /// <see cref="object"/> members, property accessors, constructors, and operators) — the set
    /// we emit smoke tests for in a first-test-file scaffold.
    /// </summary>
    private static IReadOnlyList<IMethodSymbol> CollectPublicTestableMethods(INamedTypeSymbol service)
    {
        return service.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .Where(m => !m.IsStatic)
            .Where(m => m.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .Where(m => m.AssociatedSymbol is null) // skip property/event accessors
            .ToList();
    }

    /// <summary>
    /// Inspects up to <paramref name="maxSiblings"/> most-recently-modified <c>*Tests.cs</c>
    /// fixtures in <paramref name="projectDirectory"/> and returns the pattern captured from
    /// the most recent one (used as the "primary" boilerplate template). Falls back to
    /// repo-convention defaults (returns <see cref="SiblingInferenceResult.None"/>) when no
    /// sibling fixtures exist — the per-framework emitter then writes the framework's standard
    /// boilerplate.
    /// <para>
    /// When <paramref name="compilation"/> is non-null, captured <c>using</c> directives are
    /// trimmed via semantic resolution to only those required by the captured surface. See
    /// <c>scaffold-test-preview-sibling-inference-overbroad</c>.
    /// </para>
    /// </summary>
    private static SiblingInferenceResult InferSiblingPatternFromRecent(
        string projectDirectory, string destinationFilePath, int maxSiblings, Compilation? compilation = null)
    {
        if (!Directory.Exists(projectDirectory))
            return SiblingInferenceResult.None;

        var destinationNormalized = Path.GetFullPath(destinationFilePath);
        var siblings = Directory.EnumerateFiles(projectDirectory, "*Tests.cs", SearchOption.AllDirectories)
            .Where(p =>
            {
                var normalized = Path.GetFullPath(p);
                if (string.Equals(normalized, destinationNormalized, StringComparison.OrdinalIgnoreCase))
                    return false;
                var rel = Path.GetRelativePath(projectDirectory, normalized);
                return !rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(seg => string.Equals(seg, "obj", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(seg, "bin", StringComparison.OrdinalIgnoreCase));
            })
            .Select(p => new FileInfo(p))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .Take(maxSiblings)
            .ToList();

        if (siblings.Count == 0)
            return SiblingInferenceResult.None;

        var warnings = new List<string>();
        // Use the most-recently-modified sibling as the primary pattern source. The maxSiblings
        // window above is a forward-compatible hook — today only the freshest sibling drives the
        // scaffolded shape; future revisions can union attribute lists across the window.
        try
        {
            var primary = siblings[0];
            var sourceText = File.ReadAllText(primary.FullName);
            var pattern = TestScaffoldRenderer.ExtractPatternFromSource(sourceText, primary.Name, compilation, primary.FullName);
            return new SiblingInferenceResult(pattern, warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add(
                $"Could not read sibling fixture '{siblings[0].Name}' ({ex.GetType().Name}: {ex.Message}) — scaffolded without pattern inference.");
            return new SiblingInferenceResult(null, warnings);
        }
    }

    /// <summary>
    /// Builds a brand-new <c>&lt;Service&gt;Tests.cs</c> fixture: emits the framework header,
    /// a class-level <c>ClassInitialize</c> hook (MSTest only — xUnit/NUnit get equivalent
    /// constructor/<c>OneTimeSetUp</c> patterns), a single <c>subject</c> field of the service
    /// type wired up via the inferred constructor args, and one smoke-test method per public
    /// instance method. Sibling fragments (extra usings, class attributes, base class) are
    /// layered onto the default template.
    /// </summary>
    private static string BuildFirstTestFileContent(
        string testNamespace,
        string serviceNamespace,
        string serviceTypeName,
        string constructorArgs,
        IReadOnlyList<IMethodSymbol> publicMethods,
        string framework,
        SiblingTestPattern? siblingPattern,
        INamedTypeSymbol? serviceSymbol = null)
    {
        var usingDirective = string.IsNullOrWhiteSpace(serviceNamespace)
            ? string.Empty
            : $"using {serviceNamespace};\n";

        // Collect namespaces from constructor parameter types that differ from the service's own
        // namespace — these are not captured by the single usingDirective above and would cause
        // CS0246 when the generated file is compiled as-is (scaffold-test-preview-missing-usings).
        var ctorParamUsings = TestScaffoldRenderer.BuildCtorParamUsings(serviceSymbol, serviceNamespace, testNamespace);

        var ctorCall = string.IsNullOrWhiteSpace(constructorArgs)
            ? $"new {serviceTypeName}()"
            : $"new {serviceTypeName}({constructorArgs})";

        return framework switch
        {
            "xunit" => BuildFirstTestFileXunit(testNamespace, usingDirective, ctorParamUsings, serviceTypeName, ctorCall, publicMethods, siblingPattern),
            "nunit" => BuildFirstTestFileNUnit(testNamespace, usingDirective, ctorParamUsings, serviceTypeName, ctorCall, publicMethods, siblingPattern),
            _ => BuildFirstTestFileMSTest(testNamespace, usingDirective, ctorParamUsings, serviceTypeName, ctorCall, publicMethods, siblingPattern),
        };
    }

    private static string BuildFirstTestFileMSTest(
        string testNamespace,
        string usingDirective,
        string ctorParamUsings,
        string serviceTypeName,
        string ctorCall,
        IReadOnlyList<IMethodSymbol> publicMethods,
        SiblingTestPattern? siblingPattern)
    {
        var (extraUsings, extraAttributes, baseClause, _ctorBlock) =
            TestScaffoldRenderer.BuildSiblingFragments(siblingPattern, serviceTypeName, testNamespace, usingDirective);

        // First-test-file skips the sibling ctor-block: ClassInitialize takes the role of
        // shared setup, and we don't want to inherit a fixture-injection ctor from a sibling
        // that happens to use IClassFixture<T>. The base-class clause is preserved so users
        // who base their fixtures on SharedWorkspaceTestBase keep that linkage.
        var fixtureName = serviceTypeName + "Tests";
        var sb = new System.Text.StringBuilder();
        sb.Append("using Microsoft.VisualStudio.TestTools.UnitTesting;\n");
        sb.Append(usingDirective);
        sb.Append(ctorParamUsings);
        sb.Append(extraUsings);
        sb.Append('\n');
        sb.Append("namespace ").Append(testNamespace).Append(";\n\n");
        sb.Append(extraAttributes);
        sb.Append("[TestClass]\n");
        sb.Append("public sealed class ").Append(fixtureName).Append(baseClause).Append('\n');
        sb.Append("{\n");
        sb.Append("    private static ").Append(serviceTypeName).Append("? _subject;\n\n");
        sb.Append("    [ClassInitialize]\n");
        sb.Append("    public static void ClassInit(TestContext _)\n");
        sb.Append("    {\n");
        sb.Append("        _subject = ").Append(ctorCall).Append(";\n");
        sb.Append("    }\n\n");

        if (publicMethods.Count == 0)
        {
            sb.Append("    [TestMethod]\n");
            sb.Append("    public void Subject_Is_Constructible()\n");
            sb.Append("    {\n");
            sb.Append("        Assert.IsNotNull(_subject);\n");
            sb.Append("    }\n");
        }
        else
        {
            for (var i = 0; i < publicMethods.Count; i++)
            {
                var method = publicMethods[i];
                sb.Append("    [TestMethod]\n");
                sb.Append("    public void ").Append(method.Name).Append("_Smoke_Needs_Real_Test()\n");
                sb.Append("    {\n");
                sb.Append("        Assert.IsNotNull(_subject);\n");
                sb.Append("        // TODO: invoke _subject!.").Append(method.Name).Append("(...) and assert.\n");
                sb.Append("    }\n");
                if (i < publicMethods.Count - 1) sb.Append('\n');
            }
        }

        sb.Append("}\n");
        return sb.ToString();
    }

    private static string BuildFirstTestFileXunit(
        string testNamespace,
        string usingDirective,
        string ctorParamUsings,
        string serviceTypeName,
        string ctorCall,
        IReadOnlyList<IMethodSymbol> publicMethods,
        SiblingTestPattern? siblingPattern)
    {
        var (extraUsings, extraAttributes, baseClause, _ctorBlock) =
            TestScaffoldRenderer.BuildSiblingFragments(siblingPattern, serviceTypeName, testNamespace, usingDirective);

        var fixtureName = serviceTypeName + "Tests";
        var sb = new System.Text.StringBuilder();
        sb.Append("using Xunit;\n");
        sb.Append(usingDirective);
        sb.Append(ctorParamUsings);
        sb.Append(extraUsings);
        sb.Append('\n');
        sb.Append("namespace ").Append(testNamespace).Append(";\n\n");
        sb.Append(extraAttributes);
        sb.Append("public sealed class ").Append(fixtureName).Append(baseClause).Append('\n');
        sb.Append("{\n");
        sb.Append("    private readonly ").Append(serviceTypeName).Append(" _subject;\n\n");
        sb.Append("    public ").Append(fixtureName).Append("()\n");
        sb.Append("    {\n");
        sb.Append("        _subject = ").Append(ctorCall).Append(";\n");
        sb.Append("    }\n\n");

        if (publicMethods.Count == 0)
        {
            sb.Append("    [Fact]\n");
            sb.Append("    public void Subject_Is_Constructible()\n");
            sb.Append("    {\n");
            sb.Append("        Assert.NotNull(_subject);\n");
            sb.Append("    }\n");
        }
        else
        {
            for (var i = 0; i < publicMethods.Count; i++)
            {
                var method = publicMethods[i];
                sb.Append("    [Fact]\n");
                sb.Append("    public void ").Append(method.Name).Append("_Smoke_Needs_Real_Test()\n");
                sb.Append("    {\n");
                sb.Append("        Assert.NotNull(_subject);\n");
                sb.Append("        // TODO: invoke _subject.").Append(method.Name).Append("(...) and assert.\n");
                sb.Append("    }\n");
                if (i < publicMethods.Count - 1) sb.Append('\n');
            }
        }

        sb.Append("}\n");
        return sb.ToString();
    }

    private static string BuildFirstTestFileNUnit(
        string testNamespace,
        string usingDirective,
        string ctorParamUsings,
        string serviceTypeName,
        string ctorCall,
        IReadOnlyList<IMethodSymbol> publicMethods,
        SiblingTestPattern? siblingPattern)
    {
        var (extraUsings, extraAttributes, baseClause, _ctorBlock) =
            TestScaffoldRenderer.BuildSiblingFragments(siblingPattern, serviceTypeName, testNamespace, usingDirective);

        var fixtureName = serviceTypeName + "Tests";
        var sb = new System.Text.StringBuilder();
        sb.Append("using NUnit.Framework;\n");
        sb.Append(usingDirective);
        sb.Append(ctorParamUsings);
        sb.Append(extraUsings);
        sb.Append('\n');
        sb.Append("namespace ").Append(testNamespace).Append(";\n\n");
        sb.Append(extraAttributes);
        sb.Append("[TestFixture]\n");
        sb.Append("public sealed class ").Append(fixtureName).Append(baseClause).Append('\n');
        sb.Append("{\n");
        sb.Append("    private ").Append(serviceTypeName).Append("? _subject;\n\n");
        sb.Append("    [OneTimeSetUp]\n");
        sb.Append("    public void OneTimeSetUp()\n");
        sb.Append("    {\n");
        sb.Append("        _subject = ").Append(ctorCall).Append(";\n");
        sb.Append("    }\n\n");

        if (publicMethods.Count == 0)
        {
            sb.Append("    [Test]\n");
            sb.Append("    public void Subject_Is_Constructible()\n");
            sb.Append("    {\n");
            sb.Append("        Assert.That(_subject, Is.Not.Null);\n");
            sb.Append("    }\n");
        }
        else
        {
            for (var i = 0; i < publicMethods.Count; i++)
            {
                var method = publicMethods[i];
                sb.Append("    [Test]\n");
                sb.Append("    public void ").Append(method.Name).Append("_Smoke_Needs_Real_Test()\n");
                sb.Append("    {\n");
                sb.Append("        Assert.That(_subject, Is.Not.Null);\n");
                sb.Append("        // TODO: invoke _subject!.").Append(method.Name).Append("(...) and assert.\n");
                sb.Append("    }\n");
                if (i < publicMethods.Count - 1) sb.Append('\n');
            }
        }

        sb.Append("}\n");
        return sb.ToString();
    }
}
