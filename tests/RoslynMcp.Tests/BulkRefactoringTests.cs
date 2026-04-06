using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class BulkRefactoringTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task BulkReplaceType_Preview_ReturnsChanges()
    {
        var result = await BulkRefactoringService.PreviewBulkReplaceTypeAsync(
            WorkspaceId, "IAnimal", "Shape", null, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.PreviewToken));
    }

    [TestMethod]
    public async Task BulkReplaceType_NonExistentType_ThrowsInvalidOperation()
    {
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            BulkRefactoringService.PreviewBulkReplaceTypeAsync(
                WorkspaceId, "NonExistentType123", "Shape", null, CancellationToken.None));
    }

    [TestMethod]
    public async Task BulkReplaceType_InvalidScope_ThrowsArgument()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            BulkRefactoringService.PreviewBulkReplaceTypeAsync(
                WorkspaceId, "IAnimal", "Shape", "invalid_scope", CancellationToken.None));
    }
}
