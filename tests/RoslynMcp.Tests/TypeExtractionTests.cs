namespace RoslynMcp.Tests;

[TestClass]
public sealed class TypeExtractionTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task ExtractType_FromAnimalService_RefusesWhenExternalConsumersExist()
    {
        // Regression for `dr-9-1-does-not-update-external-consumer-call-sites` (P3): the
        // SampleSolution AnimalService.CountAnimals is referenced by Program.cs and the
        // AnimalServiceTests project. Pre-fix, extracting it produced a preview token but
        // applying it broke the external callers. Post-fix the preview refuses with an
        // actionable error so the breakage surfaces before any disk mutation.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("AnimalService.cs") == true);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            TypeExtractionService.PreviewExtractTypeAsync(
                wsId, doc.FilePath!, "AnimalService", ["CountAnimals"], "AnimalCounter", null, CancellationToken.None));
        StringAssert.Contains(ex.Message, "external consumer");
        StringAssert.Contains(ex.Message, "Program.cs",
            "error message must name the affected external file so callers know which code to update");
    }

    [TestMethod]
    public async Task ExtractType_NoExternalConsumers_ProducesPreview()
    {
        // When the extracted member is only referenced from inside the source file (the
        // class itself), the new external-consumer guard does not fire and the preview
        // succeeds as before. Use a fresh fixture to control external reference state.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "ExtractTypeNoConsumersFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class ExtractTypeNoConsumersFixture",
                "{",
                "    public int InternalUser() => Compute(42);",
                "    private int Compute(int x) => x * 2;",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            var result = await TypeExtractionService.PreviewExtractTypeAsync(
                wsId, fixturePath, "ExtractTypeNoConsumersFixture", ["Compute"], "ComputeHelper", null,
                CancellationToken.None);

            Assert.IsNotNull(result);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.PreviewToken));
        }
        finally
        {
            WorkspaceManager.Close(wsId);
        }
    }

    [TestMethod]
    public async Task ExtractType_EmptyMemberList_ThrowsArgument()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("AnimalService.cs") == true);

        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            TypeExtractionService.PreviewExtractTypeAsync(
                wsId, doc.FilePath!, "AnimalService", [], "NewType", null, CancellationToken.None));
    }

    [TestMethod]
    public async Task ExtractType_NonExistentType_ThrowsInvalidOperation()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("AnimalService.cs") == true);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            TypeExtractionService.PreviewExtractTypeAsync(
                wsId, doc.FilePath!, "NonExistentType", ["Foo"], "NewType", null, CancellationToken.None));
    }

    [TestMethod]
    public async Task ExtractType_OverrideMember_StripsOverrideFromNewType()
    {
        // Regression for `dr-9-3-preserves-when-new-type-does-not-inherit-the-bas` (P4): when a
        // member carries `override` / `virtual` / `abstract` / `sealed` / `new`, the extracted
        // type (which is emitted as a plain `public sealed class` with no base list) must strip
        // those modifiers. Source audit: IT-Chat-Bot experimental promotion §9.3 — extracting
        // `Down()` from a class inheriting `Migration` produced `public override void Down(...)`
        // inside the new class and yielded CS0115 on the first compile_check.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "OverrideMemberFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public abstract class BaseMigration",
                "{",
                "    public abstract void Down();",
                "    public virtual void Up() { }",
                "}",
                "",
                "public class OverrideMemberFixture : BaseMigration",
                "{",
                "    public override void Down() { }",
                "    public sealed override void Up() { }",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            var result = await TypeExtractionService.PreviewExtractTypeAsync(
                wsId, fixturePath, "OverrideMemberFixture", ["Down", "Up"], "RollbackHelper", null,
                CancellationToken.None);

            Assert.IsNotNull(result);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.PreviewToken));

            // Locate the diff for the new file (RollbackHelper.cs) and assert no override/virtual
            // modifiers leaked into the extracted members.
            var newFileDiff = result.Changes.FirstOrDefault(
                c => c.FilePath.EndsWith("RollbackHelper.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(newFileDiff, "preview must emit a change entry for the new extracted type file");
            var diffText = newFileDiff!.UnifiedDiff;

            // The added lines (prefixed with '+') must not carry override/virtual/abstract — the
            // new type has no base to override. `new` and member-level `sealed` are also stripped.
            var addedLines = diffText
                .Split('\n')
                .Where(line => line.StartsWith('\u002B') && !line.StartsWith("\u002B\u002B\u002B"))
                .ToArray();

            foreach (var forbidden in new[] { "override", "virtual", "abstract" })
            {
                Assert.IsFalse(
                    addedLines.Any(line => System.Text.RegularExpressions.Regex.IsMatch(line, $@"\b{forbidden}\b")),
                    $"extracted members must not carry '{forbidden}' (the new type has no base). Diff:\n{diffText}");
            }

            // Sanity: the method declarations themselves still appear.
            Assert.IsTrue(addedLines.Any(l => l.Contains("void Down")),
                $"Down method should still be present in extracted type. Diff:\n{diffText}");
            Assert.IsTrue(addedLines.Any(l => l.Contains("void Up")),
                $"Up method should still be present in extracted type. Diff:\n{diffText}");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
        }
    }
}
