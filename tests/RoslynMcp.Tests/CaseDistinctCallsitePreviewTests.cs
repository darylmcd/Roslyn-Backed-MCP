using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Callsite counts must be keyed by document identity, not by an unconditionally
/// case-insensitive file path. The solution is built in memory so two documents whose paths
/// differ only by case exist on every OS; the old path-keyed grouping collapsed them into one
/// <see cref="CallsiteUpdateDto"/> wherever the comparer was case-insensitive.
/// </summary>
[TestClass]
public sealed class CaseDistinctCallsitePreviewTests
{
    private const string WorkspaceId = "case-distinct-callsites";

    [TestMethod]
    public async Task ReplaceInvocation_CaseDistinctCallerDocuments_ProduceSeparateCallsiteUpdates()
    {
        var (solution, upperPath, lowerPath) = BuildSolution(
            "public static class Target { public static int Old(int value) => value; public static int New(int value) => value; }");
        var service = new BulkRefactoringService(CreateWorkspace(solution), new PreviewStore(), new PassThroughCompilationCache());

        var preview = await service.PreviewReplaceInvocationAsync(
            WorkspaceId, "Target.Old(int)", "Target.New(int)", null, CancellationToken.None);

        AssertSeparateEntries(preview, upperPath, lowerPath);
    }

    [TestMethod]
    public async Task ParameterObject_CaseDistinctCallerDocuments_ProduceSeparateCallsiteUpdates()
    {
        var (solution, upperPath, lowerPath) = BuildSolution(
            "public static class Target { public static int Sum(int a, int b) => a + b; }",
            callExpression: "Target.Sum(1, 2)");
        var service = new ParameterObjectService(CreateWorkspace(solution), new PreviewStore());

        var preview = await service.PreviewParameterObjectAsync(
            WorkspaceId,
            SymbolLocator.ByMetadataName("Target.Sum"),
            new ParameterObjectPreviewRequest(["a", "b"], "SumArgs"),
            CancellationToken.None);

        AssertSeparateEntries(preview, upperPath, lowerPath);
    }

    private static void AssertSeparateEntries(RefactoringPreviewDto preview, string upperPath, string lowerPath)
    {
        var updates = preview.CallsiteUpdates;
        Assert.IsNotNull(updates);
        if (OperatingSystem.IsWindows())
        {
            // One physical file on Windows: the two documents aggregate into a single entry.
            Assert.HasCount(1, updates);
            Assert.AreEqual(2, updates[0].CallsiteCount);
            return;
        }

        Assert.HasCount(2, updates);
        CollectionAssert.AreEquivalent(
            new[] { upperPath, lowerPath },
            updates.Select(update => update.FilePath).ToArray());
        Assert.IsTrue(updates.All(update => update.CallsiteCount == 1));
    }

    [TestMethod]
    public async Task ReplaceInvocation_LinkedDocument_AggregatesOneEntryPerPhysicalFile()
    {
        var (solution, callerPath) = BuildLinkedSolution(
            "public static class Target { public static int Old(int value) => value; public static int New(int value) => value; }",
            "Target.Old(1)");
        var service = new BulkRefactoringService(CreateWorkspace(solution), new PreviewStore(), new PassThroughCompilationCache());

        var preview = await service.PreviewReplaceInvocationAsync(
            WorkspaceId, "Target.Old(int)", "Target.New(int)", null, CancellationToken.None);

        AssertSingleLinkedEntry(preview, callerPath);
    }

    [TestMethod]
    public async Task ParameterObject_LinkedDocument_AggregatesOneEntryPerPhysicalFile()
    {
        var (solution, callerPath) = BuildLinkedSolution(
            "public static class Target { public static int Sum(int a, int b) => a + b; }",
            "Target.Sum(1, 2)");
        var service = new ParameterObjectService(CreateWorkspace(solution), new PreviewStore());

        var preview = await service.PreviewParameterObjectAsync(
            WorkspaceId,
            SymbolLocator.ByMetadataName("Target.Sum"),
            new ParameterObjectPreviewRequest(["a", "b"], "SumArgs"),
            CancellationToken.None);

        AssertSingleLinkedEntry(preview, callerPath);
    }

    private static void AssertSingleLinkedEntry(RefactoringPreviewDto preview, string callerPath)
    {
        var updates = preview.CallsiteUpdates;
        Assert.IsNotNull(updates);
        Assert.HasCount(1, updates, "A file linked into two projects is one physical file.");
        Assert.AreEqual(callerPath, updates[0].FilePath);
        Assert.AreEqual(2, updates[0].CallsiteCount, "Both project documents' callsites are summed.");
    }

    private static (Solution Solution, string CallerPath) BuildLinkedSolution(string targetSource, string callExpression)
    {
        var root = Path.Combine(Path.GetTempPath(), "linked-" + Guid.NewGuid().ToString("N"));
        var callerPath = Path.Combine(root, "Caller.cs");
        var callerSource = SourceText.From(
            $"public static class Caller {{ public static int Run() => {callExpression}; }}");
        var reference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var first = ProjectId.CreateNewId();
        var second = ProjectId.CreateNewId();
        var solution = new AdhocWorkspace().CurrentSolution
            .AddProject(ProjectInfo.Create(first, VersionStamp.Create(), "First", "First", LanguageNames.CSharp,
                filePath: Path.Combine(root, "First.csproj"), metadataReferences: [reference]))
            .AddProject(ProjectInfo.Create(second, VersionStamp.Create(), "Second", "Second", LanguageNames.CSharp,
                filePath: Path.Combine(root, "Second.csproj"), metadataReferences: [reference],
                projectReferences: [new ProjectReference(first)]))
            .AddDocument(DocumentId.CreateNewId(first), "Target.cs",
                SourceText.From(targetSource), filePath: Path.Combine(root, "Target.cs"))
            .AddDocument(DocumentId.CreateNewId(first), "Caller.cs", callerSource, filePath: callerPath)
            .AddDocument(DocumentId.CreateNewId(second), "Caller.cs", callerSource, filePath: callerPath);
        return (solution, callerPath);
    }

    private static (Solution Solution, string UpperPath, string LowerPath) BuildSolution(
        string targetSource,
        string callExpression = "Target.Old(1)")
    {
        var root = Path.Combine(Path.GetTempPath(), "case-distinct-" + Guid.NewGuid().ToString("N"));
        var upperPath = Path.Combine(root, "Caller.cs");
        var lowerPath = Path.Combine(root, "caller.cs");
        var projectId = ProjectId.CreateNewId();
        var solution = new AdhocWorkspace().CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId, VersionStamp.Create(), "CaseProject", "CaseProject", LanguageNames.CSharp,
                filePath: Path.Combine(root, "CaseProject.csproj"),
                metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]))
            .AddDocument(DocumentId.CreateNewId(projectId), "Target.cs",
                SourceText.From(targetSource), filePath: Path.Combine(root, "Target.cs"))
            .AddDocument(DocumentId.CreateNewId(projectId), "Caller.cs",
                SourceText.From($"public static class UpperCaller {{ public static int Run() => {callExpression}; }}"),
                filePath: upperPath)
            .AddDocument(DocumentId.CreateNewId(projectId), "caller.cs",
                SourceText.From($"public static class LowerCaller {{ public static int Run() => {callExpression}; }}"),
                filePath: lowerPath);
        return (solution, upperPath, lowerPath);
    }

    private static FailClosedWorkspaceManagerStub CreateWorkspace(Solution solution) => new()
    {
        GetCurrentSolutionHandler = _ => solution,
        GetCurrentVersionHandler = _ => 1,
    };

    private sealed class PassThroughCompilationCache : ICompilationCache
    {
        public Task<Compilation?> GetCompilationAsync(string workspaceId, Project project, CancellationToken ct) =>
            project.GetCompilationAsync(ct);

        public Task<CompilationWithAnalyzers?> GetCompilationWithAnalyzersAsync(
            string workspaceId, Project project, CancellationToken ct) => throw new NotSupportedException();

        public bool IsLiveSolution(string workspaceId, Solution solution) => throw new NotSupportedException();

        public void Invalidate(string workspaceId) => throw new NotSupportedException();
    }
}
