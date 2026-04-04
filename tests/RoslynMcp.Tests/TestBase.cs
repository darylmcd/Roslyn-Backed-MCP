using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Tests;

public abstract class TestBase
{
    private static readonly object _initLock = new();
    private static bool _msbuildInitialized;

    protected static IPreviewStore PreviewStore { get; private set; } = null!;
    protected static WorkspaceManager WorkspaceManager { get; private set; } = null!;
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
    protected static DependencyAnalysisService DependencyAnalysisService { get; private set; } = null!;
    protected static CodePatternAnalyzer CodePatternAnalyzer { get; private set; } = null!;
    protected static EditService EditService { get; private set; } = null!;
    protected static FileOperationService FileOperationService { get; private set; } = null!;
    protected static ProjectMutationService ProjectMutationService { get; private set; } = null!;
    protected static CrossProjectRefactoringService CrossProjectRefactoringService { get; private set; } = null!;
    protected static OrchestrationService OrchestrationService { get; private set; } = null!;
    protected static ScaffoldingService ScaffoldingService { get; private set; } = null!;
    protected static DeadCodeService DeadCodeService { get; private set; } = null!;
    protected static SyntaxService SyntaxService { get; private set; } = null!;
    protected static WorkspaceExecutionGate WorkspaceExecutionGate { get; private set; } = null!;
    protected static DotnetCommandRunner DotnetCommandRunner { get; private set; } = null!;
    protected static GatedCommandExecutor GatedCommandExecutor { get; private set; } = null!;
    protected static BulkRefactoringService BulkRefactoringService { get; private set; } = null!;
    protected static CohesionAnalysisService CohesionAnalysisService { get; private set; } = null!;
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
    protected static string RepositoryRootPath { get; private set; } = null!;
    protected static string SampleSolutionPath { get; private set; } = null!;
    protected static string BuildFailureSolutionPath { get; private set; } = null!;
    protected static string GeneratedDocumentSolutionPath { get; private set; } = null!;

    protected static void InitializeServices()
    {
        lock (_initLock)
        {
            if (!_msbuildInitialized)
            {
                MsBuildInitializer.EnsureInitialized();
                _msbuildInitialized = true;
            }
        }

        PreviewStore = new PreviewStore();
        WorkspaceManager = new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            PreviewStore,
            new FileWatcherService(NullLogger<FileWatcherService>.Instance));
        WorkspaceExecutionGate = new WorkspaceExecutionGate(new ExecutionGateOptions(), WorkspaceManager);
        DotnetCommandRunner = new DotnetCommandRunner();
        GatedCommandExecutor = new GatedCommandExecutor(
            WorkspaceManager,
            DotnetCommandRunner,
            NullLogger<GatedCommandExecutor>.Instance);
        SymbolNavigationService = new SymbolNavigationService(
            WorkspaceManager,
            NullLogger<SymbolNavigationService>.Instance);
        SymbolSearchService = new SymbolSearchService(
            WorkspaceManager,
            NullLogger<SymbolSearchService>.Instance);
        ReferenceService = new ReferenceService(
            WorkspaceManager,
            NullLogger<ReferenceService>.Instance);
        MutationAnalysisService = new MutationAnalysisService(
            WorkspaceManager,
            NullLogger<MutationAnalysisService>.Instance);
        SymbolRelationshipService = new SymbolRelationshipService(
            WorkspaceManager,
            ReferenceService,
            NullLogger<SymbolRelationshipService>.Instance);
        DiagnosticService = new DiagnosticService(
            WorkspaceManager,
            NullLogger<DiagnosticService>.Instance);
        UndoService = new UndoService();
        RefactoringService = new RefactoringService(
            WorkspaceManager,
            PreviewStore,
            NullLogger<RefactoringService>.Instance,
            UndoService);
        BuildService = new BuildService(
            WorkspaceManager,
            GatedCommandExecutor,
            NullLogger<BuildService>.Instance);
        TestRunnerService = new TestRunnerService(
            WorkspaceManager,
            GatedCommandExecutor,
            NullLogger<TestRunnerService>.Instance);
        TestDiscoveryService = new TestDiscoveryService(
            WorkspaceManager,
            NullLogger<TestDiscoveryService>.Instance);
        CompletionService = new CompletionService(
            WorkspaceManager,
            NullLogger<CompletionService>.Instance);
        CodeActionService = new CodeActionService(
            WorkspaceManager,
            PreviewStore,
            NullLogger<CodeActionService>.Instance);
        UnusedCodeAnalyzer = new UnusedCodeAnalyzer(
            WorkspaceManager,
            NullLogger<UnusedCodeAnalyzer>.Instance);
        CodeMetricsService = new CodeMetricsService(
            WorkspaceManager,
            NullLogger<CodeMetricsService>.Instance);
        DependencyAnalysisService = new DependencyAnalysisService(
            WorkspaceManager,
            GatedCommandExecutor,
            NullLogger<DependencyAnalysisService>.Instance);
        CodePatternAnalyzer = new CodePatternAnalyzer(
            WorkspaceManager,
            NullLogger<CodePatternAnalyzer>.Instance);
        EditService = new EditService(
            WorkspaceManager,
            NullLogger<EditService>.Instance);
        FileOperationService = new FileOperationService(
            WorkspaceManager,
            PreviewStore,
            NullLogger<FileOperationService>.Instance);
        ProjectMutationService = new ProjectMutationService(
            WorkspaceManager,
            new ProjectMutationPreviewStore(),
            NullLogger<ProjectMutationService>.Instance);
        CrossProjectRefactoringService = new CrossProjectRefactoringService(
            WorkspaceManager,
            PreviewStore);
        OrchestrationService = new OrchestrationService(
            WorkspaceManager,
            new CompositePreviewStore(),
            PreviewStore,
            CrossProjectRefactoringService,
            DependencyAnalysisService);
        ScaffoldingService = new ScaffoldingService(
            WorkspaceManager,
            FileOperationService);
        DeadCodeService = new DeadCodeService(
            WorkspaceManager,
            PreviewStore);
        SyntaxService = new SyntaxService(WorkspaceManager);
        BulkRefactoringService = new BulkRefactoringService(
            WorkspaceManager,
            PreviewStore,
            NullLogger<BulkRefactoringService>.Instance);
        CohesionAnalysisService = new CohesionAnalysisService(
            WorkspaceManager,
            NullLogger<CohesionAnalysisService>.Instance);
        ConsumerAnalysisService = new ConsumerAnalysisService(
            WorkspaceManager,
            NullLogger<ConsumerAnalysisService>.Instance);
        TypeExtractionService = new TypeExtractionService(
            WorkspaceManager,
            PreviewStore,
            NullLogger<TypeExtractionService>.Instance);
        TypeMoveService = new TypeMoveService(
            WorkspaceManager,
            PreviewStore,
            NullLogger<TypeMoveService>.Instance);
        FlowAnalysisService = new FlowAnalysisService(
            WorkspaceManager,
            NullLogger<FlowAnalysisService>.Instance);
        CompileCheckService = new CompileCheckService(
            WorkspaceManager,
            NullLogger<CompileCheckService>.Instance);
        AnalyzerInfoService = new AnalyzerInfoService(
            WorkspaceManager,
            NullLogger<AnalyzerInfoService>.Instance);
        FixAllService = new FixAllService(
            WorkspaceManager,
            PreviewStore,
            NullLogger<FixAllService>.Instance);
        OperationService = new OperationService(
            WorkspaceManager,
            NullLogger<OperationService>.Instance);
        SnippetAnalysisService = new SnippetAnalysisService(
            NullLogger<SnippetAnalysisService>.Instance);
        ScriptingService = new ScriptingService(
            NullLogger<ScriptingService>.Instance);
        EditorConfigService = new EditorConfigService(
            WorkspaceManager,
            NullLogger<EditorConfigService>.Instance);

        RepositoryRootPath = FindRepositoryRoot();
        SampleSolutionPath = FindFixturePath("SampleSolution", "SampleSolution.slnx", "SampleSolution.sln");
        BuildFailureSolutionPath = FindFixturePath("BuildFailureSolution", "BuildFailureSolution.slnx", "BuildFailureSolution.sln");
        GeneratedDocumentSolutionPath = FindFixturePath("GeneratedDocumentSolution", "GeneratedDocumentSolution.slnx", "GeneratedDocumentSolution.sln");
    }

    protected static void DisposeServices()
    {
        WorkspaceManager?.Dispose();
    }

    protected static string CreateSampleSolutionCopy()
    {
        var sampleRoot = Path.GetDirectoryName(SampleSolutionPath)
            ?? throw new InvalidOperationException("Sample solution root could not be resolved.");
        var tempRoot = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", Guid.NewGuid().ToString("N"));
        CopyDirectory(sampleRoot, tempRoot);
        CopyRepositorySupportFiles(tempRoot);

        var slnxPath = Path.Combine(tempRoot, "SampleSolution.slnx");
        if (File.Exists(slnxPath))
        {
            return slnxPath;
        }

        var slnPath = Path.Combine(tempRoot, "SampleSolution.sln");
        if (File.Exists(slnPath))
        {
            return slnPath;
        }

        throw new InvalidOperationException("Copied sample solution is missing a solution file.");
    }

    protected static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destinationFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destinationFile, overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var destinationSubdirectory = Path.Combine(destinationDir, Path.GetFileName(directory));
            CopyDirectory(directory, destinationSubdirectory);
        }
    }

    private static string FindFixturePath(string fixtureDirectory, params string[] candidateFiles)
    {
        var dir = RepositoryRootPath;
        while (dir is not null)
        {
            foreach (var candidateFile in candidateFiles)
            {
                var candidate = Path.Combine(dir, "samples", fixtureDirectory, candidateFile);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            $"Could not find fixture '{fixtureDirectory}'. Ensure the samples directory exists at the repo root.");
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "RoslynMcp.slnx")) &&
                File.Exists(Path.Combine(dir, "Directory.Build.props")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    private static void CopyRepositorySupportFiles(string destinationRoot)
    {
        foreach (var fileName in new[] { "Directory.Build.props", "Directory.Packages.props", "global.json" })
        {
            var sourcePath = Path.Combine(RepositoryRootPath, fileName);
            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, Path.Combine(destinationRoot, fileName), overwrite: true);
            }
        }
    }
}
