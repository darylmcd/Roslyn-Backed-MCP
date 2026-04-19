using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

public abstract class TestBase
{
    private static readonly object _initLock = new();
    private static bool _msbuildInitialized;
    private static bool _servicesInitialized;
    private static readonly WorkspaceIdCache _workspaceIdCache = new();

    /// <summary>
    /// Validation timeouts for integration tests. These match production defaults
    /// (<see cref="ValidationServiceOptions"/>): timeouts should fail loudly when a real
    /// regression appears, not be padded to mask perf smells. The shared workspace cache
    /// below eliminated the previous MSBuild contention that caused 5-minute hangs.
    /// </summary>
    private static readonly ValidationServiceOptions TestValidationOptions = new();

    protected static IPreviewStore PreviewStore { get; private set; } = null!;
    protected static WorkspaceManager WorkspaceManager { get; private set; } = null!;
    protected static IFileWatcherService FileWatcher { get; private set; } = null!;
    protected static SymbolNavigationService SymbolNavigationService { get; private set; } = null!;
    protected static SymbolSearchService SymbolSearchService { get; private set; } = null!;
    protected static ReferenceService ReferenceService { get; private set; } = null!;
    protected static SymbolRelationshipService SymbolRelationshipService { get; private set; } = null!;
    protected static MutationAnalysisService MutationAnalysisService { get; private set; } = null!;
    protected static DiagnosticService DiagnosticService { get; private set; } = null!;
    protected static RefactoringService RefactoringService { get; private set; } = null!;
    protected static BuildService BuildService { get; private set; } = null!;
    protected static TestRunnerService TestRunnerService { get; private set; } = null!;
    protected static TestDiscoveryService TestDiscoveryService { get; private set; } = null!;
    protected static CompletionService CompletionService { get; private set; } = null!;
    protected static CodeActionService CodeActionService { get; private set; } = null!;
    protected static UnusedCodeAnalyzer UnusedCodeAnalyzer { get; private set; } = null!;
    protected static CodeMetricsService CodeMetricsService { get; private set; } = null!;
    protected static NamespaceDependencyService NamespaceDependencyService { get; private set; } = null!;
    protected static DiRegistrationService DiRegistrationService { get; private set; } = null!;
    protected static NuGetDependencyService NuGetDependencyService { get; private set; } = null!;
    protected static CodePatternAnalyzer CodePatternAnalyzer { get; private set; } = null!;
    protected static EditService EditService { get; private set; } = null!;
    protected static FileOperationService FileOperationService { get; private set; } = null!;
    protected static ProjectMutationService ProjectMutationService { get; private set; } = null!;
    protected static CrossProjectRefactoringService CrossProjectRefactoringService { get; private set; } = null!;
    protected static PackageMigrationOrchestrator PackageMigrationOrchestrator { get; private set; } = null!;
    protected static ClassSplitOrchestrator ClassSplitOrchestrator { get; private set; } = null!;
    protected static ExtractAndWireOrchestrator ExtractAndWireOrchestrator { get; private set; } = null!;
    protected static CompositeApplyOrchestrator CompositeApplyOrchestrator { get; private set; } = null!;
    protected static ScaffoldingService ScaffoldingService { get; private set; } = null!;
    protected static DeadCodeService DeadCodeService { get; private set; } = null!;
    protected static SyntaxService SyntaxService { get; private set; } = null!;
    protected static WorkspaceExecutionGate WorkspaceExecutionGate { get; private set; } = null!;
    protected static DotnetCommandRunner DotnetCommandRunner { get; private set; } = null!;
    protected static GatedCommandExecutor GatedCommandExecutor { get; private set; } = null!;
    protected static BulkRefactoringService BulkRefactoringService { get; private set; } = null!;
    protected static CohesionAnalysisService CohesionAnalysisService { get; private set; } = null!;
    protected static CouplingAnalysisService CouplingAnalysisService { get; private set; } = null!;
    protected static ConsumerAnalysisService ConsumerAnalysisService { get; private set; } = null!;
    protected static TypeExtractionService TypeExtractionService { get; private set; } = null!;
    protected static TypeMoveService TypeMoveService { get; private set; } = null!;
    protected static UndoService UndoService { get; private set; } = null!;
    protected static FlowAnalysisService FlowAnalysisService { get; private set; } = null!;
    protected static CompileCheckService CompileCheckService { get; private set; } = null!;
    protected static AnalyzerInfoService AnalyzerInfoService { get; private set; } = null!;
    protected static FixAllService FixAllService { get; private set; } = null!;
    protected static OperationService OperationService { get; private set; } = null!;
    protected static SnippetAnalysisService SnippetAnalysisService { get; private set; } = null!;
    protected static ScriptingService ScriptingService { get; private set; } = null!;
    protected static EditorConfigService EditorConfigService { get; private set; } = null!;
    protected static MsBuildEvaluationService MsBuildEvaluationService { get; private set; } = null!;
    protected static ExtractMethodService ExtractMethodService { get; private set; } = null!;
    protected static ChangeTracker ChangeTracker { get; private set; } = null!;
    protected static RefactoringSuggestionService RefactoringSuggestionService { get; private set; } = null!;
    protected static FormatVerifyService FormatVerifyService { get; private set; } = null!;
    protected static InterfaceExtractionService InterfaceExtractionService { get; private set; } = null!;
    protected static ExceptionFlowService ExceptionFlowService { get; private set; } = null!;
    protected static string RepositoryRootPath { get; private set; } = null!;
    protected static string SampleSolutionPath { get; private set; } = null!;
    protected static string BuildFailureSolutionPath { get; private set; } = null!;
    protected static string GeneratedDocumentSolutionPath { get; private set; } = null!;

    protected static void InitializeServices()
    {
        // Idempotent: services and workspace manager are created once per test assembly,
        // not once per test class. Previously each [ClassInitialize] disposed and recreated
        // the WorkspaceManager (32 times per run), causing MSBuild file-lock contention on
        // the shared sample fixtures and unbounded build hangs under load.
        lock (_initLock)
        {
            if (_servicesInitialized)
            {
                return;
            }

            if (!_msbuildInitialized)
            {
                MsBuildInitializer.EnsureInitialized();
                _msbuildInitialized = true;
            }

            InitializeServicesCore();
            _servicesInitialized = true;
        }
    }

    private static void InitializeServicesCore()
    {
        var services = TestServiceContainer.Create(TestValidationOptions);

        // Tests retain workspaces across the full assembly run instead of disposing them
        // per-class. We need a higher limit than the production default (8) because
        // ~22 test classes load fixture solutions (some loading multiple) without ever
        // closing them. The cap is still bounded so a runaway test that loads in a loop
        // will fail loudly rather than exhaust memory.
        PreviewStore = services.PreviewStore;
        WorkspaceManager = services.WorkspaceManager;
        FileWatcher = services.FileWatcher;
        SymbolNavigationService = services.SymbolNavigationService;
        SymbolSearchService = services.SymbolSearchService;
        ReferenceService = services.ReferenceService;
        SymbolRelationshipService = services.SymbolRelationshipService;
        MutationAnalysisService = services.MutationAnalysisService;
        DiagnosticService = services.DiagnosticService;
        RefactoringService = services.RefactoringService;
        BuildService = services.BuildService;
        TestRunnerService = services.TestRunnerService;
        TestDiscoveryService = services.TestDiscoveryService;
        CompletionService = services.CompletionService;
        CodeActionService = services.CodeActionService;
        UnusedCodeAnalyzer = services.UnusedCodeAnalyzer;
        CodeMetricsService = services.CodeMetricsService;
        NamespaceDependencyService = services.NamespaceDependencyService;
        DiRegistrationService = services.DiRegistrationService;
        NuGetDependencyService = services.NuGetDependencyService;
        CodePatternAnalyzer = services.CodePatternAnalyzer;
        EditService = services.EditService;
        FileOperationService = services.FileOperationService;
        ProjectMutationService = services.ProjectMutationService;
        CrossProjectRefactoringService = services.CrossProjectRefactoringService;
        PackageMigrationOrchestrator = services.PackageMigrationOrchestrator;
        ClassSplitOrchestrator = services.ClassSplitOrchestrator;
        ExtractAndWireOrchestrator = services.ExtractAndWireOrchestrator;
        CompositeApplyOrchestrator = services.CompositeApplyOrchestrator;
        ScaffoldingService = services.ScaffoldingService;
        DeadCodeService = services.DeadCodeService;
        SyntaxService = services.SyntaxService;
        WorkspaceExecutionGate = services.WorkspaceExecutionGate;
        DotnetCommandRunner = services.DotnetCommandRunner;
        GatedCommandExecutor = services.GatedCommandExecutor;
        BulkRefactoringService = services.BulkRefactoringService;
        CohesionAnalysisService = services.CohesionAnalysisService;
        CouplingAnalysisService = services.CouplingAnalysisService;
        ConsumerAnalysisService = services.ConsumerAnalysisService;
        TypeExtractionService = services.TypeExtractionService;
        TypeMoveService = services.TypeMoveService;
        UndoService = services.UndoService;
        FlowAnalysisService = services.FlowAnalysisService;
        CompileCheckService = services.CompileCheckService;
        AnalyzerInfoService = services.AnalyzerInfoService;
        FixAllService = services.FixAllService;
        OperationService = services.OperationService;
        SnippetAnalysisService = services.SnippetAnalysisService;
        ScriptingService = services.ScriptingService;
        EditorConfigService = services.EditorConfigService;
        MsBuildEvaluationService = services.MsBuildEvaluationService;
        ExtractMethodService = services.ExtractMethodService;
        ChangeTracker = services.ChangeTracker;
        RefactoringSuggestionService = services.RefactoringSuggestionService;
        FormatVerifyService = services.FormatVerifyService;
        InterfaceExtractionService = services.InterfaceExtractionService;
        ExceptionFlowService = services.ExceptionFlowService;

        RepositoryRootPath = TestFixtureFileSystem.FindRepositoryRoot();
        SampleSolutionPath = TestFixtureFileSystem.FindFixturePath(RepositoryRootPath, "SampleSolution", "SampleSolution.slnx", "SampleSolution.sln");
        BuildFailureSolutionPath = TestFixtureFileSystem.FindFixturePath(RepositoryRootPath, "BuildFailureSolution", "BuildFailureSolution.slnx", "BuildFailureSolution.sln");
        GeneratedDocumentSolutionPath = TestFixtureFileSystem.FindFixturePath(RepositoryRootPath, "GeneratedDocumentSolution", "GeneratedDocumentSolution.slnx", "GeneratedDocumentSolution.sln");
    }

    /// <summary>
    /// No-op since the test assembly now owns service lifetime. Disposal happens in
    /// <see cref="AssemblyLifecycle.Cleanup"/> after all tests complete. Kept for source
    /// compatibility with existing <c>[ClassCleanup]</c> hooks across test classes.
    /// </summary>
    protected static void DisposeServices()
    {
        // Intentional no-op. See AssemblyLifecycle.Cleanup.
    }

    /// <summary>
    /// Disposes the shared <see cref="WorkspaceManager"/> and resets the workspace cache.
    /// Called from <see cref="AssemblyLifecycle.Cleanup"/> after the entire test assembly
    /// finishes. Test code should not call this directly.
    /// </summary>
    internal static void DisposeAssemblyResources()
    {
        lock (_initLock)
        {
            if (!_servicesInitialized)
            {
                return;
            }

            try
            {
                WorkspaceManager?.Dispose();
            }
            catch
            {
                // Best-effort: avoid masking real test failures with cleanup errors.
            }

            _workspaceIdCache.Clear();
            _servicesInitialized = false;
        }
    }

    /// <summary>
    /// Loads a workspace from the given solution path, caching the resulting <c>WorkspaceId</c>
    /// so multiple test classes that need the same fixture share a single
    /// <see cref="Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace"/> instance. Eliminates the
    /// 22× duplicate <c>SampleSolution</c> loads that previously caused MSBuild file-lock
    /// contention. Test classes can still call <see cref="WorkspaceManager"/>.<c>LoadAsync</c>
    /// directly for fixtures they want isolated (e.g., temp copies via
    /// <see cref="CreateSampleSolutionCopy"/>).
    /// </summary>
    protected static async Task<string> GetOrLoadWorkspaceIdAsync(string solutionPath, CancellationToken ct = default)
    {
        return await _workspaceIdCache.GetOrLoadAsync(WorkspaceManager, solutionPath, ct).ConfigureAwait(false);
    }

    protected static string CreateSampleSolutionCopy()
    {
        return TestFixtureFileSystem.CreateSampleSolutionCopy(RepositoryRootPath, SampleSolutionPath);
    }

    protected static void DeleteDirectoryIfExists(string path)
    {
        TestFixtureFileSystem.DeleteDirectoryIfExists(path);
    }
}
