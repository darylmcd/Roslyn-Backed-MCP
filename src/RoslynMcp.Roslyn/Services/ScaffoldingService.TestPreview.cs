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
    public async Task<RefactoringPreviewDto> PreviewScaffoldTestAsync(
        string workspaceId,
        ScaffoldTestDto request,
        CancellationToken ct,
        ITestNameSuggestionProvider? testNameSuggestionProvider = null)
    {
        var project = ResolveProject(workspaceId, request.TestProjectName);
        ValidateIsTestProject(project);
        var projectDirectory = Path.GetDirectoryName(project.FilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{project.FilePath}'.");
        var testNamespace = project.Name;

        var framework = ResolveTestFramework(request.TestFramework, project.FilePath);

        // Accept a dotted FQN as input (callers who hit the ambiguity error get pointed at
        // "the fully qualified type name", then re-invoke with `Namespace.Type`). The resolver
        // only ever looks up the simple name, so strip to that for lookup — and treat the
        // matched symbol's Name as authoritative once we have it, so the downstream class
        // identifier is always a single identifier (dotted identifiers are a CS syntax error).
        var lookupName = StripToSimpleTypeName(request.TargetTypeName);
        var typeInfo = await ResolveTargetTypeAndMethodAsync(
            workspaceId, request.TestProjectName, lookupName, request.TargetMethodName, ct).ConfigureAwait(false);

        var simpleTypeName = typeInfo.MatchedType?.Name ?? lookupName;
        var testFilePath = Path.Combine(projectDirectory, $"{simpleTypeName}GeneratedTests.cs");

        // Sibling-pattern inference (scaffold-test-sibling-pattern-inference). When an explicit
        // referenceTestFile is supplied we use that as the pattern source; otherwise we
        // auto-detect the most-recently-modified `*Tests.cs` in the project directory. Empty
        // string opts out of inference.
        // Per scaffold-test-preview-sibling-inference-overbroad: we pass the test project's
        // compilation so usings can be trimmed to those actually referenced by the captured
        // surface (base list + ctor params). Without semantic resolution the scaffold pulls in
        // every using from the sibling fixture (typically 10+ unused imports).
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var testRoslynProject = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, project.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.FilePath, project.FilePath, StringComparison.OrdinalIgnoreCase));
        var testProjectCompilation = testRoslynProject is null
            ? null
            : await testRoslynProject.GetCompilationAsync(ct).ConfigureAwait(false);
        var siblingInference = InferSiblingTestPattern(request.ReferenceTestFile, projectDirectory, testFilePath, testProjectCompilation);
        var siblingWarnings = siblingInference.Warnings;
        var sampledTestName = await SuggestSampledTestNameAsync(
            request,
            simpleTypeName,
            typeInfo.TargetNamespace,
            typeInfo.TargetMethod,
            projectDirectory,
            testFilePath,
            testNameSuggestionProvider,
            ct).ConfigureAwait(false);

        var content = BuildTestContent(
            testNamespace, request, simpleTypeName, typeInfo.TargetNamespace, typeInfo.ConstructorArgs, framework,
            typeInfo.TargetMethod, typeInfo.MatchedType, siblingInference.Pattern, sampledTestName.MethodName,
            isTargetInaccessible: typeInfo.IsTargetInaccessible);
        var preview = await _fileOperationService.PreviewCreateFileAsync(workspaceId, new CreateFileDto(project.Name, testFilePath, content), ct).ConfigureAwait(false);

        var combinedWarnings = CombineWarnings(typeInfo.Warnings, siblingWarnings, sampledTestName.Warning);
        return combinedWarnings.Count == 0 ? preview : preview with { Warnings = combinedWarnings };
    }

    private static async Task<TestNameSuggestionResult> SuggestSampledTestNameAsync(
        ScaffoldTestDto request,
        string simpleTypeName,
        string targetNamespace,
        IMethodSymbol? targetMethod,
        string projectDirectory,
        string testFilePath,
        ITestNameSuggestionProvider? provider,
        CancellationToken ct)
    {
        if (!request.UseSampling || string.IsNullOrWhiteSpace(request.TargetMethodName))
        {
            return new TestNameSuggestionResult(null);
        }

        if (provider is null)
        {
            return new TestNameSuggestionResult(
                null,
                "useSampling was true but no sampling provider was available; emitted the deterministic placeholder test name.");
        }

        try
        {
            var context = new ScaffoldTestNameSuggestionContext(
                simpleTypeName,
                request.TargetMethodName,
                FormatMethodSignature(targetMethod),
                string.IsNullOrWhiteSpace(targetNamespace) ? null : targetNamespace,
                CollectSiblingTestMethodNames(projectDirectory, testFilePath, maxNames: 6));
            var result = await provider.SuggestTestNameAsync(context, ct).ConfigureAwait(false);
            var normalized = NormalizeSuggestedTestMethodName(result.MethodName);
            if (normalized is not null)
            {
                return result with { MethodName = normalized };
            }

            return string.IsNullOrWhiteSpace(result.MethodName)
                ? new TestNameSuggestionResult(null, result.Warning)
                : new TestNameSuggestionResult(
                    null,
                    AppendWarning(result.Warning, $"Sampled test method name '{result.MethodName}' was not a valid C# identifier; emitted the deterministic placeholder test name."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new TestNameSuggestionResult(
                null,
                $"Sampling test-name suggestion failed ({ex.GetType().Name}: {ex.Message}); emitted the deterministic placeholder test name.");
        }
    }

    private static IReadOnlyList<string> CombineWarnings(List<string>? a, IReadOnlyList<string>? b, string? c = null)
    {
        if ((a is null || a.Count == 0) && (b is null || b.Count == 0) && string.IsNullOrWhiteSpace(c))
            return Array.Empty<string>();
        var combined = new List<string>();
        if (a is not null) combined.AddRange(a);
        if (b is not null) combined.AddRange(b);
        if (!string.IsNullOrWhiteSpace(c)) combined.Add(c);
        return combined;
    }

    private static string AppendWarning(string? existingWarning, string warning)
        => string.IsNullOrWhiteSpace(existingWarning)
            ? warning
            : $"{existingWarning} {warning}";

    private async Task<ResolvedTargetTypeInfo>
        ResolveTargetTypeAndMethodAsync(
            string workspaceId, string testProjectName, string targetTypeName, string? targetMethodName, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var testProject = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, testProjectName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.FilePath, testProjectName, StringComparison.OrdinalIgnoreCase));

        if (testProject is null)
            return ResolvedTargetTypeInfo.NotFound;

        var projectsToSearch = GetProjectsToSearch(solution, testProject);
        var matchedType = await FindTargetTypeAsync(projectsToSearch, targetTypeName, ct).ConfigureAwait(false);
        var nsubstituteAvailable = IsNSubstituteAvailable(testProject);

        // scaffold-test-internal-target-accessibility: when the target type/method is internal
        // and the test assembly lacks InternalsVisibleTo, the previous output produced
        // direct `new TargetType()` / `subject.Method()` calls that fail compile with CS0122.
        // Surface this as a warning + non-applicable scaffold so callers can decide between
        // adding InternalsVisibleTo, moving the target to public surface, or scaffolding from
        // a project that already has access.
        var testCompilation = await testProject.GetCompilationAsync(ct).ConfigureAwait(false);
        var testAssembly = testCompilation?.Assembly;
        return CreateResolvedTargetTypeInfo(
            matchedType,
            targetMethodName,
            warnOnPrivateMethod: true,
            nsubstituteAvailable,
            testAssembly,
            testProject.Name);
    }

    private static List<Project> GetProjectsToSearch(Solution solution, Project testProject)
    {
        var projectsToSearch = new List<Project> { testProject };
        foreach (var projectRef in testProject.ProjectReferences)
        {
            var referencedProject = solution.GetProject(projectRef.ProjectId);
            if (referencedProject is not null)
            {
                projectsToSearch.Add(referencedProject);
            }
        }

        return projectsToSearch;
    }

    private static async Task<INamedTypeSymbol?> FindTargetTypeAsync(
        IReadOnlyList<Project> projectsToSearch,
        string targetTypeName,
        CancellationToken ct)
    {
        foreach (var project in projectsToSearch)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            var candidates = GetMatchingTargetTypeCandidates(compilation, targetTypeName, ct).ToList();
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (candidates.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Ambiguous type name '{targetTypeName}' — found in multiple namespaces: " +
                    string.Join(", ", candidates.Select(c => c.ToDisplayString())) +
                    ". Use the fully qualified type name.");
            }
        }

        return null;
    }

    /// <summary>
    /// Per scaffold-test-sibling-pattern-inference: infers the boilerplate shape from a
    /// sibling <c>*Tests.cs</c> fixture and returns a <see cref="SiblingInferenceResult"/>.
    /// When an explicit <paramref name="referenceTestFile"/> is supplied we use that as the
    /// pattern source; empty string opts out of inference. Otherwise the most-recently-modified
    /// <c>*Tests.cs</c> in <paramref name="projectDirectory"/> is auto-detected.
    /// <para>
    /// When <paramref name="compilation"/> is non-null, captured <c>using</c> directives are
    /// trimmed via semantic resolution to only those required by the captured surface (base
    /// types + constructor parameter types). See <c>scaffold-test-preview-sibling-inference-overbroad</c>.
    /// </para>
    /// </summary>
    internal static SiblingInferenceResult InferSiblingTestPattern(
        string? referenceTestFile,
        string projectDirectory,
        string destinationFilePath,
        Compilation? compilation = null)
    {
        // Explicit opt-out: empty string means "do not infer".
        if (referenceTestFile is not null && string.IsNullOrWhiteSpace(referenceTestFile))
            return SiblingInferenceResult.None;

        string? resolved;
        var warnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(referenceTestFile))
        {
            if (!File.Exists(referenceTestFile))
            {
                warnings.Add(
                    $"referenceTestFile '{referenceTestFile}' not found on disk — falling back to auto-detection.");
                resolved = FindMostRecentSiblingTestFile(projectDirectory, destinationFilePath);
            }
            else
            {
                resolved = referenceTestFile;
            }
        }
        else
        {
            resolved = FindMostRecentSiblingTestFile(projectDirectory, destinationFilePath);
        }

        if (resolved is null)
            return new SiblingInferenceResult(null, warnings);

        try
        {
            var sourceText = File.ReadAllText(resolved);
            var pattern = ExtractPatternFromSource(sourceText, Path.GetFileName(resolved), compilation, resolved);
            return new SiblingInferenceResult(pattern, warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add(
                $"Could not read referenceTestFile '{resolved}' ({ex.GetType().Name}: {ex.Message}) — scaffolded without pattern inference.");
            return new SiblingInferenceResult(null, warnings);
        }
    }

    private static string? FindMostRecentSiblingTestFile(string projectDirectory, string destinationFilePath)
    {
        if (!Directory.Exists(projectDirectory))
            return null;

        // Only consider files ending in *Tests.cs (the canonical convention that scaffold
        // itself produces). Exclude the destination so we never self-reference, and skip
        // obj/bin subdirectories so we don't pick up generated files.
        var destinationNormalized = Path.GetFullPath(destinationFilePath);
        return Directory.EnumerateFiles(projectDirectory, "*Tests.cs", SearchOption.AllDirectories)
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
            .Select(fi => fi.FullName)
            .FirstOrDefault();
    }

    /// <summary>
    /// Roslyn-parses the sibling source file, picks the first top-level class declaration
    /// (ignoring nested types), and captures: class-level attribute lists, base type list,
    /// constructor parameters (for <c>IClassFixture&lt;T&gt;</c> / fixture injection),
    /// and any <c>using</c>s we'll need to carry over so the scaffolded file compiles.
    /// Returns <c>null</c> when the file isn't parseable or has no class declaration.
    /// <para>
    /// Per <c>scaffold-test-preview-sibling-inference-overbroad</c>: when a non-null
    /// <paramref name="compilation"/> is supplied, the captured <c>using</c> set is trimmed
    /// to only those required to resolve identifiers actually referenced in the captured
    /// surface (base list + constructor parameter types) — semantic resolution is performed
    /// against the test project's compilation. Without this trim the scaffold pulls in every
    /// <c>using</c> from the sibling fixture (typically 10+ unused imports when the MRU
    /// sibling is a Playwright / Selenium fixture).
    /// </para>
    /// </summary>
    private static SiblingTestPattern? ExtractPatternFromSource(
        string sourceText,
        string sourceFileName,
        Compilation? compilation,
        string? sourceFilePath)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetCompilationUnitRoot();

        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword))
                              || c.Modifiers.Count == 0);
        if (classDecl is null)
            return null;

        var attributes = classDecl.AttributeLists
            .Select(list => list.ToString().Trim())
            .Where(a => a.Length > 0)
            .ToList();

        var baseTypes = classDecl.BaseList?.Types
            .Select(t => t.Type.ToString().Trim())
            .Where(t => t.Length > 0)
            .ToList() ?? new List<string>();

        // The first instance constructor is our pattern source. Static constructors are skipped.
        var ctor = classDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault(c => !c.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)));

        var parameters = new List<(string TypeText, string Name)>();
        if (ctor is not null)
        {
            foreach (var p in ctor.ParameterList.Parameters)
            {
                var typeText = p.Type?.ToString().Trim() ?? string.Empty;
                var name = p.Identifier.ValueText;
                if (!string.IsNullOrEmpty(typeText) && !string.IsNullOrEmpty(name))
                    parameters.Add((typeText, name));
            }
        }

        // Collect usings so the scaffolded file can compile. We deliberately do NOT
        // replicate the sibling's namespace declaration — the scaffolder already picks the
        // correct namespace for the new file from the target test project.
        var allUsings = root.Usings
            .Select(u => u.Name?.ToString().Trim() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();

        // Per scaffold-test-preview-sibling-inference-overbroad: trim usings to those
        // actually required to resolve identifiers in the captured surface. When a Compilation
        // is supplied AND the sibling syntax tree is part of it, we use semantic resolution to
        // determine the required namespaces. Otherwise we fall back to keeping all usings (the
        // pre-fix behavior) so callers without a Compilation don't silently lose usings.
        var trimmedUsings = TrimUsingsToReferencedNamespaces(
            classDecl, ctor, allUsings, compilation, sourceFilePath);

        return new SiblingTestPattern(attributes, baseTypes, parameters, trimmedUsings, sourceFileName);
    }

    /// <summary>
    /// Per scaffold-test-preview-sibling-inference-overbroad: returns the subset of
    /// <paramref name="allUsings"/> that semantic resolution proves are required by identifier
    /// references inside the captured surface — the class declaration's <c>BaseList</c> and the
    /// constructor's parameter type list. When no Compilation is supplied (or the sibling's
    /// syntax tree isn't part of it), returns <paramref name="allUsings"/> unchanged so
    /// callers without semantic context don't silently drop required imports.
    /// </summary>
    private static IReadOnlyList<string> TrimUsingsToReferencedNamespaces(
        ClassDeclarationSyntax classDecl,
        ConstructorDeclarationSyntax? ctor,
        IReadOnlyList<string> allUsings,
        Compilation? compilation,
        string? sourceFilePath)
    {
        if (compilation is null || allUsings.Count == 0)
            return allUsings;

        // Locate the syntax tree in the compilation. Match by absolute file path. If the
        // sibling source isn't part of the compilation (e.g. a referenceTestFile pointing
        // outside the loaded workspace), we conservatively keep all usings.
        SyntaxTree? siblingTree = null;
        if (!string.IsNullOrEmpty(sourceFilePath))
        {
            var fullPath = Path.GetFullPath(sourceFilePath);
            siblingTree = compilation.SyntaxTrees.FirstOrDefault(t =>
                !string.IsNullOrEmpty(t.FilePath) &&
                string.Equals(Path.GetFullPath(t.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));
        }

        if (siblingTree is null)
            return allUsings;

        var semanticModel = compilation.GetSemanticModel(siblingTree);

        // Re-find the class declaration in the compilation's tree (the parsed-from-text tree
        // we extracted above is a different syntax tree instance; semantic model needs the
        // tree owned by the compilation).
        var siblingRoot = siblingTree.GetCompilationUnitRoot();
        var compilationClassDecl = siblingRoot.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => string.Equals(c.Identifier.ValueText, classDecl.Identifier.ValueText, StringComparison.Ordinal)
                              && c.SpanStart == classDecl.SpanStart);
        if (compilationClassDecl is null)
            return allUsings;

        // Collect every identifier-name reference in the captured surface (BaseList + ctor
        // parameter types). Resolve each to a symbol via the semantic model and pull its
        // ContainingNamespace. The resulting set of namespace strings is what the scaffolded
        // file needs to bring into scope to compile.
        var referencedNamespaces = new HashSet<string>(StringComparer.Ordinal);

        var surfaceNodes = new List<SyntaxNode>();
        if (compilationClassDecl.BaseList is not null)
            surfaceNodes.AddRange(compilationClassDecl.BaseList.Types.Select(t => t.Type));

        var compilationCtor = compilationClassDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault(c => !c.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)));
        if (compilationCtor is not null)
        {
            foreach (var p in compilationCtor.ParameterList.Parameters)
            {
                if (p.Type is not null)
                    surfaceNodes.Add(p.Type);
            }
        }

        foreach (var node in surfaceNodes)
        {
            // For each type-reference node (TypeSyntax), resolve every IdentifierNameSyntax /
            // GenericNameSyntax descendant. This catches both the outer type and any generic
            // type arguments (e.g. IClassFixture<CustomWebApplicationFactory> resolves both
            // IClassFixture and CustomWebApplicationFactory).
            foreach (var nameNode in node.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
            {
                var typeInfo = semanticModel.GetTypeInfo(nameNode).Type
                    ?? semanticModel.GetSymbolInfo(nameNode).Symbol as ITypeSymbol;
                if (typeInfo is null)
                    continue;
                var ns = typeInfo.ContainingNamespace;
                if (ns is null || ns.IsGlobalNamespace)
                    continue;
                var nsString = ns.ToDisplayString();
                if (!string.IsNullOrEmpty(nsString))
                    referencedNamespaces.Add(nsString);
            }
        }

        // Keep only usings whose name matches one of the referenced namespaces. Framework
        // usings are also filtered downstream in BuildSiblingFragments — the trim here just
        // narrows the set to namespaces semantic resolution proved are needed.
        return allUsings.Where(u => referencedNamespaces.Contains(u)).ToList();
    }

    private static string BuildTestContent(
        string testNamespace,
        ScaffoldTestDto request,
        string simpleTypeName,
        string targetNamespace,
        string constructorArgs,
        string framework,
        IMethodSymbol? targetMethod,
        INamedTypeSymbol? matchedType,
        SiblingTestPattern? siblingPattern,
        string? suggestedMethodName = null,
        bool isTargetInaccessible = false)
    {
        var methodName = string.IsNullOrWhiteSpace(request.TargetMethodName)
            ? "Generated_Test"
            : suggestedMethodName ?? $"{request.TargetMethodName}_Needs_Test";

        var usingDirective = string.IsNullOrWhiteSpace(targetNamespace)
            ? string.Empty
            : $"using {targetNamespace};\n";

        // Collect namespaces from constructor parameter types that differ from the target's own
        // namespace — these are not captured by the single usingDirective above and would cause
        // CS0246 when the generated file is compiled as-is (scaffold-test-preview-missing-usings).
        var ctorParamUsings = BuildCtorParamUsings(matchedType, targetNamespace, testNamespace);

        var useStaticScaffold = ShouldUseStaticTestScaffold(matchedType);

        // scaffold-test-internal-target-accessibility: when the target is internal-not-visible
        // (or any containing type is private/internal-not-visible), skip the direct-call shape
        // — we cannot synthesize compiling code without the caller setting up
        // InternalsVisibleTo. Emit an Inconclusive/placeholder body and rely on the
        // typeInfo.Warnings entry to explain the choice.
        var ctorCall = (isTargetInaccessible || useStaticScaffold)
            ? string.Empty
            : string.IsNullOrWhiteSpace(constructorArgs)
                ? $"new {simpleTypeName}()"
                : $"new {simpleTypeName}({constructorArgs})";

        var methodTargetBlock = isTargetInaccessible
            ? BuildInaccessibleTargetPlaceholderBlock(framework, simpleTypeName, request.TargetMethodName)
            : BuildMethodTargetInvocationBlock(
                framework, simpleTypeName, request.TargetMethodName, targetMethod, useStaticScaffold);

        // When target is inaccessible, suppress the `var subject = new T(...);` setup line
        // — there is no callable type. The framework-specific `isStaticType=true` branch
        // already produces a body that doesn't reference `subject`, so we route through it.
        var suppressInstanceSubject = useStaticScaffold || isTargetInaccessible;

        return framework switch
        {
            "xunit" => BuildXUnitTestContent(testNamespace, usingDirective, ctorParamUsings, simpleTypeName, methodName, ctorCall, methodTargetBlock, suppressInstanceSubject, siblingPattern),
            "nunit" => BuildNUnitTestContent(testNamespace, usingDirective, ctorParamUsings, simpleTypeName, methodName, ctorCall, methodTargetBlock, suppressInstanceSubject, siblingPattern),
            _ => BuildMSTestTestContent(testNamespace, usingDirective, ctorParamUsings, simpleTypeName, methodName, ctorCall, methodTargetBlock, suppressInstanceSubject, siblingPattern),
        };
    }

    /// <summary>
    /// scaffold-test-internal-target-accessibility: emits a placeholder body when the target
    /// type/method is not visible to the test assembly. Replaces what would have been
    /// <c>subject.M()</c> or <c>Type.M()</c> calls — those would compile-fail with CS0122.
    /// The accompanying warning (see <see cref="BuildInaccessibleTypeWarning"/> /
    /// <see cref="BuildInaccessibleMethodWarning"/>) explains the choice and points at
    /// <c>InternalsVisibleTo</c>.
    /// </summary>
    private static string BuildInaccessibleTargetPlaceholderBlock(string framework, string targetTypeName, string? targetMethodName)
    {
        var inconclusiveCall = framework switch
        {
            "xunit" => "Assert.Fail(\"" + InaccessibleTargetAssertReason(targetTypeName, targetMethodName) + "\");",
            "nunit" => "Assert.Inconclusive(\"" + InaccessibleTargetAssertReason(targetTypeName, targetMethodName) + "\");",
            _ => "Assert.Inconclusive(\"" + InaccessibleTargetAssertReason(targetTypeName, targetMethodName) + "\");",
        };
        var methodFragment = string.IsNullOrWhiteSpace(targetMethodName)
            ? string.Empty
            : "." + targetMethodName;
        return
            $"        // Target '{targetTypeName}{methodFragment}' is not visible to this test assembly\n" +
            "        // (declared accessibility forbids a direct call). Add InternalsVisibleTo, expose\n" +
            "        // the target publicly, or scaffold from a project that already has access.\n" +
            "        " + inconclusiveCall + "\n";
    }

    private static string InaccessibleTargetAssertReason(string targetTypeName, string? targetMethodName)
    {
        var methodFragment = string.IsNullOrWhiteSpace(targetMethodName)
            ? string.Empty
            : "." + targetMethodName;
        return $"Scaffolded test for '{targetTypeName}{methodFragment}' is non-applicable: target is not accessible from this test assembly. See preview warnings for guidance.";
    }

    private static string? FormatMethodSignature(IMethodSymbol? method)
    {
        if (method is null)
        {
            return null;
        }

        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type.ToMinimalDisplay()} {p.Name}"));
        return $"{method.ReturnType.ToMinimalDisplay()} {method.Name}({parameters})";
    }

    private static IReadOnlyList<string> CollectSiblingTestMethodNames(
        string projectDirectory,
        string destinationFilePath,
        int maxNames)
    {
        if (!Directory.Exists(projectDirectory))
        {
            return Array.Empty<string>();
        }

        var destinationNormalized = Path.GetFullPath(destinationFilePath);
        var names = new List<string>();
        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*Tests.cs", SearchOption.AllDirectories)
                     .Where(p =>
                     {
                         var normalized = Path.GetFullPath(p);
                         if (string.Equals(normalized, destinationNormalized, StringComparison.OrdinalIgnoreCase))
                         {
                             return false;
                         }

                         var rel = Path.GetRelativePath(projectDirectory, normalized);
                         return !rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             .Any(seg => string.Equals(seg, "obj", StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(seg, "bin", StringComparison.OrdinalIgnoreCase));
                     })
                     .Select(p => new FileInfo(p))
                     .OrderByDescending(fi => fi.LastWriteTimeUtc))
        {
            try
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file.FullName));
                var root = tree.GetRoot();
                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (!LooksLikeTestMethod(method))
                    {
                        continue;
                    }

                    names.Add(method.Identifier.Text);
                    if (names.Count >= maxNames)
                    {
                        return names;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return names;
    }

    private static bool LooksLikeTestMethod(MethodDeclarationSyntax method)
        => method.AttributeLists
            .SelectMany(static list => list.Attributes)
            .Select(static attr => attr.Name.ToString())
            .Any(static name =>
                name.Contains("TestMethod", StringComparison.Ordinal) ||
                name.Contains("Test", StringComparison.Ordinal) ||
                name.Contains("Fact", StringComparison.Ordinal) ||
                name.Contains("Theory", StringComparison.Ordinal));

    private static string? NormalizeSuggestedTestMethodName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        var candidate = rawName
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => !line.StartsWith("```", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        candidate = candidate.Trim('`', '"', '\'', ';', ' ');
        var colon = candidate.LastIndexOf(':');
        if (colon >= 0 && colon < candidate.Length - 1)
        {
            candidate = candidate[(colon + 1)..].Trim();
        }
        if (candidate.EndsWith("()", StringComparison.Ordinal))
        {
            candidate = candidate[..^2].Trim();
        }
        var paren = candidate.IndexOf('(');
        if (paren > 0)
        {
            candidate = candidate[..paren].Trim();
        }
        var parts = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 0)
        {
            candidate = parts[^1];
        }

        try
        {
            IdentifierValidation.ThrowIfInvalidIdentifier(candidate, "sampled test method name");
            return candidate;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Renders the sibling-inferred class-level attribute lists and base-list / constructor
    /// pieces on top of the default scaffold. Framework-level attributes (<c>[TestClass]</c>,
    /// <c>[TestFixture]</c>) are injected by the per-framework builders; this helper only
    /// replicates attributes that appear on the sibling class decl so cross-cutting
    /// conventions (e.g. <c>[Trait("Category","Integration")]</c>) carry over. Returns all
    /// three render fragments ready for string-interpolation into the per-framework emitter.
    /// </summary>
    private static (string ExtraUsings, string ExtraAttributes, string BaseClause, string CtorBlock) BuildSiblingFragments(
        SiblingTestPattern? pattern,
        string targetTypeName,
        string testNamespace,
        string usingDirective)
    {
        if (pattern is null)
            return (string.Empty, string.Empty, string.Empty, string.Empty);

        // Carry sibling usings, but skip any already present in the explicit target-type using
        // directive, the test namespace itself, or matching the sibling's own namespace (we
        // don't try to resolve that — we just carry the explicit usings list). The
        // duplication guard uses a simple string comparison against the scaffold's
        // already-emitted using so callers see a clean file.
        var existingUsings = new HashSet<string>(StringComparer.Ordinal) { testNamespace };
        if (!string.IsNullOrWhiteSpace(usingDirective))
        {
            // "using Foo.Bar;\n" → "Foo.Bar"
            var candidate = usingDirective.Trim();
            if (candidate.StartsWith("using ", StringComparison.Ordinal) && candidate.EndsWith(";", StringComparison.Ordinal))
                existingUsings.Add(candidate[6..^1]);
        }

        var extraUsingsBuilder = new System.Text.StringBuilder();
        foreach (var u in pattern.RequiredUsings.Where(u => existingUsings.Add(u)))
        {
            // Emit using with a newline so the header block stays consistent with the default
            // scaffold style. Skip framework-specific usings (Xunit / MSTest / NUnit) — the
            // per-framework emitters inject those explicitly.
            if (IsFrameworkUsing(u))
                continue;
            extraUsingsBuilder.Append("using ").Append(u).Append(";\n");
        }

        var attributesBuilder = new System.Text.StringBuilder();
        foreach (var attr in pattern.ClassAttributes.Where(a => !IsFrameworkClassAttribute(a)))
        {
            attributesBuilder.Append(attr).Append('\n');
        }

        var baseClause = pattern.BaseTypes.Count > 0
            ? " : " + string.Join(", ", pattern.BaseTypes)
            : string.Empty;

        var ctorBlock = BuildCtorFromPattern(pattern, targetTypeName);

        return (extraUsingsBuilder.ToString(), attributesBuilder.ToString(), baseClause, ctorBlock);
    }

    private static bool IsFrameworkUsing(string ns) =>
        string.Equals(ns, "Xunit", StringComparison.Ordinal) ||
        string.Equals(ns, "NUnit.Framework", StringComparison.Ordinal) ||
        string.Equals(ns, "Microsoft.VisualStudio.TestTools.UnitTesting", StringComparison.Ordinal);

    /// <summary>
    /// Returns <c>true</c> when the captured class-level attribute should be filtered from
    /// the scaffolded output. The blocklist covers two distinct categories:
    /// <list type="bullet">
    ///   <item><description>Framework class markers (<c>[TestClass]</c>, <c>[TestFixture]</c>) —
    ///   the per-framework emitter unconditionally injects the canonical attribute, so carrying
    ///   it from the sibling would double-emit it.</description></item>
    ///   <item><description>Convention-tagging attributes (<c>[Trait]</c>, <c>[Category]</c>,
    ///   <c>[TestCategory]</c>, <c>[Collection]</c>) — these classify the sibling fixture into a
    ///   test bucket (e.g. <c>[Trait("Category","Playwright")]</c>) and over-broadly leak that
    ///   classification onto an unrelated scaffold target. Per the
    ///   <c>scaffold-test-preview-sibling-inference-overbroad</c> initiative, these attributes
    ///   should NOT carry across — sibling-inference is narrowed to file-scoped namespace +
    ///   base-class-name shape only.</description></item>
    /// </list>
    /// </summary>
    private static bool IsFrameworkClassAttribute(string attribute)
    {
        // Strip the surrounding brackets and any argument list so bare-name matching works.
        var inner = attribute.Trim();
        if (inner.StartsWith('[')) inner = inner[1..];
        if (inner.EndsWith(']')) inner = inner[..^1];
        var parenIdx = inner.IndexOf('(');
        if (parenIdx >= 0) inner = inner[..parenIdx];
        inner = inner.Trim();
        // Framework class markers — the per-framework emitter always injects the canonical
        // attribute, so we filter the sibling's copy to avoid double-emit.
        if (string.Equals(inner, "TestClass", StringComparison.Ordinal)
            || string.Equals(inner, "TestFixture", StringComparison.Ordinal))
            return true;
        // Convention-tagging attributes — carrying these from a sibling fixture leaks the
        // sibling's bucket classification (e.g. Playwright/Integration/UI) onto an unrelated
        // scaffold target. Filter aggressively.
        return string.Equals(inner, "Trait", StringComparison.Ordinal)
            || string.Equals(inner, "Category", StringComparison.Ordinal)
            || string.Equals(inner, "TestCategory", StringComparison.Ordinal)
            || string.Equals(inner, "Collection", StringComparison.Ordinal);
    }

    /// <summary>
    /// Emits a constructor block that accepts the same parameter shape as the sibling and
    /// stores each argument in a `private readonly` field. Follows the xUnit
    /// <c>IClassFixture&lt;T&gt;</c> / DI-injected-fixture convention seen in ASP.NET Core
    /// integration test projects. When the sibling has no constructor, emits an empty block.
    /// </summary>
    private static string BuildCtorFromPattern(SiblingTestPattern pattern, string targetTypeName)
    {
        if (pattern.ConstructorParameters.Count == 0)
            return string.Empty;

        var fields = new System.Text.StringBuilder();
        var assigns = new System.Text.StringBuilder();
        var ctorParams = new List<string>();

        foreach (var (typeText, name) in pattern.ConstructorParameters)
        {
            var fieldName = "_" + name;
            fields.Append($"    private readonly {typeText} {fieldName};\n");
            assigns.Append($"        {fieldName} = {name};\n");
            ctorParams.Add($"{typeText} {name}");
        }

        return
            fields.ToString() + "\n" +
            $"    public {targetTypeName}GeneratedTests({string.Join(", ", ctorParams)})\n" +
            "    {\n" +
            assigns.ToString() +
            "    }\n\n";
    }

    /// <summary>
    /// BUG-N10: static classes, or instance classes whose only public API is static members (utility types
    /// — e.g. <c>TenantConstants</c> with only <c>public const</c> fields, or <c>SnapshotContentHasher</c>
    /// with only static methods/properties), should not scaffold <c>new T()</c> + instance assertions.
    /// Considers methods, properties, fields (including consts), and events so types whose only surface
    /// is static state still skip the <c>new T()</c> template.
    /// </summary>
    private static bool ShouldUseStaticTestScaffold(INamedTypeSymbol? matchedType)
    {
        if (matchedType is null)
            return false;
        if (matchedType.IsStatic)
            return true;

        // Only consider user-authored members — the implicitly-declared default ctor and compiler-emitted
        // backing fields shouldn't tip the scales. Accessors of properties/events are tracked via their
        // owning property/event (we treat the accessor methods as part of that member, not separately).
        var candidateMembers = matchedType.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared)
            .Where(m => m.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field or SymbolKind.Event)
            .Where(m => m is not IMethodSymbol method ||
                        method.MethodKind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation)
            .ToList();

        static bool IsInstanceVisible(ISymbol m) =>
            !m.IsStatic &&
            m.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.Protected;

        static bool IsStaticVisible(ISymbol m) =>
            m.IsStatic &&
            m.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal;

        var hasVisibleInstance = candidateMembers.Any(IsInstanceVisible);
        var hasVisibleStatic = candidateMembers.Any(IsStaticVisible);

        return !hasVisibleInstance && hasVisibleStatic;
    }

    private static string BuildMethodTargetInvocationBlock(
        string framework,
        string targetTypeName,
        string? targetMethodName,
        IMethodSymbol? targetMethod,
        bool useStaticScaffold)
    {
        // Phase 1: input gate — bail out on missing/unresolved target.
        if (string.IsNullOrWhiteSpace(targetMethodName))
        {
            return "        // No target method specified.\n";
        }

        if (targetMethod is null)
        {
            return $"        // Target method '{targetMethodName}' was not resolved on {targetTypeName}.\n";
        }

        // Phase 2: static-only scaffold (utility-class shape) — emit `Type.Method()`.
        if (useStaticScaffold && targetMethod.IsStatic)
        {
            return BuildStaticInvocation(targetTypeName, targetMethodName, targetMethod);
        }

        // Phase 3: private accessibility — reflection-based invocation OR an
        // unreachable-from-static-scaffold comment.
        if (targetMethod.DeclaredAccessibility == Accessibility.Private)
        {
            return BuildPrivateReflectionInvocation(framework, targetTypeName, targetMethodName, targetMethod, useStaticScaffold);
        }

        // Phase 4: ordinary public/internal instance method — emit `subject.Method()`.
        return BuildInstanceInvocation(targetMethodName, targetMethod);
    }

    /// <summary>
    /// Static-scaffold branch: <c>useStaticScaffold &amp;&amp; targetMethod.IsStatic</c>.
    /// Emits a parameterless static call, capturing the return value when non-void.
    /// </summary>
    private static string BuildStaticInvocation(
        string targetTypeName,
        string targetMethodName,
        IMethodSymbol targetMethod)
    {
        if (targetMethod.Parameters.Length == 0 && !targetMethod.ReturnsVoid)
            return $"        _ = {targetTypeName}.{targetMethodName}();\n";
        if (targetMethod.Parameters.Length == 0 && targetMethod.ReturnsVoid)
            return $"        {targetTypeName}.{targetMethodName}();\n";
        return $"        // Add arguments for static method '{targetMethodName}'.\n";
    }

    /// <summary>
    /// Private-method branch: reflection invocation with framework-specific not-null
    /// assertion. When the scaffold is static-only and the method is a private instance
    /// member, return an explanatory comment instead — the scaffold has no <c>subject</c>
    /// instance to invoke against.
    /// </summary>
    private static string BuildPrivateReflectionInvocation(
        string framework,
        string targetTypeName,
        string targetMethodName,
        IMethodSymbol targetMethod,
        bool useStaticScaffold)
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

    /// <summary>
    /// Public/internal instance-method branch. Emits <c>subject.Method()</c> for
    /// parameterless methods (capturing the return value when non-void) or a
    /// commented-out example for methods that take parameters.
    /// </summary>
    private static string BuildInstanceInvocation(string targetMethodName, IMethodSymbol targetMethod)
    {
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
        string ctorParamUsings,
        string targetTypeName,
        string methodName,
        string ctorCall,
        string methodBlock,
        bool isStaticType,
        SiblingTestPattern? siblingPattern)
    {
        var (extraUsings, extraAttributes, baseClause, ctorBlock) =
            BuildSiblingFragments(siblingPattern, targetTypeName, testNamespace, usingDirective);
        var instanceSetup = isStaticType
            ? string.Empty
            : "        var subject = " + ctorCall + ";\n\n";
        var tailAssert = isStaticType
            ? "        Assert.IsTrue(true);\n"
            : "        Assert.IsNotNull(subject);\n";
        return
            "using Microsoft.VisualStudio.TestTools.UnitTesting;\n" +
            usingDirective +
            ctorParamUsings +
            extraUsings +
            "\nnamespace " + testNamespace + ";\n\n" +
            extraAttributes +
            "[TestClass]\n" +
            "public class " + targetTypeName + "GeneratedTests" + baseClause + "\n" +
            "{\n" +
            ctorBlock +
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
        string ctorParamUsings,
        string targetTypeName,
        string methodName,
        string ctorCall,
        string methodBlock,
        bool isStaticType,
        SiblingTestPattern? siblingPattern)
    {
        var (extraUsings, extraAttributes, baseClause, ctorBlock) =
            BuildSiblingFragments(siblingPattern, targetTypeName, testNamespace, usingDirective);
        var instanceSetup = isStaticType
            ? string.Empty
            : "        var subject = " + ctorCall + ";\n\n";
        var tailAssert = isStaticType
            ? "        Assert.True(true);\n"
            : "        Assert.NotNull(subject);\n";
        return
            "using Xunit;\n" +
            usingDirective +
            ctorParamUsings +
            extraUsings +
            "\nnamespace " + testNamespace + ";\n\n" +
            extraAttributes +
            "public class " + targetTypeName + "GeneratedTests" + baseClause + "\n" +
            "{\n" +
            ctorBlock +
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
        string ctorParamUsings,
        string targetTypeName,
        string methodName,
        string ctorCall,
        string methodBlock,
        bool isStaticType,
        SiblingTestPattern? siblingPattern)
    {
        var (extraUsings, extraAttributes, baseClause, ctorBlock) =
            BuildSiblingFragments(siblingPattern, targetTypeName, testNamespace, usingDirective);
        var instanceSetup = isStaticType
            ? string.Empty
            : "        var subject = " + ctorCall + ";\n\n";
        var tailAssert = isStaticType
            ? "        Assert.That(true, Is.True);\n"
            : "        Assert.That(subject, Is.Not.Null);\n";
        return
            "using NUnit.Framework;\n" +
            usingDirective +
            ctorParamUsings +
            extraUsings +
            "\nnamespace " + testNamespace + ";\n\n" +
            extraAttributes +
            "[TestFixture]\n" +
            "public class " + targetTypeName + "GeneratedTests" + baseClause + "\n" +
            "{\n" +
            ctorBlock +
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
