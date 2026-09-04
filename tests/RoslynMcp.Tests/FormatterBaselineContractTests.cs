using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoslynMcp.Tests;

/// <summary>
/// Contract tests for the repository formatter baseline inventory
/// (<c>eng/format-baseline.json</c>, produced by <c>eng/generate-format-baseline.ps1</c>).
/// The inventory records existing formatter debt; these tests assert it stays sorted,
/// de-duplicated, internally consistent, and that the debt is never silenced instead
/// of being repaired.
/// </summary>
[TestClass]
public sealed class FormatterBaselineContractTests
{
    private const int _expectedSchemaVersion = 1;

    /// <summary>
    /// Mirrors <c>$formatPhaseMarkerPrefix</c> in <c>eng/format-diagnostic-contract.ps1</c>.
    /// <see cref="GeneratorAndGate_ImportOneSharedDiagnosticGrammar"/> asserts the two stay identical.
    /// </summary>
    private const string _formatPhaseMarkerPrefix = "##format-phase##";

    /// <summary>
    /// Deliberately unchanged. A longer bound would hide host contention instead of naming it;
    /// the diagnostics around it exist so a future measured replacement can be justified by
    /// the phase timings every successful run now records.
    /// </summary>
    private static readonly TimeSpan _processTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Bound on draining the already-started stdout/stderr readers after the generator tree is
    /// killed. <see cref="Process.Kill(bool)"/> returns before the tree has actually torn down, and
    /// the reads only complete once every inherited pipe-write handle is closed - which is slowest
    /// under exactly the contention this diagnostic exists to name. Negligible next to
    /// <see cref="_processTimeout"/>.
    /// </summary>
    private static readonly TimeSpan _drainTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Process names that contend for the same MSBuild/NuGet/compiler-server resources the
    /// generator needs. Matched by name because that is all <see cref="Process"/> exposes without
    /// elevated access.
    /// </summary>
    private static readonly string[] _competingProcessNames =
    [
        "dotnet",
        "MSBuild",
        "testhost",
        "VBCSCompiler",
    ];

    private const int _maxCompetingProcessesReported = 10;

    private static readonly string[] _trackedDiagnosticIds =
    [
        "FINALNEWLINE",
        "IDE1006",
        "IMPORTS",
        "WHITESPACE",
    ];

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [TestMethod]
    public void Inventory_IsSortedDeduplicatedAndInternallyConsistent()
    {
        var inventory = LoadTrackedInventory();

        Assert.AreEqual(
            _expectedSchemaVersion,
            inventory.SchemaVersion,
            "The inventory schema is a contract consumed by the changed-file format gate.");
        Assert.AreEqual(
            "dotnet format RoslynMcp.slnx --verify-no-changes --no-restore",
            inventory.Command,
            "The recorded command must match what the generator actually runs.");
        Assert.IsTrue(inventory.Files.Length > 0, "The inventory must not be empty.");

        AssertStrictlyAscendingOrdinal(
            inventory.Files.Select(file => file.Path).ToArray(),
            "files[].path");
        AssertStrictlyAscendingOrdinal(inventory.DiagnosticIds, "diagnosticIds");

        var derivedFindingCount = 0;
        var derivedTotalsByDiagnosticId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in inventory.Files)
        {
            Assert.IsFalse(
                file.Path.Contains('\\', StringComparison.Ordinal),
                $"Paths must use forward slashes; '{file.Path}' does not.");
            Assert.IsFalse(
                Path.IsPathRooted(file.Path),
                $"Paths must be repository-relative; '{file.Path}' is rooted.");

            AssertStrictlyAscendingOrdinal(
                file.DiagnosticIds,
                $"files[].diagnosticIds for '{file.Path}'");
            var countKeys = file.CountsByDiagnosticId.Keys.Order(StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(
                file.DiagnosticIds,
                countKeys,
                $"countsByDiagnosticId keys must mirror diagnosticIds for '{file.Path}'.");

            var derivedFileCount = 0;
            foreach (var (diagnosticId, count) in file.CountsByDiagnosticId)
            {
                Assert.IsTrue(
                    count > 0,
                    $"'{file.Path}' records a non-positive count for {diagnosticId}.");
                CollectionAssert.Contains(
                    inventory.DiagnosticIds,
                    diagnosticId,
                    $"'{diagnosticId}' on '{file.Path}' is missing from the declared diagnosticIds set.");

                derivedFileCount += count;
                derivedTotalsByDiagnosticId[diagnosticId] =
                    derivedTotalsByDiagnosticId.GetValueOrDefault(diagnosticId) + count;
            }

            Assert.AreEqual(
                derivedFileCount,
                file.FindingCount,
                $"findingCount for '{file.Path}' must equal the sum of its per-id counts.");
            derivedFindingCount += derivedFileCount;
        }

        Assert.AreEqual(
            derivedFindingCount,
            inventory.Totals.FindingCount,
            "totals.findingCount must equal the sum of every file's findingCount.");
        Assert.AreEqual(
            inventory.Files.Length,
            inventory.Totals.FileCount,
            "totals.fileCount must equal the number of inventoried files.");
        CollectionAssert.AreEqual(
            derivedTotalsByDiagnosticId.Keys.Order(StringComparer.Ordinal).ToArray(),
            inventory.Totals.CountsByDiagnosticId.Keys.Order(StringComparer.Ordinal).ToArray(),
            "totals.countsByDiagnosticId must cover exactly the diagnostic ids present in files[].");
        foreach (var (diagnosticId, derivedCount) in derivedTotalsByDiagnosticId)
        {
            Assert.AreEqual(
                derivedCount,
                inventory.Totals.CountsByDiagnosticId[diagnosticId],
                $"totals.countsByDiagnosticId['{diagnosticId}'] must equal the derived sum.");
        }
    }

    [TestMethod]
    public void Inventory_ListsOnlyPathsThatExistOnDisk()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var inventory = LoadTrackedInventory();

        var missing = inventory.Files
            .Select(file => file.Path)
            .Where(path => !File.Exists(
                Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        Assert.AreEqual(
            0,
            missing.Length,
            $"The inventory references files that no longer exist: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void EditorConfig_DoesNotSilenceOrRelabelAnyInventoriedDiagnostic()
    {
        var editorConfig = LoadRepositoryFile(".editorconfig");
        var silencedSeverities = new[] { "none", "silent", "suggestion" };

        foreach (var diagnosticId in _trackedDiagnosticIds)
        {
            foreach (var severity in silencedSeverities)
            {
                var pattern = $@"^\s*dotnet_diagnostic\.{Regex.Escape(diagnosticId)}\.severity\s*=\s*{severity}\s*$";
                Assert.IsFalse(
                    Regex.IsMatch(editorConfig, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline),
                    $"'{diagnosticId}' is downgraded to '{severity}' in .editorconfig. The baseline exists to track this debt, not to hide it.");
            }

            Assert.IsFalse(
                Regex.IsMatch(
                    editorConfig,
                    $@"^\s*(dotnet_)?NoWarn\s*=.*{Regex.Escape(diagnosticId)}",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline),
                $"'{diagnosticId}' appears in a NoWarn list in .editorconfig.");
        }

        Assert.IsTrue(
            Regex.IsMatch(
                editorConfig,
                @"^\s*dotnet_naming_rule\.private_fields_should_be_camel_case\.severity\s*=\s*warning\s*$",
                RegexOptions.Multiline),
            "The IDE1006 source rule must remain at 'warning'; downgrading it would relabel the debt away.");
        Assert.IsTrue(
            Regex.IsMatch(
                editorConfig,
                @"^\s*dotnet_naming_symbols\.private_constants\.required_modifiers\s*=\s*const\s*$",
                RegexOptions.Multiline),
            "Private constants must use the modifier-specific exemption instead of inheriting the instance-field rule.");
        Assert.IsTrue(
            Regex.IsMatch(
                editorConfig,
                @"^\s*dotnet_naming_symbols\.private_static_readonly_fields\.required_modifiers\s*=\s*static,\s*readonly\s*$",
                RegexOptions.Multiline),
            "Private static readonly fields must use the modifier-specific exemption instead of inheriting the instance-field rule.");
    }

    [TestMethod]
    public void GeneratorAndGate_ImportOneSharedDiagnosticGrammar()
    {
        var contract = LoadRepositoryFile("eng", "format-diagnostic-contract.ps1");
        var consumers = new[]
        {
            LoadRepositoryFile("eng", "generate-format-baseline.ps1"),
            LoadRepositoryFile("eng", "verify-changed-format.ps1"),
        };

        StringAssert.Contains(contract, "$formatDiagnosticPattern =");
        StringAssert.Contains(contract, "$formatTruncationMarker =");
        StringAssert.Contains(
            contract,
            $"$formatPhaseMarkerPrefix = '{_formatPhaseMarkerPrefix}'",
            "The phase-marker prefix this test class parses must be the one the contract declares.");
        foreach (var consumer in consumers)
        {
            StringAssert.Contains(consumer, "format-diagnostic-contract.ps1");
            Assert.IsFalse(
                Regex.IsMatch(
                    consumer,
                    @"^\s*\$(?:formatDiagnosticPattern|diagnosticPattern|formatTruncationMarker|truncationMarker|formatPhaseMarkerPrefix|phaseMarkerPrefix)\s*=",
                    RegexOptions.Multiline),
                "Formatter grammar consumers must not redeclare the shared regex, truncation marker, or phase-marker prefix.");
        }
    }

    [TestMethod]
    public void ClassifyGeneratorTimeout_NamesTheEvidenceInsteadOfBlamingTheBound()
    {
        var markers =
            new[]
            {
                "##format-phase## restore start elapsedMs=8",
                "##format-phase## restore end elapsedMs=41230",
                "##format-phase## format start elapsedMs=41233",
            };

        Assert.AreEqual(
            "generator-never-started",
            ClassifyGeneratorTimeout(
                [],
                competingProcessesAtStart: 4,
                competingProcessesStillRunning: 4,
                stderrDrained: true),
            "A drained but empty stderr means the generator never reached its first dotnet invocation.");
        Assert.AreEqual(
            "host-contention",
            ClassifyGeneratorTimeout(
                [],
                competingProcessesAtStart: 4,
                competingProcessesStillRunning: 4,
                stderrDrained: false),
            "An undrained stderr is missing evidence, so it must never be reported as 'never started'.");
        Assert.AreEqual(
            "host-contention",
            ClassifyGeneratorTimeout(
                markers,
                competingProcessesAtStart: 4,
                competingProcessesStillRunning: 3,
                stderrDrained: true),
            "A competitor that pre-dated the run and outlived the bound is the contention signature.");
        Assert.AreEqual(
            "generator-hang",
            ClassifyGeneratorTimeout(
                markers,
                competingProcessesAtStart: 0,
                competingProcessesStillRunning: 0,
                stderrDrained: true),
            "Phases started on an otherwise idle host and never finished: a real hang.");
        Assert.AreEqual(
            "generator-hang",
            ClassifyGeneratorTimeout(
                markers,
                competingProcessesAtStart: 4,
                competingProcessesStillRunning: 0,
                stderrDrained: true),
            "Competitors that all exited cannot explain a stall that outlasted them.");
    }

    [TestMethod]
    public void DescribeCompetingProcesses_NamesTheCappedHeadAndAccountsForTheRemainder()
    {
        Assert.AreEqual(
            "none",
            DescribeCompetingProcesses([]),
            "An empty snapshot must read as an absence of contention, not as an empty list.");

        Assert.AreEqual(
            "2 [dotnet#1000, MSBuild#1001]",
            DescribeCompetingProcesses(
                [new CompetingProcess(1000, "dotnet"), new CompetingProcess(1001, "MSBuild")]),
            "Below the cap every competitor is named and no remainder suffix appears.");

        var overCap = Enumerable
            .Range(0, _maxCompetingProcessesReported + 2)
            .Select(index => new CompetingProcess(1000 + index, "dotnet"))
            .ToArray();

        Assert.AreEqual(
            "12 [dotnet#1000, dotnet#1001, dotnet#1002, dotnet#1003, dotnet#1004, dotnet#1005, "
            + "dotnet#1006, dotnet#1007, dotnet#1008, dotnet#1009, +2 more]",
            DescribeCompetingProcesses(overCap),
            "Truncation must keep the true total up front and account for the unnamed tail; a "
            + "silently clipped list would understate the contention this diagnostic measures.");
    }

    [TestMethod]
    public async Task DrainAsync_SeparatesWhatWasReadFromWhetherTheReadSucceededAsync()
    {
        var drained = await DrainAsync(Task.FromResult("payload"), "stdout");

        Assert.IsTrue(drained.Drained, "An already-completed reader must be reported as drained.");
        Assert.AreEqual("payload", drained.Text, "A drained reader must surface its own text verbatim.");
        Assert.AreEqual(
            "7 chars",
            DescribeDrainedLength(drained),
            "A successful drain reports the captured size.");

        var stalled = await DrainAsync(
            new TaskCompletionSource<string>().Task,
            "stdout",
            TimeSpan.FromMilliseconds(50));

        Assert.IsFalse(
            stalled.Drained,
            "A reader that outlives the bound must never be reported as drained.");
        StringAssert.Contains(
            stalled.Text,
            "stdout not drained within",
            "The placeholder must name the stream and the failure.");
        Assert.AreEqual(
            "unavailable (drain failed)",
            DescribeDrainedLength(stalled),
            "A failed drain must never be reported as a character count: the placeholder's own "
            + "length reads as a plausible payload size, which is exactly the ambiguity between a "
            + "truncated payload and a failed read that this diagnostic exists to remove.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task Generator_IsDeterministicAndTheTrackedInventoryCoversTheLiveRunAsync()
    {
        var firstRun = await RunGeneratorCheckAsync();
        var secondRun = await RunGeneratorCheckAsync(noRestore: true);

        Assert.AreEqual(
            firstRun.StdOut,
            secondRun.StdOut,
            $"The generator must be byte-deterministic. stderr={firstRun.StdErr}{secondRun.StdErr}");

        // Phase markers are diagnostics, not payload. stdout is the byte-compared inventory the
        // assertion above locks, so a marker leaking there would break determinism outright.
        Assert.IsFalse(
            firstRun.StdOut.Contains(_formatPhaseMarkerPrefix, StringComparison.Ordinal),
            "Phase markers must never reach stdout; stdout is the byte-compared determinism payload.");
        Assert.IsFalse(
            secondRun.StdOut.Contains(_formatPhaseMarkerPrefix, StringComparison.Ordinal),
            "Phase markers must never reach stdout on the no-restore pass.");
        CollectionAssert.AreEqual(
            new[] { "restore start", "restore end", "format start", "format end" },
            ExtractPhaseMarkers(firstRun.StdErr)
                .Select(marker => string.Join(' ', marker.Split(' ').Skip(1).Take(2)))
                .ToArray(),
            $"The generator must bracket both phases on stderr. stderr={firstRun.StdErr}");
        CollectionAssert.AreEqual(
            new[] { "format start", "format end" },
            ExtractPhaseMarkers(secondRun.StdErr)
                .Select(marker => string.Join(' ', marker.Split(' ').Skip(1).Take(2)))
                .ToArray(),
            $"The second deterministic pass must reuse the first restore and run formatting only. stderr={secondRun.StdErr}");

        var live = ParseInventory(
            firstRun.StdOut,
            $"generator stdout (exit {firstRun.ExitCode}, stderr={firstRun.StdErr})");
        var tracked = LoadTrackedInventory();

        Assert.AreEqual(tracked.SchemaVersion, live.SchemaVersion);

        // Shrink-tolerant: repairs may remove entries, so the live run must be a subset
        // of the tracked inventory. Anything NEW is real regression and fails here.
        var trackedPaths = tracked.Files
            .Select(file => file.Path)
            .ToHashSet(StringComparer.Ordinal);
        var newPaths = live.Files
            .Select(file => file.Path)
            .Where(path => !trackedPaths.Contains(path))
            .ToArray();
        Assert.AreEqual(
            0,
            newPaths.Length,
            $"New formatter violations appeared in files absent from the baseline: {string.Join(", ", newPaths)}");

        var declaredIds = tracked.DiagnosticIds.ToHashSet(StringComparer.Ordinal);
        var newIds = live.DiagnosticIds.Where(id => !declaredIds.Contains(id)).ToArray();
        Assert.AreEqual(
            0,
            newIds.Length,
            $"The live formatter run reported diagnostic ids the baseline does not declare: {string.Join(", ", newIds)}");

        Assert.IsTrue(
            live.Totals.FindingCount <= tracked.Totals.FindingCount,
            $"Formatter debt grew: live={live.Totals.FindingCount} tracked={tracked.Totals.FindingCount}. Repair the new violations or regenerate the baseline deliberately.");
    }

    /// <summary>
    /// Classifies a generator timeout from evidence the caller already collected, so the verdict
    /// itself is unit-testable without having to stall a real process.
    /// </summary>
    /// <param name="phaseMarkers">
    /// Phase markers drained from the killed generator's stderr, in emission order.
    /// </param>
    /// <param name="competingProcessesAtStart">
    /// How many contending processes were already running when the generator was launched.
    /// </param>
    /// <param name="competingProcessesStillRunning">
    /// How many of those same processes were still alive when the timeout fired.
    /// </param>
    /// <param name="stderrDrained">
    /// Whether the generator's stderr was fully read back. When it was not, an empty
    /// <paramref name="phaseMarkers"/> means "evidence unavailable", not "no phase ran" - reporting
    /// the latter would invent a verdict out of a failed read.
    /// </param>
    internal static string ClassifyGeneratorTimeout(
        IReadOnlyList<string> phaseMarkers,
        int competingProcessesAtStart,
        int competingProcessesStillRunning,
        bool stderrDrained)
    {
        // The first marker precedes the first `dotnet` invocation, so its absence means the
        // generator produced no work at all - a launch problem, not a stall inside a phase.
        if (stderrDrained && phaseMarkers.Count == 0)
        {
            return "generator-never-started";
        }

        // Contention is only credible when the competitor pre-dated the generator AND outlived the
        // bound; one that exited early cannot explain a five-minute stall.
        if (competingProcessesAtStart > 0 && competingProcessesStillRunning > 0)
        {
            return "host-contention";
        }

        return "generator-hang";
    }

    private static string[] ExtractPhaseMarkers(string stderr)
        => stderr
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(_formatPhaseMarkerPrefix, StringComparison.Ordinal))
            .ToArray();

    /// <summary>
    /// Names the contending processes visible right now. A process class whose enumeration fails is
    /// skipped rather than reported, because an un-enumerable class is evidence of neither
    /// contention nor its absence. Enumeration is the only step that can fail: every entry
    /// <see cref="Process.GetProcessesByName(string)"/> returns already carries its
    /// <see cref="Process.Id"/>, so reading it back needs no guard of its own.
    /// </summary>
    private static IReadOnlyList<CompetingProcess> SnapshotCompetingProcesses()
    {
        var selfProcessId = Environment.ProcessId;
        var snapshot = new List<CompetingProcess>();
        foreach (var processName in _competingProcessNames)
        {
            Process[] candidates;
            try
            {
                candidates = Process.GetProcessesByName(processName);
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            catch (Win32Exception)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                using (candidate)
                {
                    if (candidate.Id != selfProcessId)
                    {
                        snapshot.Add(new CompetingProcess(candidate.Id, processName));
                    }
                }
            }
        }

        return snapshot;
    }

    private static string DescribeCompetingProcesses(IReadOnlyList<CompetingProcess> processes)
    {
        if (processes.Count == 0)
        {
            return "none";
        }

        var reported = processes
            .Take(_maxCompetingProcessesReported)
            .Select(process => $"{process.Name}#{process.Id}");
        var suffix = processes.Count > _maxCompetingProcessesReported
            ? $", +{processes.Count - _maxCompetingProcessesReported} more"
            : string.Empty;
        return $"{processes.Count} [{string.Join(", ", reported)}{suffix}]";
    }

    /// <summary>
    /// Reports how much of a stream the timeout actually captured. A failed drain has no size to
    /// report: <see cref="DrainAsync(Task{string}, string)"/> substitutes a placeholder whose own
    /// length would read as a plausible byte count, so the failure is named instead of numbered.
    /// </summary>
    private static string DescribeDrainedLength(DrainResult drain)
        => drain.Drained ? $"{drain.Text.Length} chars" : "unavailable (drain failed)";

    /// <summary>
    /// Awaits an already-running reader under <see cref="_drainTimeout"/>. The reader was started
    /// before the timeout fired; abandoning it would discard the killed generator's own account of
    /// where it stalled, which is the only evidence that matters at that moment.
    /// </summary>
    private static Task<DrainResult> DrainAsync(Task<string> readTask, string streamName)
        => DrainAsync(readTask, streamName, _drainTimeout);

    /// <summary>
    /// Explicit-bound overload. Exists so the not-drained branch is exercisable in milliseconds
    /// rather than only after a real thirty-second stall.
    /// </summary>
    private static async Task<DrainResult> DrainAsync(
        Task<string> readTask,
        string streamName,
        TimeSpan drainTimeout)
    {
        try
        {
            var completed = await Task.WhenAny(readTask, Task.Delay(drainTimeout));
            if (completed != readTask)
            {
                return new DrainResult(
                    false,
                    $"<{streamName} not drained within {drainTimeout.TotalSeconds:0} seconds>");
            }

            return new DrainResult(true, await readTask);
        }
        catch (IOException exception)
        {
            return new DrainResult(false, $"<{streamName} drain failed: {exception.Message}>");
        }
        catch (ObjectDisposedException exception)
        {
            return new DrainResult(false, $"<{streamName} drain failed: {exception.Message}>");
        }
    }

    private static async Task<ProcessResult> RunGeneratorCheckAsync(bool noRestore = false)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var generatorPath = Path.Combine(repositoryRoot, "eng", "generate-format-baseline.ps1");
        Assert.IsTrue(File.Exists(generatorPath), $"Generator not found at {generatorPath}.");

        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(generatorPath);

        // -Check never writes, so the working tree stays clean. Its exit code is 1 when the
        // tracked artifact has drifted; drift in the shrinking direction is expected and is
        // evaluated by the subset assertions rather than by the exit code.
        startInfo.ArgumentList.Add("-Check");
        if (noRestore)
        {
            startInfo.ArgumentList.Add("-NoRestore");
        }

        // Taken before launch, so nothing the generator itself spawns can appear in it.
        var competingAtStart = SnapshotCompetingProcesses();
        var elapsed = Stopwatch.StartNew();

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(_processTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            // Intersecting on pid with the pre-launch snapshot excludes the generator's own tree
            // without a parent-pid lookup: every descendant it spawned started later.
            var startingIds = competingAtStart.Select(entry => entry.Id).ToHashSet();
            var stillCompeting = SnapshotCompetingProcesses()
                .Where(entry => entry.Id != process.Id && startingIds.Contains(entry.Id))
                .ToArray();

            process.Kill(entireProcessTree: true);

            var partialStdOut = await DrainAsync(stdoutTask, "stdout");
            var partialStdErr = await DrainAsync(stderrTask, "stderr");
            var phaseMarkers = ExtractPhaseMarkers(partialStdErr.Text);
            var classification = ClassifyGeneratorTimeout(
                phaseMarkers,
                competingAtStart.Count,
                stillCompeting.Length,
                partialStdErr.Drained);

            throw new TimeoutException(
                $"Formatter baseline generator did not exit within {_processTimeout.TotalMinutes} minutes. "
                + $"classification={classification}; "
                + $"stdoutDrained={partialStdOut.Drained}; "
                + $"stderrDrained={partialStdErr.Drained}; "
                + $"phases=[{string.Join(" | ", phaseMarkers)}]; "
                + $"competingAtStart={DescribeCompetingProcesses(competingAtStart)}; "
                + $"stillCompetingAtTimeout={DescribeCompetingProcesses(stillCompeting)}; "
                + $"partialStdOut={DescribeDrainedLength(partialStdOut)}; "
                + $"partialStdErr={partialStdErr.Text}");
        }

        elapsed.Stop();
        var stdOut = await stdoutTask;
        var stdErr = await stderrTask;

        // Recorded on every successful run so TRX history accumulates the repeated phase evidence
        // any future change to _processTimeout would have to be argued from.
        Console.WriteLine(
            $"[formatter-baseline] totalMs={elapsed.ElapsedMilliseconds}; "
            + $"phases=[{string.Join(" | ", ExtractPhaseMarkers(stdErr))}]; "
            + $"competingAtStart={DescribeCompetingProcesses(competingAtStart)}");

        return new ProcessResult(process.ExitCode, stdOut, stdErr);
    }

    private static void AssertStrictlyAscendingOrdinal(string[] values, string description)
    {
        for (var index = 1; index < values.Length; index++)
        {
            Assert.IsTrue(
                string.CompareOrdinal(values[index - 1], values[index]) < 0,
                $"{description} must be strictly ascending ordinal and duplicate-free; '{values[index - 1]}' precedes '{values[index]}'.");
        }
    }

    private static FormatBaseline LoadTrackedInventory()
        => ParseInventory(LoadRepositoryFile("eng", "format-baseline.json"), "eng/format-baseline.json");

    private static FormatBaseline ParseInventory(string json, string source)
    {
        var inventory = JsonSerializer.Deserialize<FormatBaseline>(json, _jsonOptions)
            ?? throw new InvalidOperationException($"{source} deserialized to null.");

        Assert.IsNotNull(inventory.Files, $"{source} is missing 'files'.");
        Assert.IsNotNull(inventory.DiagnosticIds, $"{source} is missing 'diagnosticIds'.");
        Assert.IsNotNull(inventory.Totals, $"{source} is missing 'totals'.");
        Assert.IsNotNull(
            inventory.Totals.CountsByDiagnosticId,
            $"{source} is missing 'totals.countsByDiagnosticId'.");
        return inventory;
    }

    private static string LoadRepositoryFile(params string[] relativePathSegments)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var path = Path.Combine([repositoryRoot, .. relativePathSegments]);
        return File.ReadAllText(path).ReplaceLineEndings("\n");
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

    private sealed record CompetingProcess(int Id, string Name);

    private sealed record DrainResult(bool Drained, string Text);

    private sealed record FormatBaseline(
        int SchemaVersion,
        string Command,
        string[] DiagnosticIds,
        FormatBaselineTotals Totals,
        FormatBaselineFile[] Files);

    private sealed record FormatBaselineTotals(
        int FindingCount,
        int FileCount,
        Dictionary<string, int> CountsByDiagnosticId);

    private sealed record FormatBaselineFile(
        string Path,
        int FindingCount,
        string[] DiagnosticIds,
        Dictionary<string, int> CountsByDiagnosticId);
}
