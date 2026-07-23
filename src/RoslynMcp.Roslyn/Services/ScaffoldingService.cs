using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    /// Widened to <c>internal</c> so the extracted <see cref="TypeScaffolder"/> collaborator and
    /// the shared <see cref="TestScaffoldRenderer"/> can reuse it without back-referencing the facade.
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
}
