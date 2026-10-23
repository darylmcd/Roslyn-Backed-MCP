using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RenameMetadataRefusalTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MetadataRenameRefusal_PreservesPublicGuidanceWithoutPreviewEffects(bool onSolution)
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(),
            VersionStamp.Create(), "Fixture", "Fixture", LanguageNames.CSharp,
            metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]));
        var manager = new FailClosedWorkspaceManagerStub { GetCurrentSolutionHandler = _ => solution };
        var store = new TrackingPreviewStore(new PreviewStore());
        var service = new RefactoringService(manager, store, NullLogger<RefactoringService>.Instance);
        var locator = SymbolLocator.ByMetadataName("System.String");
        var failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(async () =>
        {
            if (onSolution) await service.PreviewRenameOnSolutionAsync("fixture", solution, locator, "Renamed", CancellationToken.None);
            else await service.PreviewRenameAsync("fixture", locator, "Renamed", CancellationToken.None);
        });
        StringAssert.Contains(failure.Message, "source declaration");
        Assert.IsEmpty(store.GetRedeemableTokens("fixture"));
        Assert.AreSame(solution, manager.GetCurrentSolution("fixture"));
    }
}
