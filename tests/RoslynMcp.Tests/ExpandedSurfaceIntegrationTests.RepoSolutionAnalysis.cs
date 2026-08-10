namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
[TestCategory("RepoSolution")]
public sealed class ExpandedSurfaceIntegrationTests_RepoSolutionAnalysis : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Reflection_And_Di_Analysis_Run_On_Repo_Solution()
    {
        var repositorySolutionPath = Path.Combine(RepositoryRootPath, "RoslynMcp.slnx");
        var status = await WorkspaceManager.LoadAsync(repositorySolutionPath, CancellationToken.None);

        try
        {
            var reflectionUsages = await CodePatternAnalyzer.FindReflectionUsagesAsync(
                status.WorkspaceId,
                "RoslynMcp.Roslyn",
                CancellationToken.None);
            var diRegistrations = await DiRegistrationService.GetDiRegistrationsAsync(
                status.WorkspaceId,
                "RoslynMcp.Roslyn",
                CancellationToken.None);

            Assert.IsTrue(reflectionUsages.Count > 0, "Expected reflection analysis to complete with results on the repo solution.");
            Assert.IsTrue(diRegistrations.Any(registration =>
                registration.FilePath.EndsWith("ServiceCollectionExtensions.cs", StringComparison.OrdinalIgnoreCase)),
                "Expected DI registrations from ServiceCollectionExtensions.cs.");
        }
        finally
        {
            WorkspaceManager.Close(status.WorkspaceId);
        }
    }

    /// <summary>
    /// hoststdio-middleware-tools-namespace-cycle regression guard: the
    /// <c>RoslynMcp.Host.Stdio.Middleware</c> &lt;-&gt; <c>RoslynMcp.Host.Stdio.Tools</c> namespace
    /// cycle was broken by extracting the shared elicitation-choice contract into
    /// <c>RoslynMcp.Host.Stdio.Elicitation</c>. <c>Middleware -&gt; Tools</c> survives (the filter's
    /// <c>WorkspaceTools.ResolveOptionalWorkspaceId</c> call); <c>Tools -&gt; Middleware</c> must NOT
    /// come back. Re-adding <c>using RoslynMcp.Host.Stdio.Middleware;</c> to any file in the
    /// <c>Tools</c> namespace — or a <c>RoslynMcp.Host.Stdio.{Middleware,Tools}</c> import inside
    /// <c>ElicitationChoicePrompt</c> — reds this test.
    /// <see cref="RoslynMcp.Roslyn.Services.NamespaceDependencyService"/> builds its edges strictly
    /// from <c>UsingDirectiveSyntax</c>, so this asserts the layering, not merely the reference graph.
    /// </summary>
    [TestMethod]
    public async Task No_Circular_Namespace_Dependency_Between_Middleware_And_Tools()
    {
        const string MiddlewareNamespace = "RoslynMcp.Host.Stdio.Middleware";
        const string ToolsNamespace = "RoslynMcp.Host.Stdio.Tools";

        var repositorySolutionPath = Path.Combine(RepositoryRootPath, "RoslynMcp.slnx");
        var status = await WorkspaceManager.LoadAsync(repositorySolutionPath, CancellationToken.None);

        try
        {
            var graph = await NamespaceDependencyService.GetNamespaceDependenciesAsync(
                status.WorkspaceId,
                projectFilter: "RoslynMcp.Host.Stdio",
                CancellationToken.None);

            Assert.IsTrue(
                graph.AnalyzedProjectCount > 0,
                "Test precondition: namespace-dependency analysis must have walked at least one project — " +
                "an empty CircularDependencies list from a zero-project walk proves nothing.");

            // Precondition: both namespaces must actually be present in the walked graph, otherwise
            // a typo'd/renamed namespace would make the cycle assertion vacuously pass.
            var namespacesInGraph = graph.Nodes.Select(node => node.Namespace).ToHashSet(StringComparer.Ordinal);
            Assert.IsTrue(
                namespacesInGraph.Contains(MiddlewareNamespace),
                $"Test precondition: expected '{MiddlewareNamespace}' in the analyzed namespace graph.");
            Assert.IsTrue(
                namespacesInGraph.Contains(ToolsNamespace),
                $"Test precondition: expected '{ToolsNamespace}' in the analyzed namespace graph.");

            // Precondition: the surviving Middleware -> Tools edge is intentional and must stay
            // (StructuredCallToolFilter's WorkspaceTools.ResolveOptionalWorkspaceId call). Only ONE
            // direction had to be broken.
            Assert.IsTrue(
                graph.Edges.Any(edge =>
                    edge.FromNamespace == MiddlewareNamespace && edge.ToNamespace == ToolsNamespace),
                $"Test precondition: expected the surviving '{MiddlewareNamespace}' -> '{ToolsNamespace}' edge.");

            // The actual guard: no Tools -> Middleware edge, hence no cycle between the two.
            Assert.IsFalse(
                graph.Edges.Any(edge =>
                    edge.FromNamespace == ToolsNamespace && edge.ToNamespace == MiddlewareNamespace),
                $"'{ToolsNamespace}' must not import '{MiddlewareNamespace}' — the shared elicitation-choice " +
                "contract lives in RoslynMcp.Host.Stdio.Elicitation precisely so this edge stays absent.");

            var offendingCycles = graph.CircularDependencies
                .Where(cycle =>
                    cycle.Cycle.Contains(MiddlewareNamespace, StringComparer.Ordinal)
                    && cycle.Cycle.Contains(ToolsNamespace, StringComparer.Ordinal))
                .Select(cycle => string.Join(" -> ", cycle.Cycle))
                .ToList();

            Assert.AreEqual(
                0,
                offendingCycles.Count,
                $"Expected no namespace cycle spanning '{MiddlewareNamespace}' and '{ToolsNamespace}'. Found: "
                + string.Join(" | ", offendingCycles));
        }
        finally
        {
            WorkspaceManager.Close(status.WorkspaceId);
        }
    }
}
