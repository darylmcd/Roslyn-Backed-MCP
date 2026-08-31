using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

#pragma warning disable RS1036 // Test-only analyzer double; not shipped
#pragma warning disable RS1038 // Test assembly references Workspaces by design
#pragma warning disable RS1041 // Test targets net10 same as product
#pragma warning disable RS2008 // No release tracking for test-only diagnostic descriptors

namespace RoslynMcp.Tests;

[TestClass]
public sealed class DiagnosticQueryServiceRegressionTests
{
    private const string ConfigurableDiagnosticId = "RMCPTEST001";

    [TestMethod]
    public async Task GetDiagnosticsAsync_ErrorOnlyHonorsAnalyzerConfigEscalation()
    {
        using var workspace = new AdhocWorkspace();
        var project = CreateDiagnosticProject(
            workspace,
            new ConfigurableWarningAnalyzer(),
            escalateWarningToError: true);
        var workspaceManager = new VersionedWorkspaceManager("configured-analyzer", project.Solution, 1);
        var compilationCache = new AdhocCompilationCache();
        var service = new DiagnosticQueryService(workspaceManager, compilationCache);

        var result = await service.GetDiagnosticsAsync(
            workspaceManager.WorkspaceId,
            new DiagnosticQueryFilters(null, null, "Error", null),
            CancellationToken.None);

        Assert.HasCount(1, result.AnalyzerDiagnostics);
        var diagnostic = result.AnalyzerDiagnostics.Single();
        Assert.AreEqual(ConfigurableDiagnosticId, diagnostic.Id);
        Assert.AreEqual("Error", diagnostic.Severity);
        Assert.AreEqual(1, result.AnalyzerErrors);
        Assert.AreEqual(1, result.TotalErrors);
        Assert.AreEqual(1, compilationCache.AnalyzerCompilationRequestCount);
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_ErrorOnlySkipsAnalyzerWhenConfigurationProvesNoError()
    {
        using var workspace = new AdhocWorkspace();
        var project = CreateDiagnosticProject(
            workspace,
            new ConfigurableWarningAnalyzer(),
            escalateWarningToError: false);
        var workspaceManager = new VersionedWorkspaceManager("warning-analyzer", project.Solution, 1);
        var compilationCache = new AdhocCompilationCache();
        var service = new DiagnosticQueryService(workspaceManager, compilationCache);

        var result = await service.GetDiagnosticsAsync(
            workspaceManager.WorkspaceId,
            new DiagnosticQueryFilters(null, null, "Error", null),
            CancellationToken.None);

        Assert.HasCount(0, result.AnalyzerDiagnostics);
        Assert.AreEqual(0, result.AnalyzerErrors);
        Assert.AreEqual(0, compilationCache.AnalyzerCompilationRequestCount,
            "An Error-only query must retain the analyzer-pass fast path when effective configuration proves no analyzer can report Error.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_OlderCompletionCannotReplaceNewerVersionCaches()
    {
        using var workspace = new AdhocWorkspace();
        var project = CreateDiagnosticProject(workspace, analyzer: null, escalateWarningToError: false);
        var workspaceManager = new VersionedWorkspaceManager("version-race", project.Solution, 1)
        {
            BlockFirstStatusRequest = true,
        };
        var service = new DiagnosticQueryService(workspaceManager, new AdhocCompilationCache());
        var filters = new DiagnosticQueryFilters(null, null, "Error", null);

        var olderQuery = service.GetDiagnosticsAsync(
            workspaceManager.WorkspaceId,
            filters,
            CancellationToken.None);
        await workspaceManager.FirstStatusRequestStarted.WaitAsync(TimeSpan.FromSeconds(5));

        DiagnosticsResultDto newerResult;
        try
        {
            workspaceManager.Version = 2;
            newerResult = await service.GetDiagnosticsAsync(
                workspaceManager.WorkspaceId,
                filters,
                CancellationToken.None);
        }
        finally
        {
            workspaceManager.ReleaseFirstStatusRequest();
        }

        await olderQuery;

        var cachedResult = await service.GetDiagnosticsAsync(
            workspaceManager.WorkspaceId,
            filters,
            CancellationToken.None);
        Assert.AreSame(newerResult, cachedResult,
            "A late version-1 completion must not evict the reusable version-2 result entry.");
        Assert.IsTrue(service.TryGetCachedDiagnostics(
            workspaceManager.WorkspaceId,
            version: 2,
            out _),
            "A late version-1 completion must not replace the version-2 raw diagnostic cache.");
    }

    private static Project CreateDiagnosticProject(
        AdhocWorkspace workspace,
        DiagnosticAnalyzer? analyzer,
        bool escalateWarningToError)
    {
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            "roslyn-mcp-tests",
            "diagnostic-query");
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "DiagnosticQueryProject",
                "DiagnosticQueryProject",
                LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences:
                [
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                ]))
            .AddDocument(
                DocumentId.CreateNewId(projectId),
                "Probe.cs",
                SourceText.From("internal sealed class Probe { }"),
                filePath: Path.Combine(projectDirectory, "Probe.cs"));

        if (analyzer is not null)
        {
            solution = solution.AddAnalyzerReference(
                projectId,
                new InMemoryAnalyzerReference(analyzer));
        }

        if (escalateWarningToError)
        {
            solution = solution.AddAnalyzerConfigDocument(
                DocumentId.CreateNewId(projectId),
                ".editorconfig",
                SourceText.From($$"""
                    root = true

                    [*.cs]
                    dotnet_diagnostic.{{ConfigurableDiagnosticId}}.severity = error
                    """),
                filePath: Path.Combine(projectDirectory, ".editorconfig"));
        }

        Assert.IsTrue(workspace.TryApplyChanges(solution));
        return workspace.CurrentSolution.GetProject(projectId)!;
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class ConfigurableWarningAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Rule = new(
            ConfigurableDiagnosticId,
            "Configurable warning",
            "Configurable warning",
            "Testing",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxTreeAction(static syntaxContext =>
            {
                var root = syntaxContext.Tree.GetRoot(syntaxContext.CancellationToken);
                syntaxContext.ReportDiagnostic(Diagnostic.Create(Rule, root.GetLocation()));
            });
        }
    }

    private sealed class InMemoryAnalyzerReference(DiagnosticAnalyzer analyzer) : AnalyzerReference
    {
        public override string Display => "In-memory diagnostic query analyzer";

        public override string FullPath => string.Empty;

        public override object Id => analyzer.GetType();

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) =>
            language == LanguageNames.CSharp ? [analyzer] : [];

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [analyzer];

        public override ImmutableArray<ISourceGenerator> GetGenerators(string language) => [];
    }

    private sealed class AdhocCompilationCache : ICompilationCache
    {
        public int AnalyzerCompilationRequestCount { get; private set; }

        public Task<Compilation?> GetCompilationAsync(
            string workspaceId,
            Project project,
            CancellationToken ct) => project.GetCompilationAsync(ct);

        public async Task<CompilationWithAnalyzers?> GetCompilationWithAnalyzersAsync(
            string workspaceId,
            Project project,
            CancellationToken ct)
        {
            AnalyzerCompilationRequestCount++;
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null)
            {
                return null;
            }

            var analyzers = project.AnalyzerReferences
                .SelectMany(reference => reference.GetAnalyzers(project.Language))
                .ToImmutableArray();
            return analyzers.IsEmpty
                ? null
                : compilation.WithAnalyzers(
                    analyzers,
                    new CompilationWithAnalyzersOptions(
                        project.AnalyzerOptions,
                        onAnalyzerException: null,
                        concurrentAnalysis: true,
                        logAnalyzerExecutionTime: false,
                        reportSuppressedDiagnostics: false));
        }

        public bool IsLiveSolution(string workspaceId, Solution solution) => true;

        public void Invalidate(string workspaceId)
        {
        }
    }

    private sealed class VersionedWorkspaceManager(
        string workspaceId,
        Solution solution,
        int initialVersion) : IWorkspaceManager
    {
        private readonly TaskCompletionSource _firstStatusRequestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstStatusRequest =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _statusRequestCount;

        public string WorkspaceId { get; } = workspaceId;
        public Solution Solution { get; private set; } = solution;
        public int Version { get; set; } = initialVersion;
        public bool BlockFirstStatusRequest { get; init; }
        public Task FirstStatusRequestStarted => _firstStatusRequestStarted.Task;

        public event Action<string>? WorkspaceClosed
        {
            add { }
            remove { }
        }

        public event Action<string>? WorkspaceReloaded
        {
            add { }
            remove { }
        }

        public void ReleaseFirstStatusRequest() => _releaseFirstStatusRequest.TrySetResult();

        public int GetCurrentVersion(string workspaceId) => Version;

        public Solution GetCurrentSolution(string workspaceId) => Solution;

        public WorkspaceStatusDto GetStatus(string workspaceId) => CreateStatus();

        public async Task<WorkspaceStatusDto> GetStatusAsync(
            string workspaceId,
            CancellationToken cancellationToken = default)
        {
            if (BlockFirstStatusRequest && Interlocked.Increment(ref _statusRequestCount) == 1)
            {
                _firstStatusRequestStarted.TrySetResult();
                await _releaseFirstStatusRequest.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return CreateStatus();
        }

        public bool ContainsWorkspace(string workspaceId) => workspaceId == WorkspaceId;

        public bool IsStale(string workspaceId) => false;

        public bool TryApplyChanges(string workspaceId, Solution newSolution)
        {
            Solution = newSolution;
            Version++;
            return true;
        }

        public void RestoreVersion(string workspaceId, int version) => Version = version;

        public Project? GetProject(string workspaceId, string projectNameOrPath) =>
            Solution.Projects.FirstOrDefault(project =>
                string.Equals(project.Name, projectNameOrPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(project.FilePath, projectNameOrPath, StringComparison.OrdinalIgnoreCase));

        public Task<WorkspaceStatusDto> LoadAsync(
            string path,
            EvictPolicy evictPolicy,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<WorkspaceStatusDto> ReloadAsync(
            string workspaceId,
            CancellationToken ct) => throw new NotSupportedException();

        public bool Close(string workspaceId) => throw new NotSupportedException();

        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [CreateStatus()];

        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();

        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(
            string workspaceId,
            string? projectName,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<string?> GetSourceTextAsync(
            string workspaceId,
            string filePath,
            CancellationToken ct) => throw new NotSupportedException();

        private WorkspaceStatusDto CreateStatus() => new(
            WorkspaceId,
            LoadedPath: null,
            Version,
            SnapshotToken: $"{WorkspaceId}:{Version}",
            DateTimeOffset.UtcNow,
            Solution.ProjectIds.Count,
            Solution.Projects.Sum(project => project.DocumentIds.Count),
            Projects: [],
            IsLoaded: true,
            IsStale: false,
            WorkspaceDiagnostics: [],
            StaleReason: null,
            RestoreRequired: false,
            BuildRequired: false);
    }
}
