namespace RoslynMcp.Tests;

/// <summary>
/// replace-invocation-pattern-refactor: the new tool rewrites every call-site of an old
/// method to invoke a new method whose parameter list is a permutation of the old. The
/// reorder is driven by parameter-name equality so the caller only needs to declare the FQ
/// signatures and the rewriter derives the index mapping. Audit F#2 (2026-04-15) — the
/// concrete scenario that motivated this tool — had callers of
/// <c>Build(a, b, c)</c> migrate to <c>Generate(b, c, a)</c> with argument reorder; the
/// fixture below replays that shape and asserts every call-form is rewritten correctly.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class ReplaceInvocationTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// Three call-forms exist in real code and must all be rewritten:
    ///   1. Positional:        Build(a, b, c)
    ///   2. Positional literals: Build(1, "x", true)
    ///   3. Named out-of-order: Build(arg3: true, arg1: 1, arg2: "x")
    ///
    /// After rewrite, every site should invoke Generate with arguments ordered (b, c, a)
    /// per the declared new signature. Named callers preserve their names (the C# compiler
    /// locates named args by name regardless of lexical order, so the rewritten source stays
    /// compilable), but the lexical order is shuffled into the new parameter order.
    /// </summary>
    [TestMethod]
    public async Task ReplaceInvocation_ReordersArgumentsAcrossCallForms()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        var fixturePath = Path.Combine(sampleLibDir, "ReplaceInvocationFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            """
            namespace SampleLib;

            public static class ReplaceInvocationHelper
            {
                public static string Build(int arg1, string arg2, bool arg3) => $"{arg1}-{arg2}-{arg3}";
                public static string Generate(string arg2, bool arg3, int arg1) => $"{arg1}-{arg2}-{arg3}";
            }

            public static class ReplaceInvocationCallers
            {
                public static string Positional(int a, string b, bool c)
                {
                    return ReplaceInvocationHelper.Build(a, b, c);
                }

                public static string PositionalLiterals()
                {
                    return ReplaceInvocationHelper.Build(1, "x", true);
                }

                public static string NamedOutOfOrder()
                {
                    return ReplaceInvocationHelper.Build(arg3: true, arg1: 1, arg2: "x");
                }
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var scopedWorkspaceId = loadResult.WorkspaceId;

        try
        {
            var preview = await BulkRefactoringService.PreviewReplaceInvocationAsync(
                scopedWorkspaceId,
                oldMethod: "SampleLib.ReplaceInvocationHelper.Build(int, string, bool)",
                newMethod: "SampleLib.ReplaceInvocationHelper.Generate(string, bool, int)",
                scope: null,
                CancellationToken.None);

            Assert.IsNotNull(preview, "Preview must be returned for a valid reorder.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken), "Preview token must be populated.");

            var fixtureChange = preview.Changes
                .FirstOrDefault(c => c.FilePath.EndsWith("ReplaceInvocationFixture.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(fixtureChange, "The fixture file must appear in the preview changes.");

            var addedLines = fixtureChange.UnifiedDiff.Split('\n')
                .Where(line => line.StartsWith('\u002B') && !line.StartsWith("\u002B\u002B\u002B"))
                .ToList();
            var addedText = string.Join('\n', addedLines);

            // Assertion 1 — positional call ordered (a, b, c) → (b, c, a).
            StringAssert.Contains(
                addedText,
                "ReplaceInvocationHelper.Generate(b, c, a)",
                "Positional caller must be rewritten with arguments permuted per the new parameter order.");

            // Assertion 2 — positional-literals call (1, \"x\", true) → (\"x\", true, 1).
            StringAssert.Contains(
                addedText,
                "ReplaceInvocationHelper.Generate(\"x\", true, 1)",
                "Positional-literals caller must be reordered to match the new parameter order.");

            // Assertion 3 — named-out-of-order call must be rewritten so the lexical order
            // matches the new signature. Names are preserved; only their positions change.
            // Expected emission (new order: arg2, arg3, arg1):
            //   Generate(arg2: "x", arg3: true, arg1: 1)
            StringAssert.Contains(
                addedText,
                "ReplaceInvocationHelper.Generate(arg2: \"x\", arg3: true, arg1: 1)",
                "Named-argument caller must be rewritten with named args preserved and lexical order matching the new parameter order.");

            // Negative assertion — no call-site should still reference the old method name.
            Assert.IsFalse(
                addedText.Contains("ReplaceInvocationHelper.Build(", StringComparison.Ordinal),
                "All call-sites must be rewritten to Generate; no Build( invocations should remain in the post-rewrite lines.");

            // CallsiteUpdates: exactly one file, three rewritten invocations.
            Assert.IsNotNull(preview.CallsiteUpdates, "CallsiteUpdates should be populated for a non-zero rewrite.");
            Assert.AreEqual(1, preview.CallsiteUpdates.Count, "Only the fixture file should be touched.");
            Assert.AreEqual(3, preview.CallsiteUpdates[0].CallsiteCount, "Three call-sites were rewritten.");
        }
        finally
        {
            WorkspaceManager.Close(scopedWorkspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    /// <summary>
    /// Missing parens → clear ArgumentException. Guard: the FQ parser must reject input that
    /// has no '(' so callers cannot accidentally pass just a method name and get a confusing
    /// downstream "overload match failed" message.
    /// </summary>
    [TestMethod]
    public async Task ReplaceInvocation_MissingParens_ThrowsArgument()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);

        try
        {
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                BulkRefactoringService.PreviewReplaceInvocationAsync(
                    loadResult.WorkspaceId,
                    oldMethod: "SampleLib.ReplaceInvocationHelper.Build",
                    newMethod: "SampleLib.ReplaceInvocationHelper.Generate(string, bool, int)",
                    scope: null,
                    CancellationToken.None));
        }
        finally
        {
            WorkspaceManager.Close(loadResult.WorkspaceId);
            TryDeleteDirectory(Path.GetDirectoryName(copiedSolutionPath)!);
        }
    }

    /// <summary>
    /// When the new method declares a parameter name that does not exist on the old method,
    /// the reorder mapping is ambiguous — the service must refuse with InvalidOperationException
    /// rather than silently emit a positional mapping that happens to match by type alone.
    /// </summary>
    [TestMethod]
    public async Task ReplaceInvocation_NewParamName_NotInOldMethod_Throws()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        var fixturePath = Path.Combine(sampleLibDir, "ReplaceInvocationParamNameFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            """
            namespace SampleLib;

            public static class ReplaceInvocationMismatch
            {
                public static int OldFn(int aa, int bb) => aa - bb;
                public static int NewFn(int cc, int dd) => cc - dd;
            }

            public static class ReplaceInvocationMismatchCallers
            {
                public static int Go() => ReplaceInvocationMismatch.OldFn(1, 2);
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);

        try
        {
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                BulkRefactoringService.PreviewReplaceInvocationAsync(
                    loadResult.WorkspaceId,
                    oldMethod: "SampleLib.ReplaceInvocationMismatch.OldFn(int, int)",
                    newMethod: "SampleLib.ReplaceInvocationMismatch.NewFn(int, int)",
                    scope: null,
                    CancellationToken.None));
        }
        finally
        {
            WorkspaceManager.Close(loadResult.WorkspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    /// <summary>
    /// scope must be null or "all" — any other value fails fast with ArgumentException so a
    /// stale caller who passes "parameters" (a valid value for bulk_replace_type_preview)
    /// gets a clear error instead of silently expanded scope.
    /// </summary>
    [TestMethod]
    public async Task ReplaceInvocation_InvalidScope_ThrowsArgument()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);

        try
        {
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                BulkRefactoringService.PreviewReplaceInvocationAsync(
                    loadResult.WorkspaceId,
                    oldMethod: "SampleLib.ReplaceInvocationHelper.Build(int, string, bool)",
                    newMethod: "SampleLib.ReplaceInvocationHelper.Generate(string, bool, int)",
                    scope: "parameters",
                    CancellationToken.None));
        }
        finally
        {
            WorkspaceManager.Close(loadResult.WorkspaceId);
            TryDeleteDirectory(Path.GetDirectoryName(copiedSolutionPath)!);
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
