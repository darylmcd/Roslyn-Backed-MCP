using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Assembly-test services resolved from the production Roslyn composition root with explicit
/// test options. The owned provider is the sole disposal root; concrete TestBase-facing views
/// are bridged from their production interface singletons so their identities cannot drift.
/// </summary>
internal sealed class TestServiceContainer : IDisposable
{
    private readonly ServiceProvider _provider;
    private int _disposeState;

    private TestServiceContainer(ServiceProvider provider)
    {
        _provider = provider;
    }

    public required IPreviewStore PreviewStore { get; init; }
    public required WorkspaceManager WorkspaceManager { get; init; }
    public required IFileWatcherService FileWatcher { get; init; }
    public required SymbolNavigationService SymbolNavigationService { get; init; }
    public required SymbolSearchService SymbolSearchService { get; init; }
    public required ReferenceService ReferenceService { get; init; }
    public required SymbolRelationshipService SymbolRelationshipService { get; init; }
    public required MutationAnalysisService MutationAnalysisService { get; init; }
    public required TypeConsumersService TypeConsumersService { get; init; }
    public required SemanticGrepService SemanticGrepService { get; init; }
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
    public required WorkspaceDriftService WorkspaceDriftService { get; init; }
    public required ParameterObjectService ParameterObjectService { get; init; }

    /// <summary>Number of successful first-entry attempts into the provider disposal root.</summary>
    internal int DisposalEntryCount => Volatile.Read(ref _disposeState);

    public static TestServiceContainer Create(ValidationServiceOptions validationOptions)
    {
        ArgumentNullException.ThrowIfNull(validationOptions);

        var services = new ServiceCollection();
        services.AddLogging(static logging => logging.ClearProviders());
        services.AddSingleton(new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 64 });
        services.AddSingleton(validationOptions);
        services.AddSingleton(new PreviewStoreOptions());
        services.AddSingleton(new ExecutionGateOptions { RateLimitMaxRequests = int.MaxValue });
        services.AddSingleton(new ScriptingServiceOptions());
        services.AddRoslynServices();

        // Tests need the production graph but not a second copy of its watcher registration.
        // Factory registration makes this watcher provider-owned and therefore disposed exactly
        // once when this container, the explicit test disposal root, is released.
        services.RemoveAll<IFileWatcherService>();
        services.AddSingleton<IFileWatcherService>(sp =>
            new FileWatcherService(sp.GetRequiredService<ILogger<FileWatcherService>>()));

        var provider = services.BuildServiceProvider();
        try
        {
            return new TestServiceContainer(provider)
            {
                PreviewStore = provider.GetRequiredService<IPreviewStore>(),
                WorkspaceManager = ResolveConcrete<IWorkspaceManager, WorkspaceManager>(provider),
                FileWatcher = provider.GetRequiredService<IFileWatcherService>(),
                SymbolNavigationService = ResolveConcrete<ISymbolNavigationService, SymbolNavigationService>(provider),
                SymbolSearchService = ResolveConcrete<ISymbolSearchService, SymbolSearchService>(provider),
                ReferenceService = ResolveConcrete<IReferenceService, ReferenceService>(provider),
                SymbolRelationshipService = ResolveConcrete<ISymbolRelationshipService, SymbolRelationshipService>(provider),
                MutationAnalysisService = ResolveConcrete<IMutationAnalysisService, MutationAnalysisService>(provider),
                TypeConsumersService = ResolveConcrete<ITypeConsumersService, TypeConsumersService>(provider),
                SemanticGrepService = ResolveConcrete<ISemanticGrepService, SemanticGrepService>(provider),
                DiagnosticService = ResolveConcrete<IDiagnosticService, DiagnosticService>(provider),
                RefactoringService = ResolveConcrete<IRefactoringService, RefactoringService>(provider),
                BuildService = ResolveConcrete<IBuildService, BuildService>(provider),
                TestRunnerService = ResolveConcrete<ITestRunnerService, TestRunnerService>(provider),
                TestDiscoveryService = ResolveConcrete<ITestDiscoveryService, TestDiscoveryService>(provider),
                CompletionService = ResolveConcrete<ICompletionService, CompletionService>(provider),
                CodeActionService = ResolveConcrete<ICodeActionService, CodeActionService>(provider),
                UnusedCodeAnalyzer = ResolveConcrete<IUnusedCodeAnalyzer, UnusedCodeAnalyzer>(provider),
                CodeMetricsService = ResolveConcrete<ICodeMetricsService, CodeMetricsService>(provider),
                NamespaceDependencyService = ResolveConcrete<INamespaceDependencyService, NamespaceDependencyService>(provider),
                DiRegistrationService = ResolveConcrete<IDiRegistrationService, DiRegistrationService>(provider),
                NuGetDependencyService = ResolveConcrete<INuGetDependencyService, NuGetDependencyService>(provider),
                CodePatternAnalyzer = ResolveConcrete<ICodePatternAnalyzer, CodePatternAnalyzer>(provider),
                EditService = ResolveConcrete<IEditService, EditService>(provider),
                FileOperationService = ResolveConcrete<IFileOperationService, FileOperationService>(provider),
                ProjectMutationService = ResolveConcrete<IProjectMutationService, ProjectMutationService>(provider),
                CrossProjectRefactoringService = ResolveConcrete<ICrossProjectRefactoringService, CrossProjectRefactoringService>(provider),
                PackageMigrationOrchestrator = ResolveConcrete<IPackageMigrationOrchestrator, PackageMigrationOrchestrator>(provider),
                ClassSplitOrchestrator = ResolveConcrete<IClassSplitOrchestrator, ClassSplitOrchestrator>(provider),
                ExtractAndWireOrchestrator = ResolveConcrete<IExtractAndWireOrchestrator, ExtractAndWireOrchestrator>(provider),
                CompositeApplyOrchestrator = ResolveConcrete<ICompositeApplyOrchestrator, CompositeApplyOrchestrator>(provider),
                ScaffoldingService = ResolveConcrete<IScaffoldingService, ScaffoldingService>(provider),
                DeadCodeService = ResolveConcrete<IDeadCodeService, DeadCodeService>(provider),
                SyntaxService = ResolveConcrete<ISyntaxService, SyntaxService>(provider),
                WorkspaceExecutionGate = ResolveConcrete<IWorkspaceExecutionGate, WorkspaceExecutionGate>(provider),
                DotnetCommandRunner = ResolveConcrete<IDotnetCommandRunner, DotnetCommandRunner>(provider),
                GatedCommandExecutor = ResolveConcrete<IGatedCommandExecutor, GatedCommandExecutor>(provider),
                BulkRefactoringService = ResolveConcrete<IBulkRefactoringService, BulkRefactoringService>(provider),
                CohesionAnalysisService = ResolveConcrete<ICohesionAnalysisService, CohesionAnalysisService>(provider),
                CouplingAnalysisService = ResolveConcrete<ICouplingAnalysisService, CouplingAnalysisService>(provider),
                RecordFieldAdditionService = ResolveConcrete<IRecordFieldAdditionService, RecordFieldAdditionService>(provider),
                ConsumerAnalysisService = ResolveConcrete<IConsumerAnalysisService, ConsumerAnalysisService>(provider),
                TypeExtractionService = ResolveConcrete<ITypeExtractionService, TypeExtractionService>(provider),
                TypeMoveService = ResolveConcrete<ITypeMoveService, TypeMoveService>(provider),
                UndoService = ResolveConcrete<IUndoService, UndoService>(provider),
                FlowAnalysisService = ResolveConcrete<IFlowAnalysisService, FlowAnalysisService>(provider),
                CompileCheckService = ResolveConcrete<ICompileCheckService, CompileCheckService>(provider),
                AnalyzerInfoService = ResolveConcrete<IAnalyzerInfoService, AnalyzerInfoService>(provider),
                FixAllService = ResolveConcrete<IFixAllService, FixAllService>(provider),
                OperationService = ResolveConcrete<IOperationService, OperationService>(provider),
                SnippetAnalysisService = ResolveConcrete<ISnippetAnalysisService, SnippetAnalysisService>(provider),
                ScriptingService = ResolveConcrete<IScriptingService, ScriptingService>(provider),
                EditorConfigService = ResolveConcrete<IEditorConfigService, EditorConfigService>(provider),
                MsBuildEvaluationService = ResolveConcrete<IMsBuildEvaluationService, MsBuildEvaluationService>(provider),
                ExtractMethodService = ResolveConcrete<IExtractMethodService, ExtractMethodService>(provider),
                ChangeTracker = ResolveConcrete<IChangeTracker, ChangeTracker>(provider),
                RefactoringSuggestionService = ResolveConcrete<IRefactoringSuggestionService, RefactoringSuggestionService>(provider),
                FormatVerifyService = ResolveConcrete<IFormatVerifyService, FormatVerifyService>(provider),
                InterfaceExtractionService = ResolveConcrete<IInterfaceExtractionService, InterfaceExtractionService>(provider),
                ExceptionFlowService = ResolveConcrete<IExceptionFlowService, ExceptionFlowService>(provider),
                WorkspaceWarmService = ResolveConcrete<IWorkspaceWarmService, WorkspaceWarmService>(provider),
                WorkspaceDriftService = ResolveConcrete<IWorkspaceDriftService, WorkspaceDriftService>(provider),
                ParameterObjectService = ResolveConcrete<IParameterObjectService, ParameterObjectService>(provider)
            };
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
        {
            _provider.Dispose();
        }
    }

    private static TConcrete ResolveConcrete<TService, TConcrete>(IServiceProvider provider)
        where TService : class
        where TConcrete : class, TService =>
        provider.GetRequiredService<TService>() as TConcrete ?? throw new InvalidOperationException(
            $"The production registration for {typeof(TService).Name} must resolve as {typeof(TConcrete).Name}.");
}

[TestClass]
public sealed class TestServiceContainerTests
{
    [TestMethod]
    public void Create_RefactoringSuggestionsReuseExposedAnalysisServices()
    {
        using var services = TestServiceContainer.Create(new ValidationServiceOptions());

        Assert.AreSame(
            services.CodeMetricsService,
            GetRequiredPrivateField(services.RefactoringSuggestionService, "_metricsService"));
        Assert.AreSame(
            services.CohesionAnalysisService,
            GetRequiredPrivateField(services.RefactoringSuggestionService, "_cohesionService"));
        Assert.AreSame(
            services.UnusedCodeAnalyzer,
            GetRequiredPrivateField(services.RefactoringSuggestionService, "_unusedCodeAnalyzer"));
    }

    [TestMethod]
    public void AddRoslynServices_ProviderOwnedWorkspaceManager_DefersWatcherDisposalToProvider()
    {
        var watcher = new CountingFileWatcher();
        var services = new ServiceCollection();
        services.AddLogging(static logging => logging.ClearProviders());
        services.AddSingleton(new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
        services.AddSingleton(new ExecutionGateOptions { RateLimitMaxRequests = int.MaxValue });
        services.AddRoslynServices();
        services.RemoveAll<IFileWatcherService>();
        services.AddSingleton<IFileWatcherService>(_ => watcher);

        var provider = services.BuildServiceProvider();
        try
        {
            var manager = (WorkspaceManager)provider.GetRequiredService<IWorkspaceManager>();

            Assert.AreSame(watcher, provider.GetRequiredService<IFileWatcherService>());
            Assert.AreEqual(1, watcher.RootMissingSubscriberCount,
                "The manager must subscribe while its provider-owned watcher is live.");

            manager.Dispose();

            Assert.AreEqual(0, watcher.RootMissingSubscriberCount,
                "Manager disposal must detach the watcher callback before the provider disposes the watcher.");
            Assert.AreEqual(0, watcher.DisposeCount,
                "A provider-owned watcher must remain alive when its consumer is manually disposed.");
        }
        finally
        {
            provider.Dispose();
        }

        Assert.AreEqual(1, watcher.DisposeCount,
            "The provider must dispose its watcher exactly once after the manager has detached.");
        Assert.AreEqual(0, watcher.RootMissingSubscriberCount,
            "Provider disposal must not retain a callback to an already-disposed manager.");
    }

    private static object GetRequiredPrivateField(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"{instance.GetType().Name} must retain collaborator field '{fieldName}'.");

        var value = field.GetValue(instance);
        Assert.IsNotNull(value, $"{instance.GetType().Name}.{fieldName} must be initialized.");
        return value;
    }

    private sealed class CountingFileWatcher : IFileWatcherService
    {
        private Action<string>? _workspaceRootMissing;
        private int _disposeCount;

        public event Action<string>? WorkspaceRootMissing
        {
            add => _workspaceRootMissing += value;
            remove => _workspaceRootMissing -= value;
        }

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public int RootMissingSubscriberCount => _workspaceRootMissing?.GetInvocationList().Length ?? 0;

        public void Watch(string workspaceId, string workspacePath) { }

        public void Unwatch(string workspaceId) { }

        public bool IsStale(string workspaceId) => false;

        public Task WaitForStaleAsync(string workspaceId, CancellationToken ct) => Task.CompletedTask;

        public string? GetStaleReason(string workspaceId) => null;

        public void MarkStale(string workspaceId, string reason) { }

        public void ClearStale(string workspaceId) { }

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
