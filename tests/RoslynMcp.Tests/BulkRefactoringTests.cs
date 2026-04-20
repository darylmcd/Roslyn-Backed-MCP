using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class BulkRefactoringTests : SharedWorkspaceTestBase
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
    public async Task BulkReplaceType_Preview_ReturnsChanges()
    {
        var result = await BulkRefactoringService.PreviewBulkReplaceTypeAsync(
            WorkspaceId, "IAnimal", "Shape", null, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.PreviewToken));
    }

    [TestMethod]
    public async Task BulkReplaceType_NonExistentType_ThrowsInvalidOperation()
    {
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            BulkRefactoringService.PreviewBulkReplaceTypeAsync(
                WorkspaceId, "NonExistentType123", "Shape", null, CancellationToken.None));
    }

    [TestMethod]
    public async Task BulkReplaceType_InvalidScope_ThrowsArgument()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            BulkRefactoringService.PreviewBulkReplaceTypeAsync(
                WorkspaceId, "IAnimal", "Shape", "invalid_scope", CancellationToken.None));
    }

    // ── dr-9-6: scope=parameters walks generic arguments in implemented interfaces ──

    /// <summary>
    /// Regression guard for <c>dr-9-6-ignores-generic-arguments-in-implemented-interfa</c>
    /// (audit §9.6). When a class implements a generic interface parameterised by the old
    /// type AND has a method parameter of the old type, a <c>scope=parameters</c> replace
    /// must rewrite both sites so the interface contract stays in sync — otherwise the
    /// rewritten method signature violates the exact-match implementation rule and the
    /// workspace fails to compile.
    /// </summary>
    [TestMethod]
    public async Task BulkReplaceType_Parameters_RewritesGenericArg_In_ImplementedInterface()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        // Shape of the audit §9.6 repro: IValidateOptions<T>.Validate(T options).
        // The class declares the interface parameterised by DrNineSixOldType and implements
        // Validate with a DrNineSixOldType parameter. scope=parameters must touch BOTH the
        // parameter declaration and the interface-base-list generic argument.
        var fixturePath = Path.Combine(sampleLibDir, "DrNineSixFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            """
            namespace SampleLib;

            public sealed class DrNineSixOldType
            {
                public string? Value { get; set; }
            }

            public sealed class DrNineSixNewType
            {
                public string? Value { get; set; }
            }

            public interface IDrNineSixValidator<T>
            {
                bool Validate(T options);
            }

            public sealed class DrNineSixValidator : IDrNineSixValidator<DrNineSixOldType>
            {
                public bool Validate(DrNineSixOldType options) => options is not null;
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var scopedWorkspaceId = loadResult.WorkspaceId;

        try
        {
            var preview = await BulkRefactoringService.PreviewBulkReplaceTypeAsync(
                scopedWorkspaceId,
                oldTypeName: "DrNineSixOldType",
                newTypeName: "DrNineSixNewType",
                scope: "parameters",
                CancellationToken.None);

            Assert.IsNotNull(preview, "Preview should be returned.");
            Assert.IsTrue(preview.Changes.Count >= 1, "At least one file should be touched.");

            var fixtureChange = preview.Changes
                .FirstOrDefault(c => c.FilePath.EndsWith("DrNineSixFixture.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(fixtureChange, "The fixture file must appear in the preview changes.");

            var diff = fixtureChange.UnifiedDiff;

            // A unified diff carries both removals (`-` lines) and additions (`+` lines).
            // The assertions below inspect the additions — i.e. the post-rewrite state —
            // so the old-type appearing in removal lines does not cause a false negative.
            var addedLines = diff.Split('\n')
                .Where(line => line.StartsWith('\u002B') && !line.StartsWith("\u002B\u002B\u002B"))
                .ToList();
            var addedText = string.Join('\n', addedLines);

            // Assertion 1 — the method parameter is rewritten (unchanged behaviour).
            StringAssert.Contains(
                addedText,
                "Validate(DrNineSixNewType options)",
                "scope=parameters must rewrite the method parameter type (pre-existing behaviour).");

            // Assertion 2 — the generic argument in the implemented interface is ALSO rewritten.
            // This is the dr-9-6 fix. Before the fix, scope=parameters skipped this site because
            // its syntactic parent walked up to SimpleBaseTypeSyntax, not ParameterSyntax.
            StringAssert.Contains(
                addedText,
                "IDrNineSixValidator<DrNineSixNewType>",
                "scope=parameters must ALSO rewrite the generic argument in the implemented-interface " +
                "base-list so the class's interface contract stays in sync with the new parameter type. " +
                "Audit §9.6 — before this fix the base-list kept the old type and the method signature " +
                "broke the interface's exact-match rule.");

            // Negative assertion — the old type must be gone from both sites in the post-rewrite lines.
            Assert.IsFalse(
                addedText.Contains("Validate(DrNineSixOldType options)", StringComparison.Ordinal),
                "Parameter type must no longer reference the old type after rewrite.");
            Assert.IsFalse(
                addedText.Contains("IDrNineSixValidator<DrNineSixOldType>", StringComparison.Ordinal),
                "Interface generic argument must no longer reference the old type after rewrite.");
        }
        finally
        {
            WorkspaceManager.Close(scopedWorkspaceId);
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
