using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
[TestCategory("Process")]
public sealed class TestResultsSummaryContractTests
{
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
            });

        AssertSucceeded(result);
    }

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
        return RunPowerShellAsync(processArguments, environment: null);
    }

    private static string GetScriptPath() => Path.Combine(
        TestFixtureFileSystem.FindRepositoryRoot(),
        "eng",
        "summarize-test-results.ps1");

    private static Task<PwshScriptResult> RunPowerShellAsync(
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?>? environment)
        => PwshScriptRunner.RunAsync(
            arguments,
            workingDirectory: TestFixtureFileSystem.FindRepositoryRoot(),
            environment: environment,
            timeout: _processTimeout,
            description: "test-results summarizer");

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
