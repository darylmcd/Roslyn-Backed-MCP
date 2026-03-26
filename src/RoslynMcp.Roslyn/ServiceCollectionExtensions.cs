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
        services.AddSingleton<IPreviewStore, PreviewStore>();
        services.AddSingleton<IProjectMutationPreviewStore, ProjectMutationPreviewStore>();
        services.AddSingleton<ICompositePreviewStore, CompositePreviewStore>();
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton<ISymbolService, SymbolService>();
        services.AddSingleton<IDiagnosticService, DiagnosticService>();
        services.AddSingleton<IValidationService, ValidationService>();
        services.AddSingleton<IRefactoringService, RefactoringService>();
        services.AddSingleton<ICompletionService, CompletionService>();
        services.AddSingleton<ICodeActionService, CodeActionService>();
        services.AddSingleton<IAdvancedAnalysisService, AdvancedAnalysisService>();
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
        return services;
    }
}
