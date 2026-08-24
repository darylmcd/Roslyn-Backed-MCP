using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class FailClosedTestDoublesTests
{
    [TestMethod]
    public void WorkspaceManager_ConfiguredListIsReturnedAndOtherMembersFailClosed()
    {
        var expected = WorkspaceStatus("workspace-a");
        var manager = new FailClosedWorkspaceManagerStub(expected);

        Assert.AreSame(expected, manager.ListWorkspaces().Single());
        Assert.ThrowsExactly<NotSupportedException>(() => manager.ContainsWorkspace("workspace-a"));
    }

    [TestMethod]
    public async Task WorkspaceGate_ReadPassesThroughCancellationAndOtherRoutesFailClosed()
    {
        IWorkspaceExecutionGate gate = new PassThroughWorkspaceExecutionGate();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            gate.RunReadAsync(
                "workspace-a",
                static ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(0);
                },
                cancellation.Token));

        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            gate.RunWriteAsync("workspace-a", static _ => Task.FromResult(0), CancellationToken.None));
    }

    private static WorkspaceStatusDto WorkspaceStatus(string workspaceId) => new(
        WorkspaceId: workspaceId,
        LoadedPath: "C:/synthetic/loaded.slnx",
        WorkspaceVersion: 1,
        SnapshotToken: workspaceId + ":1",
        LoadedAtUtc: DateTimeOffset.UtcNow,
        ProjectCount: 0,
        DocumentCount: 0,
        Projects: Array.Empty<ProjectStatusDto>(),
        IsLoaded: true,
        IsStale: false,
        WorkspaceDiagnostics: Array.Empty<DiagnosticDto>());
}
