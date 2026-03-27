using RoslynMcp.Core.Services;
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
        services.AddSingleton<IWorkspaceExecutionGate, WorkspaceExecutionGate>();
        services.AddSingleton<IDotnetCommandRunner, DotnetCommandRunner>();
        services.AddSingleton<IPreviewStore>(sp =>
        {
            var opts = sp.GetService<PreviewStoreOptions>() ?? new PreviewStoreOptions();
            return new PreviewStore(opts.MaxEntries);
        });
        services.AddSingleton<IProjectMutationPreviewStore>(sp =>
        {
            var opts = sp.GetService<PreviewStoreOptions>() ?? new PreviewStoreOptions();
            return new ProjectMutationPreviewStore(opts.MaxEntries);
        });
        services.AddSingleton<ICompositePreviewStore>(sp =>
        {
            var opts = sp.GetService<PreviewStoreOptions>() ?? new PreviewStoreOptions();
            return new CompositePreviewStore(opts.MaxEntries);
        });
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
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
        services.AddSingleton<ICodeMetricsService, CodeMetricsService>();
        services.AddSingleton<IDependencyAnalysisService, DependencyAnalysisService>();
        services.AddSingleton<ICodePatternAnalyzer, CodePatternAnalyzer>();
        services.AddSingleton<IEditService, EditService>();
        services.AddSingleton<IFileOperationService, FileOperationService>();
        services.AddSingleton<IProjectMutationService, ProjectMutationService>();
        services.AddSingleton<ICrossProjectRefactoringService, CrossProjectRefactoringService>();
        services.AddSingleton<IOrchestrationService, OrchestrationService>();
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
        return services;
    }
}
