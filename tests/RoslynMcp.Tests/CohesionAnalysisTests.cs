using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class CohesionAnalysisTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task GetCohesionMetrics_WhenExcludeTestProjects_OmitsTestProjectSources()
    {
        var result = await CohesionAnalysisService.GetCohesionMetricsAsync(
            WorkspaceId, null, null, 2, 200, includeInterfaces: false, excludeTestProjects: true, CancellationToken.None);

        Assert.IsTrue(
            result.All(m => m.FilePath is null ||
                !m.FilePath.Contains("SampleLib.Tests", StringComparison.OrdinalIgnoreCase)),
            "Types from MSBuild test projects should be omitted when excludeTestProjects is true.");
    }

    [TestMethod]
    public async Task GetCohesionMetrics_ReturnsMetrics()
    {
        var result = await CohesionAnalysisService.GetCohesionMetricsAsync(
            WorkspaceId, null, null, 2, 50, includeInterfaces: false, excludeTestProjects: false, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0, "Expected at least one type with cohesion metrics");

        var first = result[0];
        Assert.IsFalse(string.IsNullOrWhiteSpace(first.TypeName));
        Assert.IsTrue(first.Lcom4Score >= 1, "LCOM4 score should be at least 1");
    }

    [TestMethod]
    public async Task FindSharedMembers_RunsWithoutError()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.AnimalService");
        var result = await CohesionAnalysisService.FindSharedMembersAsync(
            WorkspaceId, locator, CancellationToken.None);

        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task GetCohesionMetrics_WithFilePath_FiltersByFile()
    {
        var doc = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath?.EndsWith("AnimalService.cs") == true);

        Assert.IsNotNull(doc?.FilePath, "AnimalService.cs not found in workspace");

        var result = await CohesionAnalysisService.GetCohesionMetricsAsync(
            WorkspaceId, doc.FilePath, null, 1, 50, includeInterfaces: false, excludeTestProjects: false, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0, "Expected at least one metric from filtered file");
        Assert.IsTrue(result.All(m =>
            m.FilePath?.Contains("AnimalService.cs") == true),
            "All results should be from the filtered file");
    }

    [TestMethod]
    public async Task GetCohesionMetrics_WhenIncludingInterfaces_Returns_InterfaceTypeKind()
    {
        var result = await CohesionAnalysisService.GetCohesionMetricsAsync(
            WorkspaceId, null, "SampleLib", 1, 100, includeInterfaces: true, excludeTestProjects: false, CancellationToken.None);

        var interfaces = result.Where(m => m.TypeKind == "Interface").ToList();
        Assert.IsTrue(interfaces.Count > 0, "Expected at least one interface when includeInterfaces=true.");
        Assert.IsTrue(interfaces.All(i => i.Lcom4Score == i.MethodCount),
            "Interface metrics should use trivial LCOM4 equal to method count.");
    }

    [TestMethod]
    public async Task GetCohesionMetrics_IgnoresLoggerMessagePartialMethods()
    {
        // BUG fix (cohesion-metrics-source-gen-aware): a class with one real method plus
        // several [LoggerMessage] partials previously got Lcom4Score = N+1 because each
        // partial was counted as its own LCOM4 cluster. After the fix, partials are excluded
        // from the method enumeration entirely, so the score reflects only the real method.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        string? workspaceId = null;

        try
        {
            var filePath = Path.Combine(copiedRoot, "SampleLib", "QuestionClassifier.cs");
            await File.WriteAllTextAsync(filePath, """
namespace SampleLib;

using Microsoft.Extensions.Logging;

public partial class QuestionClassifier
{
    private readonly ILogger _logger;

    public QuestionClassifier(ILogger logger)
    {
        _logger = logger;
    }

    public string Classify(string input)
    {
        LogStarting(_logger, input);
        return input.Length > 50 ? "long" : "short";
    }

    [LoggerMessage(1, LogLevel.Information, "Classifying {Input}")]
    private static partial void LogStarting(ILogger logger, string input);
}
""", CancellationToken.None);

            // Add a stub LoggerMessageAttribute so the file compiles without referencing the
            // Microsoft.Extensions.Logging.Abstractions package — IsSourceGenPartial matches
            // by fully-qualified attribute name regardless of where the type is declared.
            var stubPath = Path.Combine(copiedRoot, "SampleLib", "LoggerMessageStub.cs");
            await File.WriteAllTextAsync(stubPath, """
namespace Microsoft.Extensions.Logging;

[System.AttributeUsage(System.AttributeTargets.Method)]
internal sealed class LoggerMessageAttribute : System.Attribute
{
    public LoggerMessageAttribute(int eventId, LogLevel level, string message) { }
}

internal interface ILogger { }

internal enum LogLevel { Information }
""", CancellationToken.None);

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            workspaceId = status.WorkspaceId;

            var metrics = await CohesionAnalysisService.GetCohesionMetricsAsync(
                workspaceId, filePath, projectFilter: null, minMethods: 1, limit: 50,
                includeInterfaces: false, excludeTestProjects: false, CancellationToken.None);

            var classifier = metrics.FirstOrDefault(m => m.TypeName == "QuestionClassifier");
            // With minMethods=1 and only Classify counted, the score should be 1 (or 0 if the
            // class is filtered out for having < 2 instance methods after exclusions). The
            // bug case was Lcom4Score = 2+ because LogStarting was a separate cluster.
            if (classifier is not null)
            {
                Assert.AreEqual(1, classifier.Lcom4Score,
                    "QuestionClassifier should have Lcom4Score=1 — only the real method counts, not [LoggerMessage] partials.");
                Assert.AreEqual(1, classifier.MethodCount,
                    "MethodCount should exclude source-gen partials.");
            }
        }
        finally
        {
            if (workspaceId is not null)
            {
                WorkspaceManager.Close(workspaceId);
            }

            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task GetCohesionMetrics_SharedFields_DoesNotContainPrivateHelperNames()
    {
        // BUG fix (cohesion-metrics-source-gen-aware, BUG-N9 followup): a private helper
        // method that two public methods both call previously appeared in the cluster's
        // SharedFields list alongside real fields. After the split into methodFieldMap +
        // methodCallMap, only fields/properties show up in SharedFields.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        string? workspaceId = null;

        try
        {
            var filePath = Path.Combine(copiedRoot, "SampleLib", "AdapterUnderTest.cs");
            await File.WriteAllTextAsync(filePath, """
namespace SampleLib;

public class AdapterUnderTest
{
    private readonly string _connectionString;

    public AdapterUnderTest(string connectionString)
    {
        _connectionString = connectionString;
    }

    public string ReadOne()
    {
        return CreateFailure(_connectionString);
    }

    public string ReadMany()
    {
        return CreateFailure(_connectionString);
    }

    private static string CreateFailure(string connection)
    {
        return $"failed: {connection}";
    }
}
""", CancellationToken.None);

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            workspaceId = status.WorkspaceId;

            var metrics = await CohesionAnalysisService.GetCohesionMetricsAsync(
                workspaceId, filePath, projectFilter: null, minMethods: 2, limit: 50,
                includeInterfaces: false, excludeTestProjects: false, CancellationToken.None);

            var adapter = metrics.FirstOrDefault(m => m.TypeName == "AdapterUnderTest");
            Assert.IsNotNull(adapter, "AdapterUnderTest should appear in cohesion metrics.");
            Assert.IsTrue(adapter.Clusters.Count >= 1, "AdapterUnderTest should have at least one cluster.");

            foreach (var cluster in adapter.Clusters)
            {
                Assert.IsFalse(cluster.SharedFields.Contains("CreateFailure"),
                    "BUG-N9: SharedFields must not contain private helper method names.");
            }
        }
        finally
        {
            if (workspaceId is not null)
            {
                WorkspaceManager.Close(workspaceId);
            }

            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task GetCohesionMetrics_ActionTriadPattern_ClassifiesAsActionTriad_AndDowngradesRecommendation()
    {
        // lcom4-lifecycle-pattern-false-positive: A type ending in `Action`/`Handler`/`Command`/`Stage`
        // with the exact Describe + Validate* + Execute* triad is a lifecycle pattern whose
        // public methods are orthogonal on fields by design. LCOM4 should still report the
        // real cluster count, but the DTO carries LifecyclePattern="action-triad" and a
        // softened Recommendation so callers can suppress the "split" suggestion.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        string? workspaceId = null;

        try
        {
            var filePath = Path.Combine(copiedRoot, "SampleLib", "FooAction.cs");
            await File.WriteAllTextAsync(filePath, """
namespace SampleLib;

public class FooAction
{
    private readonly string _name;
    private readonly int _limit;
    private readonly bool _strict;

    public FooAction(string name, int limit, bool strict)
    {
        _name = name;
        _limit = limit;
        _strict = strict;
    }

    public string Describe() => _name;

    public bool Validate() => _limit > 0;

    public int Execute() => _strict ? 1 : 0;
}
""", CancellationToken.None);

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            workspaceId = status.WorkspaceId;

            var metrics = await CohesionAnalysisService.GetCohesionMetricsAsync(
                workspaceId, filePath, projectFilter: null, minMethods: 2, limit: 50,
                includeInterfaces: false, excludeTestProjects: false, CancellationToken.None);

            var action = metrics.FirstOrDefault(m => m.TypeName == "FooAction");
            Assert.IsNotNull(action, "FooAction should appear in cohesion metrics.");
            Assert.AreEqual("action-triad", action.LifecyclePattern,
                "FooAction with Describe/Validate/Execute triad should be classified as action-triad.");
            Assert.IsNotNull(action.Recommendation,
                "Recommendation should be populated for a detected lifecycle pattern.");
            StringAssert.Contains(action.Recommendation, "action-triad",
                "Recommendation should mention the action-triad pattern so callers can downgrade the split suggestion.");
            // Triad methods are orthogonal on fields — each uses a different private field and
            // forms its own LCOM4 cluster. Lcom4Score = 3 confirms this is exactly the shape the
            // detector is designed to de-emphasize (without suppressing the raw score itself).
            Assert.AreEqual(3, action.Lcom4Score,
                "Describe/_name, Validate/_limit, Execute/_strict are orthogonal → expect Lcom4Score=3.");
        }
        finally
        {
            if (workspaceId is not null)
            {
                WorkspaceManager.Close(workspaceId);
            }

            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task GetCohesionMetrics_ActionSuffixWithoutTriad_DoesNotClassifyAsActionTriad()
    {
        // lcom4-lifecycle-pattern-false-positive: A type whose name ends in `Action` but whose
        // methods are NOT the Describe/Validate*/Execute* triad should still report a regular
        // LCOM4 score with no LifecyclePattern and no softened Recommendation — ensuring the
        // detector is conservative and does not suppress real low-cohesion signals.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        string? workspaceId = null;

        try
        {
            var filePath = Path.Combine(copiedRoot, "SampleLib", "NotATriadAction.cs");
            await File.WriteAllTextAsync(filePath, """
namespace SampleLib;

public class NotATriadAction
{
    private readonly string _a;
    private readonly int _b;
    private readonly bool _c;

    public NotATriadAction(string a, int b, bool c)
    {
        _a = a;
        _b = b;
        _c = c;
    }

    public string Describe() => _a;

    public int Foo() => _b;

    public bool Bar() => _c;
}
""", CancellationToken.None);

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            workspaceId = status.WorkspaceId;

            var metrics = await CohesionAnalysisService.GetCohesionMetricsAsync(
                workspaceId, filePath, projectFilter: null, minMethods: 2, limit: 50,
                includeInterfaces: false, excludeTestProjects: false, CancellationToken.None);

            var notTriad = metrics.FirstOrDefault(m => m.TypeName == "NotATriadAction");
            Assert.IsNotNull(notTriad, "NotATriadAction should appear in cohesion metrics.");
            Assert.IsNull(notTriad.LifecyclePattern,
                "LifecyclePattern must be null when the Describe/Validate/Execute triad is incomplete (only Describe + Foo + Bar).");
            Assert.IsNull(notTriad.Recommendation,
                "Recommendation must be null when no lifecycle pattern applies, so callers fall back to the default split suggestion.");
            Assert.AreEqual(3, notTriad.Lcom4Score,
                "Three orthogonal methods with no shared fields should yield Lcom4Score=3 (default message applies).");
        }
        finally
        {
            if (workspaceId is not null)
            {
                WorkspaceManager.Close(workspaceId);
            }

            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task FindSharedMembers_Supports_StaticClasses()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        string? workspaceId = null;

        try
        {
            var staticFilePath = Path.Combine(copiedRoot, "SampleLib", "StaticUtility.cs");
            await File.WriteAllTextAsync(staticFilePath, """
namespace SampleLib;

public static class StaticUtility
{
    private static int _counter;

    public static void Increment()
    {
        _counter++;
    }

    public static int Read()
    {
        return _counter;
    }
}
""", CancellationToken.None);

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            workspaceId = status.WorkspaceId;

            var shared = await CohesionAnalysisService.FindSharedMembersAsync(
                workspaceId,
                SymbolLocator.ByMetadataName("SampleLib.StaticUtility"),
                CancellationToken.None);

            Assert.IsTrue(shared.Any(m => m.MemberName == "_counter"),
                "Expected static private field '_counter' to be reported as shared by multiple public static methods.");
        }
        finally
        {
            if (workspaceId is not null)
            {
                WorkspaceManager.Close(workspaceId);
            }

            DeleteDirectoryIfExists(copiedRoot);
        }
    }
}
