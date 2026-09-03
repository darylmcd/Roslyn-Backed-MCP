using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

public abstract class TestBase
{
    private static readonly object _initLock = new();
    private static bool _msbuildInitialized;
    private static bool _servicesInitialized;
    private static TestAssemblyFixture? _fixture;

    /// <summary>
    /// Validation timeouts for integration tests. Defaults match production
    /// (<see cref="ValidationServiceOptions"/>): timeouts should fail loudly when a real
    /// regression appears, not be padded to mask perf smells. The same
    /// <c>ROSLYNMCP_*_TIMEOUT_SECONDS</c> env vars the host binds (Program.cs
    /// BindValidationServiceOptions) are honored here so CI can run fail-fast values —
    /// production defaults are sized for arbitrary user solutions, and a wedged child on
    /// a contended runner otherwise stacks 5-minute timeouts into the job budget.
    /// </summary>
    private static readonly ValidationServiceOptions TestValidationOptions = BindValidationOptionsFromEnvironment();

    private static ValidationServiceOptions BindValidationOptionsFromEnvironment()
    {
        var opts = new ValidationServiceOptions();
        if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_BUILD_TIMEOUT_SECONDS"), out var buildSeconds) && buildSeconds > 0)
        {
            opts = opts with { BuildTimeout = TimeSpan.FromSeconds(buildSeconds) };
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_TEST_TIMEOUT_SECONDS"), out var testSeconds) && testSeconds > 0)
        {
            opts = opts with { TestTimeout = TimeSpan.FromSeconds(testSeconds) };
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS"), out var vulnSeconds) && vulnSeconds > 0)
        {
            opts = opts with { VulnerabilityScanTimeout = TimeSpan.FromSeconds(vulnSeconds) };
        }

        return opts;
    }

    /// <summary>
    /// The single assembly-owned context that actually holds the services, the repository
    /// fixture paths, the workspace-id cache, and the path-authorized MCP server session.
    /// Every <c>protected static</c> member below is a get-only forwarder over it, so a test
    /// service is declared once (in <see cref="TestServiceContainer"/>) rather than being
    /// hand-copied into a mutable static here as well.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a shared service is touched before <see cref="InitializeServices"/> ran.
    /// The previous mutable statics returned <see langword="null"/> in that window and failed
    /// later with an opaque <see cref="NullReferenceException"/>; failing here names the cause.
    /// </exception>
    internal static TestAssemblyFixture Fixture =>
        Volatile.Read(ref _fixture) ?? throw new InvalidOperationException(
            "Test services have not been initialized. Call TestBase.InitializeServices() " +
            "(normally from [ClassInitialize]) before touching a shared service or fixture path.");

    protected static IPreviewStore PreviewStore => Fixture.Services.PreviewStore;
    protected static WorkspaceManager WorkspaceManager => Fixture.Services.WorkspaceManager;
    protected static IFileWatcherService FileWatcher => Fixture.Services.FileWatcher;
    protected static SymbolNavigationService SymbolNavigationService => Fixture.Services.SymbolNavigationService;
    protected static SymbolSearchService SymbolSearchService => Fixture.Services.SymbolSearchService;
    protected static ReferenceService ReferenceService => Fixture.Services.ReferenceService;
    protected static SymbolRelationshipService SymbolRelationshipService => Fixture.Services.SymbolRelationshipService;
    protected static MutationAnalysisService MutationAnalysisService => Fixture.Services.MutationAnalysisService;
    protected static TypeConsumersService TypeConsumersService => Fixture.Services.TypeConsumersService;
    protected static SemanticGrepService SemanticGrepService => Fixture.Services.SemanticGrepService;
    protected static DiagnosticService DiagnosticService => Fixture.Services.DiagnosticService;
    protected static RefactoringService RefactoringService => Fixture.Services.RefactoringService;
    protected static BuildService BuildService => Fixture.Services.BuildService;
    protected static TestRunnerService TestRunnerService => Fixture.Services.TestRunnerService;
    protected static TestDiscoveryService TestDiscoveryService => Fixture.Services.TestDiscoveryService;
    protected static CompletionService CompletionService => Fixture.Services.CompletionService;
    protected static CodeActionService CodeActionService => Fixture.Services.CodeActionService;
    protected static UnusedCodeAnalyzer UnusedCodeAnalyzer => Fixture.Services.UnusedCodeAnalyzer;
    protected static CodeMetricsService CodeMetricsService => Fixture.Services.CodeMetricsService;
    protected static NamespaceDependencyService NamespaceDependencyService => Fixture.Services.NamespaceDependencyService;
    protected static DiRegistrationService DiRegistrationService => Fixture.Services.DiRegistrationService;
    protected static NuGetDependencyService NuGetDependencyService => Fixture.Services.NuGetDependencyService;
    protected static CodePatternAnalyzer CodePatternAnalyzer => Fixture.Services.CodePatternAnalyzer;
    protected static EditService EditService => Fixture.Services.EditService;
    protected static FileOperationService FileOperationService => Fixture.Services.FileOperationService;
    protected static ProjectMutationService ProjectMutationService => Fixture.Services.ProjectMutationService;
    protected static CrossProjectRefactoringService CrossProjectRefactoringService => Fixture.Services.CrossProjectRefactoringService;
    protected static PackageMigrationOrchestrator PackageMigrationOrchestrator => Fixture.Services.PackageMigrationOrchestrator;
    protected static ClassSplitOrchestrator ClassSplitOrchestrator => Fixture.Services.ClassSplitOrchestrator;
    protected static ExtractAndWireOrchestrator ExtractAndWireOrchestrator => Fixture.Services.ExtractAndWireOrchestrator;
    protected static CompositeApplyOrchestrator CompositeApplyOrchestrator => Fixture.Services.CompositeApplyOrchestrator;
    protected static ScaffoldingService ScaffoldingService => Fixture.Services.ScaffoldingService;
    protected static DeadCodeService DeadCodeService => Fixture.Services.DeadCodeService;
    protected static SyntaxService SyntaxService => Fixture.Services.SyntaxService;
    protected static WorkspaceExecutionGate WorkspaceExecutionGate => Fixture.Services.WorkspaceExecutionGate;
    protected static DotnetCommandRunner DotnetCommandRunner => Fixture.Services.DotnetCommandRunner;
    protected static GatedCommandExecutor GatedCommandExecutor => Fixture.Services.GatedCommandExecutor;
    protected static BulkRefactoringService BulkRefactoringService => Fixture.Services.BulkRefactoringService;
    protected static CohesionAnalysisService CohesionAnalysisService => Fixture.Services.CohesionAnalysisService;
    protected static CouplingAnalysisService CouplingAnalysisService => Fixture.Services.CouplingAnalysisService;
    protected static RecordFieldAdditionService RecordFieldAdditionService => Fixture.Services.RecordFieldAdditionService;
    protected static ConsumerAnalysisService ConsumerAnalysisService => Fixture.Services.ConsumerAnalysisService;
    protected static TypeExtractionService TypeExtractionService => Fixture.Services.TypeExtractionService;
    protected static TypeMoveService TypeMoveService => Fixture.Services.TypeMoveService;
    protected static UndoService UndoService => Fixture.Services.UndoService;
    protected static FlowAnalysisService FlowAnalysisService => Fixture.Services.FlowAnalysisService;
    protected static CompileCheckService CompileCheckService => Fixture.Services.CompileCheckService;
    protected static AnalyzerInfoService AnalyzerInfoService => Fixture.Services.AnalyzerInfoService;
    protected static FixAllService FixAllService => Fixture.Services.FixAllService;
    protected static OperationService OperationService => Fixture.Services.OperationService;
    protected static SnippetAnalysisService SnippetAnalysisService => Fixture.Services.SnippetAnalysisService;
    protected static ScriptingService ScriptingService => Fixture.Services.ScriptingService;
    protected static EditorConfigService EditorConfigService => Fixture.Services.EditorConfigService;
    protected static MsBuildEvaluationService MsBuildEvaluationService => Fixture.Services.MsBuildEvaluationService;
    protected static ExtractMethodService ExtractMethodService => Fixture.Services.ExtractMethodService;
    protected static ChangeTracker ChangeTracker => Fixture.Services.ChangeTracker;
    protected static RefactoringSuggestionService RefactoringSuggestionService => Fixture.Services.RefactoringSuggestionService;
    protected static FormatVerifyService FormatVerifyService => Fixture.Services.FormatVerifyService;
    protected static InterfaceExtractionService InterfaceExtractionService => Fixture.Services.InterfaceExtractionService;
    protected static ExceptionFlowService ExceptionFlowService => Fixture.Services.ExceptionFlowService;
    protected static WorkspaceWarmService WorkspaceWarmService => Fixture.Services.WorkspaceWarmService;
    protected static WorkspaceDriftService WorkspaceDriftService => Fixture.Services.WorkspaceDriftService;
    protected static ParameterObjectService ParameterObjectService => Fixture.Services.ParameterObjectService;

    protected static string RepositoryRootPath => Fixture.RepositoryFixtures.RepositoryRootPath;
    protected static string SampleSolutionPath => Fixture.RepositoryFixtures.SampleSolutionPath;
    protected static string BuildFailureSolutionPath => Fixture.RepositoryFixtures.BuildFailureSolutionPath;
    protected static string GeneratedDocumentSolutionPath => Fixture.RepositoryFixtures.GeneratedDocumentSolutionPath;

    /// <summary>
    /// Returns a real, connected MCP server whose server-owned security options explicitly
    /// sanction repository fixtures and the process temp directory. Direct tool tests use this
    /// instead of relying on a null-server path-validation bypass.
    /// </summary>
    protected static Task<McpServer> GetPathAuthorizedServerAsync() =>
        Fixture.GetPathAuthorizedServerAsync();

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
        // Tests retain workspaces across the full assembly run instead of disposing them
        // per-class. We need a higher limit than the production default (8) because
        // ~22 test classes load fixture solutions (some loading multiple) without ever
        // closing them. The cap is still bounded so a runaway test that loads in a loop
        // will fail loudly rather than exhaust memory. TestServiceContainer.Create owns that
        // option; this method only publishes the assembly-owned fixture.
        Volatile.Write(ref _fixture, TestAssemblyFixture.Create(TestValidationOptions));
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
    /// Hands the assembly-owned <see cref="TestAssemblyFixture"/> back for disposal and clears
    /// the once-per-assembly initialization gate so a later <see cref="InitializeServices"/>
    /// rebuilds from scratch. Called from <see cref="AssemblyLifecycle.Cleanup"/> after the
    /// entire test assembly finishes. Test code should not call this directly.
    /// </summary>
    internal static async Task DisposeAssemblyResourcesAsync()
    {
        TestAssemblyFixture? fixture;
        lock (_initLock)
        {
            fixture = _fixture;
            Volatile.Write(ref _fixture, null);
            _servicesInitialized = false;
        }

        if (fixture is null)
        {
            return;
        }

        await fixture.DisposeAsync().ConfigureAwait(false);
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
        return await Fixture.GetOrLoadWorkspaceIdAsync(solutionPath, ct).ConfigureAwait(false);
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
