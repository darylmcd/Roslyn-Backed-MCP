using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Roslyn;

/// <summary>
/// Extension methods for registering Roslyn-backed services with the <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Roslyn workspace, symbol, analysis, refactoring, and mutation services
    /// as singletons.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddRoslynServices(this IServiceCollection services)
    {
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton<ICompilationCache, CompilationCache>();
        services.AddSingleton<IWorkspaceExecutionGate>(sp =>
        {
            var opts = sp.GetService<ExecutionGateOptions>() ?? new ExecutionGateOptions();
            return new WorkspaceExecutionGate(opts, sp.GetRequiredService<IWorkspaceManager>());
        });
        services.AddSingleton<IDotnetCommandRunner, DotnetCommandRunner>();
        services.AddSingleton<IGatedCommandExecutor, GatedCommandExecutor>();
               services.AddSingleton<IPreviewStore>(sp =>
        {
            var (maxEntries, ttl) = ResolvePreviewStoreConfiguration(sp);
            return new PreviewStore(maxEntries, ttl);
        });
        services.AddSingleton<IProjectMutationPreviewStore>(sp =>
        {
            var (maxEntries, ttl) = ResolvePreviewStoreConfiguration(sp);
            return new ProjectMutationPreviewStore(maxEntries, ttl);
        });
        services.AddSingleton<ICompositePreviewStore>(sp =>
        {
            var (maxEntries, ttl) = ResolvePreviewStoreConfiguration(sp);
            var opts = sp.GetService<PreviewStoreOptions>() ?? new PreviewStoreOptions();
            // Item 6: opt-in disk persistence for cross-process token redemption.
            PersistentCompositeStorage? disk = null;
            if (!string.IsNullOrWhiteSpace(opts.PersistDirectory))
            {
                disk = new PersistentCompositeStorage(opts.PersistDirectory!, ttl);
            }
            return new CompositePreviewStore(maxEntries, ttl, disk);
        });
        services.AddSingleton<ISymbolNavigationService, SymbolNavigationService>();
        services.AddSingleton<ISymbolSearchService, SymbolSearchService>();
        services.AddSingleton<IReferenceService, ReferenceService>();
        services.AddSingleton<ISymbolRelationshipService, SymbolRelationshipService>();
        services.AddSingleton<IMutationAnalysisService, MutationAnalysisService>();
        services.AddSingleton<IDiagnosticService, DiagnosticService>();
        services.AddSingleton<IBuildService, BuildService>();
        services.AddSingleton<ITestRunnerService, TestRunnerService>();
        services.AddSingleton<ITestDiscoveryService, TestDiscoveryService>();
        services.AddSingleton<IRefactoringService, RefactoringService>();
        services.AddSingleton<ICompletionService, CompletionService>();
        services.AddSingleton<ICodeActionService, CodeActionService>();
        services.AddSingleton<IUnusedCodeAnalyzer, UnusedCodeAnalyzer>();
        services.AddSingleton<IDuplicateMethodDetectorService, DuplicateMethodDetectorService>();
        services.AddSingleton<ICodeMetricsService, CodeMetricsService>();
        services.AddSingleton<INamespaceDependencyService, NamespaceDependencyService>();
        services.AddSingleton<IDiRegistrationService, DiRegistrationService>();
        services.AddSingleton<INuGetDependencyService, NuGetDependencyService>();
        services.AddSingleton<IMsBuildEvaluationService, MsBuildEvaluationService>();
        services.AddSingleton<ISuppressionService, SuppressionService>();
        services.AddSingleton<ICodePatternAnalyzer, CodePatternAnalyzer>();
        services.AddSingleton<IEditService, EditService>();
        services.AddSingleton<IFileOperationService, FileOperationService>();
        services.AddSingleton<IProjectMutationService, ProjectMutationService>();
        services.AddSingleton<ICrossProjectRefactoringService, CrossProjectRefactoringService>();
        services.AddSingleton<IPackageMigrationOrchestrator, PackageMigrationOrchestrator>();
        services.AddSingleton<IClassSplitOrchestrator, ClassSplitOrchestrator>();
        services.AddSingleton<IExtractAndWireOrchestrator, ExtractAndWireOrchestrator>();
        services.AddSingleton<ICompositeApplyOrchestrator, CompositeApplyOrchestrator>();
        services.AddSingleton<IScaffoldingService, ScaffoldingService>();
        services.AddSingleton<IDeadCodeService, DeadCodeService>();
        services.AddSingleton<ISyntaxService, SyntaxService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();
        services.AddSingleton<ISecurityDiagnosticService, SecurityDiagnosticService>();
        services.AddSingleton<IConsumerAnalysisService, ConsumerAnalysisService>();
        services.AddSingleton<ICohesionAnalysisService, CohesionAnalysisService>();
        services.AddSingleton<ITypeMoveService, TypeMoveService>();
        services.AddSingleton<IInterfaceExtractionService, InterfaceExtractionService>();
        services.AddSingleton<IBulkRefactoringService, BulkRefactoringService>();
        services.AddSingleton<ITypeExtractionService, TypeExtractionService>();
        services.AddSingleton<IUndoService, UndoService>();
        services.AddSingleton<IFlowAnalysisService, FlowAnalysisService>();
        services.AddSingleton<ICompileCheckService, CompileCheckService>();
        services.AddSingleton<IAnalyzerInfoService, AnalyzerInfoService>();
        services.AddSingleton<ICodeFixProviderRegistry, CodeFixProviderRegistry>();
        services.AddSingleton<IFixAllService, FixAllService>();
        services.AddSingleton<IInterfaceMemberRemovalOrchestrator, InterfaceMemberRemovalOrchestrator>();
        services.AddSingleton<IFormatVerifyService, FormatVerifyService>();
        services.AddSingleton<IOperationService, OperationService>();
        services.AddSingleton<ISnippetAnalysisService, SnippetAnalysisService>();
        services.AddSingleton<IScriptingService, ScriptingService>();
        services.AddSingleton<IEditorConfigService, EditorConfigService>();
        services.AddSingleton<IExtractMethodService, ExtractMethodService>();
        services.AddSingleton<IChangeTracker, ChangeTracker>();
        services.AddSingleton<IRefactoringSuggestionService, RefactoringSuggestionService>();
        services.AddSingleton<IRestructureService, RestructureService>();
        services.AddSingleton<IStringLiteralReplaceService, StringLiteralReplaceService>();
        services.AddSingleton<IImpactSweepService, ImpactSweepService>();
        services.AddSingleton<ITestReferenceMapService, TestReferenceMapService>();
        services.AddSingleton<IWorkspaceValidationService, WorkspaceValidationService>();
        services.AddSingleton<IChangeSignatureService, ChangeSignatureService>();
        services.AddSingleton<ISymbolRefactorService, SymbolRefactorService>();
        services.AddSingleton<IExceptionFlowService, ExceptionFlowService>();
        return services;
    }

    private static (int MaxEntries, TimeSpan Ttl) ResolvePreviewStoreConfiguration(IServiceProvider serviceProvider)
    {
        var opts = serviceProvider.GetService<PreviewStoreOptions>() ?? new PreviewStoreOptions();
        var ttlMinutes = opts.TtlMinutes > 0 ? opts.TtlMinutes : 5;
        return (opts.MaxEntries, TimeSpan.FromMinutes(ttlMinutes));
    }
}
