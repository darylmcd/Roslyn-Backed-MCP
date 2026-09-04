using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Single-test-scaffolding collaborator extracted from <see cref="ScaffoldingService"/>: owns the
/// <c>scaffold_test</c> preview path — single-target type/method resolution, sibling-pattern
/// inference, and sampled-test-name selection. Content rendering (target resolution, constructor
/// arguments, framework rendering, project-file inspection) lives in the shared
/// <see cref="TestScaffoldRenderer"/> so the batch/first-test-file flows can consume it directly
/// without routing through this single-test collaborator. Constructed inline by the
/// <see cref="ScaffoldingService"/> facade (not DI-registered); its dependencies are
/// <see cref="IWorkspaceManager"/> and <see cref="IFileOperationService"/>. The facade resolves
/// and validates the project and resolves the framework before delegating, so this collaborator
/// carries no <see cref="Microsoft.Extensions.Logging.ILogger"/> (the only logging paths —
/// <c>ValidateIsTestProject</c> / <c>DetectTestFrameworkFromProjectFile</c> — stay on the facade).
/// </summary>
internal sealed class SingleTestScaffolder
{
    // Sampled content is untrusted client input. Bound it before parsing so a crafted MRTR retry
    // cannot amplify an arbitrarily large legal identifier into generated source or preview output.
    private const int _maxSuggestedTestMethodNameLength = 256;

    private readonly IWorkspaceManager _workspace;
    private readonly IFileOperationService _fileOperationService;
    private readonly IUnexpectedExceptionReporter? _exceptionReporter;
    private readonly Func<string, string> _readAllText;

    public SingleTestScaffolder(
        IWorkspaceManager workspace,
        IFileOperationService fileOperationService,
        IUnexpectedExceptionReporter? exceptionReporter = null,
        Func<string, string>? readAllText = null)
    {
        _workspace = workspace;
        _fileOperationService = fileOperationService;
        _exceptionReporter = exceptionReporter;
        _readAllText = readAllText ?? File.ReadAllText;
    }

    public async Task<RefactoringPreviewDto> PreviewAsync(
        ProjectStatusDto project,
        string framework,
        string workspaceId,
        ScaffoldTestDto request,
        CancellationToken ct,
        ITestNameSuggestionProvider? testNameSuggestionProvider = null)
    {
        var projectDirectory = Path.GetDirectoryName(project.FilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{project.FilePath}'.");
        var testNamespace = project.Name;

        // Accept a dotted FQN as input (callers who hit the ambiguity error get pointed at
        // "the fully qualified type name", then re-invoke with `Namespace.Type`). The resolver
        // only ever looks up the simple name, so strip to that for lookup — and treat the
        // matched symbol's Name as authoritative once we have it, so the downstream class
        // identifier is always a single identifier (dotted identifiers are a CS syntax error).
        var lookupName = TestScaffoldRenderer.StripToSimpleTypeName(request.TargetTypeName);

        // scaffold-sampling-mrtr-replay-cost: the sampling exchange runs FIRST, ahead of every
        // semantic step below. Its provider may terminate this leg with a protocol input-required
        // signal, after which the client replays the whole tools/call — so anything resolved before
        // the request is resolved again on every replay. Hoisted here, the initial leg builds only
        // the syntactic prompt context and the retry leg consumes the answer without rebuilding it,
        // leaving project resolution, compilation and sibling inference paid exactly once.
        var (sampledTestName, solution) = await SuggestSampledTestNameAsync(
            request, lookupName, workspaceId, project, projectDirectory, testNameSuggestionProvider, ct).ConfigureAwait(false);

        // The initial MRTR leg obtains a snapshot for the syntax-only ambiguity preflight, then
        // terminates while requesting input. The replay skips that preflight and obtains exactly
        // one snapshot here, which is reused by target resolution and sibling compilation.
        solution ??= _workspace.GetCurrentSolution(workspaceId);

        var typeInfo = await ResolveTargetTypeAndMethodAsync(
            solution, request.TestProjectName, lookupName, request.TargetMethodName, ct).ConfigureAwait(false);

        var simpleTypeName = typeInfo.MatchedType?.Name ?? lookupName;
        var testFilePath = Path.Combine(projectDirectory, GeneratedTestFileName(simpleTypeName));

        // Sibling-pattern inference (scaffold-test-sibling-pattern-inference). When an explicit
        // referenceTestFile is supplied we use that as the pattern source; otherwise we
        // auto-detect the most-recently-modified `*Tests.cs` in the project directory. Empty
        // string opts out of inference.
        // Per scaffold-test-preview-sibling-inference-overbroad: we pass the test project's
        // compilation so usings can be trimmed to those actually referenced by the captured
        // surface (base list + ctor params). Without semantic resolution the scaffold pulls in
        // every using from the sibling fixture (typically 10+ unused imports).
        var testRoslynProject = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, project.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.FilePath, project.FilePath, StringComparison.OrdinalIgnoreCase));
        var testProjectCompilation = testRoslynProject is null
            ? null
            : await testRoslynProject.GetCompilationAsync(ct).ConfigureAwait(false);
        var siblingInference = InferSiblingTestPattern(request.ReferenceTestFile, projectDirectory, testFilePath, testProjectCompilation);
        var siblingWarnings = siblingInference.Warnings;

        var content = TestScaffoldRenderer.BuildTestContent(new BuildTestContentRequest(
            testNamespace, request, simpleTypeName, typeInfo.TargetNamespace, typeInfo.ConstructorArgs, framework,
            typeInfo.TargetMethod, typeInfo.MatchedType, siblingInference.Pattern, sampledTestName.MethodName,
            IsTargetInaccessible: typeInfo.IsTargetInaccessible));
        var preview = await _fileOperationService.PreviewCreateFileAsync(workspaceId, new CreateFileDto(project.Name, testFilePath, content), ct).ConfigureAwait(false);

        var combinedWarnings = CombineWarnings(typeInfo.Warnings, siblingWarnings, sampledTestName.Warning);
        return combinedWarnings.Count == 0 ? preview : preview with { Warnings = combinedWarnings };
    }

    /// <summary>
    /// Runs the opt-in sampling exchange before any semantic resolution. On a transport whose
    /// exchange spans several replays of one logical call, the provider answers
    /// <see cref="ITestNameSuggestionProvider.TryConsumePendingSuggestion"/> on the retry leg, so
    /// the sibling enumerate-and-parse below is paid only on the leg that actually sends a prompt.
    /// </summary>
    private async Task<(TestNameSuggestionResult Suggestion, Solution? Solution)> SuggestSampledTestNameAsync(
        ScaffoldTestDto request,
        string lookupTypeName,
        string workspaceId,
        ProjectStatusDto project,
        string projectDirectory,
        ITestNameSuggestionProvider? provider,
        CancellationToken ct)
    {
        if (!request.UseSampling || string.IsNullOrWhiteSpace(request.TargetMethodName))
        {
            return (new TestNameSuggestionResult(null), null);
        }

        if (provider is null)
        {
            return (new TestNameSuggestionResult(
                null,
                "useSampling was true but no sampling provider was available; emitted the deterministic placeholder test name."), null);
        }

        if (provider.TryConsumePendingSuggestion(out var pending))
        {
            return (NormalizeSuggestion(pending), null);
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        await ThrowIfTargetTypeIsSyntacticallyAmbiguousAsync(
            solution, project.Name, project.FilePath, lookupTypeName, ct).ConfigureAwait(false);

        // Syntactic inputs only: this runs ahead of symbol resolution, so anything the compilation
        // would supply is out of reach here by design — see ScaffoldTestNameSuggestionContext.
        var siblingNames = CollectSiblingTestMethodNames(
            projectDirectory,
            Path.Combine(projectDirectory, GeneratedTestFileName(lookupTypeName)),
            maxNames: 6);
        var context = new ScaffoldTestNameSuggestionContext(
            lookupTypeName,
            request.TargetMethodName,
            siblingNames.Names);
        var suggestion = NormalizeSuggestion(
            await provider.SuggestTestNameAsync(context, ct).ConfigureAwait(false));
        if (!string.IsNullOrWhiteSpace(siblingNames.Warning))
        {
            suggestion = suggestion with
            {
                Warning = AppendWarning(suggestion.Warning, siblingNames.Warning),
            };
        }

        return (
            suggestion,
            solution);
    }

    /// <summary>
    /// Refuses a guaranteed-ambiguous target before the first sampling request. The probe inspects
    /// the loaded solution's cached syntax trees without requesting a compilation. A replay
    /// carrying an answered suggestion skips this method through
    /// <see cref="ITestNameSuggestionProvider.TryConsumePendingSuggestion"/>.
    /// </summary>
    private static async Task ThrowIfTargetTypeIsSyntacticallyAmbiguousAsync(
        Solution solution,
        string testProjectName,
        string testProjectFilePath,
        string targetTypeName,
        CancellationToken ct)
    {
        var candidates = await FindSyntacticTargetTypeCandidatesAsync(
            solution, testProjectName, testProjectFilePath, targetTypeName, ct).ConfigureAwait(false);
        if (candidates is { Count: > 1 })
        {
            throw CreateAmbiguousTargetTypeException(targetTypeName, candidates);
        }
    }

    private static async Task<IReadOnlyList<string>> FindSyntacticTargetTypeCandidatesAsync(
        Solution solution,
        string testProjectName,
        string testProjectFilePath,
        string targetTypeName,
        CancellationToken ct)
    {
        var testProject = solution.Projects.FirstOrDefault(project =>
            string.Equals(project.Name, testProjectName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(project.FilePath, testProjectFilePath, StringComparison.OrdinalIgnoreCase));
        if (testProject is null)
        {
            return [];
        }

        // Match FindTargetTypeAsync exactly: inspect the test project, then each direct
        // reference in project-reference order, and stop at the first project with any matches.
        // Documents and their cached syntax trees come from this same loaded Solution snapshot,
        // so stale-policy and evaluated-property differences cannot make the preflight disagree
        // with the authoritative resolver that follows on the same logical call.
        foreach (var project in GetProjectsToSearch(solution, testProject))
        {
            ct.ThrowIfCancellationRequested();
            var candidates = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var document in project.Documents)
            {
                ct.ThrowIfCancellationRequested();
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is null)
                {
                    continue;
                }

                foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (declaration.Kind() is not (SyntaxKind.ClassDeclaration or SyntaxKind.StructDeclaration or
                            SyntaxKind.RecordDeclaration or SyntaxKind.RecordStructDeclaration) ||
                        !string.Equals(declaration.Identifier.ValueText, targetTypeName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var (identity, display) = GetQualifiedTypeNames(declaration);
                    candidates[identity] = display;
                }
            }

            if (candidates.Count > 0)
            {
                return candidates.Values.Order(StringComparer.Ordinal).ToArray();
            }
        }

        return [];
    }

    private static (string Identity, string Display) GetQualifiedTypeNames(TypeDeclarationSyntax declaration)
    {
        var namespaceSegments = declaration.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(static namespaceDeclaration => namespaceDeclaration.Name.ToString())
            .ToArray();
        var containingTypes = declaration.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Reverse()
            .ToArray();
        var display = string.Join('.', namespaceSegments
            .Concat(containingTypes.Select(static containingType => containingType.Identifier.ValueText))
            .Append(declaration.Identifier.ValueText));
        var identity = string.Join('.', namespaceSegments
            .Concat(containingTypes.Select(GetMetadataTypeName))
            .Append(GetMetadataTypeName(declaration)));
        return (identity, display);
    }

    private static string GetMetadataTypeName(TypeDeclarationSyntax declaration) =>
        declaration.TypeParameterList is { Parameters.Count: > 0 } typeParameters
            ? $"{declaration.Identifier.ValueText}`{typeParameters.Parameters.Count}"
            : declaration.Identifier.ValueText;

    private static string GeneratedTestFileName(string simpleTypeName) => $"{simpleTypeName}GeneratedTests.cs";

    private static TestNameSuggestionResult NormalizeSuggestion(TestNameSuggestionResult result)
    {
        var normalized = NormalizeSuggestedTestMethodName(result.MethodName);
        if (normalized is not null)
        {
            return result with { MethodName = normalized };
        }

        return string.IsNullOrWhiteSpace(result.MethodName)
            ? new TestNameSuggestionResult(null, result.Warning)
            : new TestNameSuggestionResult(
                null,
                AppendWarning(
                    result.Warning,
                    "The sampled test method name was invalid or exceeded the supported length; " +
                    "emitted the deterministic placeholder test name."));
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
            Solution solution, string testProjectName, string targetTypeName, string? targetMethodName, CancellationToken ct)
    {
        var testProject = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, testProjectName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.FilePath, testProjectName, StringComparison.OrdinalIgnoreCase));

        if (testProject is null)
            return ResolvedTargetTypeInfo.NotFound;

        var projectsToSearch = GetProjectsToSearch(solution, testProject);
        var matchedType = await FindTargetTypeAsync(projectsToSearch, targetTypeName, ct).ConfigureAwait(false);
        var nsubstituteAvailable = TestScaffoldRenderer.IsNSubstituteAvailable(testProject);

        // scaffold-test-internal-target-accessibility: when the target type/method is internal
        // and the test assembly lacks InternalsVisibleTo, the previous output produced
        // direct `new TargetType()` / `subject.Method()` calls that fail compile with CS0122.
        // Surface this as a warning + non-applicable scaffold so callers can decide between
        // adding InternalsVisibleTo, moving the target to public surface, or scaffolding from
        // a project that already has access.
        var testCompilation = await testProject.GetCompilationAsync(ct).ConfigureAwait(false);
        var testAssembly = testCompilation?.Assembly;
        return TestScaffoldRenderer.CreateResolvedTargetTypeInfo(
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

            var candidates = TestScaffoldRenderer.GetMatchingTargetTypeCandidates(compilation, targetTypeName, ct).ToList();
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (candidates.Count > 1)
            {
                throw CreateAmbiguousTargetTypeException(
                    targetTypeName,
                    candidates.Select(static candidate => candidate.ToDisplayString()));
            }
        }

        return null;
    }

    private static InvalidOperationException CreateAmbiguousTargetTypeException(
        string targetTypeName,
        IEnumerable<string> candidates) =>
        new(
            $"Ambiguous type name '{targetTypeName}' — found in multiple namespaces: " +
            string.Join(", ", candidates) +
            ". Use the fully qualified type name.");

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
    private SiblingInferenceResult InferSiblingTestPattern(
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
                    "referenceTestFile was not found on disk — falling back to auto-detection.");
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
            var sourceText = _readAllText(resolved);
            var pattern = TestScaffoldRenderer.ExtractPatternFromSource(sourceText, Path.GetFileName(resolved), compilation, resolved);
            return new SiblingInferenceResult(pattern, warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add(ScaffoldingReadFailurePolicy.CreateWarning(
                _exceptionReporter,
                ex,
                "referenceTestFile"));
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

    private SiblingTestMethodNameCollection CollectSiblingTestMethodNames(
        string projectDirectory,
        string destinationFilePath,
        int maxNames)
    {
        if (!Directory.Exists(projectDirectory))
        {
            return SiblingTestMethodNameCollection.Empty;
        }

        var destinationNormalized = Path.GetFullPath(destinationFilePath);
        var names = new List<string>();
        string? warning = null;
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
                var tree = CSharpSyntaxTree.ParseText(_readAllText(file.FullName));
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
                        return new SiblingTestMethodNameCollection(names, warning);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warning ??= ScaffoldingReadFailurePolicy.CreateSiblingNameDiscoveryWarning(
                    _exceptionReporter,
                    ex);
            }
        }

        return new SiblingTestMethodNameCollection(names, warning);
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
        if (string.IsNullOrWhiteSpace(rawName) || rawName.Length > _maxSuggestedTestMethodNameLength)
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
}

/// <summary>
/// Shared test-scaffolding renderer extracted alongside <see cref="SingleTestScaffolder"/>. Owns
/// the content-rendering surface consumed by BOTH the single-test flow (<see cref="SingleTestScaffolder"/>)
/// and the batch/first-test-file flows on the <see cref="ScaffoldingService"/> facade: target-type
/// resolution and accessibility gating, constructor-argument synthesis, sibling-pattern extraction,
/// and per-framework (MSTest/xUnit/NUnit) test-file rendering. Kept as a stateless static class so
/// batch code consumes it directly rather than routing through the single-test collaborator.
/// </summary>
internal static class TestScaffoldRenderer
{
    internal static IEnumerable<INamedTypeSymbol> GetMatchingTargetTypeCandidates(
        Compilation compilation,
        string targetTypeName,
        CancellationToken ct)
    {
        return compilation.GetSymbolsWithName(targetTypeName, SymbolFilter.Type, ct)
            .OfType<INamedTypeSymbol>()
            .Where(t => t.TypeKind is TypeKind.Class or TypeKind.Struct &&
                        string.Equals(t.Name, targetTypeName, StringComparison.Ordinal));
    }

    internal static ResolvedTargetTypeInfo CreateResolvedTargetTypeInfo(
        INamedTypeSymbol? matchedType,
        string? targetMethodName,
        bool warnOnPrivateMethod,
        bool nsubstituteAvailable = false,
        IAssemblySymbol? testAssembly = null,
        string? testProjectName = null)
    {
        if (matchedType is null)
        {
            return ResolvedTargetTypeInfo.NotFound;
        }

        var targetNamespace = matchedType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : matchedType.ContainingNamespace.ToDisplayString();
        var warnings = new List<string>();

        // scaffold-test-internal-target-accessibility: gate constructor + method invocation
        // synthesis when the matched target is not visible to the test assembly. We still
        // resolve the matched type and method symbols so the warning can name them, but skip
        // emitting `new T(...)` / `subject.M()` text that would compile-fail with CS0122.
        var typeInaccessible = testAssembly is not null
            && !IsAccessibleFromAssembly(matchedType, testAssembly);

        var constructorArgs = typeInaccessible
            ? string.Empty
            : BuildConstructorArgs(matchedType, nsubstituteAvailable);

        var targetMethod = ResolveTargetMethod(matchedType, targetMethodName, warnOnPrivateMethod, warnings);

        // Note: private methods on otherwise-accessible types have their own dedicated
        // scaffold path via BuildPrivateReflectionInvocation — do NOT redirect them through
        // the inaccessible-target placeholder. Only flag method-level inaccessibility for the
        // internal-not-visible case where direct call AND reflection both fail.
        var methodInaccessible = !typeInaccessible
            && testAssembly is not null
            && targetMethod is not null
            && targetMethod.DeclaredAccessibility != Accessibility.Private
            && !IsAccessibleFromAssembly(targetMethod, testAssembly);

        if (typeInaccessible)
        {
            warnings.Add(BuildInaccessibleTypeWarning(matchedType, testProjectName));
        }
        else if (methodInaccessible)
        {
            warnings.Add(BuildInaccessibleMethodWarning(matchedType, targetMethod!, testProjectName));
        }

        return new ResolvedTargetTypeInfo(
            targetNamespace,
            constructorArgs,
            targetMethod,
            warnings.Count == 0 ? null : warnings,
            matchedType,
            IsTargetInaccessible: typeInaccessible || methodInaccessible);
    }

    /// <summary>
    /// Returns true when <paramref name="symbol"/>'s declared accessibility (and every
    /// containing-type accessibility) permits a reference from <paramref name="callerAssembly"/>.
    /// Internal symbols are accessible cross-assembly only when the defining assembly grants
    /// <c>InternalsVisibleTo(<see cref="IAssemblySymbol.Name"/>)</c>. Private symbols are
    /// never cross-assembly accessible — callers reach them via reflection (handled separately
    /// in the private-method scaffold path).
    /// </summary>
    private static bool IsAccessibleFromAssembly(ISymbol symbol, IAssemblySymbol callerAssembly)
    {
        // Walk up containers: a public method on an internal-not-visible class is still
        // unreachable from the caller's assembly.
        for (ISymbol? current = symbol; current is not null; current = current.ContainingType)
        {
            switch (current.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    break;
                case Accessibility.Internal:
                case Accessibility.ProtectedAndInternal:
                    if (!IsInternalAccessibleFromAssembly(current.ContainingAssembly, callerAssembly))
                    {
                        return false;
                    }
                    break;
                case Accessibility.Protected:
                case Accessibility.ProtectedOrInternal:
                    // Protected requires an inheritance relationship the scaffold cannot
                    // synthesize from the test assembly; treat as inaccessible for the
                    // direct-call path (private-method reflection branch covers reflection).
                    return false;
                case Accessibility.Private:
                    // Private symbols handled by the private-method reflection branch in
                    // BuildMethodTargetInvocationBlock; any private *containing type* makes
                    // the target unreachable.
                    return false;
                default:
                    return false;
            }

            // Stop once we have walked past namespace-level types.
            if (current.ContainingType is null)
            {
                break;
            }
        }

        return true;
    }

    private static bool IsInternalAccessibleFromAssembly(IAssemblySymbol? definingAssembly, IAssemblySymbol callerAssembly)
    {
        if (definingAssembly is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(definingAssembly, callerAssembly))
        {
            return true;
        }

        return definingAssembly.GivesAccessTo(callerAssembly);
    }

    private static string BuildInaccessibleTypeWarning(INamedTypeSymbol type, string? testProjectName)
    {
        var typeDisplay = type.ToDisplayString();
        var assemblyName = type.ContainingAssembly?.Name ?? "the target assembly";
        var testProjectFragment = string.IsNullOrWhiteSpace(testProjectName) ? "the test project" : $"'{testProjectName}'";
        return
            $"Target type '{typeDisplay}' is not accessible from {testProjectFragment} (declared accessibility: {type.DeclaredAccessibility}). " +
            $"Generated scaffold uses placeholders rather than direct calls. Add `[assembly: InternalsVisibleTo(\"{testProjectName ?? "TestProject"}\")]` " +
            $"to assembly '{assemblyName}', expose the type publicly, or scaffold from a project with access.";
    }

    private static string BuildInaccessibleMethodWarning(INamedTypeSymbol type, IMethodSymbol method, string? testProjectName)
    {
        var typeDisplay = type.ToDisplayString();
        var assemblyName = type.ContainingAssembly?.Name ?? "the target assembly";
        var testProjectFragment = string.IsNullOrWhiteSpace(testProjectName) ? "the test project" : $"'{testProjectName}'";
        return
            $"Target method '{typeDisplay}.{method.Name}' is not accessible from {testProjectFragment} (declared accessibility: {method.DeclaredAccessibility}). " +
            $"Generated scaffold uses a placeholder rather than a direct call. Add `[assembly: InternalsVisibleTo(\"{testProjectName ?? "TestProject"}\")]` " +
            $"to assembly '{assemblyName}', expose the method publicly, or scaffold from a project with access.";
    }

    internal static ResolvedTargetTypeInfo CreateAmbiguousTargetTypeResult(string targetTypeName)
    {
        return new ResolvedTargetTypeInfo(
            string.Empty,
            string.Empty,
            null,
            [$"Ambiguous type '{targetTypeName}' — multiple candidates; skipped."],
            null);
    }

    private static IMethodSymbol? ResolveTargetMethod(
        INamedTypeSymbol matchedType,
        string? targetMethodName,
        bool warnOnPrivateMethod,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(targetMethodName))
        {
            return null;
        }

        var targetMethod = matchedType.GetMembers(targetMethodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.MethodKind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation);

        if (targetMethod is null)
        {
            warnings.Add($"Target method '{targetMethodName}' was not found on type '{matchedType.Name}'.");
            return null;
        }

        if (warnOnPrivateMethod && targetMethod.DeclaredAccessibility == Accessibility.Private)
        {
            warnings.Add(
                $"Target method '{targetMethodName}' is private — the scaffold uses reflection to invoke it; " +
                "prefer InternalsVisibleTo or testing via public API when possible.");
        }

        return targetMethod;
    }

    internal static string BuildConstructorArgs(INamedTypeSymbol type, bool nsubstituteAvailable = false)
    {
        var constructors = type.Constructors
            .Where(c => !c.IsImplicitlyDeclared || c.Parameters.Length == 0)
            .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .ToList();

        if (constructors.Count == 0)
            return string.Empty;

        // Prefer a parameterless ctor when one exists — `new T()` always compiles and is the
        // shape callers expect for POCOs. When no parameterless ctor is accessible (the
        // DI-registered-service case this fix targets — `NamespaceRelocationService` and
        // similar expose a single ctor(IFoo, IBar, …)), fall through to the widest accessible
        // ctor and synthesize per-param placeholders below
        // (scaffold-test-preview-ctor-arg-stubs).
        var bestCtor = constructors.FirstOrDefault(c => c.Parameters.Length == 0)
            ?? constructors.OrderByDescending(c => c.Parameters.Length).First();
        if (bestCtor.Parameters.Length == 0)
            return string.Empty;

        var args = bestCtor.Parameters.Select(p =>
            $"{BuildArgExpression(p.Type, nsubstituteAvailable)} /* {p.Name} */");
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
    internal static string BuildArgExpression(ITypeSymbol parameterType, bool nsubstituteAvailable = false)
    {
        var displayName = parameterType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var constructibleDisplayName = parameterType
            .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        if (parameterType.SpecialType == SpecialType.System_String)
        {
            return "string.Empty";
        }

        return TryBuildCollectionArgExpression(parameterType)
            ?? TryBuildDictionaryArgExpression(parameterType)
            ?? BuildInterfaceOrAbstractArgExpression(parameterType, displayName, constructibleDisplayName, nsubstituteAvailable)
            ?? BuildConcreteArgExpression(parameterType, displayName, constructibleDisplayName, nsubstituteAvailable)
            ?? $"default({displayName})";
    }

    /// <summary>
    /// Empty collection interfaces (<c>IEnumerable&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>,
    /// <c>IReadOnlyCollection&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>) get
    /// <c>Array.Empty&lt;T&gt;()</c>. Returns <c>null</c> when the type is not one of those families.
    /// </summary>
    private static string? TryBuildCollectionArgExpression(ITypeSymbol parameterType)
    {
        if (parameterType is not INamedTypeSymbol named || !named.IsGenericType)
        {
            return null;
        }

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

        return null;
    }

    /// <summary>
    /// Dictionary interfaces (<c>IDictionary&lt;K,V&gt;</c>, <c>IReadOnlyDictionary&lt;K,V&gt;</c>)
    /// get <c>new Dictionary&lt;K,V&gt;()</c>. Returns <c>null</c> when the type is not a dictionary.
    /// </summary>
    private static string? TryBuildDictionaryArgExpression(ITypeSymbol parameterType)
    {
        if (parameterType is not INamedTypeSymbol named || !named.IsGenericType)
        {
            return null;
        }

        var openGenericName = named.ConstructedFrom.ToDisplayString();
        if (openGenericName is "System.Collections.Generic.IDictionary<TKey, TValue>"
            or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
        {
            var keyType = named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var valueType = named.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return $"new System.Collections.Generic.Dictionary<{keyType}, {valueType}>()";
        }

        return null;
    }

    /// <summary>
    /// Interfaces and abstract classes cannot be instantiated via <c>default(T)</c> in a way that
    /// produces a usable collaborator — <c>default</c> gives null and the first call throws NRE.
    /// When the test project references NSubstitute we emit <c>Substitute.For&lt;T&gt;()</c>;
    /// otherwise we emit a TODO placeholder so the caller notices and supplies a real/faked
    /// instance. Returns <c>null</c> when the type is neither an interface nor an abstract class.
    /// </summary>
    private static string? BuildInterfaceOrAbstractArgExpression(
        ITypeSymbol parameterType, string displayName, string constructibleDisplayName, bool nsubstituteAvailable)
    {
        if (parameterType.TypeKind == TypeKind.Interface ||
            (parameterType.TypeKind == TypeKind.Class && parameterType.IsAbstract))
        {
            return nsubstituteAvailable
                ? $"NSubstitute.Substitute.For<{constructibleDisplayName}>()"
                : $"default({displayName})! /* TODO: provide a test double for {displayName} */";
        }

        return null;
    }

    /// <summary>
    /// Concrete class with an accessible parameterless ctor → safe to <c>new T()</c>. A concrete
    /// class WITHOUT a parameterless ctor can't be safely constructed, so it emits a
    /// <c>Substitute.For&lt;T&gt;()</c> or a TODO placeholder (previously emitted <c>default(T)</c>
    /// silently). Returns <c>null</c> for anything that is not a concrete class (e.g. structs),
    /// leaving the caller's terminal <c>default(T)</c> fallback in charge.
    /// </summary>
    private static string? BuildConcreteArgExpression(
        ITypeSymbol parameterType, string displayName, string constructibleDisplayName, bool nsubstituteAvailable)
    {
        if (parameterType is not INamedTypeSymbol concrete ||
            concrete.TypeKind != TypeKind.Class ||
            concrete.IsAbstract)
        {
            return null;
        }

        if (HasAccessibleParameterlessCtor(concrete))
        {
            return $"new {constructibleDisplayName}()";
        }

        return nsubstituteAvailable
            ? $"NSubstitute.Substitute.For<{constructibleDisplayName}>()"
            : $"default({displayName})! /* TODO: provide a test double for {displayName} */";
    }

    private static bool HasAccessibleParameterlessCtor(INamedTypeSymbol type)
    {
        // A class with NO declared instance ctors has an implicit parameterless ctor.
        var instanceCtors = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .ToList();
        if (instanceCtors.Count == 0) return false;
        return instanceCtors.Any(c => c.Parameters.Length == 0);
    }

    /// <summary>
    /// Detects whether the given Roslyn project has NSubstitute on its reference graph, either
    /// as a direct <c>PackageReference</c> or brought in transitively via a project reference
    /// (e.g. a shared test-infra project). Uses MetadataReferences so transitive closure is
    /// handled by MSBuild's existing resolution — covers both cases the plan calls out
    /// (test project references AND the target test project's NuGet graph).
    /// </summary>
    internal static bool IsNSubstituteAvailable(Project? testProject)
    {
        if (testProject is null) return false;
        foreach (var reference in testProject.MetadataReferences)
        {
            if (reference.Display is null) continue;
            var fileName = Path.GetFileNameWithoutExtension(reference.Display);
            if (string.Equals(fileName, "NSubstitute", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Builds a block of <c>using</c> directives for namespaces introduced by constructor
    /// parameter types of <paramref name="typeSymbol"/> — namespaces that are NOT already
    /// covered by <paramref name="typeNamespace"/> or <paramref name="testNamespace"/>.
    /// Returns an empty string when no additional namespaces are needed.
    /// This fixes scaffold-test-preview-missing-usings: previously only the service type's own
    /// namespace was emitted, leaving any ctor-parameter namespaces as unresolved CS0246 errors.
    /// </summary>
    internal static string BuildCtorParamUsings(
        INamedTypeSymbol? typeSymbol,
        string? typeNamespace,
        string? testNamespace)
    {
        if (typeSymbol is null)
            return string.Empty;

        var bestCtor = typeSymbol.Constructors
            .Where(c => !c.IsImplicitlyDeclared || c.Parameters.Length == 0)
            .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        if (bestCtor is null || bestCtor.Parameters.Length == 0)
            return string.Empty;

        var excluded = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(typeNamespace)) excluded.Add(typeNamespace);
        if (!string.IsNullOrWhiteSpace(testNamespace)) excluded.Add(testNamespace);
        // Always exclude well-known root namespaces that don't need using directives.
        excluded.Add("System");

        var paramNamespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in bestCtor.Parameters)
            ScaffoldingService.CollectNamespaces(p.Type, paramNamespaces);

        var sb = new System.Text.StringBuilder();
        foreach (var ns in paramNamespaces
            .Where(n => !excluded.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal))
        {
            sb.Append("using ").Append(ns).Append(";\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Strip a dotted input (e.g. <c>"SampleLib.Hierarchy.Circle"</c>) to its last identifier
    /// segment so it can be used both as a lookup key against <see cref="Compilation.GetSymbolsWithName"/>
    /// (which indexes on the simple name) and as a C# identifier in scaffolded output. Callers
    /// sometimes arrive here with a fully-qualified name because the ambiguity-resolution
    /// error message suggests "use the fully qualified type name" — without this strip, the
    /// dotted input would flow into the class-name template and produce a CS syntax error.
    /// </summary>
    internal static string StripToSimpleTypeName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;
        var lastDot = input.LastIndexOf('.');
        return lastDot < 0 ? input : input[(lastDot + 1)..];
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
    internal static SiblingTestPattern? ExtractPatternFromSource(
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

        var siblingTree = TryResolveSiblingSyntaxTree(compilation, sourceFilePath);
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

        var surfaceNodes = CollectSurfaceTypeNodes(compilationClassDecl);
        var referencedNamespaces = ResolveReferencedNamespaces(semanticModel, surfaceNodes);

        // Keep only usings whose name matches one of the referenced namespaces. Framework
        // usings are also filtered downstream in BuildSiblingFragments — the trim here just
        // narrows the set to namespaces semantic resolution proved are needed.
        return allUsings.Where(u => referencedNamespaces.Contains(u)).ToList();
    }

    /// <summary>
    /// Locates the sibling source file's syntax tree inside <paramref name="compilation"/> by
    /// absolute file path. Returns <c>null</c> when no path is supplied or the sibling source
    /// isn't part of the compilation (e.g. a referenceTestFile pointing outside the loaded
    /// workspace), in which case the caller conservatively keeps all usings.
    /// </summary>
    private static SyntaxTree? TryResolveSiblingSyntaxTree(Compilation compilation, string? sourceFilePath)
    {
        if (string.IsNullOrEmpty(sourceFilePath))
            return null;

        var fullPath = Path.GetFullPath(sourceFilePath);
        return compilation.SyntaxTrees.FirstOrDefault(t =>
            !string.IsNullOrEmpty(t.FilePath) &&
            string.Equals(Path.GetFullPath(t.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Collects the captured surface's type-reference nodes: the class declaration's
    /// <c>BaseList</c> types plus the first instance constructor's parameter types. These are
    /// the nodes semantic resolution walks to determine which namespaces the scaffold needs.
    /// </summary>
    private static List<SyntaxNode> CollectSurfaceTypeNodes(ClassDeclarationSyntax compilationClassDecl)
    {
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

        return surfaceNodes;
    }

    /// <summary>
    /// Resolves every identifier-name reference in the captured <paramref name="surfaceNodes"/>
    /// to a symbol via <paramref name="semanticModel"/> and pulls its <c>ContainingNamespace</c>.
    /// This catches both the outer type and any generic type arguments (e.g.
    /// <c>IClassFixture&lt;CustomWebApplicationFactory&gt;</c> resolves both
    /// <c>IClassFixture</c> and <c>CustomWebApplicationFactory</c>). The resulting set of
    /// namespace strings is what the scaffolded file needs to bring into scope to compile.
    /// </summary>
    private static HashSet<string> ResolveReferencedNamespaces(SemanticModel semanticModel, List<SyntaxNode> surfaceNodes)
    {
        var referencedNamespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in surfaceNodes)
        {
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

        return referencedNamespaces;
    }

    internal static string BuildTestContent(BuildTestContentRequest content)
    {
        var (testNamespace, request, simpleTypeName, targetNamespace, constructorArgs, framework,
            targetMethod, matchedType, siblingPattern, suggestedMethodName, isTargetInaccessible) = content;

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

    /// <summary>
    /// Renders the sibling-inferred class-level attribute lists and base-list / constructor
    /// pieces on top of the default scaffold. Framework-level attributes (<c>[TestClass]</c>,
    /// <c>[TestFixture]</c>) are injected by the per-framework builders; this helper only
    /// replicates attributes that appear on the sibling class decl so cross-cutting
    /// conventions (e.g. <c>[Trait("Category","Integration")]</c>) carry over. Returns all
    /// three render fragments ready for string-interpolation into the per-framework emitter.
    /// </summary>
    internal static (string ExtraUsings, string ExtraAttributes, string BaseClause, string CtorBlock) BuildSiblingFragments(
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

/// <summary>
/// Resolution result for a scaffold-test target type + method: the target namespace,
/// synthesized constructor arguments, resolved method symbol, any warnings, the matched type,
/// and whether the target is inaccessible from the test assembly. Shared DTO consumed by the
/// single-test flow (<see cref="SingleTestScaffolder"/>) and the batch flow on
/// <see cref="ScaffoldingService"/> — moved out of <c>ScaffoldingService</c> during the
/// single-test collaborator extraction.
/// </summary>
internal sealed record ResolvedTargetTypeInfo(
    string TargetNamespace,
    string ConstructorArgs,
    IMethodSymbol? TargetMethod,
    List<string>? Warnings,
    INamedTypeSymbol? MatchedType,
    bool IsTargetInaccessible = false)
{
    public static ResolvedTargetTypeInfo NotFound { get; } = new(string.Empty, string.Empty, null, null, null);
}

/// <summary>
/// Captured shape of a sibling test class: attributes decorating the class declaration,
/// optional base class, and constructor-injected fixture parameters (the xUnit
/// <c>IClassFixture&lt;T&gt;</c> pattern is detected by inspecting the constructor
/// parameter list). Rendered verbatim onto the scaffolded class so integration-test
/// conventions (ASP.NET Core <c>IClassFixture&lt;CustomWebApplicationFactory&gt;</c>,
/// <c>[Trait("Category", "Integration")]</c>, etc.) replicate without a manual rewrite.
/// Shared DTO consumed by both the single-test and batch/first-test-file scaffolding flows.
/// </summary>
internal sealed record SiblingTestPattern(
    IReadOnlyList<string> ClassAttributes,
    IReadOnlyList<string> BaseTypes,
    IReadOnlyList<(string TypeText, string Name)> ConstructorParameters,
    IReadOnlyList<string> RequiredUsings,
    string SourceFileName);

/// <summary>
/// Result of sibling-pattern inference: a pattern (null when no reference is available)
/// and any warnings the caller should surface (e.g. explicit reference path missing).
/// Shared DTO consumed by both the single-test and batch/first-test-file scaffolding flows.
/// </summary>
internal sealed record SiblingInferenceResult(
    SiblingTestPattern? Pattern,
    IReadOnlyList<string> Warnings)
{
    public static SiblingInferenceResult None { get; } = new(null, Array.Empty<string>());
}

/// <summary>
/// Bounded sibling method-name discovery result. Expected per-file read failures retain names
/// collected from readable siblings and contribute one secret-safe warning for the terminal result.
/// </summary>
internal sealed record SiblingTestMethodNameCollection(
    IReadOnlyList<string> Names,
    string? Warning)
{
    public static SiblingTestMethodNameCollection Empty { get; } = new(Array.Empty<string>(), null);
}

/// <summary>
/// Request bundle for <see cref="TestScaffoldRenderer.BuildTestContent(BuildTestContentRequest)"/>.
/// Replaces the previous 11 positional parameters — the two call sites (single-test and batch)
/// were already leaning on trailing named-argument workarounds because the positional order was
/// unmanageable. Positional-record shape so the renderer can deconstruct it into the original
/// locals, keeping the rendering body byte-identical.
/// </summary>
internal sealed record BuildTestContentRequest(
    string TestNamespace,
    ScaffoldTestDto Request,
    string SimpleTypeName,
    string TargetNamespace,
    string ConstructorArgs,
    string Framework,
    IMethodSymbol? TargetMethod,
    INamedTypeSymbol? MatchedType,
    SiblingTestPattern? SiblingPattern,
    string? SuggestedMethodName = null,
    bool IsTargetInaccessible = false);
