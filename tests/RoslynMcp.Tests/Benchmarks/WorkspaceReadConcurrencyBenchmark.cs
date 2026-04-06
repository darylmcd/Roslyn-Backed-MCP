using System.Diagnostics;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Benchmarks;

/// <summary>
/// Wall-clock benchmark proving the perf unlock the design note describes:
/// under <c>UseReaderWriterLock = true</c>, N parallel reads against the same workspace
/// complete in roughly the time of one. Under <c>UseReaderWriterLock = false</c> they
/// serialize and take ~N times as long.
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
    public async Task ParallelReads_FlagOn_AreNearOneReadsWallTime()
    {
        var gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions { UseReaderWriterLock = true },
            new BenchmarkWorkspaceManager());

        var elapsed = await TimeParallelReadsAsync(gate, ParallelReaders, ReadDuration);

        var maxAllowed = TimeSpan.FromMilliseconds(ReadDuration.TotalMilliseconds * 1.5);
        Assert.IsTrue(elapsed <= maxAllowed,
            $"With RW lock on, {ParallelReaders} parallel reads should overlap (elapsed={elapsed.TotalMilliseconds:F0}ms, " +
            $"max={maxAllowed.TotalMilliseconds:F0}ms, single-read={ReadDuration.TotalMilliseconds:F0}ms).");
    }

    [TestMethod]
    [TestCategory("Benchmark")]
    public async Task ParallelReads_FlagOff_SerializeBehindLegacyMutex()
    {
        var gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions { UseReaderWriterLock = false },
            new BenchmarkWorkspaceManager());

        var elapsed = await TimeParallelReadsAsync(gate, ParallelReaders, ReadDuration);

        // Under the legacy mutex, N parallel reads should take roughly N × single-read time.
        // Use 0.7× as a generous lower bound to avoid CI flakiness.
        var minExpected = TimeSpan.FromMilliseconds(ReadDuration.TotalMilliseconds * ParallelReaders * 0.7);
        Assert.IsTrue(elapsed >= minExpected,
            $"With RW lock off, {ParallelReaders} parallel reads should serialize (elapsed={elapsed.TotalMilliseconds:F0}ms, " +
            $"min={minExpected.TotalMilliseconds:F0}ms).");
    }

    private static async Task<TimeSpan> TimeParallelReadsAsync(
        WorkspaceExecutionGate gate,
        int parallelism,
        TimeSpan readDuration)
    {
        // Warm up the workspace lock entry under both code paths so the first read does not
        // pay creation cost.
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
        public Microsoft.CodeAnalysis.Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Microsoft.CodeAnalysis.Solution newSolution) => throw new NotSupportedException();
    }
}
