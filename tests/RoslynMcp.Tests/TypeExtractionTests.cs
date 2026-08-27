using Microsoft.CodeAnalysis;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[DoNotParallelize]
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

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
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
            StringAssert.Contains(updatedSource, "public ImplicitCtorFixture(ComputeHelper computeHelper)",
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
            StringAssert.Contains(updatedSource, "ChainedCtorFixture(int seed, ComputeHelper computeHelper)",
                "the root constructor must gain the new parameter");
            StringAssert.Contains(updatedSource, "ChainedCtorFixture(ComputeHelper computeHelper)",
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
            StringAssert.Contains(updatedSource, "ExpressionBodiedCtorFixture(int seed, ComputeHelper computeHelper)",
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
        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
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
            StringAssert.Contains(updatedSource, "MethodGroupUser() => _caféHelper.Compute");
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
                "ParamsCtorFixture(int seed, ComputeHelper computeHelper, params string[] labels)");
            StringAssert.Contains(updatedSource,
                "ParamsCtorFixture(ComputeHelper computeHelper, params string[] labels)");
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
}
