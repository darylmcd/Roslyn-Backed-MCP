using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
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

public sealed partial class ScaffoldingService : IScaffoldingService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IFileOperationService _fileOperationService;
    private readonly Contracts.IPreviewStore _previewStore;
    private readonly ILogger<ScaffoldingService>? _logger;
    private readonly TypeScaffolder _typeScaffolder;

    public ScaffoldingService(
        IWorkspaceManager workspace,
        IFileOperationService fileOperationService,
        Contracts.IPreviewStore previewStore,
        ILogger<ScaffoldingService>? logger = null)
    {
        _workspace = workspace;
        _fileOperationService = fileOperationService;
        _previewStore = previewStore;
        _logger = logger;
        _typeScaffolder = new TypeScaffolder(workspace, fileOperationService);
    }

    /// <summary>
    /// Delegates <c>scaffold_type</c> preview to the <see cref="TypeScaffolder"/> collaborator.
    /// The <see cref="IScaffoldingService"/> facade contract and DI lifetime are unchanged.
    /// </summary>
    public Task<RefactoringPreviewDto> PreviewScaffoldTypeAsync(string workspaceId, ScaffoldTypeDto request, CancellationToken ct) =>
        _typeScaffolder.PreviewScaffoldTypeAsync(workspaceId, request, ct);

    private sealed record BatchScaffoldContext(
        ProjectStatusDto Project,
        Project TestProject,
        Solution Solution,
        string ProjectDirectory,
        string TestNamespace,
        string Framework,
        bool NSubstituteAvailable);

    private sealed class BatchScaffoldState
    {
        public BatchScaffoldState(Solution originalSolution)
        {
            OriginalSolution = originalSolution;
            Accumulator = originalSolution;
        }

        public Solution OriginalSolution { get; }

        public Solution Accumulator { get; set; }

        public List<string> Warnings { get; } = [];

        public List<string> CreatedFiles { get; } = [];
    }

    private sealed record ResolvedTargetTypeInfo(
        string TargetNamespace,
        string ConstructorArgs,
        IMethodSymbol? TargetMethod,
        List<string>? Warnings,
        INamedTypeSymbol? MatchedType,
        bool IsTargetInaccessible = false)
    {
        public static ResolvedTargetTypeInfo NotFound { get; } = new(string.Empty, string.Empty, null, null, null);
    }

    private string ResolveTestFramework(string? requested, string? projectFilePath)
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

    private string DetectTestFrameworkFromProjectFile(string? projectFilePath)
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
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to parse project file '{ProjectFilePath}' while detecting test framework; defaulting to mstest.", projectFilePath);
        }

        return "mstest";
    }

    private static IEnumerable<INamedTypeSymbol> GetMatchingTargetTypeCandidates(
        Compilation compilation,
        string targetTypeName,
        CancellationToken ct)
    {
        return compilation.GetSymbolsWithName(targetTypeName, SymbolFilter.Type, ct)
            .OfType<INamedTypeSymbol>()
            .Where(t => t.TypeKind is TypeKind.Class or TypeKind.Struct &&
                        string.Equals(t.Name, targetTypeName, StringComparison.Ordinal));
    }

    private static ResolvedTargetTypeInfo CreateResolvedTargetTypeInfo(
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

    private static ResolvedTargetTypeInfo CreateAmbiguousTargetTypeResult(string targetTypeName)
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

    private static string BuildConstructorArgs(INamedTypeSymbol type, bool nsubstituteAvailable = false)
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

        // Interfaces and abstract classes cannot be instantiated via `default(T)` in a way that
        // produces a usable collaborator — `default` gives null and the first call throws NRE.
        // When the test project references NSubstitute we emit `Substitute.For<T>()`; otherwise
        // we emit a TODO placeholder so the caller notices and supplies a real/faked instance.
        if (parameterType.TypeKind == TypeKind.Interface ||
            (parameterType.TypeKind == TypeKind.Class && parameterType.IsAbstract))
        {
            return nsubstituteAvailable
                ? $"NSubstitute.Substitute.For<{constructibleDisplayName}>()"
                : $"default({displayName})! /* TODO: provide a test double for {displayName} */";
        }

        // Concrete class with an accessible parameterless ctor → safe to `new T()`. Structs go
        // through `default(T)` (the existing fallback).
        if (parameterType is INamedTypeSymbol concrete &&
            concrete.TypeKind == TypeKind.Class &&
            !concrete.IsAbstract &&
            HasAccessibleParameterlessCtor(concrete))
        {
            return $"new {constructibleDisplayName}()";
        }

        // Concrete class without a parameterless ctor: can't safely construct. Emit a TODO so
        // the caller swaps in the right factory. Previously emitted `default(T)` silently.
        if (parameterType is INamedTypeSymbol concreteNoCtor &&
            concreteNoCtor.TypeKind == TypeKind.Class &&
            !concreteNoCtor.IsAbstract)
        {
            return nsubstituteAvailable
                ? $"NSubstitute.Substitute.For<{constructibleDisplayName}>()"
                : $"default({displayName})! /* TODO: provide a test double for {displayName} */";
        }

        return $"default({displayName})";
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

    private ProjectStatusDto ResolveProject(string workspaceId, string projectName)
    {
        return _workspace.GetStatus(workspaceId).Projects.FirstOrDefault(project =>
                   string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(project.FilePath, projectName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Project not found: {projectName}");
    }

    private void ValidateIsTestProject(ProjectStatusDto project)
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
        catch (Exception ex)
        {
            // If we can't parse the project file, allow and let downstream handle it.
            _logger?.LogWarning(ex, "Failed to parse project file '{ProjectFilePath}' while validating test project; allowing operation to proceed.", project.FilePath);
        }
    }

    /// <summary>
    /// Collects the namespaces referenced by <paramref name="type"/> (its containing namespace,
    /// plus generic type-argument and array-element namespaces) into <paramref name="requiredUsings"/>.
    /// Widened to <c>internal</c> so the extracted <see cref="TypeScaffolder"/> collaborator can
    /// reuse it without back-referencing the facade.
    /// </summary>
    internal static void CollectNamespaces(ITypeSymbol type, HashSet<string> requiredUsings)
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

    /// <summary>
    /// Builds a block of <c>using</c> directives for namespaces introduced by constructor
    /// parameter types of <paramref name="typeSymbol"/> — namespaces that are NOT already
    /// covered by <paramref name="typeNamespace"/> or <paramref name="testNamespace"/>.
    /// Returns an empty string when no additional namespaces are needed.
    /// This fixes scaffold-test-preview-missing-usings: previously only the service type's own
    /// namespace was emitted, leaving any ctor-parameter namespaces as unresolved CS0246 errors.
    /// </summary>
    private static string BuildCtorParamUsings(
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
            CollectNamespaces(p.Type, paramNamespaces);

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
    /// Captured shape of a sibling test class: attributes decorating the class declaration,
    /// optional base class, and constructor-injected fixture parameters (the xUnit
    /// <c>IClassFixture&lt;T&gt;</c> pattern is detected by inspecting the constructor
    /// parameter list). Rendered verbatim onto the scaffolded class so integration-test
    /// conventions (ASP.NET Core <c>IClassFixture&lt;CustomWebApplicationFactory&gt;</c>,
    /// <c>[Trait("Category", "Integration")]</c>, etc.) replicate without a manual rewrite.
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
    /// </summary>
    internal sealed record SiblingInferenceResult(
        SiblingTestPattern? Pattern,
        IReadOnlyList<string> Warnings)
    {
        public static SiblingInferenceResult None { get; } = new(null, Array.Empty<string>());
    }

    /// <summary>
    /// Strip a dotted input (e.g. <c>"SampleLib.Hierarchy.Circle"</c>) to its last identifier
    /// segment so it can be used both as a lookup key against <see cref="Compilation.GetSymbolsWithName"/>
    /// (which indexes on the simple name) and as a C# identifier in scaffolded output. Callers
    /// sometimes arrive here with a fully-qualified name because the ambiguity-resolution
    /// error message suggests "use the fully qualified type name" — without this strip, the
    /// dotted input would flow into the class-name template and produce a CS syntax error.
    /// </summary>
    private static string StripToSimpleTypeName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;
        var lastDot = input.LastIndexOf('.');
        return lastDot < 0 ? input : input[(lastDot + 1)..];
    }
}
