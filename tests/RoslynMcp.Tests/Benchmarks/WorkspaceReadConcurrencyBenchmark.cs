using System.Diagnostics;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Benchmarks;

/// <summary>
/// Wall-clock benchmark proving the perf characteristic of the per-workspace RW lock:
/// N parallel reads against the same workspace complete in roughly the time of one read.
/// </summary>
/// <remarks>
/// Marked with <see cref="TestCategoryAttribute"/> = <c>"Benchmark"</c> so it does not run
/// in the default <c>dotnet test</c> invocation. Run explicitly with:
/// <c>dotnet test --filter "TestCategory=Benchmark"</c>.
///
/// <para>
/// Uses a fake workspace manager and a synthetic <c>Task.Delay</c>-based action so the
/// benchmark is hermetic and does not depend on a loaded sample solution.
/// </para>
/// </remarks>
[TestClass]
public sealed class WorkspaceReadConcurrencyBenchmark
{
    private const int ParallelReaders = 4;
    private static readonly TimeSpan ReadDuration = TimeSpan.FromMilliseconds(200);

    [TestMethod]
    [TestCategory("Benchmark")]
    public async Task ParallelReads_AreNearOneReadsWallTime()
    {
        var gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions(),
            new BenchmarkWorkspaceManager());

        var elapsed = await TimeParallelReadsAsync(gate, ParallelReaders, ReadDuration);

        var maxAllowed = TimeSpan.FromMilliseconds(ReadDuration.TotalMilliseconds * 1.5);
        Assert.IsTrue(elapsed <= maxAllowed,
            $"{ParallelReaders} parallel reads should overlap on the per-workspace RW lock " +
            $"(elapsed={elapsed.TotalMilliseconds:F0}ms, max={maxAllowed.TotalMilliseconds:F0}ms, " +
            $"single-read={ReadDuration.TotalMilliseconds:F0}ms).");
    }

    private static async Task<TimeSpan> TimeParallelReadsAsync(
        WorkspaceExecutionGate gate,
        int parallelism,
        TimeSpan readDuration)
    {
        // Warm up the workspace lock entry so the first read does not pay creation cost.
        await gate.RunReadAsync("ws", _ => Task.FromResult(0), CancellationToken.None);

        var sw = Stopwatch.StartNew();
        var tasks = new Task[parallelism];
        for (var i = 0; i < parallelism; i++)
        {
            tasks[i] = gate.RunReadAsync("ws", async ct =>
            {
                await Task.Delay(readDuration, ct);
                return 0;
            }, CancellationToken.None);
        }
        await Task.WhenAll(tasks);
        sw.Stop();
        return sw.Elapsed;
    }

    private sealed class BenchmarkWorkspaceManager : Core.Services.IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }

        public bool ContainsWorkspace(string workspaceId) => true;

        public Task<Core.Models.WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<Core.Models.WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool Close(string workspaceId) => true;
        public IReadOnlyList<Core.Models.WorkspaceStatusDto> ListWorkspaces() => [];
        public Core.Models.WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<Core.Models.WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Core.Models.ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<Core.Models.GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => Task.FromResult<string?>(null);
        public int GetCurrentVersion(string workspaceId) => 1;
        public void RestoreVersion(string workspaceId, int version) { }
        public Microsoft.CodeAnalysis.Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Microsoft.CodeAnalysis.Solution newSolution) => throw new NotSupportedException();
    }
}
