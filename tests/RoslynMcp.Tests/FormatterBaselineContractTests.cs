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

    private static readonly TimeSpan _processTimeout = TimeSpan.FromMinutes(5);

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
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task Generator_IsDeterministicAndTheTrackedInventoryCoversTheLiveRunAsync()
    {
        var firstRun = await RunGeneratorCheckAsync();
        var secondRun = await RunGeneratorCheckAsync();

        Assert.AreEqual(
            firstRun.StdOut,
            secondRun.StdOut,
            $"The generator must be byte-deterministic. stderr={firstRun.StdErr}{secondRun.StdErr}");

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

    private static async Task<ProcessResult> RunGeneratorCheckAsync()
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
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"Formatter baseline generator did not exit within {_processTimeout.TotalMinutes} minutes.");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
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
