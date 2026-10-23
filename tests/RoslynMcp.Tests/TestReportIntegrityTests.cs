using System.Xml.Linq;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestReportIntegrityTests
{
    private const string MixedProducerIdentity = "ROSLYNMCP_MIXED_TRX_PRODUCER";

    [TestMethod]
    [DataRow("negative-passed")]
    [DataRow("negative-skipped")]
    [DataRow("failure-details")]
    [DataRow("no-positive-proof")]
    [DataRow("passed-exceeds-total")]
    [DataRow("skipped-exceeds-total")]
    public void DirectVerdict_RejectsContradictorySuccessfulDto(string shape)
    {
        var result = new TestRunResultDto(Execution(""), 1, shape == "negative-passed" ? -1
            : shape == "no-positive-proof" ? 0 : shape == "passed-exceeds-total" ? 2 : 1,
            0, shape == "negative-skipped" ? -1 : shape == "skipped-exceeds-total" ? 2 : 0,
            shape == "failure-details" ? [new("OwnedFailure", "Owned.Failure", "failed", null)] : []);
        Assert.AreEqual("test-failure", TestExecutionVerdict.Status(result));
    }

    [TestMethod]
    public void DirectVerdict_PassAndSkipProofCannotExceedTotal()
    {
        foreach (var total in new[] { 1, int.MaxValue })
        {
            var result = new TestRunResultDto(Execution(""), total, total, 0, total, []);
            Assert.AreEqual("test-failure", TestExecutionVerdict.Status(result));
        }
    }

    [TestMethod]
    public async Task OwnedTrx_PassAndSkipProofCannotExceedTotal()
    {
        foreach (var total in new[] { 1, int.MaxValue })
        {
            var report = Report(total, total);
            report.Descendants("Counters").Single().SetAttributeValue("notExecuted", total);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReadAsync(report));
        }
    }

    [TestMethod]
    [DataRow("total", "-1")]
    [DataRow("passed", "-1")]
    [DataRow("failed", "-1")]
    [DataRow("notExecuted", "-1")]
    [DataRow("executed", "-1")]
    [DataRow("error", "-1")]
    [DataRow("total", "not-an-integer")]
    [DataRow("passed", "not-an-integer")]
    [DataRow("failed", "not-an-integer")]
    [DataRow("notExecuted", "not-an-integer")]
    [DataRow("aborted", "not-an-integer")]
    [DataRow("total", "2147483648")]
    [DataRow("failed", "2147483648")]
    [DataRow("passed", "1.5")]
    [DataRow("failed", "")]
    [DataRow("passed", "2")]
    [DataRow("failed", "2")]
    [DataRow("notExecuted", "2")]
    [DataRow("executed", "2")]
    public async Task OwnedTrx_RejectsInvalidPresentCounter(string counter, string value)
    {
        var report = Report();
        report.Descendants("Counters").Single().SetAttributeValue(counter, value);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReadAsync(report));
    }

    [TestMethod]
    [DataRow("passed")]
    [DataRow("failed")]
    public async Task OwnedTrx_RejectsExecutedCounterContradictingKnownExecutedOutcomes(string outcome)
    {
        var report = outcome == "failed" ? Report(1, 0, 1) : Report();
        report.Descendants("Counters").Single().SetAttributeValue("executed", 0);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReadAsync(report));
    }

    [TestMethod]
    public async Task OwnedTrx_UnrecognizedPresentSummaryCannotProveClean()
    {
        var report = Report();
        report.Root!.Element("ResultSummary")!.SetAttributeValue("outcome", "UnrecognizedCompletion");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReadAsync(report));
    }

    [TestMethod]
    [DataRow("UnrecognizedCompletion")]
    [DataRow(null)]
    public async Task OwnedTrx_UnqualifiedUnitResultCannotProveClean(string? outcome)
    {
        var report = Report();
        var unit = new XElement("UnitTestResult", new XAttribute("testName", "UnqualifiedOwnedResult"));
        if (outcome is not null) unit.SetAttributeValue("outcome", outcome);
        report.Root!.AddFirst(new XElement("Results", unit));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReadAsync(report));
    }

    [TestMethod]
    public async Task OwnedTrx_UnprovedFragmentCannotBorrowAnotherFragmentsPositiveOutcome()
    {
        var result = await ReadAsync(Report(), Report(1, 0));
        Assert.AreEqual("test-failure", TestExecutionVerdict.Status(result));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OwnedTrx_RejectsFailedDetailsWithoutPositiveFailureCounter(bool missingCounter)
    {
        var report = Report();
        report.Root!.AddFirst(new XElement("Results", new XElement("UnitTestResult",
            new XAttribute("testName", "OwnedFailure"), new XAttribute("outcome", "Failed"))));
        if (missingCounter) report.Descendants("Counters").Single().Attribute("failed")!.Remove();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReadAsync(report));
    }

    [TestMethod]
    [DataRow("Error")]
    [DataRow("Timeout")]
    [DataRow("Aborted")]
    [DataRow("NotRunnable")]
    [DataRow("PassedButRunAborted")]
    [DataRow("Disconnected")]
    [DataRow("InProgress")]
    [DataRow("Pending")]
    public async Task OwnedTrx_RejectsAdverseDetailContradictingSuccessfulCounters(string outcome)
    {
        var report = Report();
        report.Root!.AddFirst(new XElement("Results", new XElement("UnitTestResult",
            new XAttribute("testName", "OwnedAdverseResult"), new XAttribute("outcome", outcome))));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReadAsync(report));
    }

    [TestMethod]
    [DataRow("Error")]
    [DataRow("Timeout")]
    [DataRow("Aborted")]
    [DataRow("NotRunnable")]
    [DataRow("PassedButRunAborted")]
    [DataRow("Disconnected")]
    [DataRow("InProgress")]
    [DataRow("Pending")]
    public async Task OwnedTrx_AdverseDetailCannotBorrowAnUnrelatedPositiveCounter(string outcome)
    {
        var report = Report(2, 1);
        report.Root!.Element("ResultSummary")!.SetAttributeValue("outcome", "Completed");
        report.Root.AddFirst(new XElement("Results", new XElement("UnitTestResult",
            new XAttribute("testName", "OwnedIncompleteResult"), new XAttribute("outcome", outcome))));
        Assert.AreEqual("test-failure", TestExecutionVerdict.Status(await ReadAsync(report)));
    }

    [TestMethod]
    [DataRow("error")]
    [DataRow("timeout")]
    [DataRow("aborted")]
    [DataRow("notRunnable")]
    [DataRow("passedButRunAborted")]
    [DataRow("disconnected")]
    [DataRow("inProgress")]
    [DataRow("pending")]
    public async Task OwnedTrx_AdverseCounterCannotProveClean(string counter)
    {
        var report = Report(2, 1);
        report.Root!.Element("ResultSummary")!.SetAttributeValue("outcome", char.ToUpperInvariant(counter[0]) + counter[1..]);
        report.Descendants("Counters").Single().SetAttributeValue(counter, 1);
        var result = await ReadAsync(report);
        Assert.AreEqual("test-failure", TestExecutionVerdict.Status(result));
    }

    [TestMethod]
    [DataRow("unrelated-root")]
    [DataRow("duplicate-counter")]
    [DataRow("duplicate-summary")]
    [DataRow("nested-counter")]
    [DataRow("nested-duplicate-summary")]
    [DataRow("nested-only-summary")]
    public async Task OwnedTrx_RequiresUnambiguousReportStructure(string shape)
    {
        var report = Report();
        var summary = report.Root!.Element("ResultSummary")!;
        var counters = summary.Element("Counters")!;
        switch (shape)
        {
            case "unrelated-root": report.Root.Name = "Unrelated"; break;
            case "duplicate-counter": summary.Add(new XElement(counters)); break;
            case "duplicate-summary": report.Root.Add(new XElement(summary)); break;
            case "nested-counter": counters.Remove(); summary.Add(new XElement("Unrelated", counters)); break;
            case "nested-duplicate-summary":
                report.Root.Add(new XElement("Unrelated", new XElement("ResultSummary", new XAttribute("outcome", "Aborted"))));
                break;
            case "nested-only-summary": summary.Remove(); report.Root.Add(new XElement("Unrelated", summary)); break;
        }
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReadAsync(report));
    }

    [TestMethod]
    public async Task OwnedTrx_CheckedAggregationRejectsOverflow()
    {
        var first = Report(int.MaxValue, int.MaxValue);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReadAsync(first, Report()));
    }

    [TestMethod]
    public async Task OwnedTrx_NegativeReportCannotCancelAnEarlierFailure()
    {
        var failed = Report(1, 0, 1);
        var knownFailure = await ReadAsync(failed);
        Assert.AreEqual(1, knownFailure.Failed, "The first native-shaped fragment must independently report a legitimate failure.");
        Assert.AreEqual("test-failure", TestExecutionVerdict.Status(knownFailure));
        var cancelling = Report(1, 1, -1);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ReadAsync(failed, cancelling));
    }

    [TestMethod]
    [DataRow("passed")]
    [DataRow("skipped")]
    [DataRow("other-outcomes")]
    [DataRow("warning")]
    public async Task OwnedTrx_SafeOptionalCountersAndOtherOutcomesRemainReadable(string shape)
    {
        var report = Report(shape is "other-outcomes" or "warning" ? 2 : 1, shape == "skipped" ? 0 : 1);
        var counters = report.Descendants("Counters").Single();
        counters.Attribute("failed")!.Remove();
        counters.Attribute("notExecuted")!.Remove();
        if (shape == "skipped")
        {
            counters.Attribute("passed")!.Remove();
            report.Root!.AddFirst(new XElement("Results", new XElement("UnitTestResult",
                new XAttribute("testName", "OwnedSkipped"), new XAttribute("outcome", "Inconclusive"))));
        }
        if (shape == "other-outcomes") counters.SetAttributeValue("completed", 1);
        if (shape == "warning") counters.SetAttributeValue("warning", 1);
        Assert.AreEqual("clean", TestExecutionVerdict.Status(await ReadAsync(report)));
    }

    [TestMethod]
    public async Task ActualMstestProducer_TrxRemainsAcceptedByOwnedReader()
    {
        await WithOwnerAsync(async owner =>
        {
            var assembly = typeof(TestReportIntegrityTests).Assembly.Location;
            var method = typeof(DotnetOutputParserBuildDiagnosticsTests).FullName
                + "." + nameof(DotnetOutputParserBuildDiagnosticsTests.ParseBuildDiagnostics_LocationlessNuGetError_IsReported);
            var run = await PwshScriptRunner.RunExecutableAsync("dotnet",
                ["vstest", assembly, "/TestCaseFilter:FullyQualifiedName=" + method,
                    "/Logger:trx;LogFileName=actual.trx", "/ResultsDirectory:" + owner.Path],
                environment: null, workingDirectory: Path.GetDirectoryName(assembly), timeout: TimeSpan.FromSeconds(60),
                description: "owned actual TRX producer");
            Assert.AreEqual(0, run.ExitCode, run.AllOutput);
            var result = await TestRunOutputReader.ParseAsync(owner, Execution(owner.Path), CancellationToken.None);
            Assert.AreEqual(1, result.Total);
            Assert.AreEqual(1, result.Passed);
            Assert.AreEqual("clean", TestExecutionVerdict.Status(result));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeMixedDataRowProducer(bool inconclusive)
    {
        // Dedicated native reporter fixture. Only the exact child invocation selects
        // the inconclusive branch; the parent canary qualifies both native row results.
        var identity = Environment.GetEnvironmentVariable(MixedProducerIdentity);
        if (identity is null) return;
        Assert.AreEqual(typeof(TestReportIntegrityTests).FullName + "." + nameof(NativeMixedDataRowProducer), identity);
        if (inconclusive) Assert.Inconclusive("Owned native mixed-row reporter control.");
    }

    [TestMethod]
    public async Task ActualMstestDataRows_PreserveNativePassAndInconclusiveProof()
    {
        await WithOwnerAsync(async owner =>
        {
            var assembly = typeof(TestReportIntegrityTests).Assembly.Location;
            var method = typeof(TestReportIntegrityTests).FullName + "." + nameof(NativeMixedDataRowProducer);
            var run = await PwshScriptRunner.RunExecutableAsync("dotnet",
                ["vstest", assembly, "/TestCaseFilter:FullyQualifiedName=" + method,
                    "/Logger:trx;LogFileName=mixed.trx", "/ResultsDirectory:" + owner.Path],
                environment: new Dictionary<string, string?> { [MixedProducerIdentity] = method },
                workingDirectory: Path.GetDirectoryName(assembly), timeout: TimeSpan.FromSeconds(60),
                description: "owned actual mixed MSTest TRX producer");
            Assert.AreEqual(0, run.ExitCode, run.AllOutput);
            var native = XDocument.Load(Path.Combine(owner.Path, "mixed.trx"));
            var counters = native.Descendants().Single(element => element.Name.LocalName == "Counters");
            Assert.AreEqual("2", (string?)counters.Attribute("total"));
            Assert.AreEqual("1", (string?)counters.Attribute("passed"));
            Assert.AreEqual("0", (string?)counters.Attribute("failed"));
            var results = native.Descendants().Where(element => element.Name.LocalName == "UnitTestResult").ToArray();
            var leaves = results.Where(result => !result.Descendants().Any(element => element.Name.LocalName == "UnitTestResult")).ToArray();
            Assert.AreEqual(2, leaves.Length, "The native producer must actually execute both data rows.");
            Assert.AreEqual(1, leaves.Count(result => (string?)result.Attribute("outcome") == "Passed"));
            Assert.AreEqual(1, leaves.Count(result => (string?)result.Attribute("outcome") is "NotExecuted" or "Inconclusive"));
            Console.WriteLine($"Native mixed TRX hierarchy: results={results.Length}, leaves={leaves.Length}, innerResults={native.Descendants().Count(element => element.Name.LocalName == "InnerResults")}");
            var parsed = await TestRunOutputReader.ParseAsync(owner, Execution(owner.Path), CancellationToken.None);
            Assert.AreEqual(2, parsed.Total);
            Assert.AreEqual(1, parsed.Passed);
            Assert.AreEqual(1, parsed.Skipped);
            Assert.AreEqual("clean", TestExecutionVerdict.Status(parsed));
        });
    }

    private static XDocument Report(int total = 1, int passed = 1, int failed = 0) => new(new XElement("TestRun",
        new XElement("ResultSummary", new XAttribute("outcome", failed > 0 ? "Failed" : "Passed"), new XElement("Counters",
            new XAttribute("total", total), new XAttribute("passed", passed), new XAttribute("failed", failed),
            new XAttribute("notExecuted", 0)))));

    private static async Task<TestRunResultDto> ReadAsync(params XDocument[] reports)
    {
        TestRunResultDto? result = null;
        await WithOwnerAsync(async owner =>
        {
            for (var index = 0; index < reports.Length; index++)
                await File.WriteAllTextAsync(Path.Combine(owner.Path, index + ".trx"), reports[index].ToString());
            result = await TestRunOutputReader.ParseAsync(owner, Execution(owner.Path), CancellationToken.None);
        });
        return result ?? throw new AssertFailedException("Owned collection did not produce a result.");
    }

    private static CommandExecutionDto Execution(string root) => new("dotnet", ["test"], root,
        Path.Combine(root, "OwnedTests.csproj"), 0, true, 1, "", "");

    private static async Task WithOwnerAsync(Func<PhysicalDirectoryScope.OwnedRoot, Task> action)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(TestReportIntegrityTests), Guid.NewGuid().ToString("N"));
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            owner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(root, "invocation"));
            await action(owner);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Owned report integrity fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => owner?.Delete(CancellationToken.None)),
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
