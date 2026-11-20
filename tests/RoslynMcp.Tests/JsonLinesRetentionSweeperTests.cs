using System.Text.Json;
using Microsoft.Extensions.Logging;
using RoslynMcp.Host.Stdio.Diagnostics;

namespace RoslynMcp.Tests;

/// <summary>
/// Pins the startup sweep of other processes' JSON-lines files: age, then oldest-first to the byte cap,
/// never the own pid, a live pid, a held file, a young file or a name the segment writer cannot emit.
/// </summary>
[TestClass]
public sealed class JsonLinesRetentionSweeperTests
{
    private const int OwnPid = 4242;
    private const long Kilo = 1024;
    private static readonly DateTime _now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string _secretSentinel = "SWEEP-SECRET-SENTINEL";

    private static string NewDirectory()
    {
        var directory = Path.Combine(TestTempRoot.Current, nameof(JsonLinesRetentionSweeperTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Seed(string directory, string name, DateTime lastWriteUtc, long bytes = 16)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private static JsonLinesRetentionOptions Options(int days = 14, long maxTotalBytes = JsonLinesRetentionOptions.DefaultMaxTotalBytes) =>
        JsonLinesRetentionOptions.Create(
            JsonLinesRetentionOptions.DefaultMaxSegmentBytes,
            JsonLinesRetentionOptions.DefaultMaxSegments,
            days,
            maxTotalBytes);

    private static JsonLinesSweepResult Sweep(
        string directory,
        JsonLinesRetentionOptions options,
        Func<int, bool>? isProcessAlive = null,
        Action<string>? deleteFile = null,
        int ownPid = OwnPid) =>
        JsonLinesRetentionSweeper.Sweep(directory, ownPid, options, _now, isProcessAlive ?? (static _ => false), deleteFile);

    [TestMethod]
    public void Sweep_DeletesAgedFilesOfDeadPidsAndKeepsTheBoundary()
    {
        var directory = NewDirectory();
        var window = TimeSpan.FromDays(14);
        var aged = Seed(directory, "roslyn-mcp-100.jsonl", _now - window - TimeSpan.FromSeconds(1));
        var agedRotated = Seed(directory, "roslyn-mcp-101.2.jsonl", _now - window - TimeSpan.FromSeconds(1), bytes: 50);
        var boundary = Seed(directory, "roslyn-mcp-102.jsonl", _now - window);
        var recent = Seed(directory, "roslyn-mcp-103.jsonl", _now - TimeSpan.FromDays(1));

        var result = Sweep(directory, Options());

        Assert.IsFalse(File.Exists(aged));
        Assert.IsFalse(File.Exists(agedRotated));
        Assert.IsTrue(File.Exists(boundary), "A file exactly RetentionDays old is kept.");
        Assert.IsTrue(File.Exists(recent));
        Assert.AreEqual(new JsonLinesSweepResult(2, 66, 0, 0, 0), result);
    }

    [TestMethod]
    public void Sweep_NeverDeletesOwnPidEvenWhenAgedAndReportedDead()
    {
        var directory = NewDirectory();
        var ownPid = Environment.ProcessId;
        var old = _now - TimeSpan.FromDays(60);
        var active = Seed(directory, $"roslyn-mcp-{ownPid}.jsonl", old);
        var rotated = Seed(directory, $"roslyn-mcp-{ownPid}.1.jsonl", old);
        var other = Seed(directory, "roslyn-mcp-100.jsonl", old);

        // The liveness probe says dead for everyone: only the own-pid rule can protect these files.
        var result = Sweep(directory, Options(), static _ => false, ownPid: ownPid);

        Assert.IsTrue(File.Exists(active));
        Assert.IsTrue(File.Exists(rotated));
        Assert.IsFalse(File.Exists(other));
        Assert.AreEqual(1, result.Deleted);
    }

    [TestMethod]
    public void Sweep_NeverDeletesFilesOfALivePid()
    {
        var directory = NewDirectory();
        var old = _now - TimeSpan.FromDays(60);
        var live = Seed(directory, "roslyn-mcp-200.jsonl", old);
        var liveRotated = Seed(directory, "roslyn-mcp-200.3.jsonl", old);
        var dead = Seed(directory, "roslyn-mcp-201.jsonl", old);

        var result = Sweep(directory, Options(), static pid => pid == 200);

        Assert.IsTrue(File.Exists(live));
        Assert.IsTrue(File.Exists(liveRotated));
        Assert.IsFalse(File.Exists(dead));
        Assert.AreEqual(1, result.Deleted);
        Assert.AreEqual(2, result.SkippedLive);
    }

    [TestMethod]
    public void Sweep_ProbesLivenessOncePerPid()
    {
        var directory = NewDirectory();
        var old = _now - TimeSpan.FromDays(60);
        Seed(directory, "roslyn-mcp-7.jsonl", old);
        Seed(directory, "roslyn-mcp-7.1.jsonl", old);
        Seed(directory, "roslyn-mcp-7.2.jsonl", old);
        Seed(directory, "roslyn-mcp-8.jsonl", old);
        var probes = new List<int>();

        Sweep(directory, Options(), pid =>
        {
            probes.Add(pid);
            return false;
        });

        CollectionAssert.AreEquivalent(new[] { 7, 8 }, probes);
    }

    [TestMethod]
    public void Sweep_NeverDeletesNamesTheWriterCannotEmit()
    {
        var directory = NewDirectory();
        var old = _now - TimeSpan.FromDays(60);
        string[] names =
        [
            "roslyn-mcp-x.jsonl", "roslyn-mcp-007.jsonl", "roslyn-mcp-5.0.jsonl", "roslyn-mcp-5.01.jsonl",
            "roslyn-mcp-.jsonl", "roslyn-mcp-5.jsonl.bak", "roslyn-mcp-5.json", "other-5.jsonl", "roslyn-mcp-99999999999.jsonl",
            "roslyn-mcp-٥.jsonl",
        ];
        var paths = names.Select(name => Seed(directory, name, old)).ToArray();
        var subdirectory = Path.Combine(directory, "roslyn-mcp-9.jsonl");
        Directory.CreateDirectory(subdirectory);
        Directory.SetLastWriteTimeUtc(subdirectory, old);

        var result = Sweep(directory, Options());

        foreach (var path in paths)
        {
            Assert.IsTrue(File.Exists(path), path);
        }

        Assert.IsTrue(Directory.Exists(subdirectory));
        Assert.AreEqual(default, result);
    }

    [TestMethod]
    [DataRow("roslyn-mcp-1.jsonl", true, 1)]
    [DataRow("roslyn-mcp-1234.jsonl", true, 1234)]
    [DataRow("roslyn-mcp-1234.1.jsonl", true, 1234)]
    [DataRow("roslyn-mcp-1234.100.jsonl", true, 1234)]
    [DataRow("roslyn-mcp-1234.0.jsonl", false, 0)]
    [DataRow("roslyn-mcp-0.jsonl", false, 0)]
    [DataRow("roslyn-mcp-2147483648.jsonl", false, 0)]
    [DataRow("roslyn-mcp-12.3.4.jsonl", false, 0)]
    public void TryParseFileName_AcceptsOnlyWriterNames(string name, bool expected, int expectedPid)
    {
        Assert.AreEqual(expected, JsonLinesSegmentWriter.TryParseFileName(name, out var pid));
        Assert.AreEqual(expectedPid, pid);
    }

    [TestMethod]
    public void Sweep_TrimsOldestFirstToTheCapAndNothingAtExactlyTheCap()
    {
        var directory = NewDirectory();
        var recent = _now - TimeSpan.FromHours(5);
        var oldest = Seed(directory, "roslyn-mcp-300.jsonl", recent - TimeSpan.FromHours(3), bytes: 400_000);
        var middle = Seed(directory, "roslyn-mcp-301.jsonl", recent - TimeSpan.FromHours(2), bytes: 400_000);
        var newest = Seed(directory, "roslyn-mcp-302.jsonl", recent - TimeSpan.FromHours(1), bytes: 400_000);
        var cap = JsonLinesRetentionOptions.MinimumMaxTotalBytes;

        var result = Sweep(directory, Options(maxTotalBytes: cap));

        Assert.IsFalse(File.Exists(oldest));
        Assert.IsTrue(File.Exists(middle));
        Assert.IsTrue(File.Exists(newest));
        Assert.AreEqual(new JsonLinesSweepResult(1, 400_000, 0, 0, 0), result);

        // Exactly at the cap deletes nothing.
        var exact = NewDirectory();
        var a = Seed(exact, "roslyn-mcp-300.jsonl", recent, bytes: cap / 2);
        var b = Seed(exact, "roslyn-mcp-301.jsonl", recent, bytes: cap / 2);
        Assert.AreEqual(default, Sweep(exact, Options(maxTotalBytes: cap)));
        Assert.IsTrue(File.Exists(a));
        Assert.IsTrue(File.Exists(b));
    }

    [TestMethod]
    public void Sweep_BreaksCapTiesByNameOrdinalOrder()
    {
        var directory = NewDirectory();
        var stamp = _now - TimeSpan.FromHours(5);
        var first = Seed(directory, "roslyn-mcp-310.jsonl", stamp, bytes: 600_000);
        var second = Seed(directory, "roslyn-mcp-311.jsonl", stamp, bytes: 600_000);

        Sweep(directory, Options(maxTotalBytes: JsonLinesRetentionOptions.MinimumMaxTotalBytes));

        Assert.IsFalse(File.Exists(first), "The ordinal-first name goes first on equal timestamps.");
        Assert.IsTrue(File.Exists(second));
    }

    [TestMethod]
    public void Sweep_CountsLiveAndOwnBytesTowardTheCapButNeverDeletesThem()
    {
        var directory = NewDirectory();
        var recent = _now - TimeSpan.FromHours(5);
        var own = Seed(directory, $"roslyn-mcp-{OwnPid}.jsonl", recent - TimeSpan.FromHours(9), bytes: 700_000);
        var live = Seed(directory, "roslyn-mcp-320.jsonl", recent - TimeSpan.FromHours(8), bytes: 700_000);
        var dead = Seed(directory, "roslyn-mcp-321.jsonl", recent, bytes: 100_000);

        var result = Sweep(directory, Options(maxTotalBytes: JsonLinesRetentionOptions.MinimumMaxTotalBytes), static pid => pid == 320);

        Assert.IsTrue(File.Exists(own));
        Assert.IsTrue(File.Exists(live));
        Assert.IsFalse(File.Exists(dead), "Protected bytes alone exceed the cap, so the only candidate is deleted.");
        Assert.AreEqual(1, result.Deleted);
        Assert.AreEqual(1, result.SkippedLive);
    }

    [TestMethod]
    public void Sweep_StopsWithoutThrowingWhenOnlyProtectedFilesRemainOverTheCap()
    {
        var directory = NewDirectory();
        var recent = _now - TimeSpan.FromHours(5);
        var own = Seed(directory, $"roslyn-mcp-{OwnPid}.jsonl", recent, bytes: 700_000);
        var live = Seed(directory, "roslyn-mcp-330.jsonl", recent, bytes: 700_000);

        var result = Sweep(directory, Options(maxTotalBytes: JsonLinesRetentionOptions.MinimumMaxTotalBytes), static pid => pid == 330);

        Assert.IsTrue(File.Exists(own));
        Assert.IsTrue(File.Exists(live));
        Assert.AreEqual(0, result.Deleted);
    }

    [TestMethod]
    public void Sweep_NeverDeletesAFileWrittenWithinTheGraceWindow()
    {
        var directory = NewDirectory();
        var fresh = Seed(directory, "roslyn-mcp-340.jsonl", _now - TimeSpan.FromSeconds(30), bytes: 600_000);
        var future = Seed(directory, "roslyn-mcp-341.jsonl", _now + TimeSpan.FromMinutes(5), bytes: 600_000);
        var settled = Seed(directory, "roslyn-mcp-342.jsonl", _now - TimeSpan.FromSeconds(120), bytes: 600_000);

        var result = Sweep(directory, Options(maxTotalBytes: JsonLinesRetentionOptions.MinimumMaxTotalBytes));

        Assert.IsTrue(File.Exists(fresh), "A pid-namespace alias or a just-started host writes within the grace window.");
        Assert.IsTrue(File.Exists(future), "A future timestamp is a clock-skew risk, never a deletion candidate.");
        Assert.IsFalse(File.Exists(settled));
        Assert.AreEqual(1, result.Deleted);
    }

    [TestMethod]
    public void Sweep_IOExceptionOnDeleteMeansHeldSoTheFileIsSkippedAndLaterFilesProceed()
    {
        var directory = NewDirectory();
        var old = _now - TimeSpan.FromDays(60);
        var held = Seed(directory, "roslyn-mcp-400.jsonl", old);
        var later = Seed(directory, "roslyn-mcp-401.jsonl", old + TimeSpan.FromHours(1));

        var result = Sweep(directory, Options(), deleteFile: path =>
        {
            if (path == held)
            {
                throw new IOException("held");
            }

            File.Delete(path);
        });

        Assert.IsTrue(File.Exists(held));
        Assert.IsFalse(File.Exists(later));
        Assert.AreEqual(new JsonLinesSweepResult(1, 16, 0, 1, 0), result);
    }

    [TestMethod]
    public void Sweep_SkipsAFileHeldOpenWithoutSharingOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Only Windows refuses to unlink a file another handle holds open; elsewhere liveness and the grace window are the protection.");
        }

        var directory = NewDirectory();
        var path = Seed(directory, "roslyn-mcp-410.jsonl", _now - TimeSpan.FromDays(60));
        JsonLinesSweepResult result;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = Sweep(directory, Options());
        }

        Assert.IsTrue(File.Exists(path));
        Assert.AreEqual(new JsonLinesSweepResult(0, 0, 0, 1, 0), result);
    }

    [TestMethod]
    public void Sweep_CountsUnauthorizedAccessAsFailedAndContinues()
    {
        var directory = NewDirectory();
        var old = _now - TimeSpan.FromDays(60);
        var denied = Seed(directory, "roslyn-mcp-420.jsonl", old);
        var other = Seed(directory, "roslyn-mcp-421.jsonl", old + TimeSpan.FromHours(1));

        var result = Sweep(directory, Options(), deleteFile: path =>
        {
            if (path == denied)
            {
                throw new UnauthorizedAccessException("denied");
            }

            File.Delete(path);
        });

        Assert.IsTrue(File.Exists(denied));
        Assert.IsFalse(File.Exists(other));
        Assert.AreEqual(new JsonLinesSweepResult(1, 16, 0, 0, 1), result);
    }

    [TestMethod]
    public void Sweep_TreatsAVanishedFileAsBenignAndUncounted()
    {
        var directory = NewDirectory();
        var old = _now - TimeSpan.FromDays(60);
        var vanished = Seed(directory, "roslyn-mcp-430.jsonl", old);
        var other = Seed(directory, "roslyn-mcp-431.jsonl", old + TimeSpan.FromHours(1));

        var result = Sweep(directory, Options(), deleteFile: path =>
        {
            File.Delete(path);
            if (path == vanished)
            {
                throw new FileNotFoundException("gone");
            }
        });

        Assert.IsFalse(File.Exists(other));
        Assert.AreEqual(new JsonLinesSweepResult(1, 16, 0, 0, 0), result);
    }

    [TestMethod]
    public void Sweep_MissingDirectoryIsANoOp()
    {
        var missing = Path.Combine(NewDirectory(), "absent");

        Assert.AreEqual(default, Sweep(missing, Options()));
        Assert.IsFalse(Directory.Exists(missing));
    }

    [TestMethod]
    public void RetentionOptions_ParseDefaultsAndRejectInvalidSweepValuesWithoutEchoingThem()
    {
        var defaults = JsonLinesRetentionOptions.Parse(static _ => null);
        Assert.AreEqual(14, defaults.RetentionDays);
        Assert.AreEqual(64L * 1024 * 1024, defaults.MaxTotalBytes);

        var parsed = JsonLinesRetentionOptions.Parse(name => name switch
        {
            JsonLinesRetentionOptions.RetentionDaysVariable => " 30 ",
            JsonLinesRetentionOptions.MaxTotalBytesVariable => "2097152",
            _ => null,
        });
        Assert.AreEqual(30, parsed.RetentionDays);
        Assert.AreEqual(2097152L, parsed.MaxTotalBytes);

        foreach (var (variable, value) in new[]
        {
            (JsonLinesRetentionOptions.RetentionDaysVariable, "0"),
            (JsonLinesRetentionOptions.RetentionDaysVariable, "3651"),
            (JsonLinesRetentionOptions.RetentionDaysVariable, _secretSentinel),
            (JsonLinesRetentionOptions.MaxTotalBytesVariable, (JsonLinesRetentionOptions.MinimumMaxTotalBytes - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            (JsonLinesRetentionOptions.MaxTotalBytesVariable, "1099511627777"),
            (JsonLinesRetentionOptions.MaxTotalBytesVariable, _secretSentinel),
        })
        {
            var exception = Assert.ThrowsExactly<InvalidOperationException>(
                () => JsonLinesRetentionOptions.Parse(name => name == variable ? value : null));
            Assert.Contains(variable, exception.Message);
            Assert.IsFalse(exception.Message.Contains(_secretSentinel, StringComparison.Ordinal));
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => JsonLinesRetentionOptions.Create(JsonLinesRetentionOptions.MinimumSegmentBytes, 1, 0, JsonLinesRetentionOptions.MinimumMaxTotalBytes));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => JsonLinesRetentionOptions.Create(JsonLinesRetentionOptions.MinimumSegmentBytes, 1, 1, JsonLinesRetentionOptions.MinimumMaxTotalBytes - 1));
    }

    [TestMethod]
    public void IsProcessAlive_ReportsTheCurrentProcessLiveAndAnExitedChildDead()
    {
        Assert.IsTrue(JsonLinesRetentionSweeper.IsProcessAlive(Environment.ProcessId));

        using var child = ExitedProcess.StartAndAwaitExit();

        Assert.IsFalse(JsonLinesRetentionSweeper.IsProcessAlive(child.Id));
    }

    [TestMethod]
    public void SweepAndLog_FailureWritesOneWarningWithTheExceptionTypeOnly()
    {
        var directory = NewDirectory();
        using var provider = new JsonLinesFileLoggerProvider(directory, processId: OwnPid);
        var logger = provider.CreateLogger("RoslynMcp.LogRetentionSweep");

        JsonLinesRetentionSweeper.SweepAndLog(
            logger,
            () => throw new IOException($"{_secretSentinel} C:/private/path"));

        var records = File.ReadAllLines(provider.FilePath).Select(static line => JsonDocument.Parse(line).RootElement).ToArray();
        Assert.HasCount(1, records);
        Assert.AreEqual("LogRetentionSweepFailed", records[0].GetProperty("eventName").GetString());
        Assert.AreEqual(1004, records[0].GetProperty("eventId").GetInt32());
        Assert.AreEqual("Warning", records[0].GetProperty("level").GetString());
        Assert.AreEqual("IOException", records[0].GetProperty("fields").GetProperty("exceptionType").GetString());
        var raw = File.ReadAllText(provider.FilePath);
        Assert.IsFalse(raw.Contains(_secretSentinel, StringComparison.Ordinal));
        Assert.IsFalse(raw.Contains("private/path", StringComparison.Ordinal));
        Assert.IsFalse(raw.Contains("LogRetentionSweepCompleted", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SweepAndLog_NeverThrowsWhenTheLoggerItselfThrows()
    {
        var logger = new ThrowingLogger();

        JsonLinesRetentionSweeper.SweepAndLog(logger, () => throw new IOException("sweep failed"));
        JsonLinesRetentionSweeper.SweepAndLog(logger, static () => default);

        Assert.AreEqual(2, logger.Attempts, "Both the failure and the summary record were attempted.");
    }

    private sealed class ThrowingLogger : ILogger
    {
        public int Attempts { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Attempts++;
            throw new InvalidOperationException("logger failed");
        }
    }

    [TestMethod]
    public void SweepAndLog_DeletesDeadFilesAndWritesExactlyOneSummaryRecord()
    {
        var directory = NewDirectory();
        using var child = ExitedProcess.StartAndAwaitExit();
        var dead = Seed(directory, $"roslyn-mcp-{child.Id}.jsonl", DateTime.UtcNow - TimeSpan.FromDays(60));
        using var provider = new JsonLinesFileLoggerProvider(directory, processId: OwnPid);
        var logger = provider.CreateLogger("RoslynMcp.LogRetentionSweep");

        JsonLinesRetentionSweeper.SweepAndLog(directory, OwnPid, Options(), logger);

        Assert.IsFalse(File.Exists(dead), "A dead pid's aged file must be swept.");
        var records = File.ReadAllLines(provider.FilePath).Select(static line => JsonDocument.Parse(line).RootElement).ToArray();
        Assert.HasCount(1, records);
        Assert.AreEqual("LogRetentionSweepCompleted", records[0].GetProperty("eventName").GetString());
        Assert.AreEqual("Information", records[0].GetProperty("level").GetString());
        var fields = records[0].GetProperty("fields");
        foreach (var field in new[] { "deleted", "deletedBytes", "skippedLive", "skippedHeld", "failed" })
        {
            Assert.IsTrue(fields.TryGetProperty(field, out _), field);
        }
    }
}
