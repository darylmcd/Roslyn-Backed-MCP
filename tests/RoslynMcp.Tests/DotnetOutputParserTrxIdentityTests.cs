using System.Xml;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class DotnetOutputParserTrxIdentityTests
{
    [TestMethod]
    [DataRow("http://microsoft.com/schemas/VisualStudio/TeamTest/2010")]
    [DataRow("urn:alternate-test-host")]
    public void Definitions_SeparateEqualDisplayNamesAndPreserveParameterizedLabels(string xmlns)
    {
        var result = Parse($$"""
            <TestRun xmlns="{{xmlns}}">
              <Results>
                <UnitTestResult testId="a" testName="Shared (1.5)" outcome="Failed" />
                <UnitTestResult testId="b" testName="Shared (1.5)" outcome="Failed" />
              </Results>
              <TestDefinitions>
                <UnitTest id="b"><TestMethod className="Example.Second" name="Shared" /></UnitTest>
                <UnitTest id="a"><TestMethod className="Example.First" name="Shared" /></UnitTest>
              </TestDefinitions>
              <ResultSummary><Counters total="2" passed="0" failed="2" notExecuted="0" /></ResultSummary>
            </TestRun>
            """);
        Assert.AreEqual(2, result.Total);
        Assert.AreEqual(2, result.Failed);
        CollectionAssert.AreEqual(new[] { "Example.First.Shared", "Example.Second.Shared" },
            result.Failures.Select(failure => failure.FullyQualifiedName).ToArray());
        Assert.IsTrue(result.Failures.All(failure => failure.DisplayName == "Shared (1.5)"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("<UnitTest id='other'><TestMethod className='Wrong.Type' name='Wrong' /></UnitTest>")]
    [DataRow("<UnitTest id='a'><TestMethod name='MissingClass' /></UnitTest>")]
    [DataRow("<UnitTest id='a'><TestMethod className='Missing.Method' /></UnitTest>")]
    [DataRow("<UnitTest id='a'><TestMethod className='One' name='Method' /><TestMethod className='Two' name='Method' /></UnitTest>")]
    [DataRow("<UnitTest id='a'><TestMethod className='One' name='Method' /></UnitTest><UnitTest id='a'><TestMethod className='Two' name='Method' /></UnitTest>")]
    public void MissingOrAmbiguousDefinition_PreservesExistingFallback(string definitions)
    {
        var result = Parse($$"""
            <TestRun><Results><UnitTestResult testId="a" testName="Reported.Type.Method" outcome="Failed" /></Results>
            <TestDefinitions>{{definitions}}</TestDefinitions>
            <ResultSummary><Counters total="1" failed="1" /></ResultSummary></TestRun>
            """);
        Assert.HasCount(1, result.Failures);
        Assert.AreEqual("Reported.Type.Method", result.Failures[0].FullyQualifiedName);
        Assert.AreEqual("Method", result.Failures[0].DisplayName);
    }

    [TestMethod]
    public void MalformedTrx_RemainsAnXmlFailureRatherThanFabricatedTestResults()
        => Assert.ThrowsExactly<XmlException>(() => Parse("<TestRun><TestDefinitions>"));

    private static TestRunResultDto Parse(string xml)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(DotnetOutputParserTrxIdentityTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var trx = Path.Combine(root, "result.trx");
            File.WriteAllText(trx, xml);
            var execution = new CommandExecutionDto("dotnet", ["test"], root, Path.Combine(root, "Tests.csproj"),
                1, false, 1, "", "");
            return DotnetOutputParser.ParseTestRun(execution, [XmlFileLoader.Load(trx)]);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.Dispose("Failed to release native fixture.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
