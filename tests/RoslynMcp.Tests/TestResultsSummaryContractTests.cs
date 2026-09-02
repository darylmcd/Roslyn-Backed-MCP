using System.Globalization;
using System.Text.Json;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
[TestCategory("Process")]
public sealed class TestResultsSummaryContractTests
{
    private const string SummarizerDescription = "test-results summarizer";
    private const string CollectorDescription = "hosted shard timing collector";

    private static readonly TimeSpan _processTimeout = TimeSpan.FromSeconds(30);

    [TestMethod]
    public async Task Summary_AggregatesMultipleTrxFilesAndAppendsSanitizedMarkdownAsync()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var firstTrxPath = Path.Combine(fixtureRoot, "first.trx");
            var secondTrxPath = Path.Combine(fixtureRoot, "nested", "SECOND.TRX");
            Directory.CreateDirectory(Path.GetDirectoryName(secondTrxPath)!);
            await File.WriteAllTextAsync(firstTrxPath, FirstTrx);
            await File.WriteAllTextAsync(secondTrxPath, SecondTrx);

            var outputPath = Path.Combine(fixtureRoot, "summary.md");
            await File.WriteAllTextAsync(outputPath, "existing summary content");
            var result = await RunScriptAsync(
                "-ResultsPath", fixtureRoot,
                "-OutputPath", outputPath);

            AssertSucceeded(result);
            Assert.AreEqual(string.Empty, result.StdOut);
            var markdown = await File.ReadAllTextAsync(outputPath);
            StringAssert.StartsWith(markdown, "existing summary content");
            StringAssert.Contains(
                markdown,
                "| 5 | 2 | 2 | 1 | 0 | 00:00:10.5 |");
            StringAssert.Contains(
                markdown,
                @"| Example.Failing.Fails | 00:00:04 | 1 | 00:00:04 |");
            StringAssert.Contains(
                markdown,
                @"| Example.Slow\| Class.Case\|One | 00:00:03.5 | 2 | 00:00:02.5 |");
            StringAssert.Contains(
                markdown,
                @"| Example.Slow\| Class | 00:00:03.5 | 2 | 00:00:02.5 |");
            Assert.IsTrue(
                markdown.IndexOf("Example.Failing.Fails", StringComparison.Ordinal) <
                markdown.IndexOf(@"Example.Slow\| Class.Case\|One", StringComparison.Ordinal),
                "Methods must be ordered by summed duration descending.");
            Assert.IsFalse(
                markdown.Contains("private-source", StringComparison.OrdinalIgnoreCase),
                "TRX codeBase/source-path metadata must never reach the summary.");
            Assert.IsFalse(
                markdown.Contains("\nClass", StringComparison.Ordinal),
                "Names must not inject new table rows.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task MissingResults_EmitsClearSummaryAndSucceedsAsync()
    {
        var missingPath = Path.Combine(
            TestTempRoot.Current,
            nameof(TestResultsSummaryContractTests),
            $"missing-{Guid.NewGuid():N}");

        var result = await RunScriptAsync("-ResultsPath", missingPath);

        AssertSucceeded(result);
        StringAssert.Contains(result.StdOut, "## Test timing summary");
        StringAssert.Contains(result.StdOut, "No MSTest TRX results were found.");
        Assert.AreEqual(string.Empty, result.StdErr);
    }

    [TestMethod]
    public async Task MalformedAndUnjoinableTrx_FailClosedAsync()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var malformedPath = Path.Combine(fixtureRoot, "malformed.trx");
            await File.WriteAllTextAsync(malformedPath, "<TestRun><broken>");
            var malformedResult = await RunScriptAsync("-ResultsPath", malformedPath);
            AssertFailedWith(malformedResult, "Malformed MSTest TRX input.");

            var unjoinablePath = Path.Combine(fixtureRoot, "unjoinable.trx");
            await File.WriteAllTextAsync(unjoinablePath, UnjoinableTrx);
            File.Delete(malformedPath);
            var unjoinableResult = await RunScriptAsync("-ResultsPath", unjoinablePath);
            AssertFailedWith(
                unjoinableResult,
                "Malformed MSTest TRX result cannot be joined to a test definition.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task Script_HasNoPowerShellAstParseErrorsAsync()
    {
        var scriptPath = GetScriptPath();
        const string parseCommand =
            "$tokens = $null; $errors = $null; " +
            "[System.Management.Automation.Language.Parser]::ParseFile(" +
            "$env:ROSLYN_MCP_SCRIPT_UNDER_TEST, [ref]$tokens, [ref]$errors) | Out-Null; " +
            "if ($errors.Count -gt 0) { $errors | ForEach-Object { [Console]::Error.WriteLine($_) }; exit 1 }";

        var result = await RunPowerShellAsync(
            ["-NoProfile", "-NonInteractive", "-Command", parseCommand],
            new Dictionary<string, string?>
            {
                ["ROSLYN_MCP_SCRIPT_UNDER_TEST"] = scriptPath,
            },
            SummarizerDescription);

        AssertSucceeded(result);
    }

    [TestMethod]
    public async Task Collector_UnderMinimumSamples_FailsClosedAsync()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var observations = new List<LegObservation>();
            for (var run = 1; run <= 4; run++)
            {
                observations.Add(new LegObservation($"r{run}", "w-1", "windows-latest", 100, 10));
                observations.Add(new LegObservation($"r{run}", "w-2", "windows-latest", 200, 40));
            }

            var result = await RunCollectorAsync(fixtureRoot, observations);

            AssertFailedWith(
                result,
                "has 4 sampled runs, below the required minimum of 5");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task Collector_LegClaimedByTwoImages_FailsClosedAsync()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var observations = new List<LegObservation>();
            for (var run = 1; run <= 5; run++)
            {
                var image = run == 3 ? "ubuntu-latest" : "windows-latest";
                observations.Add(new LegObservation($"r{run}", "w-1", image, 100, 10));
            }

            var result = await RunCollectorAsync(fixtureRoot, observations);

            AssertFailedWith(result, "is claimed by two hosted images");
            StringAssert.Contains(
                result.StdOut + result.StdErr,
                "Hosted images are never merged.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task Collector_RunMissingOneLegOfItsImage_FailsClosedAsync()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var observations = new List<LegObservation>();
            for (var run = 1; run <= 5; run++)
            {
                observations.Add(new LegObservation($"r{run}", "w-1", "windows-latest", 100, 10));
                if (run != 3)
                {
                    observations.Add(
                        new LegObservation($"r{run}", "w-2", "windows-latest", 200, 40));
                }
            }

            var result = await RunCollectorAsync(fixtureRoot, observations);

            AssertFailedWith(
                result,
                "Run 'r3' is missing leg 'w-2' of hosted image 'windows-latest'.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task Collector_ManifestPathEscapesResultsRoot_FailsClosedAsync()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var result = await RunCollectorAsync(
                fixtureRoot,
                BuildCompleteWindowsProfile(),
                mutate: (manifest, _) =>
                    manifest[0]["path"] = "../escaped/test-results-w-1");

            AssertFailedWith(result, "Leg manifest entry 0 'path' escapes ResultsRoot.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task Collector_DuplicateRunAndLegPair_FailsClosedAsync()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var result = await RunCollectorAsync(
                fixtureRoot,
                BuildCompleteWindowsProfile(),
                mutate: (manifest, _) => manifest.Add(
                    new Dictionary<string, object>(manifest[0])));

            AssertFailedWith(result, "Duplicate leg observation for run 'r1' leg 'w-1'.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task Collector_LegDirectoryHoldsNoTrx_FailsClosedAsync()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var result = await RunCollectorAsync(
                fixtureRoot,
                BuildCompleteWindowsProfile(),
                mutate: (_, resultsRoot) => File.Delete(
                    Path.Combine(resultsRoot, "r1", "test-results-w-1", "results.trx")));

            AssertFailedWith(
                result,
                "Leg manifest entry 0 'path' holds no TRX files: 'r1/test-results-w-1'.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task Collector_NonPositiveWallTime_FailsClosedAsync()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var result = await RunCollectorAsync(
                fixtureRoot,
                BuildCompleteWindowsProfile(),
                mutate: (manifest, _) => manifest[0]["wallTimeSeconds"] = 0);

            AssertFailedWith(
                result,
                "Leg manifest entry 0 has a non-positive 'wallTimeSeconds'.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task Collector_TrxWithZeroCases_FailsClosedAsync()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var result = await RunCollectorAsync(
                fixtureRoot,
                BuildCompleteWindowsProfile(),
                mutate: (_, resultsRoot) => File.WriteAllText(
                    Path.Combine(resultsRoot, "r1", "test-results-w-1", "results.trx"),
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                      <Results />
                      <TestDefinitions />
                    </TestRun>
                    """));

            AssertFailedWith(result, "TRX file reports zero cases:");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task Collector_MalformedTrx_FailsClosedAsync()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var result = await RunCollectorAsync(
                fixtureRoot,
                BuildCompleteWindowsProfile(),
                mutate: (_, resultsRoot) => File.WriteAllText(
                    Path.Combine(resultsRoot, "r1", "test-results-w-1", "results.trx"),
                    "<TestRun><Results>"));

            AssertFailedWith(result, "Malformed MSTest TRX input.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task Collector_ValidProfile_ReportsBothMetricsSeparatelyAndNeverMergesImagesAsync()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var observations = new List<LegObservation>();
            for (var run = 1; run <= 5; run++)
            {
                observations.Add(new LegObservation($"r{run}", "w-1", "windows-latest", 100, 10));
                observations.Add(new LegObservation($"r{run}", "w-2", "windows-latest", 200, 40));
                observations.Add(new LegObservation($"r{run}", "u-1", "ubuntu-latest", 300, 70));
            }

            var result = await RunCollectorAsync(fixtureRoot, observations);

            AssertSucceeded(result);
            Assert.AreEqual(string.Empty, result.StdErr);

            // Wall time and summed case duration must land in distinct columns, never one blended
            // number: 100s wall against 10.0s of cases, and 200s wall against 40.0s of cases.
            StringAssert.Contains(
                result.StdOut,
                "| w-1 | 5 | 100.0 | 100.0 | 100.0 | 1.00x | 10.0 | 10.0 | 10.0 | 1.00x | 2 |");
            StringAssert.Contains(
                result.StdOut,
                "| w-2 | 5 | 200.0 | 200.0 | 200.0 | 1.00x | 40.0 | 40.0 | 40.0 | 1.00x | 2 |");
            StringAssert.Contains(
                result.StdOut,
                "| Achievable gain from a perfect partition | 50.0 s (25.0%) |");

            var windowsSection = ExtractImageSection(result.StdOut, "windows-latest");
            var ubuntuSection = ExtractImageSection(result.StdOut, "ubuntu-latest");
            Assert.IsFalse(
                windowsSection.Contains("| u-1 |", StringComparison.Ordinal),
                "Hosted images must never be merged into one profile.");
            Assert.IsFalse(
                ubuntuSection.Contains("| w-1 |", StringComparison.Ordinal),
                "Hosted images must never be merged into one profile.");
            StringAssert.Contains(windowsSection, "Sampled runs: 5. Legs: 2.");
            StringAssert.Contains(ubuntuSection, "Sampled runs: 5. Legs: 1.");

            // Reproducible per-leg case duration plus a gain above the noise band adopts;
            // a single-leg image has no skew to exploit and keeps the static weights.
            StringAssert.Contains(
                windowsSection,
                "**Verdict for windows-latest: material skew.");
            StringAssert.Contains(
                ubuntuSection,
                "**Verdict for ubuntu-latest: no material skew.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public async Task CollectorScript_HasNoPowerShellAstParseErrorsAsync()
    {
        const string parseCommand =
            "$tokens = $null; $errors = $null; " +
            "[System.Management.Automation.Language.Parser]::ParseFile(" +
            "$env:ROSLYN_MCP_SCRIPT_UNDER_TEST, [ref]$tokens, [ref]$errors) | Out-Null; " +
            "if ($errors.Count -gt 0) { $errors | ForEach-Object { [Console]::Error.WriteLine($_) }; exit 1 }";

        var result = await RunPowerShellAsync(
            ["-NoProfile", "-NonInteractive", "-Command", parseCommand],
            new Dictionary<string, string?>
            {
                ["ROSLYN_MCP_SCRIPT_UNDER_TEST"] = GetCollectorScriptPath(),
            },
            CollectorDescription);

        AssertSucceeded(result);
    }

    /// <summary>
    /// Five complete runs of a two-leg <c>windows-latest</c> profile: the smallest observation set
    /// that clears every sampling gate, so a corruption hook isolates the rule under test.
    /// </summary>
    private static List<LegObservation> BuildCompleteWindowsProfile()
    {
        var observations = new List<LegObservation>();
        for (var run = 1; run <= 5; run++)
        {
            observations.Add(new LegObservation($"r{run}", "w-1", "windows-latest", 100, 10));
            observations.Add(new LegObservation($"r{run}", "w-2", "windows-latest", 200, 40));
        }

        return observations;
    }

    private sealed record LegObservation(
        string RunId,
        string Leg,
        string Image,
        int WallTimeSeconds,
        int CaseDurationSeconds);

    private static string ExtractImageSection(string report, string imageName)
    {
        var marker = "### " + imageName;
        var start = report.IndexOf(marker, StringComparison.Ordinal);
        Assert.AreNotEqual(-1, start, $"Report is missing the '{imageName}' section.");
        var next = report.IndexOf("\n### ", start + marker.Length, StringComparison.Ordinal);
        return next < 0 ? report[start..] : report[start..next];
    }

    /// <param name="mutate">
    /// Optional corruption hook run after a well-formed fixture is laid down but before the
    /// manifest is written, so a fail-closed regression can break exactly one invariant.
    /// Receives the manifest entries and the resolved ResultsRoot.
    /// </param>
    private static async Task<PwshScriptResult> RunCollectorAsync(
        string fixtureRoot,
        IReadOnlyList<LegObservation> observations,
        Action<List<Dictionary<string, object>>, string>? mutate = null)
    {
        var resultsRoot = Path.Combine(fixtureRoot, "downloads");
        var manifest = new List<Dictionary<string, object>>();
        foreach (var observation in observations)
        {
            var relativePath = $"{observation.RunId}/test-results-{observation.Leg}";
            var legDirectory = Path.Combine(
                resultsRoot,
                observation.RunId,
                $"test-results-{observation.Leg}");
            Directory.CreateDirectory(legDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(legDirectory, "results.trx"),
                BuildTwoCaseTrx(observation.CaseDurationSeconds));

            manifest.Add(new Dictionary<string, object>
            {
                ["runId"] = observation.RunId,
                ["leg"] = observation.Leg,
                ["image"] = observation.Image,
                ["wallTimeSeconds"] = observation.WallTimeSeconds,
                ["path"] = relativePath,
            });
        }

        mutate?.Invoke(manifest, resultsRoot);

        var manifestPath = Path.Combine(fixtureRoot, "legs.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

        return await RunPowerShellAsync(
            [
                "-NoProfile",
                "-NonInteractive",
                "-File",
                GetCollectorScriptPath(),
                "-ResultsRoot",
                resultsRoot,
                "-LegManifest",
                manifestPath,
            ],
            environment: null,
            description: CollectorDescription);
    }

    private static string BuildTwoCaseTrx(int totalSeconds)
    {
        // Split across two cases so the reported sum is an aggregate, not a single duration.
        var first = TimeSpan.FromSeconds(totalSeconds - 1).ToString(
            "c",
            CultureInfo.InvariantCulture);
        var second = TimeSpan.FromSeconds(1).ToString("c", CultureInfo.InvariantCulture);
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="a" testName="one" outcome="Passed" duration="{first}" />
                <UnitTestResult testId="b" testName="two" outcome="Passed" duration="{second}" />
              </Results>
              <TestDefinitions>
                <UnitTest id="a">
                  <TestMethod className="Example.Alpha" name="One" />
                </UnitTest>
                <UnitTest id="b">
                  <TestMethod className="Example.Beta" name="Two" />
                </UnitTest>
              </TestDefinitions>
            </TestRun>
            """;
    }

    private static string GetCollectorScriptPath() => Path.Combine(
        TestFixtureFileSystem.FindRepositoryRoot(),
        "eng",
        "collect-hosted-shard-timings.ps1");

    private static string CreateFixtureRoot()
    {
        var fixtureRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(TestResultsSummaryContractTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        return fixtureRoot;
    }

    private static Task<PwshScriptResult> RunScriptAsync(params string[] arguments)
    {
        var processArguments = new List<string>
        {
            "-NoProfile",
            "-NonInteractive",
            "-File",
            GetScriptPath(),
        };
        processArguments.AddRange(arguments);
        return RunPowerShellAsync(
            processArguments,
            environment: null,
            description: SummarizerDescription);
    }

    private static string GetScriptPath() => Path.Combine(
        TestFixtureFileSystem.FindRepositoryRoot(),
        "eng",
        "summarize-test-results.ps1");

    private static Task<PwshScriptResult> RunPowerShellAsync(
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?>? environment,
        string description)
        => PwshScriptRunner.RunAsync(
            arguments,
            workingDirectory: TestFixtureFileSystem.FindRepositoryRoot(),
            environment: environment,
            timeout: _processTimeout,
            description: description);

    private static void AssertSucceeded(PwshScriptResult result) => Assert.AreEqual(
        0,
        result.ExitCode,
        $"Script failed. stdout={result.StdOut} stderr={result.StdErr}");

    private static void AssertFailedWith(PwshScriptResult result, string expectedDiagnostic)
    {
        Assert.AreNotEqual(
            0,
            result.ExitCode,
            $"Script unexpectedly succeeded. stdout={result.StdOut} stderr={result.StdErr}");
        StringAssert.Contains(result.StdOut + result.StdErr, expectedDiagnostic);
    }

    private const string FirstTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="a" testName="row one" outcome="Passed" duration="00:00:02.5000000" />
            <UnitTestResult testId="a" testName="row two" outcome="Passed" duration="00:00:01.0000000" />
            <UnitTestResult testId="b" testName="failure" outcome="Failed" duration="00:00:04.0000000" />
            <UnitTestResult testId="c" testName="skipped" outcome="NotExecuted" duration="00:00:00" />
          </Results>
          <TestDefinitions>
            <UnitTest id="a">
              <TestMethod codeBase="C:\private-source\tests.dll" className="Example.Slow|&#xA;Class" name="Case|One" />
            </UnitTest>
            <UnitTest id="b">
              <TestMethod codeBase="C:\private-source\tests.dll" className="Example.Failing" name="Fails" />
            </UnitTest>
            <UnitTest id="c">
              <TestMethod codeBase="C:\private-source\tests.dll" className="Example.Skipped" name="Skips" />
            </UnitTest>
          </TestDefinitions>
        </TestRun>
        """;

    private const string SecondTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="d" testName="error" outcome="Error" duration="00:00:03.0000000" />
          </Results>
          <TestDefinitions>
            <UnitTest id="d">
              <TestMethod codeBase="/private-source/tests.dll" className="Example.Error" name="Errors" />
            </UnitTest>
          </TestDefinitions>
        </TestRun>
        """;

    private const string UnjoinableTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="missing" testName="orphan" outcome="Passed" duration="00:00:01" />
          </Results>
          <TestDefinitions />
        </TestRun>
        """;
}
