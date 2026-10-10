using Microsoft.CodeAnalysis;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

// donotparallelize-audit-wave-32: [DoNotParallelize] removed. Every workspace-backed test loads
// its own GUID-unique SampleSolution copy (CreateSampleSolutionCopy / CreateIsolatedWorkspaceAsync),
// writes fixtures only inside it, and WorkspaceManager LoadAsync/Close act only on that session.
// Preview and apply mutate only test-owned copies; reload, UndoService and ChangeTracker
// writes stay in that workspace (ApplyExtractMethod is refused on route before mutation).
// Tokens are redeemed from the bounded (20-entry), oldest-first-evicting PreviewStore immediately
// after the preview, so displacing one would need 20 newer previews inside that window. The roots
// tests build private McpRootsTestServerFactory pairs over GUID-named directories; no static or
// environment mutation.
// Validated by a bounded repeated (3x) concurrent run alongside its wave-32 siblings and
// parallel-enabled workspace-loading classes, green every time.
[TestClass]
public sealed class TypeExtractionTests : IsolatedWorkspaceTestBase
{
    private readonly List<string> _directoriesToDelete = [];

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestCleanup]
    public async Task TestCleanup() =>
        await CleanupFailureCollector.DeleteDirectoriesAsync(
            _directoriesToDelete,
            TestFixtureFileSystem.DeleteDirectoryIfExists);

    [TestMethod]
    public async Task LegacyProtocol_ToolDispatch_UsesConfiguredSanctionedRoot()
    {
        var sanctionedRoot = Path.Combine(Path.GetTempPath(), "roots-legacy-sanctioned-" + Guid.NewGuid().ToString("N"));
        var outsideRoot = Path.Combine(Path.GetTempPath(), "roots-legacy-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(outsideRoot);

        await using var session = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(
            sanctionedRoot,
            CancellationToken.None);

        try
        {
            var result = await session.Client.CallToolAsync(
                "roots_boundary_probe",
                new Dictionary<string, object?> { ["path"] = outsideRoot },
                cancellationToken: CancellationToken.None);

            Assert.AreNotEqual(true, result.IsError, "The probe should report the validator outcome as normal tool content.");
            Assert.HasCount(1, result.Content);
            var content = Assert.IsInstanceOfType<TextContentBlock>(result.Content[0]);
            StringAssert.StartsWith(content.Text, "rejected: ");
            StringAssert.Contains(content.Text, "outside the configured sanctioned-root boundary");
        }
        finally
        {
            QueueDirectoryForCleanup(sanctionedRoot);
            QueueDirectoryForCleanup(outsideRoot);
        }
    }

    [TestMethod]
    public async Task LegacyProtocol_ClientRoots_NarrowButNeverWidenConfiguredBoundary()
    {
        var configuredRoot = Path.Combine(Path.GetTempPath(), "roots-legacy-configured-" + Guid.NewGuid().ToString("N"));
        var clientRoot = Path.Combine(configuredRoot, "client-root");
        var configuredOnlyPath = Path.Combine(configuredRoot, "configured-only.cs");
        var allowedPath = Path.Combine(clientRoot, "allowed.cs");
        var clientOnlyRoot = Path.Combine(Path.GetTempPath(), "roots-legacy-client-only-" + Guid.NewGuid().ToString("N"));
        var clientOnlyPath = Path.Combine(clientOnlyRoot, "outside.cs");
        Directory.CreateDirectory(clientRoot);
        Directory.CreateDirectory(clientOnlyRoot);

        await using var session = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(
            configuredRoot,
            CancellationToken.None,
            clientRootPaths: [clientRoot, clientOnlyRoot]);

        try
        {
            var allowed = await session.Client.CallToolAsync(
                "roots_boundary_probe",
                new Dictionary<string, object?> { ["path"] = allowedPath },
                cancellationToken: CancellationToken.None);
            var configuredOnly = await session.Client.CallToolAsync(
                "roots_boundary_probe",
                new Dictionary<string, object?> { ["path"] = configuredOnlyPath },
                cancellationToken: CancellationToken.None);
            var clientOnly = await session.Client.CallToolAsync(
                "roots_boundary_probe",
                new Dictionary<string, object?> { ["path"] = clientOnlyPath },
                cancellationToken: CancellationToken.None);

            Assert.AreEqual("allowed", Assert.IsInstanceOfType<TextContentBlock>(allowed.Content[0]).Text);
            StringAssert.StartsWith(
                Assert.IsInstanceOfType<TextContentBlock>(configuredOnly.Content[0]).Text,
                "rejected: ");
            StringAssert.StartsWith(
                Assert.IsInstanceOfType<TextContentBlock>(clientOnly.Content[0]).Text,
                "rejected: ");
        }
        finally
        {
            QueueDirectoryForCleanup(configuredRoot);
            QueueDirectoryForCleanup(clientOnlyRoot);
        }
    }

    [TestMethod]
    public async Task ModernProtocol_ToolDispatch_UsesConfiguredSanctionedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "roots-modern-no-capability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        await using var session = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(
            root,
            CancellationToken.None,
            useLatestProtocol: true);

        try
        {
            var result = await session.Client.CallToolAsync(
                "roots_boundary_probe",
                new Dictionary<string, object?> { ["path"] = root },
                cancellationToken: CancellationToken.None);

            Assert.AreNotEqual(true, result.IsError);
            Assert.HasCount(1, result.Content);
            var content = Assert.IsInstanceOfType<TextContentBlock>(result.Content[0]);
            Assert.AreEqual("allowed", content.Text);
        }
        finally
        {
            QueueDirectoryForCleanup(root);
        }
    }

    // Direct tool calls use an explicitly configured, connected test server. A missing server no
    // longer bypasses the production boundary; the separate rejection test below verifies the
    // negative DI path with a non-covering configured root.
    [TestMethod]
    public async Task PreviewExtractType_Tool_ConfiguredServer_ProducesPreview()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "ExtractTypeToolFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class ExtractTypeToolFixture",
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
            var server = await GetPathAuthorizedServerAsync();
            var json = await TypeExtractionTools.PreviewExtractType(
                server,
                WorkspaceExecutionGate,
                TypeExtractionService,
                wsId,
                fixturePath,
                "ExtractTypeToolFixture",
                ["Compute"],
                "ComputeHelper",
                null,
                CancellationToken.None);

            Assert.IsFalse(string.IsNullOrWhiteSpace(json));
            StringAssert.Contains(json, "previewToken");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
        }
    }

    [TestMethod]
    public async Task PreviewExtractType_Tool_OutOfRootPath_RejectsWithArgumentException()
    {
        // Root-boundary regression: when a real MCP server configures a root that
        // does NOT cover the requested file, extract_type_preview must reject before
        // dispatching to the service. Uses a real client/server pair so the actual
        // ValidatePathAgainstRootsAsync root-matching path is exercised end-to-end.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "ExtractTypeOutOfRootFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class ExtractTypeOutOfRootFixture",
                "{",
                "    public int InternalUser() => Compute(42);",
                "    private int Compute(int x) => x * 2;",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        // Sanction a root that is a SIBLING of solutionDir, not an ancestor of fixturePath.
        var sanctionedRoot = Path.Combine(Path.GetTempPath(), "roots-boundary-sanctioned-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sanctionedRoot);

        await using var session = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(
            sanctionedRoot, CancellationToken.None);

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                TypeExtractionTools.PreviewExtractType(
                    session.Server,
                    WorkspaceExecutionGate,
                    TypeExtractionService,
                    wsId,
                    fixturePath,
                    "ExtractTypeOutOfRootFixture",
                    ["Compute"],
                    "ComputeHelper",
                    null,
                    CancellationToken.None));

            StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
            QueueDirectoryForCleanup(sanctionedRoot);
        }
    }

    [TestMethod]
    public async Task ExtractType_FromAnimalService_RefusesWhenExternalConsumersExist()
    {
        // Regression for `dr-9-1-does-not-update-external-consumer-call-sites` (P3): extracting
        // a member with references from another source file must refuse the preview before any
        // disk mutation. Keep the fixture local to this test so it does not drift as the shared
        // sample solution evolves.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var sourcePath = Path.Combine(sampleLibDir, "ExternalConsumerFixture.cs");
        var callerPath = Path.Combine(sampleLibDir, "ExternalConsumerCaller.cs");

        await File.WriteAllTextAsync(sourcePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class ExternalConsumerFixture",
                "{",
                "    public int Compute(int value) => value * 2;",
                "}",
                "",
            }));

        await File.WriteAllTextAsync(callerPath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class ExternalConsumerCaller",
                "{",
                "    public int Use(ExternalConsumerFixture fixture) => fixture.Compute(21);",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    wsId, sourcePath, "ExternalConsumerFixture", ["Compute"], "ComputeHelper", null, CancellationToken.None));

            StringAssert.Contains(ex.Message, "external consumer");
            StringAssert.Contains(ex.Message, "ExternalConsumerCaller.cs",
                "error message must name the affected external file so callers know which code to update");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
        }
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
            Assert.AreEqual(
                PreviewKind.ExtractType,
                PreviewStore.PeekKind(result.PreviewToken),
                "extract_type_preview must record its producer family.");

            var wrongRoute = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                ExtractMethodTools.ApplyExtractMethod(
                    WorkspaceExecutionGate,
                    RefactoringService,
                    PreviewStore,
                    result.PreviewToken,
                    CancellationToken.None));
            StringAssert.Contains(wrongRoute.Message, "extract_type_apply");
            StringAssert.Contains(wrongRoute.Message, "extract_method_apply");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
        }
    }

    [TestMethod]
    public void SameFilePath_UsesPlatformIdentityAfterCanonicalization()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcp", "path-identity", "segment");
        var canonical = Path.Combine(root, "Consumer.cs");
        var equivalent = Path.Combine(root, ".", "Consumer.cs");
        var caseVariant = Path.Combine(root, "consumer.cs");

        Assert.IsTrue(global::RoslynMcp.Roslyn.Services.TypeExtractionService.IsSameFilePath(canonical, equivalent));
        Assert.AreEqual(
            OperatingSystem.IsWindows(),
            global::RoslynMcp.Roslyn.Services.TypeExtractionService.IsSameFilePath(canonical, caseVariant),
            "Windows path identity is case-insensitive; case-sensitive hosts must keep distinct files external.");
    }

    [TestMethod]
    public async Task ExtractType_EmptyMemberList_ThrowsArgument()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("AnimalService.cs") == true);

        await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            TypeExtractionService.PreviewExtractTypeAsync(
                wsId, doc.FilePath!, "AnimalService", [], "NewType", null, CancellationToken.None));
    }

    [TestMethod]
    public async Task ExtractType_DanglingReference_ThrowsWithStructuredBlockingDependencies()
    {
        // extract-type-preview-refusal-missing-blocking-deps: the compile-safety refusal already
        // computed per-(member, referenced-symbol) detail but flattened it into prose. It must now
        // also carry the structured list so a caller can widen `memberNames` programmatically.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "ExtractTypeDanglingFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class ExtractTypeDanglingFixture",
                "{",
                "    private int _seed = 7;",
                "    public int Compute(int x) => x * _seed;",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            // Extracting Compute leaves _seed behind on the source type, so the generated code
            // would not compile — the refusal fires before the external-consumer check.
            var ex = await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    wsId, fixturePath, "ExtractTypeDanglingFixture", ["Compute"], "ComputeHelper", null,
                    CancellationToken.None));

            StringAssert.Contains(ex.Message, "would not compile",
                "prose message must be unchanged so existing callers keep working");
            Assert.IsTrue(ex.BlockingDependencies.Count > 0,
                "refusal must carry at least one structured blocking dependency");
            Assert.IsTrue(ex.BlockingDependencies.Any(d =>
                    string.Equals(d.Member, "Compute", StringComparison.Ordinal)),
                "the blocking dependency must be attributed to the extracted member that references the leftover state");
            Assert.IsTrue(ex.BlockingDependencies.Any(d => d.Reason.Contains("_seed", StringComparison.Ordinal)),
                $"the reason must name the symbol that remains behind. Actual: {string.Join(" | ", ex.BlockingDependencies.Select(d => d.Reason))}");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
        }
    }

    [TestMethod]
    public async Task ExtractType_MemberNotFound_ThrowsWithStructuredBlockingDependencies()
    {
        // extract-type-preview-refusal-missing-blocking-deps: the member-not-found refusal held the
        // unmatched names in a HashSet but only emitted them as a comma-joined sentence.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("AnimalService.cs") == true);

        var ex = await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
            TypeExtractionService.PreviewExtractTypeAsync(
                wsId, doc.FilePath!, "AnimalService", ["NoSuchMemberOnAnimalService"], "NewType", null,
                CancellationToken.None));

        StringAssert.Contains(ex.Message, "NoSuchMemberOnAnimalService");
        Assert.AreEqual(1, ex.BlockingDependencies.Count,
            "exactly one member name was unmatched, so exactly one structured entry is expected");
        Assert.AreEqual("NoSuchMemberOnAnimalService", ex.BlockingDependencies[0].Member);
        StringAssert.Contains(ex.BlockingDependencies[0].Reason, "not found in type 'AnimalService'");
    }

    [TestMethod]
    public async Task ExtractType_NonExistentType_ThrowsInvalidOperation()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("AnimalService.cs") == true);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
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

    [TestMethod]
    public async Task ExtractType_NewMember_StripsNewModifierFromNewType()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "NewModifierFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class NewModifierBase",
                "{",
                "    public int Count => 1;",
                "}",
                "",
                "public class NewModifierFixture : NewModifierBase",
                "{",
                "    public new int Count => 2;",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            var result = await TypeExtractionService.PreviewExtractTypeAsync(
                wsId, fixturePath, "NewModifierFixture", ["Count"], "CountHelper", null, CancellationToken.None);

            Assert.IsNotNull(result);
            var newFileDiff = result.Changes.FirstOrDefault(
                c => c.FilePath.EndsWith("CountHelper.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(newFileDiff, "preview must emit a change entry for the new extracted type file");
            var diffText = newFileDiff!.UnifiedDiff;

            var addedLines = diffText
                .Split('\n')
                .Where(line => line.StartsWith('\u002B') && !line.StartsWith("\u002B\u002B\u002B"))
                .ToArray();

            Assert.IsFalse(
                addedLines.Any(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"\bnew\b")),
                $"extracted members must not carry 'new' in the new type. Diff:\n{diffText}");
            Assert.IsTrue(
                addedLines.Any(line => line.Contains("int Count", StringComparison.Ordinal)),
                $"property should still be present in the extracted type. Diff:\n{diffText}");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
        }
    }

    [TestMethod]
    public async Task ExtractType_PreservesBlankLineBetweenNamespaceAndClass()
    {
        // Regression for `dr-9-5-strips-the-blank-line-between-namespace-and-clas` (P4):
        // `extract_type_preview` generated the new type file without a blank line between
        // the namespace declaration and the class, producing the non-idiomatic layout
        //     namespace SampleLib;
        //     public sealed class NewType
        // instead of the standard C# convention
        //     namespace SampleLib;
        //
        //     public sealed class NewType
        // Root cause: `BuildNewFileRoot` called `NormalizeWhitespace()` which collapses the
        // blank line. Fixed by injecting a blank line on the type declaration's leading
        // trivia after normalization.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "BlankLineFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class BlankLineFixture",
                "{",
                "    public int InternalUser() => Helper(42);",
                "    private int Helper(int x) => x * 2;",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            var result = await TypeExtractionService.PreviewExtractTypeAsync(
                wsId, fixturePath, "BlankLineFixture", ["Helper"], "HelperService", null,
                CancellationToken.None);

            Assert.IsNotNull(result);
            var newFileDiff = result.Changes.FirstOrDefault(
                c => c.FilePath.EndsWith("HelperService.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(newFileDiff, "preview must emit a change entry for the new extracted type file");
            var diffText = newFileDiff!.UnifiedDiff;

            // Extract the added lines (new file contents).
            var addedLines = diffText
                .Split('\n')
                .Where(line => line.StartsWith('\u002B') && !line.StartsWith("\u002B\u002B\u002B"))
                .Select(line => line.TrimEnd('\r').Substring(1)) // strip the '+' prefix and any CR
                .ToArray();

            // Find the namespace declaration line and assert the following line is blank.
            var namespaceIndex = Array.FindIndex(addedLines, l => l.TrimStart().StartsWith("namespace "));
            Assert.IsTrue(namespaceIndex >= 0,
                $"extracted file must contain a namespace declaration. Diff:\n{diffText}");
            Assert.IsTrue(namespaceIndex + 1 < addedLines.Length,
                $"extracted file must have content after the namespace declaration. Diff:\n{diffText}");
            Assert.AreEqual(string.Empty, addedLines[namespaceIndex + 1],
                $"the line immediately after the namespace declaration must be blank (standard C# layout). " +
                $"Actual next line: '{addedLines[namespaceIndex + 1]}'. Full diff:\n{diffText}");

            // Sanity: the class declaration should follow the blank line.
            Assert.IsTrue(namespaceIndex + 2 < addedLines.Length &&
                addedLines[namespaceIndex + 2].TrimStart().StartsWith("public sealed class HelperService"),
                $"class declaration must follow the blank line. Diff:\n{diffText}");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
        }
    }

    [TestMethod]
    public async Task ExtractType_ConstructorRequested_RefusesWithStructuredBlockingDependency()
    {
        // Regression for `type-extraction-member-shape-validation` (1/3): a constructor's identifier
        // is ALWAYS the source type's name, so `GetMemberName` mapped it to that name and
        // `PartitionMembers` happily selected it. Neither `BuildNewFileRoot` nor
        // `EnsurePublicAccessibility` retargets a constructor identifier, so the declaration was
        // emitted verbatim inside `public sealed class {newTypeName}` — an ill-formed member named
        // after the OLD type (CS1520-class breakage). It must be refused with structured data now.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "ConstructorShapeFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class ConstructorShapeFixture",
                "{",
                "    public ConstructorShapeFixture() { }",
                "    private int Compute(int x) => x * 2;",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    wsId, fixturePath, "ConstructorShapeFixture", ["ConstructorShapeFixture"], "ComputeHelper",
                    null, CancellationToken.None));

            Assert.IsTrue(ex.BlockingDependencies.Count > 0,
                "the constructor refusal must carry structured blocking dependencies, not prose only");
            Assert.IsTrue(ex.BlockingDependencies.Any(d =>
                    string.Equals(d.Member, "ConstructorShapeFixture", StringComparison.Ordinal)),
                "the blocking dependency must be attributed to the requested constructor name");
            Assert.IsTrue(ex.BlockingDependencies.Any(d => d.Reason.Contains("constructor", StringComparison.Ordinal)),
                $"the reason must say why the shape is refused. Actual: {string.Join(" | ", ex.BlockingDependencies.Select(d => d.Reason))}");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
        }
    }

    [TestMethod]
    public async Task ExtractType_ImplicitInterfaceImplementation_RefusesWithStructuredBlockingDependency()
    {
        // extract-type-interface-implementation-guard: moving a member that implements an interface of
        // the source type leaves `: IGuardedContract` on the source without the member (CS0535).
        var ex = await PreviewInterfaceFixtureAsync(
            "ImplicitInterfaceFixture",
            [
                "public interface IGuardedContract { int Compute(int x); }",
                "",
                "public class ImplicitInterfaceFixture : IGuardedContract",
                "{",
                "    public int Compute(int x) => x * 2;",
                "}",
            ],
            "Compute");

        Assert.AreEqual(1, ex.BlockingDependencies.Count);
        Assert.AreEqual("Compute", ex.BlockingDependencies[0].Member);
        StringAssert.Contains(ex.BlockingDependencies[0].Reason, "IGuardedContract.Compute");
    }

    [TestMethod]
    public async Task ExtractType_ExplicitInterfaceImplementation_RefusesWithStructuredBlockingDependency()
    {
        // Explicit implementations are selected by bare identifier and would gain `public` (CS0106).
        var ex = await PreviewInterfaceFixtureAsync(
            "ExplicitInterfaceFixture",
            [
                "public interface IGuardedExplicit { int Compute(int x); }",
                "",
                "public class ExplicitInterfaceFixture : IGuardedExplicit",
                "{",
                "    int IGuardedExplicit.Compute(int x) => x * 2;",
                "}",
            ],
            "Compute");

        Assert.AreEqual(1, ex.BlockingDependencies.Count);
        Assert.AreEqual("Compute", ex.BlockingDependencies[0].Member);
        StringAssert.Contains(ex.BlockingDependencies[0].Reason, "IGuardedExplicit.Compute");
    }

    [TestMethod]
    public async Task ExtractType_NonInterfaceMemberOfInterfaceImplementingType_StillExtracts()
    {
        // Control: the guard keys on the selected member, not on the source type having interfaces.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var fixturePath = Path.Combine(solutionDir, "SampleLib", "InterfaceControlFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public interface IControlContract { int Compute(int x); }",
                "",
                "public class InterfaceControlFixture : IControlContract",
                "{",
                "    public int Compute(int x) => x * 2;",
                "    public int Unrelated() => 7;",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            var result = await TypeExtractionService.PreviewExtractTypeAsync(
                wsId, fixturePath, "InterfaceControlFixture", ["Unrelated"], "UnrelatedHelper", null,
                CancellationToken.None);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Changes.Any(
                c => c.FilePath.EndsWith("UnrelatedHelper.cs", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
        }
    }

    private async Task<ExtractTypeBlockingDependencyException> PreviewInterfaceFixtureAsync(
        string typeName, string[] bodyLines, string memberName)
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var fixturePath = Path.Combine(solutionDir, "SampleLib", typeName + ".cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[] { "namespace SampleLib;", "" }.Concat(bodyLines).Append("")));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            return await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    wsId, fixturePath, typeName, [memberName], "ExtractedHelper", null, CancellationToken.None));
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
        }
    }

    [TestMethod]
    public async Task ExtractType_MultiDeclaratorField_SplitsOnlyRequestedVariables()
    {
        // Regression for `type-extraction-member-shape-validation` (2/3): `GetMemberName` named a
        // field by its FIRST declarator only, so requesting "a" from `private int a = 1, b = 2;`
        // moved the whole declaration and silently dragged `b` along (and requesting "b" matched
        // nothing at all). The declaration must now be split, with attributes/modifiers/type and
        // every initializer preserved on BOTH halves — including the retained, non-extracted one.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "MultiDeclaratorFieldFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class MultiDeclaratorFieldFixture",
                "{",
                "    private int a = 1, b = 2;",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            var result = await TypeExtractionService.PreviewExtractTypeAsync(
                wsId, fixturePath, "MultiDeclaratorFieldFixture", ["a"], "ValueHolder", null,
                CancellationToken.None);

            Assert.IsNotNull(result);

            var newFileDiff = result.Changes.FirstOrDefault(
                c => c.FilePath.EndsWith("ValueHolder.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(newFileDiff, "preview must emit a change entry for the new extracted type file");
            var newFileAdded = AddedDiffLines(newFileDiff!.UnifiedDiff);

            Assert.IsTrue(newFileAdded.Any(l => l.Contains("a = 1", StringComparison.Ordinal)),
                $"the requested declarator (with its initializer) must move to the new type. Diff:\n{newFileDiff.UnifiedDiff}");
            Assert.IsFalse(newFileAdded.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\bb\b")),
                $"the unrequested sibling declarator must NOT be dragged into the new type. Diff:\n{newFileDiff.UnifiedDiff}");

            var sourceDiff = result.Changes.FirstOrDefault(
                c => c.FilePath.EndsWith("MultiDeclaratorFieldFixture.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(sourceDiff, "preview must emit a change entry for the edited source file");
            var sourceAdded = AddedDiffLines(sourceDiff!.UnifiedDiff);

            Assert.IsTrue(sourceAdded.Any(l => l.Contains("private int b = 2;", StringComparison.Ordinal)),
                $"the retained half must keep the original modifiers, type and initializer. Diff:\n{sourceDiff.UnifiedDiff}");
            Assert.IsFalse(sourceAdded.Any(l => l.Contains("a = 1", StringComparison.Ordinal)),
                $"the extracted declarator must be gone from the source type. Diff:\n{sourceDiff.UnifiedDiff}");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
        }
    }

    [TestMethod]
    public async Task ExtractType_AmbiguousOverload_RefusesWithStructuredCandidates()
    {
        // Regression for `type-extraction-member-shape-validation` (3/3): `PartitionMembers` removed
        // a name from its pending set on the FIRST source-order match, so for an overloaded name the
        // first-declared overload was extracted and every later same-named overload silently fell
        // through to the keep list — the caller was never told a choice had been made for them.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "OverloadShapeFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class OverloadShapeFixture",
                "{",
                "    private int Foo(int x) => x;",
                "    private int Foo(string s) => s.Length;",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    wsId, fixturePath, "OverloadShapeFixture", ["Foo"], "FooHelper", null,
                    CancellationToken.None));

            Assert.IsTrue(ex.BlockingDependencies.Count >= 2,
                $"every ambiguous candidate must be reported, not just the first. Actual count: {ex.BlockingDependencies.Count}");
            Assert.IsTrue(ex.BlockingDependencies.All(d => string.Equals(d.Member, "Foo", StringComparison.Ordinal)),
                "each candidate entry must be attributed to the ambiguous requested name");

            var reasons = string.Join(" | ", ex.BlockingDependencies.Select(d => d.Reason));
            StringAssert.Contains(reasons, "(int x)",
                "the refusal must list each candidate's signature so the caller can disambiguate");
            StringAssert.Contains(reasons, "(string s)",
                "the refusal must list each candidate's signature so the caller can disambiguate");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
        }
    }

    [TestMethod]
    public async Task ExtractType_ImplicitConstructor_SynthesizesAssigningConstructor()
    {
        // Regression for type-extraction-composition-constructor-coverage (1/4): a source type
        // with no declared constructor previously got the readonly composition field with no
        // parameter and no assignment — a permanently-null field that compiled cleanly, so the
        // breakage was silent. The extraction must now synthesize a constructor that assigns it.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "ImplicitCtorFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class ImplicitCtorFixture",
                "{",
                "    public int InternalUser() => 42;",
                "    private int Compute(int x) => x * 2;",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            var result = await TypeExtractionService.PreviewExtractTypeAsync(
                wsId, fixturePath, "ImplicitCtorFixture", ["Compute"], "ComputeHelper", null,
                CancellationToken.None);

            var updatedSource = await GetModifiedDocumentTextAsync(result.PreviewToken, fixturePath);
            StringAssert.Contains(updatedSource, "public ImplicitCtorFixture(global::SampleLib.ComputeHelper computeHelper)",
                "a constructor accepting the extracted type must be synthesized when the type had only the implicit constructor");
            StringAssert.Contains(updatedSource, "_computeHelper = computeHelper;",
                "the synthesized constructor must assign the composition field");

            await AssertModifiedSolutionCompilesAsync(result.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
        }
    }

    [TestMethod]
    public async Task ExtractType_ChainedConstructorOverloads_WiresEveryConstructor()
    {
        // Regression for type-extraction-composition-constructor-coverage (2/4): with overloaded
        // constructors only the FIRST declared one gained the parameter/assignment; a
        // `: this(...)`-chained overload additionally produced CS1729 at apply time because the
        // delegated argument list was never updated. Every constructor must now carry the
        // parameter, only the root assigns, and the chain forwards the new argument.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "ChainedCtorFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class ChainedCtorFixture",
                "{",
                "    private readonly int _seed;",
                "    public ChainedCtorFixture(int seed) { _seed = seed; }",
                "    public ChainedCtorFixture() : this(7) { }",
                "    public int InternalUser() => _seed;",
                "    private int Compute(int x) => x * 2;",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            var result = await TypeExtractionService.PreviewExtractTypeAsync(
                wsId, fixturePath, "ChainedCtorFixture", ["Compute"], "ComputeHelper", null,
                CancellationToken.None);

            var updatedSource = await GetModifiedDocumentTextAsync(result.PreviewToken, fixturePath);
            StringAssert.Contains(updatedSource, "ChainedCtorFixture(int seed, global::SampleLib.ComputeHelper computeHelper)",
                "the root constructor must gain the new parameter");
            StringAssert.Contains(updatedSource, "ChainedCtorFixture(global::SampleLib.ComputeHelper computeHelper)",
                "the chained constructor must gain the new parameter too");
            StringAssert.Contains(updatedSource, "this(7, computeHelper)",
                "the chained initializer must forward the new argument to the delegated constructor");
            var assignmentCount = updatedSource.Split("_computeHelper = computeHelper;").Length - 1;
            Assert.AreEqual(1, assignmentCount,
                "only the ROOT constructor assigns the readonly field — the chain delegates the single write. " +
                $"Updated source:\n{updatedSource}");

            await AssertModifiedSolutionCompilesAsync(result.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
        }
    }

    [TestMethod]
    public async Task ExtractType_ExpressionBodiedConstructor_RewritesToBlockWithAssignment()
    {
        // Regression for type-extraction-composition-constructor-coverage (3/4): an
        // expression-bodied constructor gained the parameter but the `Body is not null` guard
        // silently skipped the assignment, so callers passed a value that was thrown away. The
        // constructor must be rewritten to a block body carrying both the original expression
        // and the field assignment.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "ExpressionBodiedCtorFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class ExpressionBodiedCtorFixture",
                "{",
                "    private readonly int _seed;",
                "    public ExpressionBodiedCtorFixture(int seed) => _seed = seed;",
                "    public int InternalUser() => _seed;",
                "    private int Compute(int x) => x * 2;",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var wsId = loadResult.WorkspaceId;

        try
        {
            var result = await TypeExtractionService.PreviewExtractTypeAsync(
                wsId, fixturePath, "ExpressionBodiedCtorFixture", ["Compute"], "ComputeHelper", null,
                CancellationToken.None);

            var updatedSource = await GetModifiedDocumentTextAsync(result.PreviewToken, fixturePath);
            StringAssert.Contains(updatedSource, "ExpressionBodiedCtorFixture(int seed, global::SampleLib.ComputeHelper computeHelper)",
                "the expression-bodied constructor must gain the new parameter");
            StringAssert.Contains(updatedSource, "_seed = seed;",
                "the original expression body must survive as a block statement");
            StringAssert.Contains(updatedSource, "_computeHelper = computeHelper;",
                "the rewritten block body must assign the composition field");
            Assert.IsFalse(updatedSource.Contains("=> _seed = seed;", StringComparison.Ordinal),
                $"the constructor must no longer be expression-bodied. Updated source:\n{updatedSource}");

            await AssertModifiedSolutionCompilesAsync(result.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
        }
    }

    [TestMethod]
    public async Task ExtractType_PrimaryConstructor_RefusesWithTopologyMessage()
    {
        // Regression for type-extraction-composition-constructor-coverage (4/4): a primary
        // constructor (record or `class C(...)`) is invisible to the
        // `OfType<ConstructorDeclarationSyntax>()` scan, so the old code shipped a preview with
        // an unassigned composition field. The topology must be refused before any syntax is
        // emitted, with prose naming the unsupported shape and the remedy.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, "PrimaryCtorFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public record PrimaryCtorFixture(int Seed)",
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
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    wsId, fixturePath, "PrimaryCtorFixture", ["Compute"], "ComputeHelper", null,
                    CancellationToken.None));

            StringAssert.Contains(ex.Message, "Refusing to extract type",
                "the refusal must use the standard refusal prose so ToolErrorHandler maps it");
            StringAssert.Contains(ex.Message, "primary constructor",
                "the refusal must name the unsupported topology");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            QueueDirectoryForCleanup(solutionDir);
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("2Invalid")]
    [DataRow("class")]
    public async Task ExtractType_InvalidNewTypeName_ThrowsNamedArgument(string? newTypeName)
    {
        var exception = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            TypeExtractionService.PreviewExtractTypeAsync(
                "unused-workspace",
                "unused.cs",
                "Unused",
                ["Member"],
                newTypeName!,
                null,
                CancellationToken.None));

        Assert.AreEqual("newTypeName", exception.ParamName);
    }

    [TestMethod]
    public async Task ExtractType_UnicodeNewTypeName_ProducesCompilingPreview()
    {
        var fixture = await CreateExtractionFixtureAsync(
            "UnicodeTypeNameFixture.cs",
            """
            namespace SampleLib;

            public class UnicodeTypeNameFixture
            {
                public int InternalUser() => Compute(21);
                public System.Func<int, int> MethodGroupUser() => Compute;
                public static int StaticUser() => StaticCompute(21);
                private int Compute(int value) => value * 2;
                private static int StaticCompute(int value) => value * 3;
            }
            """);

        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId,
                fixture.FilePath,
                "UnicodeTypeNameFixture",
                ["Compute", "StaticCompute"],
                "CaféHelper",
                null,
                CancellationToken.None);

            var updatedSource = await GetModifiedDocumentTextAsync(preview.PreviewToken, fixture.FilePath);
            StringAssert.Contains(updatedSource, "_caféHelper.Compute(21)");
            StringAssert.Contains(updatedSource, "MethodGroupUser() => this._caféHelper.Compute");
            StringAssert.Contains(updatedSource, "CaféHelper.StaticCompute(21)");
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
            QueueDirectoryForCleanup(fixture.SolutionDirectory);
        }
    }

    [TestMethod]
    public async Task ExtractType_ParamsConstructorAndChain_InsertBeforeParamsAndCompile()
    {
        var fixture = await CreateExtractionFixtureAsync(
            "ParamsCtorFixture.cs",
            """
            namespace SampleLib;

            public class ParamsCtorFixture
            {
                private readonly int _seed;
                public ParamsCtorFixture(int seed, params string[] labels) { _seed = seed + labels.Length; }
                public ParamsCtorFixture(params string[] labels) : this(0, labels) { }
                public int InternalUser() => _seed;
                private int Compute(int value) => value * 2;
            }
            """);

        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId,
                fixture.FilePath,
                "ParamsCtorFixture",
                ["Compute"],
                "ComputeHelper",
                null,
                CancellationToken.None);
            var updatedSource = await GetModifiedDocumentTextAsync(preview.PreviewToken, fixture.FilePath);

            StringAssert.Contains(updatedSource,
                "ParamsCtorFixture(int seed, global::SampleLib.ComputeHelper computeHelper, params string[] labels)");
            StringAssert.Contains(updatedSource,
                "ParamsCtorFixture(global::SampleLib.ComputeHelper computeHelper, params string[] labels)");
            StringAssert.Contains(updatedSource, "this(0, computeHelper, labels)");
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
            QueueDirectoryForCleanup(fixture.SolutionDirectory);
        }
    }

    [TestMethod]
    public async Task ExtractType_MultipartPartialType_RefusesBeforeCompositionRewrite()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDirectory = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDirectory = Path.Combine(solutionDirectory, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDirectory, "PartialFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            """
            namespace SampleLib;

            public partial class PartialFixture
            {
                public int InternalUser() => Compute(21);
                private int Compute(int value) => value * 2;
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(sampleLibDirectory, "PartialFixture.Constructor.cs"),
            """
            namespace SampleLib;

            public partial class PartialFixture
            {
                public PartialFixture() { }
            }
            """);
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);

        try
        {
            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    loadResult.WorkspaceId,
                    fixturePath,
                    "PartialFixture",
                    ["Compute"],
                    "ComputeHelper",
                    null,
                    CancellationToken.None));

            StringAssert.Contains(exception.Message, "multiple partial declarations");
            StringAssert.Contains(exception.Message, "constructors declared in other parts");
        }
        finally
        {
            WorkspaceManager.Close(loadResult.WorkspaceId);
            QueueDirectoryForCleanup(solutionDirectory);
        }
    }

    [TestMethod]
    [DataRow(
        "public extern TopologyFixture();",
        "has no body",
        DisplayName = "bodyless constructor")]
    [DataRow(
        "public TopologyFixture(int seed) { } public TopologyFixture() : this(seed: 1) { }",
        "uses named arguments",
        DisplayName = "named this initializer")]
    [DataRow(
        "public TopologyFixture(int seed) { } public TopologyFixture() : this(\"unresolved\") { }",
        "delegation target",
        DisplayName = "unresolved this target")]
    public async Task ExtractType_UnsupportedConstructorTopology_RefusesWithSpecificMessage(
        string constructors,
        string expectedMessage)
    {
        var fixture = await CreateExtractionFixtureAsync(
            "TopologyFixture.cs",
            $$"""
            namespace SampleLib;

            public class TopologyFixture
            {
                {{constructors}}
                public int InternalUser() => Compute(21);
                private int Compute(int value) => value * 2;
            }
            """);

        try
        {
            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId,
                    fixture.FilePath,
                    "TopologyFixture",
                    ["Compute"],
                    "ComputeHelper",
                    null,
                    CancellationToken.None));

            StringAssert.Contains(exception.Message, expectedMessage);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
            QueueDirectoryForCleanup(fixture.SolutionDirectory);
        }
    }

    [TestMethod]
    public async Task ExtractType_PrivateMembersUsedOnlyByExtractedCode_KeepOriginalAccessibility()
    {
        // Regression for `extract-type-preserve-private-fields`: every extracted member used to be
        // forced to `public`, leaking a private field and helper that no retained code touches.
        var fixture = await CreateExtractionFixtureAsync(
            "PrivateOnlyExtractedFixture.cs",
            """
            namespace SampleLib;

            public class PrivateOnlyExtractedFixture
            {
                public int Keep() => 1;
                private int _factor = 3;
                private int Scale(int value) => value * _factor;
                public int Compute(int value) => Scale(value);
            }
            """);

        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId,
                fixture.FilePath,
                "PrivateOnlyExtractedFixture",
                ["Compute", "Scale", "_factor"],
                "ScaleHelper",
                null,
                CancellationToken.None);

            var newFilePath = Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ScaleHelper.cs");
            var newTypeSource = await GetModifiedDocumentTextAsync(preview.PreviewToken, newFilePath);
            StringAssert.Contains(newTypeSource, "private int _factor");
            StringAssert.Contains(newTypeSource, "private int Scale(");
            StringAssert.Contains(newTypeSource, "public int Compute(");
            Assert.IsFalse(newTypeSource.Contains("public int Scale(", StringComparison.Ordinal),
                $"an extracted private helper no retained code references must stay private. New type:\n{newTypeSource}");
            Assert.IsFalse(newTypeSource.Contains("public int _factor", StringComparison.Ordinal),
                $"an extracted private field no retained code references must stay private. New type:\n{newTypeSource}");
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
            QueueDirectoryForCleanup(fixture.SolutionDirectory);
        }
    }

    [TestMethod]
    public async Task ExtractType_PrivateMembersReferencedByRetainedCode_WidenToPublicOnly()
    {
        // The composition rewrite turns retained references into `_holder.Member`, so exactly the
        // extracted members a retained method still names must become reachable; an unreferenced
        // sibling stays private.
        var fixture = await CreateExtractionFixtureAsync(
            "RetainedReferenceFixture.cs",
            """
            namespace SampleLib;

            public class RetainedReferenceFixture
            {
                public int Keep() => Helper(2) + _offset;
                private int _offset = 5;
                private int Helper(int value) => value + _offset;
                private int Hidden(int value) => value - _offset;
                public int Visible(int value) => Hidden(value);
            }
            """);

        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId,
                fixture.FilePath,
                "RetainedReferenceFixture",
                ["Helper", "_offset", "Hidden", "Visible"],
                "OffsetHolder",
                null,
                CancellationToken.None);

            var newFilePath = Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "OffsetHolder.cs");
            var newTypeSource = await GetModifiedDocumentTextAsync(preview.PreviewToken, newFilePath);
            StringAssert.Contains(newTypeSource, "public int Helper(");
            StringAssert.Contains(newTypeSource, "public int _offset");
            StringAssert.Contains(newTypeSource, "private int Hidden(");
            StringAssert.Contains(newTypeSource, "public int Visible(");
            var updatedSource = await GetModifiedDocumentTextAsync(preview.PreviewToken, fixture.FilePath);
            StringAssert.Contains(updatedSource, "_offsetHolder.Helper(2)");
            StringAssert.Contains(updatedSource, "_offsetHolder._offset");
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
            QueueDirectoryForCleanup(fixture.SolutionDirectory);
        }
    }

    [TestMethod]
    [DataRow("\n", "implicit")]
    [DataRow("\r\n", "implicit")]
    [DataRow("\n", "block")]
    [DataRow("\r\n", "block")]
    [DataRow("\n", "expression")]
    [DataRow("\r\n", "expression")]
    [DataRow("\n", "chain")]
    [DataRow("\r\n", "chain")]
    public async Task ExtractType_PreviewAndApply_PreserveUntouchedTrivia(string eol, string topology)
    {
        var constructor = topology switch
        {
            "implicit" => "",
            "block" => "\tpublic TriviaFixture(int seed = 1) : base() { /* before */ _seed  =  seed; /* after */ } // ctor tail",
            "expression" => "\tpublic TriviaFixture(int seed = 1) /* arrow before */ => /* arrow after */ _seed  =  seed; // ctor tail",
            "chain" => "\tpublic TriviaFixture(int seed, params string[] labels) { /* before */ _seed  =  seed; /* after */ } // ctor tail"
                + eol + "\tpublic TriviaFixture(params string[] labels) : this(1 /* argument */, labels) { /* chain body */ }",
            _ => throw new AssertFailedException("Unknown constructor topology."),
        };
        var prefix = "// file header  " + eol + "#nullable enable" + eol
            + "namespace   SampleLib;" + eol + eol + "public class TriviaFixture" + eol + "{" + eol;
        var retained = "\t// retained marker  " + eol + "#region Kept" + eol
            + "\tpublic   int Keep( ) {  return _seed; } // exact tail  " + eol + "#endregion" + eol;
        var suffix = "}" + eol + eol + "// unrelated marker  " + eol
            + "public class UnrelatedTrivia { public int Value( )=>  7; }" + eol;
        var source = prefix + "\tprivate int _seed = 1;" + eol
            + "\tstatic TriviaFixture( ) { /* static body */ }" + eol
            + (constructor.Length == 0 ? "" : constructor + eol) + retained
            + "\tpublic int User( ) => this /* receiver */ . /* dot */ Compute( 2 );" + eol
            + "\tpublic static int StaticUser( ) => TriviaFixture /* static receiver */ . /* static dot */ StaticCompute( 3 );" + eol
            + "\tprivate static int StaticCompute(int value) => value * 3;" + eol
            + "\tprivate int Compute(int value) => value * 2;" + eol + suffix;
        var fixture = await CreateExtractionFixtureAsync("TriviaFixture.cs", source);
        try
        {
            var originalBytes = await File.ReadAllBytesAsync(fixture.FilePath);
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "TriviaFixture", ["Compute", "StaticCompute"],
                "ComputeHelper", null, CancellationToken.None);
            var text = await GetModifiedDocumentTextAsync(preview.PreviewToken, fixture.FilePath);
            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(fixture.FilePath));
            StringAssert.StartsWith(text, prefix);
            StringAssert.EndsWith(text, suffix);
            StringAssert.Contains(text, retained);
            StringAssert.Contains(text, "\tstatic TriviaFixture( ) { /* static body */ }" + eol);
            StringAssert.Contains(text, "this /* receiver */ ._computeHelper. /* dot */ Compute( 2 )");
            StringAssert.Contains(text, "ComputeHelper /* static receiver */ . /* static dot */ StaticCompute( 3 )");
            if (topology is "block" or "chain")
                StringAssert.Contains(text, "/* before */ _seed  =  seed; /* after */");
            if (topology == "expression")
            {
                StringAssert.Contains(text, "/* arrow before */");
                StringAssert.Contains(text, "/* arrow after */ _seed  =  seed; // ctor tail");
            }
            if (topology == "chain")
            {
                StringAssert.Contains(text, "1 /* argument */, computeHelper, labels");
                StringAssert.Contains(text, "{ /* chain body */ }");
            }
            Assert.AreEqual(0, text.Replace(eol, "", StringComparison.Ordinal).Count(c => c is '\r' or '\n'));
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
            var applied = await RefactoringService.ApplyRefactoringAsync(
                preview.PreviewToken, "extract_type_apply", CancellationToken.None);
            Assert.IsTrue(applied.Success);
            CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes(text), await File.ReadAllBytesAsync(fixture.FilePath));
            await WorkspaceManager.ReloadAsync(fixture.WorkspaceId, CancellationToken.None);
            var project = WorkspaceManager.GetCurrentSolution(fixture.WorkspaceId).Projects.Single(p => p.Name == "SampleLib");
            var compilation = await project.GetCompilationAsync();
            Assert.IsNotNull(compilation);
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.HasCount(0, errors, string.Join(eol, errors.Select(d => d.ToString())));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
            QueueDirectoryForCleanup(fixture.SolutionDirectory);
        }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("\r\n")]
    public async Task ExtractType_CompactSourceAndSplitField_PreserveRetainedSeparators(string headerEol)
    {
        var fixture = await CreateExtractionFixtureAsync("CompactTriviaFixture.cs",
            "namespace SampleLib;" + headerEol + " public class CompactTriviaFixture { "
            + "private int a = 1, /* extracted separator */ removed = 2, /* retained separator */ b  =  3, /* second separator */ c = 4; "
            + "public int Keep( ) => a + b + c; public int User( ) => removed; } // final comment");
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "CompactTriviaFixture", ["removed"],
                "FieldHolder", null, CancellationToken.None);
            var text = await GetModifiedDocumentTextAsync(preview.PreviewToken, fixture.FilePath);
            StringAssert.StartsWith(text, "namespace SampleLib;" + headerEol + " public class CompactTriviaFixture { ");
            StringAssert.EndsWith(text, "} // final comment");
            StringAssert.Contains(text,
                "private int a = 1, /* retained separator */ b  =  3, /* second separator */ c = 4;");
            StringAssert.Contains(text, "public int Keep( ) => a + b + c;");
            StringAssert.Contains(text, "public int User( ) => this._fieldHolder.removed;");
            var helper = await GetModifiedDocumentTextAsync(preview.PreviewToken,
                Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "FieldHolder.cs"));
            StringAssert.Contains(helper, "/* extracted separator */");
            if (headerEol.Length > 0)
                Assert.AreEqual(0, text.Replace(headerEol, "", StringComparison.Ordinal).Count(c => c is '\r' or '\n'));
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
            QueueDirectoryForCleanup(fixture.SolutionDirectory);
        }
    }

    [TestMethod]
    public async Task ExtractType_FirstRetainedField_PreservesRemovedCommaComments()
    {
        var fixture = await CreateExtractionFixtureAsync("FirstRetainedTrivia.cs",
            "namespace SampleLib; public class FirstRetainedTrivia { "
            + "private int a = 1, /* first retained separator */ b = 2, c = 3; "
            + "public int Keep( ) => b + c; public int User( ) => a; }");
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "FirstRetainedTrivia", ["a"],
                "FieldHolder", null, CancellationToken.None);
            var text = await GetModifiedDocumentTextAsync(preview.PreviewToken, fixture.FilePath);
            StringAssert.Contains(text, "/* first retained separator */ b = 2, c = 3;");
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
            QueueDirectoryForCleanup(fixture.SolutionDirectory);
        }
    }



    [TestMethod]
    [DataRow("parameter")]
    [DataRow("local")]
    [DataRow("member")]
    [DataRow("inherited")]
    [DataRow("assignment-local")]
    [DataRow("consumer-local")]
    [DataRow("static-parameter")]
    [DataRow("static-local")]
    [DataRow("nested-namespace")]
    [DataRow("global-namespace")]
    [DataRow("escaped-namespace")]
    [DataRow("nested-type")]
    [DataRow("namespace-imports")]
    [DataRow("imported-type")]
    [DataRow("imported-alias")]
    [DataRow("unicode")]
    [DataRow("qualified-receiver")]
    [DataRow("generic-import")]
    [DataRow("attribute-import")]
    [DataRow("namespace-capture")]
    [DataRow("external-import")]
    [DataRow("alias-qualified")]
    [DataRow("receiver-directives")]
    public async Task ExtractType_GeneratedBindings_PreserveExistingNames(string scenario)
    {
        var namespacePrefix = scenario switch
        {
            "nested-namespace" => "namespace BindingOuter { namespace BindingInner { ",
            "namespace-imports" => "namespace BindingOuter { using Alias = System.Int32; namespace BindingInner { using Alias = System.String; ",
            "imported-type" or "external-import" => "using System.Text; namespace BindingCases { ",
            "generic-import" => "using System.Collections.Generic; namespace BindingCases { ",
            "attribute-import" => "using System; namespace BindingCases { ",
            "alias-qualified" => "using Alias = System.Text; namespace BindingCases { ",
            "imported-alias" => "namespace BindingCases { using ComputeHelper = System.String; ",
            "global-namespace" => "",
            "escaped-namespace" => "namespace @class { ",
            _ => "namespace BindingCases { ",
        };
        var namespaceSuffix = scenario is "nested-namespace" or "namespace-imports" ? " } }" :
            scenario == "global-namespace" ? "" : " }";
        var qualifiedNamespace = scenario switch
        {
            "nested-namespace" or "namespace-imports" => "BindingOuter.BindingInner.",
            "global-namespace" => "",
            "escaped-namespace" => "class.",
            _ => "BindingCases.",
        };
        var importedType = scenario is "imported-type" or "external-import";
        var helperName = scenario switch
        {
            "unicode" => "CaféHelper",
            "imported-type" or "external-import" => "StringBuilder",
            "generic-import" => "Dictionary",
            "attribute-import" => "ObsoleteAttribute",
            "namespace-capture" => "System",
            "alias-qualified" => "Alias",
            _ => "ComputeHelper",
        };
        var constructor = scenario switch
        {
            "parameter" => "public BindingFixture(int computeHelper) { _seed = computeHelper; } ",
            "local" => "public BindingFixture(int seed = 1) { int computeHelper = seed; _seed = computeHelper; } ",
            "assignment-local" => "public BindingFixture(int seed = 1) { int _computeHelper = seed; _seed = _computeHelper; } ",
            "member" or "inherited" => "public BindingFixture(int seed = 1) { _seed = seed + _computeHelper; } ",
            "imported-type" or "external-import" => "public BindingFixture(int seed = 1) { _seed = seed + new StringBuilder(\"kept\").Length; } ",
            _ => "public BindingFixture(int seed = 1) { _seed = seed; } ",
        };
        var members = scenario switch
        {
            "member" => "public int _computeHelper = 5; ",
            "nested-type" => "public class ComputeHelper { } ",
            "alias-qualified" => "public int KeepAlias( ) => new Alias::StringBuilder(\"kept\").Length; ",
            "generic-import" => "public int GenericKeep( ) => new Dictionary<int /* key */, List<string /* item */>> { [1] = [\"a\"] }.Count; ",
            _ => "",
        };
        var user = scenario == "consumer-local"
            ? "public int User() { int _computeHelper = 3; return Compute(_seed) + _computeHelper; } "
            : "public int User() => Compute(_seed); ";
        var receiver = scenario switch
        {
            "qualified-receiver" => "global:: /* alias comment */ BindingCases /* namespace comment */ . /* qualifier dot */ BindingFixture /* receiver comment */",
            "receiver-directives" => "global::\n#if true\n/* alias directive */ BindingCases\n#endif\n. /* qualifier dot */ BindingFixture",
            _ => "BindingFixture",
        };
        var staticUser = scenario switch
        {
            "static-parameter" => "public static int StaticUser(int ComputeHelper) => StaticCompute(ComputeHelper); ",
            "static-local" => "public static int StaticUser(int value) { int ComputeHelper = value; return StaticCompute(ComputeHelper); } ",
            _ => "public static int StaticUser(int value) => " + receiver + ". /* member dot */ StaticCompute(value); ",
        };
        var extra = scenario switch
        {
            "namespace-imports" => " + typeof(Alias).Name.Length",
            "imported-alias" => " + typeof(ComputeHelper).Name.Length",
            "imported-type" or "external-import" => " + new StringBuilder(\"helper\").Length",
            "generic-import" => " + new Dictionary<int, List<string>> { [1] = [\"a\"] }.Count",
            "attribute-import" => " + new ObsoleteAttribute(\"helper\").Message!.Length",
            "alias-qualified" => " + new Alias::StringBuilder(\"helper\").Length",
            _ => "",
        };
        var source = namespacePrefix
            + (scenario == "inherited" ? "public class BindingBase { protected int _computeHelper = 5; } " : "")
            + (scenario == "attribute-import" ? "[Obsolete(\"source\")] " : "")
            + "public class BindingFixture" + (scenario == "inherited" ? " : BindingBase" : "") + " { "
            + "private readonly int _seed; " + members + constructor
            + "public BindingFixture(string label) : this(2) { } " + user + staticUser
            + "public System.Func<int, int> MethodGroupUser() => Compute; "
            + "public static System.Func<int, int> StaticMethodGroupUser() => StaticCompute; "
            + (scenario == "attribute-import" ? "[Obsolete(\"member\")] " : "")
            + "private int Compute(int value) => value * 2" + extra + "; "
            + "private static int StaticCompute(int value) => value * 3" + extra + "; }" + namespaceSuffix;
        var fixture = await CreateExtractionFixtureAsync("BindingFixture.cs", source);
        var peerPath = Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ImportedConsumer.cs");
        var peerSource = "using System.Text; namespace BindingCases { public static class ImportedConsumer { public static int Value( ) => new StringBuilder(\"peer\").Length; } } namespace BindingCases.Child { public static class ChildConsumer { public static int Value( ) => new StringBuilder(\"child\").Length; } }";
        var externalPath = Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ExternalConsumer.cs");
        var externalSource = "using System.Text; using BindingCases; namespace OtherConsumers { public static class ExternalConsumer { public static int Value( ) => new StringBuilder(\"external\").Length; } }";
        var unaffectedPath = Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "UnaffectedConsumer.cs");
        var unaffectedSource = "using System.Text; namespace Unaffected { public static class UnaffectedConsumer { public static int Value( ) => new StringBuilder(\"untouched\").Length; } }";
        var dependentPath = Path.Combine(fixture.SolutionDirectory, "SampleApp", "DependentConsumer.cs");
        var dependentSource = "using System.Text; using BindingCases; namespace DependentConsumers { public static class DependentConsumer { public static int Value( ) => new StringBuilder(\"dependent\").Length; } }";
        try
        {
            if (importedType)
            {
                await File.WriteAllTextAsync(peerPath, peerSource);
                if (scenario == "external-import")
                {
                    await File.WriteAllTextAsync(externalPath, externalSource);
                    await File.WriteAllTextAsync(unaffectedPath, unaffectedSource);
                    await File.WriteAllTextAsync(dependentPath, dependentSource);
                }
                await WorkspaceManager.ReloadAsync(fixture.WorkspaceId, CancellationToken.None);
            }
            var originalBytes = await File.ReadAllBytesAsync(fixture.FilePath);
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "BindingFixture", ["Compute", "StaticCompute"],
                helperName, null, CancellationToken.None);
            var text = await GetModifiedDocumentTextAsync(preview.PreviewToken, fixture.FilePath);
            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(fixture.FilePath));
            if (scenario == "receiver-directives")
            {
                foreach (var preserved in new[] { "\n#if true\n", "\n#endif\n", "/* alias directive */", "/* qualifier dot */" })
                    Assert.AreEqual(1, text.Split(preserved, StringSplitOptions.None).Length - 1);
            }
            if (scenario == "qualified-receiver")
            {
                foreach (var comment in new[] { "/* alias comment */", "/* namespace comment */", "/* qualifier dot */", "/* receiver comment */", "/* member dot */" })
                    Assert.AreEqual(1, text.Split(comment, StringSplitOptions.None).Length - 1);
            }
            if (importedType)
                Assert.AreEqual(peerSource, await File.ReadAllTextAsync(peerPath));
            if (scenario == "external-import")
            {
                Assert.AreEqual(externalSource, await File.ReadAllTextAsync(externalPath));
                Assert.AreEqual(dependentSource, await File.ReadAllTextAsync(dependentPath));
                Assert.AreEqual(unaffectedSource, await GetModifiedDocumentTextAsync(preview.PreviewToken, unaffectedPath));
            }
            StringAssert.Contains(text, constructor[..constructor.IndexOf('{')].TrimEnd().Split('(')[0]);
            var modifiedPeers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (importedType)
            {
                foreach (var path in scenario == "external-import" ? new[] { peerPath, externalPath, dependentPath } : new[] { peerPath })
                {
                    Assert.AreEqual(1, preview.Changes.Count(change => FileSystemPath.Comparer.Equals(change.FilePath, path)));
                    modifiedPeers.Add(path, await GetModifiedDocumentTextAsync(preview.PreviewToken, path));
                }
            }
            if (scenario == "generic-import")
                StringAssert.Contains(text, "int /* key */, List<string /* item */>");
            if (scenario == "alias-qualified")
                StringAssert.Contains(text, "public int KeepAlias( ) => new Alias::StringBuilder(\"kept\").Length;");
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
            await AssertAppliedExtractionRunsAsync(
                fixture.WorkspaceId, preview.PreviewToken, fixture.FilePath, text, assembly =>
            {
                var helperType = assembly.GetType(qualifiedNamespace + helperName, throwOnError: true)!;
                var sourceType = assembly.GetType(qualifiedNamespace + "BindingFixture", throwOnError: true)!;
                var helper = Activator.CreateInstance(helperType)!;
                var root = Activator.CreateInstance(sourceType, scenario == "parameter" ? [3, helper] : [helper, 3])!;
                var chained = Activator.CreateInstance(sourceType, ["chain", helper])!;
                var aliasOffset = scenario == "generic-import" ? 1 : extra.Length > 0 ? 6 : 0;
                var offset = scenario is "member" or "inherited" ? 10 : scenario == "consumer-local" ? 3 : importedType ? 8 : 0;
                offset += aliasOffset;
                Assert.AreEqual(6 + offset, sourceType.GetMethod("User")!.Invoke(root, null));
                Assert.AreEqual(4 + offset, sourceType.GetMethod("User")!.Invoke(chained, null));
                Assert.AreEqual(9 + aliasOffset, sourceType.GetMethod("StaticUser")!.Invoke(null, [3]));
                var group = (Func<int, int>)sourceType.GetMethod("MethodGroupUser")!.Invoke(root, null)!;
                var staticGroup = (Func<int, int>)sourceType.GetMethod("StaticMethodGroupUser")!.Invoke(null, null)!;
                Assert.AreEqual(4 + aliasOffset, group(2));
                Assert.AreEqual(6 + aliasOffset, staticGroup(2));
                if (importedType)
                {
                    Assert.AreEqual(4, assembly.GetType("BindingCases.ImportedConsumer")!.GetMethod("Value")!.Invoke(null, null));
                    Assert.AreEqual(5, assembly.GetType("BindingCases.Child.ChildConsumer")!.GetMethod("Value")!.Invoke(null, null));
                }
                if (scenario == "external-import")
                    Assert.AreEqual(8, assembly.GetType("OtherConsumers.ExternalConsumer")!.GetMethod("Value")!.Invoke(null, null));
                if (scenario == "attribute-import")
                {
                    Assert.AreEqual("source", sourceType.GetCustomAttributes(typeof(ObsoleteAttribute), false)
                        .Cast<ObsoleteAttribute>().Single().Message);
                    Assert.AreEqual("member", helperType.GetMethod("Compute")!
                        .GetCustomAttributes(typeof(ObsoleteAttribute), false).Cast<ObsoleteAttribute>().Single().Message);
                }
            });
            if (importedType)
            {
                var peerText = modifiedPeers[peerPath];
                CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes(peerText), await File.ReadAllBytesAsync(peerPath));
                StringAssert.StartsWith(peerText, "using System.Text; namespace BindingCases { public static class ImportedConsumer { public static int Value( ) => ");
            }
            if (scenario == "external-import")
            {
                foreach (var path in new[] { externalPath, dependentPath })
                {
                    var modified = modifiedPeers[path];
                    CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes(modified), await File.ReadAllBytesAsync(path));
                    StringAssert.Contains(modified, "public static int Value( ) => ");
                }
                Assert.AreEqual(unaffectedSource, await File.ReadAllTextAsync(unaffectedPath));
                var dependent = WorkspaceManager.GetCurrentSolution(fixture.WorkspaceId).Projects.Single(p => p.Name == "SampleApp");
                var dependentCompilation = await dependent.GetCompilationAsync();
                Assert.IsNotNull(dependentCompilation);
                Assert.HasCount(0, dependentCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray());
                var document = dependent.Documents.Single(d => d.FilePath == dependentPath);
                var model = await document.GetSemanticModelAsync();
                var documentRoot = await document.GetSyntaxRootAsync();
                Assert.IsNotNull(model);
                Assert.IsNotNull(documentRoot);
                var creation = documentRoot.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax>().Single();
                Assert.AreEqual("System.Text", model.GetTypeInfo(creation).Type!.ContainingNamespace.ToDisplayString());
            }
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
            QueueDirectoryForCleanup(fixture.SolutionDirectory);
        }
    }

    [TestMethod]
    [DataRow("nullable")]
    [DataRow("region")]
    [DataRow("conditional")]
    [DataRow("using-region")]
    [DataRow("using-conditional")]
    [DataRow("var")]
    [DataRow("dynamic")]
    [DataRow("named-var")]
    [DataRow("dynamic-alias-collision")]
    [DataRow("define")]
    [DataRow("undef-debug")]
    [DataRow("dynamic-define")]
    [DataRow("dynamic-extern")]
    public async Task ExtractType_LexicalContext_PreservesSemantics(string scenario)
    {
        var prefix = scenario switch
        {
            "nullable" => "#nullable disable\n",
            "region" => "#region context\nnamespace LexicalCases {\n",
            "conditional" => "#if true\nnamespace LexicalCases {\n",
            "using-region" => "#region context\nusing System;\nnamespace LexicalCases {\n",
            "using-conditional" => "#if true\nusing System;\nnamespace LexicalCases {\n",
            "define" or "dynamic-define" => "#define LOCAL\nnamespace LexicalCases {\n",
            "undef-debug" => "#undef DEBUG\nnamespace LexicalCases {\n",
            "dynamic-extern" => "#define LOCAL\nextern alias FixtureAlias;\nnamespace LexicalCases {\n",
            _ => "namespace LexicalCases {\n",
        };
        var suffix = scenario switch
        {
            "nullable" => "",
            "region" or "using-region" => "\n}\n#endregion\n",
            "conditional" or "using-conditional" => "\n}\n#endif\n",
            _ => "\n}\n",
        };
        var kept = scenario switch
        {
            "var" => "public static int Kept() { var item = new { Value = 7 }; return item.Value; }",
            "dynamic" => "public static int Kept() { dynamic /* type comment */ item = new System.Text.StringBuilder(\"dynamic\"); return item.Length; }",
            "dynamic-alias-collision" => "public static int Kept() { int __TypeExtractionDynamic = 1; dynamic /* type comment */ item = new System.Text.StringBuilder(\"six666\"); return item.Length + __TypeExtractionDynamic; }",
            "dynamic-define" or "dynamic-extern" => "public static int Kept() { dynamic /* type comment */ item = new System.Text.StringBuilder(\"dynamic\"); return item.Length + Compute() - 7; }",
            "named-var" => "public static int Kept() { var item = new System.Text.StringBuilder(\"named77\"); return item.Length; }",
            "define" or "undef-debug" => "public static int Kept() => Compute();",
            _ => "public static int Kept() => 7;",
        };
        var dynamicCase = scenario is "dynamic" or "dynamic-alias-collision" or "dynamic-define" or "dynamic-extern";
        var helperName = scenario is "var" or "named-var" ? "@var" : dynamicCase ? "@dynamic" : "LexicalHelper";
        var moved = scenario switch
        {
            "nullable" => "private static string Compute(string value) => value;",
            "define" or "dynamic-define" or "dynamic-extern" => "private static int Compute() {\n#if LOCAL\nreturn 7;\n#else\nreturn 9;\n#endif\n}",
            "undef-debug" => "private static int Compute() {\n#if DEBUG\nreturn 9;\n#else\nreturn 7;\n#endif\n}",
            _ => "private static int Compute(int value) => value * 2;",
        };
        var source = prefix + "public class LexicalFixture { " + kept + " " + moved + " }" + suffix;
        var fixture = await CreateExtractionFixtureAsync("LexicalFixture.cs", source);
        try
        {
            if (scenario == "dynamic-extern")
            {
                var assemblyPath = Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "AliasSurface.dll");
                var assembly = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("AliasSurface",
                    [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("namespace AliasSurface { public class Marker { } }")],
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                await using (var output = File.Create(assemblyPath))
                    Assert.IsTrue(assembly.Emit(output).Success);
                var projectPath = Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "SampleLib.csproj");
                var projectText = await File.ReadAllTextAsync(projectPath);
                await File.WriteAllTextAsync(projectPath, projectText.Replace("</Project>",
                    "<ItemGroup><Reference Include=\"AliasSurface\"><HintPath>AliasSurface.dll</HintPath><Aliases>FixtureAlias</Aliases></Reference></ItemGroup></Project>", StringComparison.Ordinal));
                await WorkspaceManager.ReloadAsync(fixture.WorkspaceId, CancellationToken.None);
            }
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(fixture.WorkspaceId, fixture.FilePath,
                "LexicalFixture", ["Compute"], helperName, null, CancellationToken.None);
            var text = await GetModifiedDocumentTextAsync(preview.PreviewToken, fixture.FilePath);
            Assert.AreEqual(source, await File.ReadAllTextAsync(fixture.FilePath));
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
            if (scenario == "nullable")
            {
                var stored = PreviewStore.Retrieve(preview.PreviewToken);
                Assert.IsNotNull(stored);
                var compilation = await stored.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync();
                Assert.IsNotNull(compilation);
                var method = compilation.GetTypeByMetadataName("LexicalHelper")!.GetMembers("Compute").OfType<IMethodSymbol>().Single();
                Assert.AreEqual(NullableAnnotation.None, method.ReturnType.NullableAnnotation);
                Assert.AreEqual(NullableAnnotation.None, method.Parameters[0].Type.NullableAnnotation);
            }
            if (dynamicCase || scenario == "named-var")
            {
                var stored = PreviewStore.Retrieve(preview.PreviewToken);
                Assert.IsNotNull(stored);
                var document = stored.Value.ModifiedSolution.Projects.SelectMany(project => project.Documents)
                    .Single(document => document.FilePath == fixture.FilePath);
                var root = await document.GetSyntaxRootAsync();
                var model = await document.GetSemanticModelAsync();
                Assert.IsNotNull(root);
                Assert.IsNotNull(model);
                var local = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>()
                    .Single(variable => variable.Identifier.ValueText == "item");
                var type = ((ILocalSymbol)model.GetDeclaredSymbol(local)!).Type;
                if (dynamicCase)
                {
                    Assert.AreEqual(TypeKind.Dynamic, type.TypeKind);
                    Assert.AreEqual(1, text.Split("/* type comment */", StringSplitOptions.None).Length - 1);
                }
                else
                    Assert.AreEqual("global::System.Text.StringBuilder", type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
            var aliasName = scenario == "dynamic-alias-collision" ? "__TypeExtractionDynamic1" : "__TypeExtractionDynamic";
            var namespacePosition = prefix.IndexOf("namespace", StringComparison.Ordinal);
            var expectedPrefix = dynamicCase
                ? prefix[..namespacePosition] + "using " + aliasName + " = dynamic;\n" + prefix[namespacePosition..]
                : prefix;
            StringAssert.StartsWith(text, expectedPrefix);
            StringAssert.EndsWith(text, suffix);
            await AssertAppliedExtractionRunsAsync(fixture.WorkspaceId, preview.PreviewToken, fixture.FilePath, text, assembly =>
            {
                var type = assembly.GetType((scenario == "nullable" ? "" : "LexicalCases.") + "LexicalFixture", true)!;
                Assert.AreEqual(7, type.GetMethod("Kept")!.Invoke(null, null));
            });
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
            QueueDirectoryForCleanup(fixture.SolutionDirectory);
        }
    }

    [TestMethod]
    [DataRow("public class ComputeHelper { }")]
    [DataRow("namespace ComputeHelper { public class Existing { } }")]
    public async Task ExtractType_DuplicateTargetName_RefusesBeforeEffects(string declaration)
    {
        var fixture = await CreateExtractionFixtureAsync("DuplicateTarget.cs",
            "namespace BindingCases { " + declaration
            + " public class DuplicateTarget { private int Compute(int value) => value * 2; } }");
        try
        {
            var bytes = await File.ReadAllBytesAsync(fixture.FilePath);
            var error = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "DuplicateTarget", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            Assert.AreEqual("newTypeName", error.ParamName);
            StringAssert.Contains(error.PublicMessage, "Choose a different");
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(fixture.FilePath));
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ComputeHelper.cs")));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
            QueueDirectoryForCleanup(fixture.SolutionDirectory);
        }
    }

    public static IEnumerable<object[]> DerivedKeywordIdentifiers()
    {
        foreach (var spelling in Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetReservedKeywordKinds()
            .Concat(Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetContextualKeywordKinds())
            .Select(Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetText)
            .Where(text => text.Length > 0 && char.IsLetter(text[0]))
            .Select(text => char.ToUpperInvariant(text[0]) + text[1..])
            .Distinct(StringComparer.Ordinal))
        {
            yield return [spelling, false, "KeywordCtorFixture"];
        }
        yield return ["@class", false, "KeywordCtorFixture"];
        yield return ["@Class", false, "KeywordCtorFixture"];
        yield return ["@class", true, "@KeywordCtorFixture"];
        yield return ["Class", true, "@KeywordCtorFixture"];
    }

    [TestMethod]
    [DynamicData(nameof(DerivedKeywordIdentifiers))]
    public async Task ExtractType_DerivedKeywordIdentifiers_PreviewApplyAndConstruct(
        string helperName, bool implicitConstructor, string sourceName)
    {
        var constructor = implicitConstructor ? "" :
            "public " + sourceName + "(int seed = 1) { _seed = seed; } "
            + "public " + sourceName + "(string label) : this(2) { } ";
        var fixture = await CreateExtractionFixtureAsync("KeywordCtorFixture.cs",
            "namespace KeywordExtraction; public class " + sourceName + " { "
            + "private readonly int _seed = 3; " + constructor
            + "public int User() => Compute(_seed); "
            + "public static int StaticUser() => " + sourceName + ".StaticCompute(3); "
            + "private int Compute(int value) => value * 2; "
            + "private static int StaticCompute(int value) => value * 3; }");
        try
        {
            var originalBytes = await File.ReadAllBytesAsync(fixture.FilePath);
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, sourceName, ["Compute", "StaticCompute"],
                helperName, null, CancellationToken.None);
            var text = await GetModifiedDocumentTextAsync(preview.PreviewToken, fixture.FilePath);
            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(fixture.FilePath));
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
            await AssertAppliedExtractionRunsAsync(
                fixture.WorkspaceId, preview.PreviewToken, fixture.FilePath, text, assembly =>
            {
                var helperType = assembly.GetType("KeywordExtraction." + helperName.TrimStart('@'), throwOnError: true)!;
                var sourceType = assembly.GetType("KeywordExtraction." + sourceName.TrimStart('@'), throwOnError: true)!;
                var helper = Activator.CreateInstance(helperType)!;
                var root = Activator.CreateInstance(sourceType,
                    implicitConstructor ? [helper] : [helper, 3])!;
                Assert.AreEqual(6, sourceType.GetMethod("User")!.Invoke(root, null));
                Assert.AreEqual(9, sourceType.GetMethod("StaticUser")!.Invoke(null, null));
                if (!implicitConstructor)
                {
                    var chained = Activator.CreateInstance(sourceType, ["chain", helper])!;
                    Assert.AreEqual(4, sourceType.GetMethod("User")!.Invoke(chained, null));
                }
            });
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
            QueueDirectoryForCleanup(fixture.SolutionDirectory);
        }
    }

    private static async Task AssertAppliedExtractionRunsAsync(
        string workspaceId, string previewToken, string filePath, string expectedText,
        Action<System.Reflection.Assembly> verify)
    {
        var applied = await RefactoringService.ApplyRefactoringAsync(
            previewToken, "extract_type_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success);
        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes(expectedText), await File.ReadAllBytesAsync(filePath));
        await WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None);
        var project = WorkspaceManager.GetCurrentSolution(workspaceId).Projects.Single(p => p.Name == "SampleLib");
        var compilation = await project.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        using var assemblyBytes = new MemoryStream();
        var emitted = compilation.Emit(assemblyBytes);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        assemblyBytes.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext(Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            verify(context.LoadFromStream(assemblyBytes));
        }
        finally
        {
            context.Unload();
        }
    }

    private static async Task<(string WorkspaceId, string FilePath, string SolutionDirectory)> CreateExtractionFixtureAsync(
        string fileName,
        string source)
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDirectory = Path.GetDirectoryName(copiedSolutionPath)!;
        var filePath = Path.Combine(solutionDirectory, "SampleLib", fileName);
        await File.WriteAllTextAsync(filePath, source);
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        return (loadResult.WorkspaceId, filePath, solutionDirectory);
    }

    /// <summary>
    /// Fetches the post-extraction text of <paramref name="filePath"/> from the preview's stored
    /// modified solution — the exact content an apply would write to disk.
    /// </summary>
    private static async Task<string> GetModifiedDocumentTextAsync(string previewToken, string filePath)
    {
        var retrieved = PreviewStore.Retrieve(previewToken);
        Assert.IsNotNull(retrieved, "the preview token must be redeemable immediately after the preview");
        var document = retrieved.Value.ModifiedSolution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => FileSystemPath.Comparer.Equals(
                Path.GetFullPath(d.FilePath ?? string.Empty), Path.GetFullPath(filePath)));
        Assert.IsNotNull(document, $"modified solution must contain the source document '{filePath}'");
        return (await document.GetTextAsync()).ToString();
    }

    /// <summary>
    /// Acceptance gate for type-extraction-composition-constructor-coverage: the previewed source
    /// (updated source type + generated new type) must compile with zero errors — specifically none
    /// of CS1737 (required after optional), CS1729 (no matching constructor overload for the
    /// `this(...)` chain), or CS0191 (readonly field assigned outside a constructor).
    /// </summary>
    private static async Task AssertModifiedSolutionCompilesAsync(string previewToken)
    {
        var retrieved = PreviewStore.Retrieve(previewToken);
        Assert.IsNotNull(retrieved, "the preview token must be redeemable immediately after the preview");
        var project = retrieved.Value.ModifiedSolution.Projects
            .FirstOrDefault(p => string.Equals(p.Name, "SampleLib", StringComparison.Ordinal));
        Assert.IsNotNull(project, "modified solution must contain the SampleLib project");
        var compilation = await project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        Assert.AreEqual(0, errors.Length,
            "the previewed solution must compile clean (no CS1737/CS1729/CS0191 or any other error). " +
            $"Errors:\n{string.Join(Environment.NewLine, errors)}");
    }

    /// <summary>
    /// Returns the added ('+') lines of a unified diff with the marker stripped, skipping the
    /// '+++' file header.
    /// </summary>
    private static string[] AddedDiffLines(string unifiedDiff)
    {
        return unifiedDiff
            .Split('\n')
            .Where(line => line.StartsWith('+') && !line.StartsWith("+++"))
            .Select(line => line.TrimEnd('\r')[1..])
            .ToArray();
    }

    private void QueueDirectoryForCleanup(string path) => _directoriesToDelete.Add(path);

    [TestMethod]
    public async Task ArgumentRefusals_NullAndEmptyMembersPreserveNamedIdentity()
    {
        var nullError = await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
            TypeExtractionService.PreviewExtractTypeAsync("unused", "unused.cs", "Unused",
                null!, "Extracted", null, CancellationToken.None));
        Assert.AreEqual("memberNames", nullError.ParamName);
        var emptyError = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            TypeExtractionService.PreviewExtractTypeAsync("unused", "unused.cs", "Unused",
                [], "Extracted", null, CancellationToken.None));
        Assert.AreEqual("memberNames", emptyError.ParamName);
        StringAssert.Contains(emptyError.PublicMessage, "At least one member");
    }

    [TestMethod]
    [DataRow("PRIVATE-EXTRACTION-SENTINEL!")]
    [DataRow("@")]
    [DataRow("class")]
    [DataRow("async")]
    public async Task ArgumentRefusals_TypeIdentifierHasSafeGuidance(string name)
    {
        var error = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            TypeExtractionService.PreviewExtractTypeAsync("unused", "unused.cs", "Unused",
                ["Member"], name, null, CancellationToken.None));
        Assert.AreEqual("newTypeName", error.ParamName);
        StringAssert.Contains(error.PublicMessage, "C# identifier");
        Assert.IsFalse(error.PublicMessage.Contains(name, StringComparison.Ordinal));
        Assert.IsNotNull(error.InnerException);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ArgumentRefusals_TypeMalformedDestinationHasNamedRedactedCause(bool longPath)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        if (longPath && !OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows path normalization enforces the maximum path length.");
        var destination = longPath ? new string('x', 32768) : "PRIVATE-EXTRACTION-SENTINEL\0.cs";
        var path = workspace.GetPath("SampleLib", "MalformedDestinationProbe.cs");
        await File.WriteAllTextAsync(path,
            "namespace SampleLib; public sealed class MalformedDestinationProbe { public int Get() => 42; }",
            CancellationToken.None);
        var id = await workspace.LoadAsync(CancellationToken.None);
        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            TypeExtractionService.PreviewExtractTypeAsync(id, path, "MalformedDestinationProbe",
                ["Get"], "Extracted", destination, CancellationToken.None));
        Console.WriteLine("TYPE-PATH-PROBE:" + error.GetType().Name + ":" + error.ParamName + ":" + error.InnerException?.GetType().Name);
        Assert.AreEqual("newFilePath", error.ParamName);
        if (longPath)
            Assert.IsInstanceOfType<PathTooLongException>(error.InnerException);
        else
            Assert.IsInstanceOfType<ArgumentException>(error.InnerException);
    }

}
