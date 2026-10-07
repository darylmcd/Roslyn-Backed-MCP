using RoslynMcp.Core.Services;
namespace RoslynMcp.Tests;

/// <summary>
/// project-filter-unknown-name-format-exception-di regression coverage: <c>format_check</c>,
/// <c>trace_exception_flow</c> and <c>get_di_registrations</c> must reject a non-blank project
/// filter that matches no loaded project (the <c>ProjectFilterHelper.ResolveProjects</c> contract)
/// instead of reporting a clean/empty success. A blank filter still means the whole solution.
/// </summary>
// Read-only: every test calls a read-side service against the assembly-shared SampleSolution
// workspace — no *_apply, reload, close, or on-disk mutation.
[TestClass]
public sealed class ProjectFilterUnknownNameScanTests : SharedWorkspaceTestBase
{
    private const string UnknownProject = """PRIVATE-project "quoted" \candidate solution""";

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
    public async Task FormatCheck_UnknownProjectName_ThrowsInsteadOfReportingClean()
    {
        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            FormatVerifyService.CheckAsync(WorkspaceId, UnknownProject, CancellationToken.None));
        AssertUnknownProjectRejection(ex);
    }

    [TestMethod]
    public async Task FormatCheck_BlankProjectName_ChecksWholeSolution()
    {
        var result = await FormatVerifyService.CheckAsync(WorkspaceId, "  ", CancellationToken.None);
        Assert.IsTrue(result.CheckedDocuments > 0, "A blank projectName must check the whole solution.");
    }

    [TestMethod]
    public async Task TraceExceptionFlow_UnknownScopeProject_Throws()
    {
        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            ExceptionFlowService.TraceExceptionFlowAsync(
                WorkspaceId,
                "System.InvalidOperationException",
                UnknownProject,
                maxResults: null,
                CancellationToken.None));
        AssertUnknownProjectRejection(ex);
    }

    [TestMethod]
    public async Task GetDiRegistrations_UnknownProject_Throws()
    {
        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            DiRegistrationService.GetDiRegistrationsAsync(WorkspaceId, UnknownProject, CancellationToken.None));
        AssertUnknownProjectRejection(ex);
    }

    private static void AssertUnknownProjectRejection(PublicArgumentException ex)
    {
        Assert.AreEqual("projectName", ex.ParamName);
        StringAssert.Contains(ex.PublicMessage, "workspace_status");
        Assert.IsFalse(ex.Message.Contains(UnknownProject, StringComparison.Ordinal));
    }
}
