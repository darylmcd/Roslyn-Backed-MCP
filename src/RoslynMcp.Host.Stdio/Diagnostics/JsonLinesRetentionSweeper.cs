using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Runtime;

namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>Outcome of one startup sweep. Counts are files; <paramref name="DeletedBytes"/> is their total size.</summary>
/// <param name="Deleted">Files this sweep deleted.</param>
/// <param name="DeletedBytes">Total size of the deleted files.</param>
/// <param name="SkippedLive">Files left alone because their pid is a live process (the own pid is not counted).</param>
/// <param name="SkippedHeld">Files left alone because the delete raised an <see cref="IOException"/> (held open).</param>
/// <param name="Failed">Files whose delete raised <see cref="UnauthorizedAccessException"/>.</param>
internal readonly record struct JsonLinesSweepResult(int Deleted, long DeletedBytes, int SkippedLive, int SkippedHeld, int Failed);

/// <summary>
/// Startup sweep of the JSON-lines files other processes left in the shared log directory. It deletes a
/// file only when the segment writer could have emitted its name (<see cref="JsonLinesSegmentWriter.TryParseFileName"/>),
/// its pid is neither this process nor a live one, and it was not written within the last
/// <see cref="GracePeriod"/>. Candidates older than <see cref="JsonLinesRetentionOptions.RetentionDays"/> go
/// first; then the oldest remaining candidates go until the total size of every sink file, protected ones
/// included, fits <see cref="JsonLinesRetentionOptions.MaxTotalBytes"/>. The sweep never rotates and never
/// writes: <see cref="JsonLinesSegmentWriter"/> stays the only rotation authority.
/// </summary>
/// <remarks>
/// <para>Limits, all documented in <c>docs/stdio-client-integration.md</c>:</para>
/// <list type="bullet">
/// <item>Detecting a file "held open by another live host" through <see cref="IOException"/> on delete works
/// on Windows only. On Linux and macOS unlink succeeds on an open file, so protection there is liveness plus
/// the grace period.</item>
/// <item>Liveness is a pid lookup in this process's pid namespace. A live host in another namespace (for
/// example a container sharing the directory) can look dead; only the grace period protects a file it is
/// actively writing.</item>
/// <item>Live and own bytes count toward the cap but are never deleted, so a cap smaller than them trims
/// every dead candidate, oldest first, at the next start.</item>
/// <item>A pid reused by an unrelated live process keeps that pid's old files: over-retention bounded by
/// <c>MaxSegments + 1</c> files per pid.</item>
/// </list>
/// </remarks>
internal static class JsonLinesRetentionSweeper
{
    /// <summary>A file written more recently than this is never deleted, whatever its pid looks like.</summary>
    internal static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(60);

    private static readonly Action<ILogger, int, long, int, int, int, Exception?> LogSweepCompleted =
        LoggerMessage.Define<int, long, int, int, int>(
            LogLevel.Information,
            new EventId(LogEvents.RetentionSweepCompleted, LogEvents.RetentionSweepCompletedName),
            "log retention sweep completed: deleted={Deleted} deletedBytes={DeletedBytes} " +
            "skippedLive={SkippedLive} skippedHeld={SkippedHeld} failed={Failed}");

    private static readonly Action<ILogger, string, Exception?> LogSweepFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(LogEvents.RetentionSweepFailed, LogEvents.RetentionSweepFailedName),
            "log retention sweep failed: exceptionType={ExceptionType}");

    /// <summary>
    /// Sweeps <paramref name="directory"/>. A missing directory is a no-op. An enumeration failure propagates;
    /// <see cref="SweepAndLog"/> reports it. <paramref name="deleteFile"/> is the delete seam (default
    /// <see cref="File.Delete(string)"/>): <see cref="FileNotFoundException"/> or
    /// <see cref="DirectoryNotFoundException"/> means the file vanished (benign, uncounted),
    /// <see cref="IOException"/> means held, <see cref="UnauthorizedAccessException"/> means failed.
    /// </summary>
    internal static JsonLinesSweepResult Sweep(
        string directory,
        int ownPid,
        JsonLinesRetentionOptions options,
        DateTime nowUtc,
        Func<int, bool> isProcessAlive,
        Action<string>? deleteFile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(isProcessAlive);
        deleteFile ??= File.Delete;

        if (!Directory.Exists(directory))
        {
            return default;
        }

        var graceCutoff = nowUtc - GracePeriod;
        var ageCutoff = nowUtc - TimeSpan.FromDays(options.RetentionDays);
        var liveness = new Dictionary<int, bool>();
        var candidates = new List<(string Path, string Name, long Length, DateTime LastWriteUtc)>();
        var totalBytes = 0L;
        var skippedLive = 0;

        foreach (var file in new DirectoryInfo(directory).EnumerateFiles(JsonLinesSegmentWriter.FileStemPrefix + "*.jsonl"))
        {
            if (!JsonLinesSegmentWriter.TryParseFileName(file.Name, out var pid))
            {
                continue;
            }

            totalBytes += file.Length;
            if (pid == ownPid)
            {
                continue;
            }

            var lastWriteUtc = file.LastWriteTimeUtc;
            if (lastWriteUtc > graceCutoff)
            {
                continue;
            }

            if (!liveness.TryGetValue(pid, out var alive))
            {
                alive = isProcessAlive(pid);
                liveness[pid] = alive;
            }

            if (alive)
            {
                skippedLive++;
                continue;
            }

            candidates.Add((file.FullName, file.Name, file.Length, lastWriteUtc));
        }

        int deleted = 0, skippedHeld = 0, failed = 0;
        var deletedBytes = 0L;

        void TryDelete((string Path, string Name, long Length, DateTime LastWriteUtc) candidate)
        {
            try
            {
                deleteFile(candidate.Path);
                deleted++;
                deletedBytes += candidate.Length;
                totalBytes -= candidate.Length;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Vanished between enumeration and delete (another sweep, or the owner): benign, not ours to count.
                totalBytes -= candidate.Length;
            }
            catch (IOException)
            {
                // Held open by another process. Only Windows reports this: elsewhere unlink succeeds on an open file.
                skippedHeld++;
            }
            catch (UnauthorizedAccessException)
            {
                failed++;
            }
        }

        var expired = candidates.Where(candidate => candidate.LastWriteUtc < ageCutoff).ToList();
        foreach (var candidate in expired)
        {
            TryDelete(candidate);
        }

        var remaining = candidates
            .Except(expired)
            .OrderBy(static candidate => candidate.LastWriteUtc)
            .ThenBy(static candidate => candidate.Name, StringComparer.Ordinal);
        foreach (var candidate in remaining)
        {
            if (totalBytes <= options.MaxTotalBytes)
            {
                break;
            }

            TryDelete(candidate);
        }

        return new JsonLinesSweepResult(deleted, deletedBytes, skippedLive, skippedHeld, failed);
    }

    /// <summary>
    /// Runs <see cref="Sweep"/> with the real clock and process liveness and logs one summary record
    /// (or one warning with the exception type only). Never throws, so a fire-and-forget task over it cannot fault unobserved.
    /// </summary>
    internal static void SweepAndLog(string directory, int ownPid, JsonLinesRetentionOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        try
        {
            var result = Sweep(directory, ownPid, options, TimeProvider.System.GetUtcNow().UtcDateTime, IsProcessAlive);
            LogSweepCompleted(
                logger, result.Deleted, result.DeletedBytes, result.SkippedLive, result.SkippedHeld, result.Failed, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSweepFailed(logger, ex.GetType().Name, null);
        }
    }

    /// <summary>
    /// True unless the pid is provably gone. A process lookup that fails for any reason other than "no such
    /// process" counts as live: when liveness is unknown, the safe answer is to keep the file.
    /// </summary>
    internal static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return ProcessExecutablePathResolver.Resolve(process).Status != ProcessExecutablePathStatus.Exited;
        }
        catch (ArgumentException)
        {
            // GetProcessById: no process with this id is running.
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Access denied, platform failure or any other error: liveness is unknown, so treat it as live.
            return true;
        }
    }
}
