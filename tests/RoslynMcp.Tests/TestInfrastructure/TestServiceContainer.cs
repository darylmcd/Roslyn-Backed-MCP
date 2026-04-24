using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Tests;

internal sealed class TestServiceContainer
{
    public required IPreviewStore PreviewStore { get; init; }
    public required WorkspaceManager WorkspaceManager { get; init; }
    public required IFileWatcherService FileWatcher { get; init; }
    public required SymbolNavigationService SymbolNavigationService { get; init; }
    public required SymbolSearchService SymbolSearchService { get; init; }
    public required ReferenceService ReferenceService { get; init; }
    public required SymbolRelationshipService SymbolRelationshipService { get; init; }
    public required MutationAnalysisService MutationAnalysisService { get; init; }
    public required DiagnosticService DiagnosticService { get; init; }
    public required RefactoringService RefactoringService { get; init; }
    public required BuildService BuildService { get; init; }
    public required TestRunnerService TestRunnerService { get; init; }
    public required TestDiscoveryService TestDiscoveryService { get; init; }
    public required CompletionService CompletionService { get; init; }
    public required CodeActionService CodeActionService { get; init; }
    public required UnusedCodeAnalyzer UnusedCodeAnalyzer { get; init; }
    public required CodeMetricsService CodeMetricsService { get; init; }
    public required NamespaceDependencyService NamespaceDependencyService { get; init; }
    public required DiRegistrationService DiRegistrationService { get; init; }
    public required NuGetDependencyService NuGetDependencyService { get; init; }
    public required CodePatternAnalyzer CodePatternAnalyzer { get; init; }
    public required EditService EditService { get; init; }
    public required FileOperationService FileOperationService { get; init; }
    public required ProjectMutationService ProjectMutationService { get; init; }
    public required CrossProjectRefactoringService CrossProjectRefactoringService { get; init; }
    public required PackageMigrationOrchestrator PackageMigrationOrchestrator { get; init; }
    public required ClassSplitOrchestrator ClassSplitOrchestrator { get; init; }
    public required ExtractAndWireOrchestrator ExtractAndWireOrchestrator { get; init; }
    public required CompositeApplyOrchestrator CompositeApplyOrchestrator { get; init; }
    public required ScaffoldingService ScaffoldingService { get; init; }
    public required DeadCodeService DeadCodeService { get; init; }
    public required SyntaxService SyntaxService { get; init; }
    public required WorkspaceExecutionGate WorkspaceExecutionGate { get; init; }
    public required DotnetCommandRunner DotnetCommandRunner { get; init; }
    public required GatedCommandExecutor GatedCommandExecutor { get; init; }
    public required BulkRefactoringService BulkRefactoringService { get; init; }
    public required CohesionAnalysisService CohesionAnalysisService { get; init; }
    public required CouplingAnalysisService CouplingAnalysisService { get; init; }
    public required RecordFieldAdditionService RecordFieldAdditionService { get; init; }
    public required ConsumerAnalysisService ConsumerAnalysisService { get; init; }
    public required TypeExtractionService TypeExtractionService { get; init; }
    public required TypeMoveService TypeMoveService { get; init; }
    public required UndoService UndoService { get; init; }
    public required FlowAnalysisService FlowAnalysisService { get; init; }
    public required CompileCheckService CompileCheckService { get; init; }
    public required AnalyzerInfoService AnalyzerInfoService { get; init; }
    public required FixAllService FixAllService { get; init; }
    public required OperationService OperationService { get; init; }
    public required SnippetAnalysisService SnippetAnalysisService { get; init; }
    public required ScriptingService ScriptingService { get; init; }
    public required EditorConfigService EditorConfigService { get; init; }
    public required MsBuildEvaluationService MsBuildEvaluationService { get; init; }
    public required ExtractMethodService ExtractMethodService { get; init; }
    public required ChangeTracker ChangeTracker { get; init; }
    public required RefactoringSuggestionService RefactoringSuggestionService { get; init; }
    public required FormatVerifyService FormatVerifyService { get; init; }
    public required InterfaceExtractionService InterfaceExtractionService { get; init; }
    public required ExceptionFlowService ExceptionFlowService { get; init; }
    public required WorkspaceWarmService WorkspaceWarmService { get; init; }

    public static TestServiceContainer Create(ValidationServiceOptions validationOptions)
    {
        var previewStore = new PreviewStore();
        var fileWatcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        var workspaceManager = new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            previewStore,
            fileWatcher,
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 64 });
        var workspaceExecutionGate = new WorkspaceExecutionGate(new ExecutionGateOptions(), workspaceManager);
        var compilationCache = new CompilationCache(workspaceManager);
        var dotnetCommandRunner = new DotnetCommandRunner();
        var gatedCommandExecutor = new GatedCommandExecutor(
            workspaceManager,
            dotnetCommandRunner,
            NullLogger<GatedCommandExecutor>.Instance);
        var referenceService = new ReferenceService(
            workspaceManager,
            NullLogger<ReferenceService>.Instance);
        var mutationAnalysisService = new MutationAnalysisService(
            workspaceManager,
            NullLogger<MutationAnalysisService>.Instance);
        var diagnosticService = new DiagnosticService(
            workspaceManager,
            compilationCache,
            NullLogger<DiagnosticService>.Instance);
        var undoService = new UndoService(NullLogger<UndoService>.Instance, workspaceManager);
        var changeTracker = new ChangeTracker(workspaceManager);
        var msBuildEvaluationService = new MsBuildEvaluationService(workspaceManager);
        var namespaceDependencyService = new NamespaceDependencyService(
            workspaceManager,
            compilationCache,
            NullLogger<NamespaceDependencyService>.Instance);
        var diRegistrationService = new DiRegistrationService(
            workspaceManager,
            compilationCache,
            NullLogger<DiRegistrationService>.Instance);
        var nuGetDependencyService = new NuGetDependencyService(
            workspaceManager,
            gatedCommandExecutor,
            msBuildEvaluationService,
            NullLogger<NuGetDependencyService>.Instance,
            validationOptions);
        var fileOperationService = new FileOperationService(
            workspaceManager,
            previewStore,
            NullLogger<FileOperationService>.Instance);
        var crossProjectRefactoringService = new CrossProjectRefactoringService(
            workspaceManager,
            previewStore);
        var compositePreviewStore = new CompositePreviewStore();

        // semantic-edit-with-compile-check-wrapper: hoist CompileCheckService construction
        // above EditService so EditService can accept it as an optional dependency for
        // the verify+autoRevertOnError pathway.
        var compileCheckService = new CompileCheckService(
            workspaceManager,
            NullLogger<CompileCheckService>.Instance);

        return new TestServiceContainer
        {
            PreviewStore = previewStore,
            WorkspaceManager = workspaceManager,
            FileWatcher = fileWatcher,
            WorkspaceExecutionGate = workspaceExecutionGate,
            DotnetCommandRunner = dotnetCommandRunner,
            GatedCommandExecutor = gatedCommandExecutor,
            SymbolNavigationService = new SymbolNavigationService(
                workspaceManager,
                NullLogger<SymbolNavigationService>.Instance),
            SymbolSearchService = new SymbolSearchService(
                workspaceManager,
                NullLogger<SymbolSearchService>.Instance),
            ReferenceService = referenceService,
            MutationAnalysisService = mutationAnalysisService,
            SymbolRelationshipService = new SymbolRelationshipService(
                workspaceManager,
                referenceService,
                NullLogger<SymbolRelationshipService>.Instance),
            DiagnosticService = diagnosticService,
            UndoService = undoService,
            RefactoringService = new RefactoringService(
                workspaceManager,
                previewStore,
                NullLogger<RefactoringService>.Instance,
                undoService,
                changeTracker,
                new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance),
                new PostApplySymbolResolver()),
            BuildService = new BuildService(
                workspaceManager,
                gatedCommandExecutor,
                NullLogger<BuildService>.Instance,
                validationOptions),
            TestRunnerService = new TestRunnerService(
                workspaceManager,
                gatedCommandExecutor,
                NullLogger<TestRunnerService>.Instance,
                validationOptions),
            TestDiscoveryService = new TestDiscoveryService(
                workspaceManager,
                NullLogger<TestDiscoveryService>.Instance,
                validationOptions),
            CompletionService = new CompletionService(
                workspaceManager,
                NullLogger<CompletionService>.Instance),
            CodeActionService = new CodeActionService(
                workspaceManager,
                previewStore,
                NullLogger<CodeActionService>.Instance),
            UnusedCodeAnalyzer = new UnusedCodeAnalyzer(
                workspaceManager,
                compilationCache,
                NullLogger<UnusedCodeAnalyzer>.Instance),
            CodeMetricsService = new CodeMetricsService(
                workspaceManager,
                NullLogger<CodeMetricsService>.Instance),
            NamespaceDependencyService = namespaceDependencyService,
            DiRegistrationService = diRegistrationService,
            NuGetDependencyService = nuGetDependencyService,
            CodePatternAnalyzer = new CodePatternAnalyzer(
                workspaceManager,
                NullLogger<CodePatternAnalyzer>.Instance),
            EditService = new EditService(
                workspaceManager,
                NullLogger<EditService>.Instance,
                undoService,
                changeTracker,
                previewStore: null,
                compileCheckService: compileCheckService),
            FileOperationService = fileOperationService,
            ProjectMutationService = new ProjectMutationService(
                workspaceManager,
                new ProjectMutationPreviewStore(),
                msBuildEvaluationService,
                NullLogger<ProjectMutationService>.Instance),
            CrossProjectRefactoringService = crossProjectRefactoringService,
            PackageMigrationOrchestrator = new PackageMigrationOrchestrator(workspaceManager, compositePreviewStore),
            ClassSplitOrchestrator = new ClassSplitOrchestrator(workspaceManager, compositePreviewStore),
            ExtractAndWireOrchestrator = new ExtractAndWireOrchestrator(
                workspaceManager,
                compositePreviewStore,
                previewStore,
                crossProjectRefactoringService,
                diRegistrationService),
            CompositeApplyOrchestrator = new CompositeApplyOrchestrator(workspaceManager, compositePreviewStore, changeTracker),
            ScaffoldingService = new ScaffoldingService(
                workspaceManager,
                fileOperationService,
                previewStore),
            DeadCodeService = new DeadCodeService(
                workspaceManager,
                previewStore),
            SyntaxService = new SyntaxService(workspaceManager),
            BulkRefactoringService = new BulkRefactoringService(
                workspaceManager,
                previewStore,
                NullLogger<BulkRefactoringService>.Instance),
            CohesionAnalysisService = new CohesionAnalysisService(
                workspaceManager,
                NullLogger<CohesionAnalysisService>.Instance),
            CouplingAnalysisService = new CouplingAnalysisService(
                workspaceManager,
                NullLogger<CouplingAnalysisService>.Instance),
            RecordFieldAdditionService = new RecordFieldAdditionService(
                workspaceManager,
                NullLogger<RecordFieldAdditionService>.Instance),
            ConsumerAnalysisService = new ConsumerAnalysisService(
                workspaceManager,
                NullLogger<ConsumerAnalysisService>.Instance),
            TypeExtractionService = new TypeExtractionService(
                workspaceManager,
                previewStore,
                NullLogger<TypeExtractionService>.Instance),
            InterfaceExtractionService = new InterfaceExtractionService(
                workspaceManager,
                previewStore,
                NullLogger<InterfaceExtractionService>.Instance),
            TypeMoveService = new TypeMoveService(
                workspaceManager,
                previewStore,
                NullLogger<TypeMoveService>.Instance),
            FlowAnalysisService = new FlowAnalysisService(
                workspaceManager,
                NullLogger<FlowAnalysisService>.Instance),
            CompileCheckService = compileCheckService,
            AnalyzerInfoService = new AnalyzerInfoService(
                workspaceManager,
                NullLogger<AnalyzerInfoService>.Instance),
            FixAllService = new FixAllService(
                workspaceManager,
                previewStore,
                NullLogger<FixAllService>.Instance),
            OperationService = new OperationService(
                workspaceManager,
                NullLogger<OperationService>.Instance),
            SnippetAnalysisService = new SnippetAnalysisService(
                NullLogger<SnippetAnalysisService>.Instance),
            ScriptingService = new ScriptingService(
                NullLogger<ScriptingService>.Instance,
                new ScriptingServiceOptions()),
            EditorConfigService = new EditorConfigService(
                workspaceManager,
                NullLogger<EditorConfigService>.Instance,
                undoService,
                changeTracker),
            MsBuildEvaluationService = msBuildEvaluationService,
            ExtractMethodService = new ExtractMethodService(
                workspaceManager,
                previewStore,
                NullLogger<ExtractMethodService>.Instance),
            ChangeTracker = changeTracker,
            RefactoringSuggestionService = new RefactoringSuggestionService(
                new CodeMetricsService(workspaceManager, NullLogger<CodeMetricsService>.Instance),
                new CohesionAnalysisService(workspaceManager, NullLogger<CohesionAnalysisService>.Instance),
                new UnusedCodeAnalyzer(workspaceManager, compilationCache, NullLogger<UnusedCodeAnalyzer>.Instance),
                NullLogger<RefactoringSuggestionService>.Instance),
            FormatVerifyService = new FormatVerifyService(workspaceManager, NullLogger<FormatVerifyService>.Instance),
            ExceptionFlowService = new ExceptionFlowService(
                workspaceManager,
                NullLogger<ExceptionFlowService>.Instance),
            WorkspaceWarmService = new WorkspaceWarmService(
                workspaceManager,
                NullLogger<WorkspaceWarmService>.Instance)
        };
    }
}