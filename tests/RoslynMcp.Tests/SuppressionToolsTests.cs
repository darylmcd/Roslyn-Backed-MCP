using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class SuppressionToolsTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("add_pragma_suppression")]
    [DataRow("pragma_scope_widen")]
    public async Task PragmaWriteTool_OutOfRootPath_RejectsBeforeServiceDispatch(string toolName)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var sanctionedRoot = Path.Combine(
            Path.GetTempPath(),
            "suppression-sanctioned-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sanctionedRoot);

        await using var session = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(
            sanctionedRoot,
            CancellationToken.None);

        try
        {
            var filePath = workspace.GetPath("SampleLib", "AnimalService.cs");
            var service = new RejectDispatchSuppressionWriteService();

            Task InvokeTool() => toolName switch
            {
                "add_pragma_suppression" => SuppressionTools.AddPragmaSuppression(
                    session.Server,
                    WorkspaceExecutionGate,
                    service,
                    workspace.WorkspaceId,
                    filePath,
                    line: 1,
                    diagnosticId: "CS0168",
                    ct: CancellationToken.None),
                "pragma_scope_widen" => SuppressionTools.PragmaScopeWiden(
                    session.Server,
                    WorkspaceExecutionGate,
                    service,
                    workspace.WorkspaceId,
                    filePath,
                    line: 1,
                    diagnosticId: "CS0168",
                    ct: CancellationToken.None),
                _ => throw new AssertFailedException($"Unexpected tool: {toolName}"),
            };

            var error = await Assert.ThrowsExactlyAsync<ArgumentException>(InvokeTool);

            StringAssert.Contains(error.Message, "outside the configured sanctioned-root boundary");
            Assert.AreEqual(0, service.DispatchCount, "Boundary rejection must happen before mutation dispatch.");
        }
        finally
        {
            Directory.Delete(sanctionedRoot, recursive: true);
        }
    }

    private sealed class RejectDispatchSuppressionWriteService : IPinnedSuppressionWriteService
    {
        public int DispatchCount { get; private set; }

        public Task<TextEditResultDto> AddPragmaWarningDisableAsync(
            string workspaceId,
            string filePath,
            int line,
            string diagnosticId,
            string canonicalWritePath,
            CancellationToken ct)
        {
            DispatchCount++;
            throw new AssertFailedException("Out-of-boundary add_pragma_suppression reached the mutation service.");
        }

        public Task<PragmaWidenResultDto> WidenPragmaScopeAsync(
            string workspaceId,
            string filePath,
            int line,
            string diagnosticId,
            string canonicalWritePath,
            CancellationToken ct)
        {
            DispatchCount++;
            throw new AssertFailedException("Out-of-boundary pragma_scope_widen reached the mutation service.");
        }
    }
}
