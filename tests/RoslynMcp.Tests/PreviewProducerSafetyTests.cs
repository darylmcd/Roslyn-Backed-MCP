using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PreviewProducerSafetyTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();
    [TestMethod]
    public async Task SignatureProducer_PreservesBoundedDiffSafetyBeforeApply()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "SignatureSafety.cs");
        var calls = string.Join("\n", Enumerable.Range(0, 1500)
            .Select(index => $"public static int Call{index}() => Compute({index});"));
        await File.WriteAllTextAsync(path,
            "namespace SampleLib; public static class SignatureSafety { public static int Compute(int value) => value;\n" + calls + "\n}");
        await workspace.LoadAsync();
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var service = new ChangeSignatureService(WorkspaceManager, PreviewStore, RefactoringService);
        var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.ByMetadataName("SampleLib.SignatureSafety.Compute"),
            new ChangeSignatureRequest("add", Name: "extra", ParameterType: "int", DefaultValue: "0"), CancellationToken.None);
        await AssertSafetyAsync(workspace.WorkspaceId, original, preview, path, aggregate: false);
    }

    [TestMethod]
    public async Task SharedExpressionProducer_PreservesBoundedDiffSafetyBeforeApply()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "SharedSafety.cs");
        const string expression = "value * 3 + 1";
        var methods = string.Join("\n", Enumerable.Range(0, 700)
            .Select(index => $"public static int Compute{index}(int value) => {expression};"));
        var source = "namespace SampleLib; public static class SharedSafety {\n" + methods + "\n}";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync();
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var text = SourceText.From(source);
        var start = text.Lines.GetLinePosition(source.IndexOf(expression, StringComparison.Ordinal));
        var end = text.Lines.GetLinePosition(source.IndexOf(expression, StringComparison.Ordinal) + expression.Length);
        var preview = await ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(workspace.WorkspaceId,
            path, start.Line + 1, start.Character + 1, end.Line + 1, end.Character + 1,
            "SharedCalculation", "private", false, CancellationToken.None);
        await AssertSafetyAsync(workspace.WorkspaceId, original, preview, path, aggregate: false);
    }

    [TestMethod]
    public async Task BatchScaffoldProducer_PreservesAggregateDiffSafetyBeforeApply()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "BatchSafety.cs");
        // Each generated test contains more than 256 characters of scaffold boilerplate.
        // Size the batch against the production aggregate bound, without changing that bound.
        var targetCount = SolutionDiffHelper.DefaultMaxTotalChars / 256 + 1;
        var classes = string.Join("\n", Enumerable.Range(0, targetCount)
            .Select(index => $"public sealed class BatchSafety{index} {{ public int Compute() => {index}; }}"));
        await File.WriteAllTextAsync(path, "namespace SampleLib;\n" + classes);
        await workspace.LoadAsync();
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var preview = await ScaffoldingService.PreviewScaffoldTestBatchAsync(workspace.WorkspaceId,
            new ScaffoldTestBatchDto("SampleLib.Tests", Enumerable.Range(0, targetCount)
                .Select(index => new ScaffoldTestBatchTargetDto("BatchSafety" + index)).ToArray()), CancellationToken.None);
        await AssertSafetyAsync(workspace.WorkspaceId, original, preview, path, aggregate: true);
    }

    private static async Task AssertSafetyAsync(string workspaceId, Microsoft.CodeAnalysis.Solution original,
        RefactoringPreviewDto preview, string sourcePath, bool aggregate)
    {
        Assert.IsTrue(aggregate
            ? preview.Changes.Any(change => change.FilePath == SolutionDiffHelper.TruncatedSentinelFilePath)
            : preview.Changes.Any(change => change.UnifiedDiff.Contains("@@ truncated @@", StringComparison.Ordinal)),
            "The actual producer fixture must exceed its real bounded-diff limit.");
        var stored = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        Assert.IsTrue(stored.Value.DiffTruncated, "The producer must persist the bounded diff's safety state.");
        Assert.AreSame(original, stored.Value.OriginalSolution);
        Assert.AreNotEqual(PreviewKind.Unspecified, PreviewStore.PeekKind(preview.PreviewToken));
        Assert.ThrowsExactly<InvalidOperationException>(() => ToolDispatch.RequireCompatibleProducer(
            PreviewStore, preview.PreviewToken, PreviewKind.FormatDocument, "format_document_apply"));
        var before = await File.ReadAllBytesAsync(sourcePath);
        var version = WorkspaceManager.GetCurrentVersion(workspaceId);
        var result = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "apply_with_verify", false, CancellationToken.None);
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error!, "truncated");
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspaceId));
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(sourcePath));
        Assert.IsNotNull(PreviewStore.Retrieve(preview.PreviewToken));
    }
}
