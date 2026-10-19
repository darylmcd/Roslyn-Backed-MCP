using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CohesionSemanticIdentityTests : IsolatedWorkspaceTestBase
{
    private static IsolatedWorkspaceScope? _scope;
    private static IsolatedWorkspaceScope Scope => _scope
        ?? throw new InvalidOperationException("The cohesion identity workspace is not initialized.");

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        _scope = await InitializeWithCleanupAsync(
            CreateIsolatedWorkspaceCopy(),
            static async (scope, ct) =>
            {
                await File.WriteAllTextAsync(scope.GetPath("SampleLib", "PartialCohesion.First.cs"), """
namespace SampleLib;

public partial class PartialCohesion
{
    private int _first = 1;
    private int _second = 2;
    private int _third = 3;

    public int First() => _first;
}
""", ct);
                await File.WriteAllTextAsync(scope.GetPath("SampleLib", "PartialCohesion.Second.cs"), """
namespace SampleLib;

public partial class PartialCohesion
{
    public int Second() => _second;
    public int Third() => _third;
}
""", ct);
                await File.WriteAllTextAsync(scope.GetPath("SampleLib", "OverloadedCohesion.cs"), """
namespace SampleLib;

public class OverloadedCohesion
{
    private int _first = 1;
    private int _second = 2;

    public int Read() => _first;
    public int Read(int offset) => _second + offset;
    public int ReadFirst() => _first;
}

public class OverloadedHelperCohesion
{
    public int First() => Helper(1);
    public int Second() => Helper("two");
    private int Helper(int value) => value;
    private int Helper(string value) => value.Length;
}

public class GenericHelperCohesion
{
    public int First() => Helper<int>(1);
    public string Second() => Helper("two");
    private T Helper<T>(T value) => value;
}

public partial class HandwrittenPartialCohesion
{
    private int _value = 1;
    public partial int Read();
    private partial int Helper();
    public int Other() => Helper();
    public int Direct() => _value;
}

public partial class HandwrittenPartialCohesion
{
    public partial int Read() => _value;
    private partial int Helper() => _value;
}

public class DirectCallCohesion
{
    public int First() => Second();
    public int Second() => Helper();
    private int Helper() => 1;
    public int Unrelated() => 2;
}

public class HighestRankedCohesion
{
    public int First() => 1;
    public int Second() => 2;
    public int Third() => 3;
    public int Fourth() => 4;
    public int Fifth() => 5;
}

public class NameofCohesion
{
    private string _value = "value";
    public string First() => nameof(Helper) + nameof(_value);
    public string Second() => nameof(Helper) + nameof(_value);
    private string Helper() => _value;
}

public class MethodGroupCohesion
{
    public string First() => ((System.Func<string>)Helper)();
    public string Second() => Helper();
    private string Helper() => "value";
}

public class UserNameofCohesion
{
    public string First() => @nameof(1);
    public string Second() => @nameof(2);
    private string @nameof(int value) => value.ToString();
}
""", ct);
                await scope.LoadAsync(ct);
            }, CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        try
        {
            _scope?.Dispose();
        }
        finally
        {
            _scope = null;
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("PartialCohesion.First.cs")]
    [DataRow("PartialCohesion.Second.cs")]
    public async Task PartialType_UsesEachMemberTree_AndEmitsOneLogicalMetric(string? fileName)
    {
        var scan = await CohesionAnalysisService.GetCohesionMetricsDetailedAsync(
            Scope.WorkspaceId, fileName is null ? null : Scope.GetPath("SampleLib", fileName),
            projectFilter: "SampleLib", minMethods: 2, limit: 100,
            includeInterfaces: false, excludeTestProjects: true, CancellationToken.None);

        Assert.IsTrue(scan.IsComplete, "All declarations must use the semantic model owning their syntax tree.");
        Assert.AreEqual(0, scan.FailedTypeCount);
        var metrics = scan.Metrics.Where(metric => metric.TypeName == "PartialCohesion").ToList();
        Assert.HasCount(1, metrics, "A partial type must appear once even in a project-wide scan.");
        var metric = metrics[0];
        Assert.AreEqual(3, metric.MethodCount);
        Assert.AreEqual(3, metric.FieldCount);
        Assert.AreEqual(3, metric.Lcom4Score);
        CollectionAssert.AreEquivalent(new[] { "First", "Second", "Third" },
            metric.Clusters.SelectMany(cluster => cluster.Methods).ToArray());
        CollectionAssert.AreEquivalent(new[] { "_first", "_second", "_third" },
            metric.Clusters.SelectMany(cluster => cluster.SharedFields).ToArray());
    }

    [TestMethod]
    public async Task PartialType_ProducesOneRefactoringSuggestion()
    {
        var suggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            Scope.WorkspaceId, projectFilter: "SampleLib", limit: 100, CancellationToken.None);

        var matches = suggestions.Where(suggestion =>
            suggestion.Category == "cohesion" && suggestion.TargetSymbol == "PartialCohesion").ToList();
        Assert.HasCount(1, matches, "A multi-file partial type must neither abort nor duplicate suggestions.");
        Assert.AreEqual("high", matches[0].Severity);
        StringAssert.Contains(matches[0].Description, "3 independent clusters, 3 methods");
    }

    [TestMethod]
    public async Task Overloads_PreserveEveryMethodAndItsFieldConnections()
    {
        var metric = await GetMetricAsync("OverloadedCohesion");

        Assert.AreEqual(3, metric.MethodCount);
        Assert.AreEqual(2, metric.Lcom4Score);
        Assert.AreEqual(3, metric.Clusters.Sum(cluster => cluster.Methods.Count));
        var firstCluster = metric.Clusters.Single(cluster => cluster.SharedFields.Contains("_first"));
        CollectionAssert.AreEquivalent(new[] { "Read", "ReadFirst" }, firstCluster.Methods.ToArray());
        var secondCluster = metric.Clusters.Single(cluster => cluster.SharedFields.Contains("_second"));
        CollectionAssert.AreEqual(new[] { "Read" }, secondCluster.Methods.ToArray());
    }

    [TestMethod]
    public async Task HelperOverloads_DoNotCreateFalseSharedCallConnections()
    {
        var metric = await GetMetricAsync("OverloadedHelperCohesion");

        Assert.AreEqual(4, metric.MethodCount);
        Assert.AreEqual(4, metric.Clusters.Sum(cluster => cluster.Methods.Count));
        Assert.IsFalse(metric.Clusters.Any(cluster =>
            cluster.Methods.Contains("First") && cluster.Methods.Contains("Second")),
            "Calling different overloads must not merge the callers solely by helper name.");
    }

    [TestMethod]
    public async Task GenericHelperConstructedCalls_ShareTheirOriginalMethodIdentity()
    {
        var metric = await GetMetricAsync("GenericHelperCohesion");

        Assert.IsTrue(metric.Clusters.Any(cluster =>
            cluster.Methods.Contains("First") && cluster.Methods.Contains("Second")),
            "Constructed calls to the same generic helper must retain their shared-call connection.");
    }

    [TestMethod]
    public async Task HandwrittenPartialMethods_AnalyzeImplementationOnce_AndConnectCalls()
    {
        var metric = await GetMetricAsync("HandwrittenPartialCohesion");

        Assert.AreEqual(4, metric.MethodCount);
        Assert.AreEqual(1, metric.Lcom4Score);
        CollectionAssert.AreEquivalent(new[] { "Read", "Helper", "Other", "Direct" },
            metric.Clusters.Single().Methods.ToArray());
        CollectionAssert.AreEqual(new[] { "_value" }, metric.Clusters.Single().SharedFields.ToArray());
        var shared = await CohesionAnalysisService.FindSharedMembersAsync(
            Scope.WorkspaceId, SymbolLocator.ByMetadataName("SampleLib.HandwrittenPartialCohesion"), CancellationToken.None);
        var value = shared.Single(member => member.MemberName == "_value");
        CollectionAssert.AreEquivalent(new[] { "Read", "Direct" }, value.CallingMethods.ToArray());
    }

    [TestMethod]
    public async Task DirectCalls_ConnectPublicAndPrivateCallees_ButNotUnrelatedMethods()
    {
        var metric = await GetMetricAsync("DirectCallCohesion");

        Assert.AreEqual(2, metric.Lcom4Score);
        var callers = metric.Clusters.Single(cluster => cluster.Methods.Contains("First"));
        CollectionAssert.AreEquivalent(new[] { "First", "Second", "Helper" }, callers.Methods.ToArray());
        Assert.HasCount(0, callers.SharedFields);
    }

    [TestMethod]
    public async Task SharedGenericHelper_ExplicitAndInferredCallsMatchWithoutMergingOverloads()
    {
        var shared = await CohesionAnalysisService.FindSharedMembersAsync(
            Scope.WorkspaceId, SymbolLocator.ByMetadataName("SampleLib.GenericHelperCohesion"), CancellationToken.None);

        var helper = shared.Single(member => member.MemberName == "Helper");
        CollectionAssert.AreEquivalent(new[] { "First", "Second" }, helper.CallingMethods.ToArray());
        var overloads = await CohesionAnalysisService.FindSharedMembersAsync(
            Scope.WorkspaceId, SymbolLocator.ByMetadataName("SampleLib.OverloadedHelperCohesion"), CancellationToken.None);
        Assert.HasCount(0, overloads);
    }

    [TestMethod]
    public async Task NameofOperands_DoNotCreateCohesionOrSharedMemberDependencies()
    {
        var metric = await GetMetricAsync("NameofCohesion");
        Assert.AreEqual(3, metric.Lcom4Score);
        var shared = await CohesionAnalysisService.FindSharedMembersAsync(
            Scope.WorkspaceId, SymbolLocator.ByMetadataName("SampleLib.NameofCohesion"), CancellationToken.None);
        Assert.HasCount(0, shared);
    }

    [TestMethod]
    [DataRow("MethodGroupCohesion", "Helper")]
    [DataRow("UserNameofCohesion", "nameof")]
    public async Task MethodGroups_KeepRealCohesionAndSharedMemberDependencies(string typeName, string helperName)
    {
        var metric = await GetMetricAsync(typeName);
        Assert.AreEqual(1, metric.Lcom4Score);
        var shared = await CohesionAnalysisService.FindSharedMembersAsync(
            Scope.WorkspaceId, SymbolLocator.ByMetadataName($"SampleLib.{typeName}"), CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "First", "Second" },
            shared.Single(member => member.MemberName == helperName).CallingMethods.ToArray());
    }

    [TestMethod]
    public async Task ResultLimit_IsAppliedAfterSeverityRanking()
    {
        var metrics = await CohesionAnalysisService.GetCohesionMetricsAsync(
            Scope.WorkspaceId, Scope.GetPath("SampleLib", "OverloadedCohesion.cs"),
            projectFilter: null, minMethods: 2, limit: 1,
            includeInterfaces: false, excludeTestProjects: true, CancellationToken.None);

        Assert.HasCount(1, metrics);
        Assert.AreEqual("HighestRankedCohesion", metrics[0].TypeName);
    }

    private static async Task<CohesionMetricsDto> GetMetricAsync(string typeName)
    {
        var metrics = await CohesionAnalysisService.GetCohesionMetricsAsync(
            Scope.WorkspaceId, Scope.GetPath("SampleLib", "OverloadedCohesion.cs"),
            projectFilter: null, minMethods: 2, limit: 100,
            includeInterfaces: false, excludeTestProjects: true, CancellationToken.None);
        return metrics.Single(metric => metric.TypeName == typeName);
    }
}
