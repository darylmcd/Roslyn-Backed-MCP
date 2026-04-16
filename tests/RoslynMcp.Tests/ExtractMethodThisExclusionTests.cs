namespace RoslynMcp.Tests;

/// <summary>
/// Item #9 — regression guard for `extract-method-preview-fabricates-this-parameter`.
/// Jellyfin stress test §5 repro: extract_method_preview of a small region inside an
/// instance method produced a signature like `(MusicManager this, Audio item, …)` —
/// Roslyn's DataFlowsIn exposed the implicit `this` pointer as an `IParameterSymbol`
/// (specifically `IThisParameterSymbol`), and the filter `s is IParameterSymbol`
/// accepted it. The extracted method is always declared on the same containing type,
/// so `this` is implicitly available and must never appear in the parameter list.
/// </summary>
[TestClass]
public sealed class ExtractMethodThisExclusionTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task ExtractMethod_From_InstanceMethod_Reading_Field_Does_Not_Add_This_Parameter()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        // Set up a class with an instance method that reads an instance field via
        // an unqualified identifier — the specific shape that causes DataFlowsIn to
        // include the `this` pointer.
        var fixturePath = Path.Combine(sampleLibDir, "Item9InstanceMethodHost.cs");
        await File.WriteAllTextAsync(fixturePath,
            """
            namespace SampleLib;

            public class Item9InstanceMethodHost
            {
                private readonly int _multiplier = 3;

                public int Compute(int input)
                {
                    var doubled = input * 2;
                    var result = doubled * _multiplier;
                    return result;
                }
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            // Extract the two statements (`var doubled = ...` and `var result = ...`).
            // `_multiplier` is read via implicit `this`, which is what used to pull
            // the `this` pointer into the synthesized parameter list.
            var previewDto = await ExtractMethodService.PreviewExtractMethodAsync(
                workspaceId,
                fixturePath,
                startLine: 8,
                startColumn: 9,
                endLine: 9,
                endColumn: 46,
                methodName: "CalculateResult",
                ct: CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(previewDto.PreviewToken, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"Apply failed: {applyResult.Error}");

            var updated = await File.ReadAllTextAsync(fixturePath);

            // The signature must NOT contain `Item9InstanceMethodHost this` — that is
            // the fabricated-parameter shape. The extraction routine must recognize
            // the implicit `this` as already available to the extracted method.
            Assert.IsFalse(
                updated.Contains("Item9InstanceMethodHost this", StringComparison.Ordinal),
                $"Extracted method signature must not contain a fabricated `this` parameter. Got:\n{updated}");
            Assert.IsFalse(
                updated.Contains("(this", StringComparison.Ordinal),
                $"Extracted method must not receive `this` as an explicit parameter. Got:\n{updated}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    [TestMethod]
    public async Task ExtractMethod_From_Static_Method_Still_Works_Unchanged()
    {
        // Sanity — static methods never have a `this`, so the Item #9 filter is a no-op
        // and existing extractions must keep working.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        var fixturePath = Path.Combine(sampleLibDir, "Item9StaticHost.cs");
        await File.WriteAllTextAsync(fixturePath,
            """
            namespace SampleLib;

            public static class Item9StaticHost
            {
                public static int Compute(int input)
                {
                    var doubled = input * 2;
                    var tripled = doubled + doubled + doubled;
                    return tripled;
                }
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var previewDto = await ExtractMethodService.PreviewExtractMethodAsync(
                workspaceId,
                fixturePath,
                startLine: 6,
                startColumn: 9,
                endLine: 7,
                endColumn: 58,
                methodName: "ComputeTripledFromDoubled",
                ct: CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(previewDto.PreviewToken, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"Static extraction must still work. Got error: {applyResult.Error}");

            var updated = await File.ReadAllTextAsync(fixturePath);
            Assert.IsTrue(
                updated.Contains("ComputeTripledFromDoubled", StringComparison.Ordinal),
                "Extracted method must exist in the updated file.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
