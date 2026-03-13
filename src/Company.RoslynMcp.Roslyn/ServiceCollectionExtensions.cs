using Company.RoslynMcp.Core.Services;
using Company.RoslynMcp.Roslyn.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Company.RoslynMcp.Roslyn;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRoslynServices(this IServiceCollection services)
    {
        services.AddSingleton<IPreviewStore, PreviewStore>();
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton<ISymbolService, SymbolService>();
        services.AddSingleton<IDiagnosticService, DiagnosticService>();
        services.AddSingleton<IRefactoringService, RefactoringService>();
        return services;
    }
}
