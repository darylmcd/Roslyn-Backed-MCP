using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TrxReportIntegrityTests
{
    [TestMethod]
    public void FailedResults_ResolveReportLocalDefinitionsWithoutLosingDisplayLabels()
    {
        var path = WriteReport("""
            <TestRun><Results>
              <UnitTestResult testId="one" testName="Case(1)" outcome="Failed" />
              <UnitTestResult testId="two" testName="Case(1)" outcome="Failed" />
            </Results><TestDefinitions>
              <UnitTest id="one"><TestMethod className="Alpha.Tests" name="Case" /></UnitTest>
              <UnitTest id="two"><TestMethod className="Beta.Tests" name="Case" /></UnitTest>
            </TestDefinitions><ResultSummary outcome="Failed"><Counters total="2" executed="2" passed="0" failed="2" notExecuted="0" /></ResultSummary></TestRun>
            """);
        try
        {
            var result = DotnetOutputParser.ParseTestRun(Execution(), [path]);
            CollectionAssert.AreEqual(new[] { "Alpha.Tests.Case", "Beta.Tests.Case" },
                result.Failures.Select(f => f.FullyQualifiedName).ToArray());
            CollectionAssert.AreEqual(new[] { "Case(1)", "Case(1)" },
                result.Failures.Select(f => f.DisplayName).ToArray());
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void FailedResults_DuplicateOrMissingDefinitionNeverInventsIdentity()
    {
        var path = WriteReport("""
            <TestRun><Results>
              <UnitTestResult testId="dup" testName="Shared" outcome="Failed" />
              <UnitTestResult testId="missing" testName="Shared" outcome="Failed" />
            </Results><TestDefinitions>
              <UnitTest id="dup"><TestMethod className="Alpha" name="Case" /></UnitTest>
              <UnitTest id="dup"><TestMethod className="Beta" name="Case" /></UnitTest>
            </TestDefinitions><ResultSummary outcome="Failed"><Counters total="2" executed="2" passed="0" failed="2" notExecuted="0" /></ResultSummary></TestRun>
            """);
        try
        {
            var result = DotnetOutputParser.ParseTestRun(Execution(), [path]);
            Assert.IsTrue(result.Failures.All(f => f.FullyQualifiedName is null));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void FailedResult_DottedDisplayLabelWithoutTestIdIsNotVerifiedIdentity()
    {
        var path = WriteReport("""
            <TestRun><Results><UnitTestResult testName="Alpha.Tests.Case(1)" outcome="Failed" /></Results>
              <ResultSummary outcome="Failed"><Counters total="1" executed="1" passed="0" failed="1" /></ResultSummary></TestRun>
            """);
        try
        {
            var result = DotnetOutputParser.ParseTestRun(Execution(), [path]);
            Assert.AreEqual("Case(1)", result.Failures.Single().DisplayName);
            Assert.IsNull(result.Failures.Single().FullyQualifiedName);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void SuccessfulProcess_AdverseSummaryFailsReportAndWorkspaceVerdict()
    {
        var path = WriteReport("""
            <TestRun><Results><UnitTestResult testName="Alpha.Case" outcome="Passed" /></Results>
              <ResultSummary outcome="Failed"><Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" /></ResultSummary></TestRun>
            """);
        try
        {
            var result = DotnetOutputParser.ParseTestRun(Execution(), [path]);
            Assert.AreEqual("TestReportFailure", result.FailureEnvelope?.ErrorKind);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [DataRow("<TestRun><ResultSummary outcome=\"Completed\"><Counters total=\"1\" passed=\"2\" failed=\"0\" /></ResultSummary></TestRun>")]
    [DataRow("<TestRun><ResultSummary outcome=\"Completed\"><Counters total=\"-1\" passed=\"0\" failed=\"0\" /></ResultSummary></TestRun>")]
    [DataRow("<TestRun><ResultSummary outcome=\"Completed\"><Counters total=\"x\" passed=\"0\" failed=\"0\" /></ResultSummary></TestRun>")]
    [DataRow("<TestRun />")]
    [DataRow("<TestRun><ResultSummary outcome=\"Completed\"><Counters total=\"1\" passed=\"0\" failed=\"0\" notExecuted=\"0\" /></ResultSummary><Results><UnitTestResult outcome=\"NotExecuted\" /></Results></TestRun>")]
    [DataRow("<TestRun><ResultSummary outcome=\"Completed\"><Counters total=\"1\" passed=\"1\" failed=\"0\" /></ResultSummary></TestRun>")]
    public void InvalidReport_FailsClosed(string xml)
    {
        var path = WriteReport(xml);
        try
        {
            var result = DotnetOutputParser.ParseTestRun(Execution(), [path]);
            Assert.AreEqual("TestReportFailure", result.FailureEnvelope?.ErrorKind);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void SuppliedMissingPath_FailsClosed()
    {
        var result = DotnetOutputParser.ParseTestRun(Execution(), [Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".trx")]);
        Assert.AreEqual("TestReportFailure", result.FailureEnvelope?.ErrorKind);
    }

    [TestMethod]
    public void AggregatedCounterOverflow_FailsClosed()
    {
        var xml = "<TestRun><Results><UnitTestResult testName=\"Alpha.Case\" outcome=\"Passed\" /></Results><ResultSummary outcome=\"Completed\"><Counters total=\"2147483647\" passed=\"2147483647\" failed=\"0\" /></ResultSummary></TestRun>";
        var first = WriteReport(xml);
        var second = WriteReport(xml);
        try
        {
            var result = DotnetOutputParser.ParseTestRun(Execution(), [first, second]);
            Assert.AreEqual("TestReportFailure", result.FailureEnvelope?.ErrorKind);
            StringAssert.Contains(result.FailureEnvelope!.Summary, "Aggregated TRX counters exceed");
        }
        finally { File.Delete(first); File.Delete(second); }
    }

    [TestMethod]
    public void ReportLocalId_MayResolveDifferentlyInSeparateReports()
    {
        static string Xml(string className) => $"""
            <TestRun><Results><UnitTestResult testId="shared" testName="Case(1)" outcome="Failed" /></Results>
            <TestDefinitions><UnitTest id="shared"><TestMethod className="{className}" name="Case" /></UnitTest></TestDefinitions>
            <ResultSummary outcome="Failed"><Counters total="1" passed="0" failed="1" /></ResultSummary></TestRun>
            """;
        var first = WriteReport(Xml("Alpha"));
        var second = WriteReport(Xml("Beta"));
        try
        {
            var result = DotnetOutputParser.ParseTestRun(Execution(), [first, second]);
            CollectionAssert.AreEquivalent(new[] { "Alpha.Case", "Beta.Case" },
                result.Failures.Select(f => f.FullyQualifiedName).ToArray());
        }
        finally { File.Delete(first); File.Delete(second); }
    }

    [TestMethod]
    public void NoTrxPathAndValidZeroReportRemainDistinct()
    {
        Assert.IsNull(DotnetOutputParser.ParseTestRun(Execution(), []).FailureEnvelope);
        var path = WriteReport("""
            <TestRun><ResultSummary outcome="Completed"><Counters total="0" executed="0" passed="0" failed="0" /></ResultSummary></TestRun>
            """);
        try { Assert.IsNull(DotnetOutputParser.ParseTestRun(Execution(), [path]).FailureEnvelope); }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void PartialResultRows_WithPositiveCountersRemainValid()
    {
        var path = WriteReport("""
            <TestRun><Results><UnitTestResult testName="Alpha.Case" outcome="Passed" /></Results>
              <ResultSummary outcome="Completed"><Counters total="2" executed="2" passed="2" failed="0" /></ResultSummary></TestRun>
            """);
        try
        {
            var result = DotnetOutputParser.ParseTestRun(Execution(), [path]);
            Assert.AreEqual(2, result.Total);
            Assert.IsNull(result.FailureEnvelope);
        }
        finally { File.Delete(path); }
    }

    private static CommandExecutionDto Execution() => new("dotnet", ["test"], Path.GetTempPath(),
        "sample.csproj", 0, true, 1, "", "");

    private static string WriteReport(string xml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rmcp-trx-integrity-{Guid.NewGuid():N}.trx");
        File.WriteAllText(path, xml);
        return path;
    }
}
