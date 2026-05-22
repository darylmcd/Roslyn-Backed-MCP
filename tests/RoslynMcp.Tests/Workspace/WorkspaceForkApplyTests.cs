using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class WorkspaceForkApplyTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task WorkspaceForkApply_KeepSuccess_MutatesOnlyFork()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogPath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalSource = await File.ReadAllTextAsync(dogPath, CancellationToken.None);
        var token = await StoreDogPreviewAsync(workspace.WorkspaceId, source =>
            source.Replace("Woof", "ForkWoof", StringComparison.Ordinal));
        var validationService = CreateValidationService();

        var json = await ValidationBundleTools.WorkspaceForkApply(
            WorkspaceExecutionGate,
            WorkspaceManager,
            PreviewStore,
            validationService,
            TestRunnerService,
            workspace.WorkspaceId,
            token,
            retention: "keep",
            runTests: false,
            testFilter: null,
            forkName: "success",
            ct: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.IsTrue(root.GetProperty("success").GetBoolean());
        Assert.IsTrue(root.GetProperty("retained").GetBoolean());

        var forkPath = root.GetProperty("forkPath").GetString()
            ?? throw new AssertFailedException("forkPath should be returned.");
        var forkWorkspaceId = root.GetProperty("forkWorkspaceId").GetString()
            ?? throw new AssertFailedException("forkWorkspaceId should be returned for retained forks.");

        try
        {
            var forkDogPath = Path.Combine(forkPath, "SampleLib", "Dog.cs");
            StringAssert.Contains(await File.ReadAllTextAsync(forkDogPath, CancellationToken.None), "ForkWoof");
            Assert.AreEqual(originalSource, await File.ReadAllTextAsync(dogPath, CancellationToken.None),
                "workspace_fork_apply must not mutate the source workspace file.");
        }
        finally
        {
            WorkspaceManager.Close(forkWorkspaceId);
            DeleteDirectoryIfExists(forkPath);
        }
    }

    [TestMethod]
    public async Task WorkspaceForkApply_DefaultRetention_KeepsFailedForkLoadable()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var token = await StoreDogPreviewAsync(workspace.WorkspaceId, _ => "namespace SampleLib; public class Dog {");
        var validationService = CreateValidationService();

        var json = await ValidationBundleTools.WorkspaceForkApply(
            WorkspaceExecutionGate,
            WorkspaceManager,
            PreviewStore,
            validationService,
            TestRunnerService,
            workspace.WorkspaceId,
            token,
            retention: "drop-on-success",
            runTests: false,
            testFilter: null,
            forkName: "failure",
            ct: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.IsFalse(root.GetProperty("success").GetBoolean());
        Assert.IsTrue(root.GetProperty("retained").GetBoolean(),
            "drop-on-success should retain failed forks for inspection.");

        var forkPath = root.GetProperty("forkPath").GetString()
            ?? throw new AssertFailedException("forkPath should be returned.");
        var forkWorkspaceId = root.GetProperty("forkWorkspaceId").GetString()
            ?? throw new AssertFailedException("forkWorkspaceId should be returned for retained failed forks.");

        try
        {
            Assert.IsTrue(Directory.Exists(forkPath), "Failed retained fork should remain on disk.");
            Assert.IsTrue(WorkspaceManager.ContainsWorkspace(forkWorkspaceId),
                "Retained failed fork should remain loaded for follow-up inspection.");
        }
        finally
        {
            WorkspaceManager.Close(forkWorkspaceId);
            DeleteDirectoryIfExists(forkPath);
        }
    }

    private static WorkspaceValidationService CreateValidationService() =>
        new(
            CompileCheckService,
            DiagnosticService,
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker);

    private static async Task<string> StoreDogPreviewAsync(string workspaceId, Func<string, string> mutate)
    {
        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var dog = solution.Projects
            .SelectMany(project => project.Documents)
            .First(document => string.Equals(document.Name, "Dog.cs", StringComparison.Ordinal));

        var source = (await dog.GetTextAsync(CancellationToken.None).ConfigureAwait(false)).ToString();
        var modified = solution.WithDocumentText(dog.Id, SourceText.From(mutate(source)));

        return PreviewStore.Store(
            workspaceId,
            modified,
            WorkspaceManager.GetCurrentVersion(workspaceId),
            "workspace_fork_apply test preview");
    }
}
