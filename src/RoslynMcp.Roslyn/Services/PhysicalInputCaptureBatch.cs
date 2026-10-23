using System.Collections.Immutable;


namespace RoslynMcp.Roslyn.Services;

/// <summary>Fresh captures of a fully authorized, materialized phase input set.</summary>
internal static class PhysicalInputCaptureBatch
{
    // Resource cap: four streams and up to 1 MiB requested active read buffers per phase, limited by CPU count.
    private static int MaximumConcurrency => Math.Min(Environment.ProcessorCount, 4);

    internal static Task<ImmutableArray<PhysicalInputIdentity>> CaptureAsync(ImmutableArray<string> paths, CancellationToken ct)
        => RunAsync(paths, PhysicalInputIdentity.CaptureAsync, MaximumConcurrency, ct);

    internal static async Task AssertCurrentAsync(ImmutableArray<PhysicalInputIdentity> expected, CancellationToken ct)
    {
        await RunAsync(expected, async (input, token) =>
        {
            var actual = await PhysicalInputIdentity.CaptureAsync(input.FilePath, token).ConfigureAwait(false);
            PhysicalInputIdentity.AssertMatches(input, actual);
            return actual;
        }, MaximumConcurrency, ct).ConfigureAwait(false);
    }
    // Narrow internal seam for deterministic scheduling/failure tests. Production supplies only physical capture and its typed identity comparison; no policy callback.
    internal static async Task<ImmutableArray<PhysicalInputIdentity>> RunAsync<TInput>(ImmutableArray<TInput> paths,
        Func<TInput, CancellationToken, Task<PhysicalInputIdentity>> capture, int capacity, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        var result = ImmutableArray.CreateBuilder<PhysicalInputIdentity>(paths.Length);
        for (var offset = 0; offset < paths.Length;)
        {
            ct.ThrowIfCancellationRequested();
            var count = Math.Min(capacity, paths.Length - offset);
            var tasks = new Task<PhysicalInputIdentity>[count];
            for (var index = 0; index < count; index++)
            {
                var path = paths[offset + index];
                // Do not cancel task scheduling: every dispatched operation must be awaited.
                tasks[index] = Task.Run(() => capture(path, ct), CancellationToken.None);
            }
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch
            {
                // WhenAll has drained and observed every task. Rethrow by input order,
                // rather than completion order, preserving the first failing input.
                foreach (var task in tasks) await task.ConfigureAwait(false);
                throw;
            }
            foreach (var task in tasks) result.Add(await task.ConfigureAwait(false));
            offset += count;
        }
        return result.MoveToImmutable();
    }
}
