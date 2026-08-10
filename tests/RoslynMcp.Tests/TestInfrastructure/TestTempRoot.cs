namespace RoslynMcp.Tests;

/// <summary>
/// Single owner of the temp-directory root every artifact this test assembly creates lives under.
/// <para>
/// The root is scoped to ONE test-assembly process (<see cref="Current"/> embeds the process id),
/// so <c>[AssemblyCleanup]</c> can delete it wholesale without touching a concurrently-running
/// assembly's fixtures. That isolation is the point: every path here used to be built directly
/// under the SHARED parent <c>%TEMP%/RoslynMcpTests</c> while cleanup deleted that parent
/// recursively, so whichever assembly finished first wiped the in-flight fixture trees of every
/// other run — surfacing as <see cref="DirectoryNotFoundException"/> at fixture-write time in
/// tests that had nothing to do with the change under test (row
/// <c>test-temp-root-shared-cleanup-race</c>).
/// </para>
/// <para>
/// Callers combine against <see cref="Current"/> and never re-derive
/// <c>Path.GetTempPath()</c> + <see cref="SharedParentName"/> themselves; a site that does is
/// outside the isolation and re-opens the race.
/// </para>
/// </summary>
internal static class TestTempRoot
{
    /// <summary>Directory name under the OS temp path shared by all runs (each run gets a subdirectory).</summary>
    internal const string SharedParentName = "RoslynMcpTests";

    /// <summary>Prefix identifying a per-run subdirectory of <see cref="SharedParent"/>.</summary>
    internal const string RunDirectoryPrefix = "run-";

    /// <summary>
    /// A run directory older than this is assumed abandoned (a crashed or killed test host) and is
    /// eligible for reaping. Deliberately far longer than any real test run: a LIVE sibling run is
    /// never this old, which is what keeps the reaper from re-introducing the cross-run delete.
    /// </summary>
    internal static readonly TimeSpan StaleRunAge = TimeSpan.FromDays(1);

    /// <summary>The shared parent — enumerated for reaping, never itself deleted.</summary>
    internal static string SharedParent { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), SharedParentName);

    /// <summary>
    /// This process's temp root. Stable for the lifetime of the process and unique across
    /// concurrent runs (process id + a random suffix, so a recycled pid cannot collide).
    /// </summary>
    internal static string Current { get; } = System.IO.Path.Combine(
        SharedParent,
        RunDirectoryPrefix + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>
    /// Deletes THIS run's root only. Safe to call concurrently with other test-assembly processes.
    /// </summary>
    internal static void DeleteCurrent() => TestFixtureFileSystem.DeleteDirectoryIfExists(Current);

    /// <summary>
    /// Best-effort removal of run directories abandoned by earlier crashed hosts, so isolating the
    /// root does not trade a race for an unbounded <c>%TEMP%</c> leak. Only directories older than
    /// <see cref="StaleRunAge"/> are considered, and this run's own root is never a candidate.
    /// </summary>
    internal static void ReapAbandonedRuns() => ReapAbandonedRuns(DateTime.UtcNow);

    /// <summary>Test seam for <see cref="ReapAbandonedRuns()"/>; <paramref name="utcNow"/> anchors the age check.</summary>
    internal static void ReapAbandonedRuns(DateTime utcNow)
    {
        if (!Directory.Exists(SharedParent))
        {
            return;
        }

        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateDirectories(SharedParent, RunDirectoryPrefix + "*");
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return;
        }

        foreach (var candidate in candidates.ToList())
        {
            if (string.Equals(candidate, Current, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsStale(candidate, utcNow))
            {
                continue;
            }

            // A concurrent run may be reaping the same abandoned directory, or still hold a handle
            // inside it; either way losing the race is harmless — the next run retries.
            try
            {
                TestFixtureFileSystem.DeleteDirectoryIfExists(candidate);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool IsStale(string runDirectory, DateTime utcNow)
    {
        try
        {
            // Newest of create/write time: a long run that keeps writing must not look abandoned.
            var lastActivityUtc = Directory.GetLastWriteTimeUtc(runDirectory);
            var createdUtc = Directory.GetCreationTimeUtc(runDirectory);
            if (createdUtc > lastActivityUtc)
            {
                lastActivityUtc = createdUtc;
            }

            return utcNow - lastActivityUtc > StaleRunAge;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Cannot establish age — treat as live rather than risk deleting a running sibling.
            return false;
        }
    }
}
