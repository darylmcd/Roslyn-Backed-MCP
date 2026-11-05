using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression (relocation-source-delete-preserves-other-members, same mechanism): the dead-code
/// "effectively empty file" decision ignored assembly attributes and <c>#if</c>-disabled
/// declarations, so <c>removeEmptyFiles</c> deleted a file that still held them. It now shares
/// <c>NamespaceMemberClassifier.HasRemainingContent</c> with the namespace relocation.
/// </summary>
[TestClass]
public sealed class DeadCodeRemainingContentTests
{
    private const string WorkspaceId = "dead-code-remaining-content-ws";

    private readonly List<UnusedCodeAnalyzerTestHarness> _harnesses = [];

    [TestCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            await CleanupFailureCollector.RunAsync(
                "Failed to dispose analyzer fixtures.",
                _harnesses.Select(harness => CleanupFailureCollector.FromAction(harness.Dispose)).ToArray());
        }
        finally
        {
            _harnesses.Clear();
        }
    }

    [TestMethod]
    [DataRow("namespace Sample\n{\n    internal sealed class Dead { }\n}\n", true,
        DisplayName = "sole type: file removed")]
    [DataRow("namespace Sample;\n\ninternal sealed class Dead { }\n", true,
        DisplayName = "sole type, file-scoped: file removed")]
    [DataRow("namespace Sample\n{\n    internal sealed class Dead\n    {\n#if NEVER\n        private int hidden;\n#endif\n    }\n}\n", true,
        DisplayName = "disabled region inside the removed type: file removed")]
    [DataRow("namespace Sample\n{\n    internal sealed class Dead { }\n\n#if NEVER\n    internal sealed class Hidden { }\n#endif\n}\n", false,
        DisplayName = "disabled declaration after the removed type: file kept")]
    [DataRow("namespace Sample\n{\n#if NEVER\n    internal sealed class Hidden { }\n#endif\n    internal sealed class Dead { }\n}\n", false,
        DisplayName = "disabled declaration before the removed type: file kept")]
    [DataRow("[assembly: System.Reflection.AssemblyMetadata(\"keep\", \"1\")]\n\nnamespace Sample\n{\n    internal sealed class Dead { }\n}\n", false,
        DisplayName = "assembly attribute: file kept")]
    public async Task PreviewRemoveDeadCode_RemoveEmptyFiles_Deletes_The_File_Only_When_Nothing_Remains(
        string source, bool expectedFileRemoved)
    {
        var (service, workspaceManager, previewStore) = BuildService(source);
        var handle = await HandleForAsync(workspaceManager.GetCurrentSolution(WorkspaceId), "Sample.Dead");

        var preview = await service.PreviewRemoveDeadCodeAsync(
            WorkspaceId, new DeadCodeRemovalDto([handle], RemoveEmptyFiles: true), default);

        var modified = previewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        var document = modified.Projects.SelectMany(p => p.Documents).SingleOrDefault(d => d.Name == "Sample.cs");
        if (expectedFileRemoved)
        {
            Assert.IsNull(document, "nothing but trivia remains, so the file must be removed");
        }
        else
        {
            Assert.IsNotNull(document, "the file still holds content and must survive removeEmptyFiles");
            var text = (await document.GetTextAsync()).ToString();
            Assert.IsFalse(text.Contains("Dead", StringComparison.Ordinal), $"The dead type must be removed:\n{text}");
            Assert.IsTrue(
                text.Contains("Hidden", StringComparison.Ordinal) || text.Contains("AssemblyMetadata", StringComparison.Ordinal),
                $"The retained content is gone:\n{text}");
        }
    }

    [TestMethod]
    public async Task PreviewRemoveDeadCode_KeepEmptyFiles_Does_Not_Warn_When_Disabled_Declarations_Remain()
    {
        const string source =
            "namespace Sample\n{\n    internal sealed class Dead { }\n\n#if NEVER\n    internal sealed class Hidden { }\n#endif\n}\n";
        var (service, workspaceManager, _) = BuildService(source);
        var handle = await HandleForAsync(workspaceManager.GetCurrentSolution(WorkspaceId), "Sample.Dead");

        var preview = await service.PreviewRemoveDeadCodeAsync(
            WorkspaceId, new DeadCodeRemovalDto([handle]), default);

        Assert.IsFalse(
            preview.Warnings?.Any(w => w.Contains("empty", StringComparison.Ordinal)) ?? false,
            $"The file is not empty. Warnings:\n{string.Join("\n", preview.Warnings ?? [])}");
    }

    private (DeadCodeService Service, FailClosedWorkspaceManagerStub WorkspaceManager, PreviewStore PreviewStore) BuildService(string source)
    {
        var workspace = UnusedCodeAnalyzerTestHarness.CreateWorkspace(source,
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        var harness = new UnusedCodeAnalyzerTestHarness(WorkspaceId, workspace);
        _harnesses.Add(harness);
        var previewStore = new PreviewStore();
        var service = new DeadCodeService(
            harness.WorkspaceManager,
            previewStore,
            static (_, _, _) => Task.FromResult<IEnumerable<ReferencedSymbol>>([]));
        return (service, harness.WorkspaceManager, previewStore);
    }

    private static async Task<string> HandleForAsync(Solution solution, string metadataName)
    {
        var compilation = await solution.Projects.Single().GetCompilationAsync();
        var symbol = compilation!.GetTypeByMetadataName(metadataName)
            ?? throw new InvalidOperationException($"'{metadataName}' was not found in the test solution.");
        return SymbolHandleSerializer.CreateHandle(symbol);
    }
}
