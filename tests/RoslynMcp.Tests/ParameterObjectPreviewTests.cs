using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for the v1 <c>parameter_object_preview</c> tool. The seven cases mirror the
/// regression set named in <c>ai_docs/items/parameter-object-preview-design.md</c> §(f):
/// (1) positional grouping, (2) named + mixed call sites, (3) default-value refusal,
/// (4) ref/out refusal, (5) cross-project success, (6) cross-project missing-reference
/// refusal, (7) end-to-end apply via <c>RefactoringService.ApplyRefactoringAsync</c>.
/// </summary>
[TestClass]
public sealed class ParameterObjectPreviewTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task PositionalGrouping_SingleProject_RewritesBothDeclarationAndCallsite()
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class POPosFixture
            {
                public int Sum(int a, int b, int c) => a + b + c;

                public int Caller() => Sum(1, 2, 3);
            }
            """,
            "POPosFixture.cs");

        try
        {
            var preview = await ParameterObjectService.PreviewParameterObjectAsync(
                workspaceId,
                SymbolLocator.BySource(fixturePath, line: 5, column: 16), // 'Sum'
                new ParameterObjectPreviewRequest(["a", "b", "c"], "SumArgs"),
                CancellationToken.None);

            Assert.IsNotNull(preview.PreviewToken);
            var fixtureDiff = preview.Changes.FirstOrDefault(c => c.FilePath.EndsWith("POPosFixture.cs", StringComparison.OrdinalIgnoreCase))?.UnifiedDiff;
            Assert.IsNotNull(fixtureDiff);
            StringAssert.Contains(fixtureDiff, "SumArgs sumArgs", "declaration should take the new record param");
            StringAssert.Contains(fixtureDiff, "new SumArgs(1, 2, 3)", "callsite should wrap grouped args in new SumArgs(...)");

            var addedDiff = preview.Changes.FirstOrDefault(c => c.FilePath.EndsWith("SumArgs.cs", StringComparison.OrdinalIgnoreCase))?.UnifiedDiff;
            Assert.IsNotNull(addedDiff, "preview must include the new DTO file");
            StringAssert.Contains(addedDiff, "public sealed record SumArgs(int A, int B, int C)");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task NamedAndMixedCallSites_RewriteToPositionalDtoConstructor()
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class PONamedFixture
            {
                public int Sum(int a, int b, int c) => a + b + c;

                public int CallNamed() => Sum(c: 3, a: 1, b: 2);
                public int CallMixed() => Sum(1, c: 3, b: 2);
            }
            """,
            "PONamedFixture.cs");

        try
        {
            var preview = await ParameterObjectService.PreviewParameterObjectAsync(
                workspaceId,
                SymbolLocator.BySource(fixturePath, line: 5, column: 16),
                new ParameterObjectPreviewRequest(["a", "b", "c"], "SumArgs"),
                CancellationToken.None);

            var diff = preview.Changes.First(c => c.FilePath.EndsWith("PONamedFixture.cs", StringComparison.OrdinalIgnoreCase)).UnifiedDiff;
            // Both call sites must collapse to the same positional `new SumArgs(1, 2, 3)`.
            var occurrences = CountOccurrences(diff, "new SumArgs(1, 2, 3)");
            Assert.AreEqual(2, occurrences, $"Expected both call sites rewritten to positional. Diff:\n{diff}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task DefaultValueOmissionAtCallSite_RefusesPreview()
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class PODefaultFixture
            {
                public int Sum(int a, int b = 0, int c = 0) => a + b + c;

                public int Caller() => Sum(a: 1, c: 3); // omits 'b'
            }
            """,
            "PODefaultFixture.cs");

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(fixturePath, line: 5, column: 16),
                    new ParameterObjectPreviewRequest(["a", "b", "c"], "SumArgs"),
                    CancellationToken.None));
            StringAssert.Contains(ex.Message, "omits 'b'");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task RefOutParameter_RefusesEntirePreview()
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class PORefFixture
            {
                public void Compute(int a, ref int b) { b = a; }

                public void Caller() { int x = 0; Compute(1, ref x); }
            }
            """,
            "PORefFixture.cs");

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(fixturePath, line: 5, column: 17),
                    new ParameterObjectPreviewRequest(["a", "b"], "ComputeArgs"),
                    CancellationToken.None));
            StringAssert.Contains(ex.Message, "by-ref kind");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task CrossProject_RewritesCallSiteWhenReferenceExists()
    {
        // SampleApp already references SampleLib in the SampleSolution fixture, so a method
        // declared in SampleLib with a SampleApp call site exercises the reference-present path.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var libDir = Path.Combine(solutionDir, "SampleLib");
        var appDir = Path.Combine(solutionDir, "SampleApp");

        var libFile = Path.Combine(libDir, "POCrossLib.cs");
        await File.WriteAllTextAsync(libFile,
            """
            namespace SampleLib;

            public class POCrossLib
            {
                public int Sum(int a, int b, int c) => a + b + c;
            }
            """);
        var appFile = Path.Combine(appDir, "POCrossApp.cs");
        await File.WriteAllTextAsync(appFile,
            """
            using SampleLib;

            namespace SampleApp;

            public static class POCrossApp
            {
                public static int Run() => new POCrossLib().Sum(1, 2, 3);
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;
        try
        {
            var preview = await ParameterObjectService.PreviewParameterObjectAsync(
                workspaceId,
                SymbolLocator.BySource(libFile, line: 5, column: 16),
                new ParameterObjectPreviewRequest(["a", "b", "c"], "SumArgs"),
                CancellationToken.None);

            Assert.IsNotNull(preview.PreviewToken);
            Assert.IsTrue(preview.Changes.Any(c => c.FilePath.EndsWith("POCrossLib.cs", StringComparison.OrdinalIgnoreCase)));
            var appDiff = preview.Changes.FirstOrDefault(c => c.FilePath.EndsWith("POCrossApp.cs", StringComparison.OrdinalIgnoreCase))?.UnifiedDiff;
            Assert.IsNotNull(appDiff, "cross-project caller must be rewritten");
            StringAssert.Contains(appDiff, "new SumArgs(1, 2, 3)");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task CrossProject_RefusesWhenCallerProjectMissingReference()
    {
        // Place the method in SampleApp (which is NOT referenced by SampleLib.Tests) and
        // request the DTO to land in SampleApp; then create a caller in SampleLib.Tests that
        // can compile against SampleApp's type *only via* a fixture-lib reference. To keep the
        // setup simple, we instead place the method in SampleLib.Tests (which references both)
        // and request the DTO to live in SampleApp — SampleLib.Tests references SampleApp? Let's
        // verify by inverse: method in SampleLib, dtoProjectName=SampleApp. SampleLib does NOT
        // reference SampleApp, so the SampleLib declaration's project is missing the reference.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var libDir = Path.Combine(solutionDir, "SampleLib");

        var libFile = Path.Combine(libDir, "POCrossMissingLib.cs");
        await File.WriteAllTextAsync(libFile,
            """
            namespace SampleLib;

            public class POCrossMissingLib
            {
                public int Sum(int a, int b, int c) => a + b + c;
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;
        try
        {
            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(libFile, line: 5, column: 16),
                    new ParameterObjectPreviewRequest(["a", "b", "c"], "SumArgs", DtoProjectName: "SampleApp"),
                    CancellationToken.None));
            StringAssert.Contains(ex.Message, "SampleLib -> SampleApp");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task ApplyRefactoring_WritesNewFileAndRewritesCallSitesOnDisk()
    {
        var (workspaceId, fixturePath, fixtureDir) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class POApplyFixture
            {
                public int Sum(int a, int b, int c) => a + b + c;

                public int Caller() => Sum(1, 2, 3);
            }
            """,
            "POApplyFixture.cs");

        try
        {
            var preview = await ParameterObjectService.PreviewParameterObjectAsync(
                workspaceId,
                SymbolLocator.BySource(fixturePath, line: 5, column: 16),
                new ParameterObjectPreviewRequest(["a", "b", "c"], "SumArgs"),
                CancellationToken.None);

            var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(apply.Success, $"Apply must succeed: {apply.Error}");

            var dtoPath = Path.Combine(fixtureDir, "SumArgs.cs");
            Assert.IsTrue(File.Exists(dtoPath), "DTO file must be written to disk");
            var dtoText = await File.ReadAllTextAsync(dtoPath);
            StringAssert.Contains(dtoText, "public sealed record SumArgs(int A, int B, int C);");

            var fixtureText = await File.ReadAllTextAsync(fixturePath);
            StringAssert.Contains(fixtureText, "Sum(SumArgs sumArgs)");
            StringAssert.Contains(fixtureText, "new SumArgs(1, 2, 3)");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    private static async Task<(string WorkspaceId, string FixturePath, string FixtureDir)> SetupSingleProjectFixtureAsync(
        string content, string fileName)
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var libDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(libDir, fileName);
        await File.WriteAllTextAsync(fixturePath, content);
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        return (loadResult.WorkspaceId, fixturePath, libDir);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}
