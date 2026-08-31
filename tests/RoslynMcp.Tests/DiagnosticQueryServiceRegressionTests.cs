using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
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
    public void GetEffectiveReportDiagnostic_HonorsConfigurationPrecedence()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("internal sealed class Probe { }");
        EffectiveSeverityCase[] cases =
        [
            new(
                "syntax-tree option",
                Tree: ReportDiagnostic.Error,
                Global: ReportDiagnostic.Suppress,
                Specific: ReportDiagnostic.Warn,
                General: ReportDiagnostic.Error,
                Default: DiagnosticSeverity.Warning,
                EnabledByDefault: true,
                Expected: ReportDiagnostic.Error),
            new(
                "global analyzer-config option",
                Tree: null,
                Global: ReportDiagnostic.Error,
                Specific: ReportDiagnostic.Suppress,
                General: ReportDiagnostic.Default,
                Default: DiagnosticSeverity.Warning,
                EnabledByDefault: true,
                Expected: ReportDiagnostic.Error),
            new(
                "command-line specific option",
                Tree: null,
                Global: null,
                Specific: ReportDiagnostic.Error,
                General: ReportDiagnostic.Default,
                Default: DiagnosticSeverity.Warning,
                EnabledByDefault: true,
                Expected: ReportDiagnostic.Error),
            new(
                "disabled-by-default descriptor",
                Tree: null,
                Global: null,
                Specific: null,
                General: ReportDiagnostic.Error,
                Default: DiagnosticSeverity.Warning,
                EnabledByDefault: false,
                Expected: ReportDiagnostic.Suppress),
            new(
                "general warning escalation",
                Tree: null,
                Global: null,
                Specific: null,
                General: ReportDiagnostic.Error,
                Default: DiagnosticSeverity.Warning,
                EnabledByDefault: true,
                Expected: ReportDiagnostic.Error),
            new(
                "descriptor default",
                Tree: null,
                Global: null,
                Specific: null,
                General: ReportDiagnostic.Default,
                Default: DiagnosticSeverity.Info,
                EnabledByDefault: true,
                Expected: ReportDiagnostic.Info),
        ];

        foreach (var testCase in cases)
        {
            var specificOptions = testCase.Specific is { } specific
                ? ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(
                    ConfigurableDiagnosticId,
                    specific)
                : ImmutableDictionary<string, ReportDiagnostic>.Empty;
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithGeneralDiagnosticOption(testCase.General)
                .WithSpecificDiagnosticOptions(specificOptions)
                .WithSyntaxTreeOptionsProvider(
                    new FixedSyntaxTreeOptionsProvider(testCase.Tree, testCase.Global));
            var descriptor = new DiagnosticDescriptor(
                ConfigurableDiagnosticId,
                "Configurable diagnostic",
                "Configurable diagnostic",
                "Testing",
                testCase.Default,
                testCase.EnabledByDefault);

            var actual = DiagnosticProjectAnalyzer.GetEffectiveReportDiagnostic(
                descriptor,
                options,
                syntaxTree,
                CancellationToken.None);

            Assert.AreEqual(testCase.Expected, actual, testCase.Name);
        }
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_OlderCompletionCannotReplaceNewerVersionCaches()
    {
        using var workspace = new AdhocWorkspace();
        var project = CreateDiagnosticProject(workspace, analyzer: null, escalateWarningToError: false);
        var workspaceManager = new VersionedWorkspaceManager("version-race", project.Solution, 1)
        {
            BlockedStatusRequestCount = 1,
        };
        var service = new DiagnosticQueryService(workspaceManager, new AdhocCompilationCache());
        var filters = new DiagnosticQueryFilters(null, null, "Error", null);

        var olderQuery = service.GetDiagnosticsAsync(
            workspaceManager.WorkspaceId,
            filters,
            CancellationToken.None);
        await workspaceManager.BlockedStatusRequestsStarted.WaitAsync(TimeSpan.FromSeconds(5));

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
            workspaceManager.ReleaseBlockedStatusRequests();
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

    [TestMethod]
    public async Task GetDiagnosticsAsync_ConcurrentDistinctFiltersKeepsResultCacheBounded()
    {
        using var workspace = new AdhocWorkspace();
        const int queryCount = 64;
        var workspaceManager = new VersionedWorkspaceManager(
            "concurrent-filter-cache",
            workspace.CurrentSolution,
            initialVersion: 1)
        {
            BlockedStatusRequestCount = queryCount,
        };
        var service = new DiagnosticQueryService(workspaceManager, new AdhocCompilationCache());
        var filters = Enumerable.Range(0, queryCount)
            .Select(index => new DiagnosticQueryFilters(
                Project: null,
                File: null,
                Severity: $"unsupported-{index}",
                DiagnosticId: null))
            .ToArray();

        var queries = filters
            .Select(filter => service.GetDiagnosticsAsync(
                workspaceManager.WorkspaceId,
                filter,
                CancellationToken.None))
            .ToArray();
        await workspaceManager.BlockedStatusRequestsStarted.WaitAsync(TimeSpan.FromSeconds(5));

        workspaceManager.ReleaseBlockedStatusRequests();
        await Task.WhenAll(queries);

        var cachedResults = ReadCachedResults(service, workspaceManager.WorkspaceId);
        Assert.HasCount(8, cachedResults,
            "A synchronized burst of distinct filters must not exceed the per-workspace cache cap.");
        foreach (var cached in cachedResults)
        {
            var reused = await service.GetDiagnosticsAsync(
                workspaceManager.WorkspaceId,
                cached.Key,
                CancellationToken.None);
            Assert.AreSame(cached.Value, reused,
                "Every retained completed result must remain reusable by its exact filter.");
        }
    }

    private static IReadOnlyList<KeyValuePair<DiagnosticQueryFilters, DiagnosticsResultDto>>
        ReadCachedResults(DiagnosticQueryService service, string workspaceId)
    {
        var cacheField = typeof(DiagnosticQueryService).GetField(
            "_resultCache",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(cacheField);
        var cache = cacheField.GetValue(service);
        Assert.IsNotNull(cache);

        var tryGetValue = cache.GetType().GetMethod("TryGetValue");
        Assert.IsNotNull(tryGetValue);
        object?[] arguments = [workspaceId, null];
        Assert.AreEqual(true, tryGetValue.Invoke(cache, arguments));
        var workspaceEntry = arguments[1];
        Assert.IsNotNull(workspaceEntry);

        var resultsProperty = workspaceEntry.GetType().GetProperty("Results");
        Assert.IsNotNull(resultsProperty);
        var results = resultsProperty.GetValue(workspaceEntry) as IEnumerable;
        Assert.IsNotNull(results);

        return results.Cast<object>()
            .Select(item =>
            {
                var itemType = item.GetType();
                return new KeyValuePair<DiagnosticQueryFilters, DiagnosticsResultDto>(
                    (DiagnosticQueryFilters)itemType.GetProperty("Key")!.GetValue(item)!,
                    (DiagnosticsResultDto)itemType.GetProperty("Value")!.GetValue(item)!);
            })
            .ToList();
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

    private sealed class FixedSyntaxTreeOptionsProvider(
        ReportDiagnostic? treeSeverity,
        ReportDiagnostic? globalSeverity) : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(
            SyntaxTree tree,
            CancellationToken cancellationToken) => GeneratedKind.Unknown;

        public override bool TryGetDiagnosticValue(
            SyntaxTree tree,
            string diagnosticId,
            CancellationToken cancellationToken,
            out ReportDiagnostic severity)
        {
            severity = treeSeverity ?? ReportDiagnostic.Default;
            return treeSeverity.HasValue;
        }

        public override bool TryGetGlobalDiagnosticValue(
            string diagnosticId,
            CancellationToken cancellationToken,
            out ReportDiagnostic severity)
        {
            severity = globalSeverity ?? ReportDiagnostic.Default;
            return globalSeverity.HasValue;
        }
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
        private readonly TaskCompletionSource _blockedStatusRequestsStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseBlockedStatusRequests =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _statusRequestCount;

        public string WorkspaceId { get; } = workspaceId;
        public Solution Solution { get; private set; } = solution;
        public int Version { get; set; } = initialVersion;
        public int BlockedStatusRequestCount { get; init; }
        public Task BlockedStatusRequestsStarted => _blockedStatusRequestsStarted.Task;

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

        public void ReleaseBlockedStatusRequests() =>
            _releaseBlockedStatusRequests.TrySetResult();

        public int GetCurrentVersion(string workspaceId) => Version;

        public Solution GetCurrentSolution(string workspaceId) => Solution;

        public WorkspaceStatusDto GetStatus(string workspaceId) => CreateStatus();

        public async Task<WorkspaceStatusDto> GetStatusAsync(
            string workspaceId,
            CancellationToken cancellationToken = default)
        {
            if (BlockedStatusRequestCount > 0)
            {
                var requestNumber = Interlocked.Increment(ref _statusRequestCount);
                if (requestNumber == BlockedStatusRequestCount)
                {
                    _blockedStatusRequestsStarted.TrySetResult();
                }

                if (requestNumber <= BlockedStatusRequestCount)
                {
                    await _releaseBlockedStatusRequests.Task
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
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

    private sealed record EffectiveSeverityCase(
        string Name,
        ReportDiagnostic? Tree,
        ReportDiagnostic? Global,
        ReportDiagnostic? Specific,
        ReportDiagnostic General,
        DiagnosticSeverity Default,
        bool EnabledByDefault,
        ReportDiagnostic Expected);
}
