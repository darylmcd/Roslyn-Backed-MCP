using Company.RoslynMcp.Core.Services;
using Company.RoslynMcp.Roslyn.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Company.RoslynMcp.Roslyn;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRoslynServices(this IServiceCollection services)
    {
        services.AddSingleton<IWorkspaceExecutionGate, WorkspaceExecutionGate>();
        services.AddSingleton<IDotnetCommandRunner, DotnetCommandRunner>();
        services.AddSingleton<IPreviewStore, PreviewStore>();
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton<ISymbolService, SymbolService>();
        services.AddSingleton<IDiagnosticService, DiagnosticService>();
        services.AddSingleton<IValidationService, ValidationService>();
        services.AddSingleton<IRefactoringService, RefactoringService>();
        services.AddSingleton<ICompletionService, CompletionService>();
        services.AddSingleton<ICodeActionService, CodeActionService>();
        services.AddSingleton<IAdvancedAnalysisService, AdvancedAnalysisService>();
        services.AddSingleton<IEditService, EditService>();
        services.AddSingleton<ISyntaxService, SyntaxService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();
        return services;
    }
}
