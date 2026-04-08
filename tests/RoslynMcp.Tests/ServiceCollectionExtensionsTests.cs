using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ServiceCollectionExtensionsTests
{
    [TestMethod]
    public void AddRoslynServices_Resolves_Core_Singletons()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddRoslynServices();

        using var sp = services.BuildServiceProvider();

        Assert.IsNotNull(sp.GetRequiredService<IWorkspaceManager>());
        Assert.IsNotNull(sp.GetRequiredService<IWorkspaceExecutionGate>());
        Assert.IsNotNull(sp.GetRequiredService<ICompileCheckService>());
        Assert.IsNotNull(sp.GetRequiredService<ICodeActionService>());
        Assert.IsNotNull(sp.GetRequiredService<INamespaceDependencyService>());
        Assert.IsNotNull(sp.GetRequiredService<IDiRegistrationService>());
        Assert.IsNotNull(sp.GetRequiredService<INuGetDependencyService>());
        Assert.IsNotNull(sp.GetRequiredService<IDeadCodeService>());
        Assert.IsNotNull(sp.GetRequiredService<IPreviewStore>());
        Assert.IsNotNull(sp.GetRequiredService<IProjectMutationPreviewStore>());
        Assert.IsNotNull(sp.GetRequiredService<ICompositePreviewStore>());
    }
}
