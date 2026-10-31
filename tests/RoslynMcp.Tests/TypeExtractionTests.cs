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
// Only the preview side of TypeExtractionService runs — no *_apply, no reload, no UndoService or
// ChangeTracker write (the one ApplyExtractMethod call is refused on route before any mutation).
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
        QueueDirectoryForCleanup(sanctionedRoot);
        Directory.CreateDirectory(outsideRoot);
        QueueDirectoryForCleanup(outsideRoot);

        await using var session = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(
            sanctionedRoot,
            CancellationToken.None);

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
        QueueDirectoryForCleanup(configuredRoot);
        Directory.CreateDirectory(clientOnlyRoot);
        QueueDirectoryForCleanup(clientOnlyRoot);

        await using var session = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(
            configuredRoot,
            CancellationToken.None,
            clientRootPaths: [clientRoot, clientOnlyRoot]);

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

    [TestMethod]
    public async Task ModernProtocol_ToolDispatch_UsesConfiguredSanctionedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "roots-modern-no-capability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        QueueDirectoryForCleanup(root);

        await using var session = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(
            root,
            CancellationToken.None,
            useLatestProtocol: true);

        var result = await session.Client.CallToolAsync(
            "roots_boundary_probe",
            new Dictionary<string, object?> { ["path"] = root },
            cancellationToken: CancellationToken.None);

        Assert.AreNotEqual(true, result.IsError);
        Assert.HasCount(1, result.Content);
        var content = Assert.IsInstanceOfType<TextContentBlock>(result.Content[0]);
        Assert.AreEqual("allowed", content.Text);
    }

    // Direct tool calls use an explicitly configured, connected test server. A missing server no
    // longer bypasses the production boundary; the separate rejection test below verifies the
    // negative DI path with a non-covering configured root.
    [TestMethod]
    public async Task PreviewExtractType_Tool_ConfiguredServer_ProducesPreview()
    {
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        }
    }

    [TestMethod]
    public async Task PreviewExtractType_Tool_OutOfRootPath_RejectsWithArgumentException()
    {
        // Root-boundary regression: when a real MCP server configures a root that
        // does NOT cover the requested file, extract_type_preview must reject before
        // dispatching to the service. Uses a real client/server pair so the actual
        // ValidatePathAgainstRootsAsync root-matching path is exercised end-to-end.
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        QueueDirectoryForCleanup(sanctionedRoot);

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
        }
    }

    [TestMethod]
    public async Task ExtractType_FromAnimalService_RefusesWhenExternalConsumersExist()
    {
        // Regression for `dr-9-1-does-not-update-external-consumer-call-sites` (P3): extracting
        // a member with references from another source file must refuse the preview before any
        // disk mutation. Keep the fixture local to this test so it does not drift as the shared
        // sample solution evolves.
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        }
    }

    [TestMethod]
    public async Task ExtractType_NoExternalConsumers_ProducesPreview()
    {
        // When the extracted member is only referenced from inside the source file (the
        // class itself), the new external-consumer guard does not fire and the preview
        // succeeds as before. Use a fresh fixture to control external reference state.
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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

    /// <summary>
    /// extract-type-override-modifier-stripped: the extracted type is a base-less <c>public sealed class</c>, so
    /// moving an <c>override</c> member silently changes dispatch (<c>ToString()</c> falls back to
    /// <c>object.ToString</c>) or leaves the source without a required implementation (CS0534). The preview must be
    /// refused with a structured blocking dependency before any token, version change or disk effect exists.
    /// </summary>
    [TestMethod]
    public async Task ExtractType_OverrideToString_RefusesWithStructuredBlockingDependency()
    {
        var fixture = await CreateExtractionFixtureAsync("OverrideToStringFixture.cs", """
            namespace SampleLib;

            public class OverrideToStringFixture
            {
                public override string ToString() => "O1";
                public int Keep() => 1;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            var ex = await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "OverrideToStringFixture", ["ToString"], "ExtractedHelper", null,
                    CancellationToken.None));

            Assert.AreEqual(1, ex.BlockingDependencies.Count);
            Assert.AreEqual("ToString", ex.BlockingDependencies[0].Member);
            StringAssert.Contains(ex.BlockingDependencies[0].Reason, "overrides");
            StringAssert.Contains(ex.BlockingDependencies[0].Reason, "object.ToString");
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ExtractedHelper.cs")));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task ExtractType_SealedOverrideMember_RefusesWithStructuredBlockingDependency()
    {
        var ex = await PreviewRefusedFixtureAsync(
            "SealedOverrideFixture",
            [
                "public class SealedOverrideBase { public virtual void Up() { } }",
                "",
                "public class SealedOverrideFixture : SealedOverrideBase",
                "{",
                "    public sealed override void Up() { }",
                "}",
            ],
            "Up");

        Assert.AreEqual(1, ex.BlockingDependencies.Count);
        Assert.AreEqual("Up", ex.BlockingDependencies[0].Member);
        StringAssert.Contains(ex.BlockingDependencies[0].Reason, "overrides");
        StringAssert.Contains(ex.BlockingDependencies[0].Reason, "SealedOverrideBase.Up");
    }

    [TestMethod]
    public async Task ExtractType_OverrideOfAbstractMember_RefusesWithStructuredBlockingDependency()
    {
        // Previously reached only the compile-delta backstop (CS0534): the source lost the implementation.
        var ex = await PreviewRefusedFixtureAsync(
            "AbstractOverrideDerived",
            [
                "public abstract class AbstractOverrideBase { public abstract void Down(); }",
                "",
                "public class AbstractOverrideDerived : AbstractOverrideBase",
                "{",
                "    public override void Down() { }",
                "    public int Keep() => 1;",
                "}",
            ],
            "Down");

        Assert.AreEqual(1, ex.BlockingDependencies.Count);
        Assert.AreEqual("Down", ex.BlockingDependencies[0].Member);
        StringAssert.Contains(ex.BlockingDependencies[0].Reason, "AbstractOverrideBase.Down");
    }

    [TestMethod]
    public async Task ExtractType_OverrideProperty_RefusesWithStructuredBlockingDependency()
    {
        var ex = await PreviewRefusedFixtureAsync(
            "OverridePropertyFixture",
            [
                "public class OverridePropertyBase { public virtual int Count => 1; }",
                "",
                "public class OverridePropertyFixture : OverridePropertyBase",
                "{",
                "    public override int Count => 2;",
                "}",
            ],
            "Count");

        Assert.AreEqual(1, ex.BlockingDependencies.Count);
        Assert.AreEqual("Count", ex.BlockingDependencies[0].Member);
        StringAssert.Contains(ex.BlockingDependencies[0].Reason, "OverridePropertyBase.Count");
    }

    [TestMethod]
    public async Task ExtractType_AbstractMember_RefusesWithStructuredBlockingDependency()
    {
        // Previously an unstructured InvalidOperationException from the (now removed) body validator.
        var ex = await PreviewRefusedFixtureAsync(
            "AbstractHostFixture",
            [
                "public abstract class AbstractHostFixture",
                "{",
                "    public abstract void Run();",
                "    public int Keep() => 1;",
                "}",
            ],
            "Run");

        Assert.AreEqual(1, ex.BlockingDependencies.Count);
        Assert.AreEqual("Run", ex.BlockingDependencies[0].Member);
        StringAssert.Contains(ex.BlockingDependencies[0].Reason, "abstract");
    }

    [TestMethod]
    public async Task ExtractType_VirtualMemberWithOverrides_RefusesWithStructuredBlockingDependency()
    {
        // Stripping `virtual` would orphan the derived override (CS0115) or silently mis-dispatch.
        var ex = await PreviewRefusedFixtureAsync(
            "VirtualHostFixture",
            [
                "public class VirtualHostFixture { public virtual int Slot() => 1; }",
                "",
                "public class VirtualHostDerived : VirtualHostFixture { public override int Slot() => 2; }",
            ],
            "Slot");

        Assert.AreEqual(1, ex.BlockingDependencies.Count);
        Assert.AreEqual("Slot", ex.BlockingDependencies[0].Member);
        StringAssert.Contains(ex.BlockingDependencies[0].Reason, "overridden");
        StringAssert.Contains(ex.BlockingDependencies[0].Reason, "VirtualHostDerived.Slot");
    }

    [TestMethod]
    public async Task ExtractType_OverrideThatImplementsInterface_ReportsOneBlockerWithBothReasons()
    {
        var ex = await PreviewRefusedFixtureAsync(
            "OverrideInterfaceFixture",
            [
                "public interface IOverrideContract { int Compute(int x); }",
                "",
                "public class OverrideInterfaceBase { public virtual int Compute(int x) => x; }",
                "",
                "public class OverrideInterfaceFixture : OverrideInterfaceBase, IOverrideContract",
                "{",
                "    public override int Compute(int x) => x * 2;",
                "}",
            ],
            "Compute");

        Assert.AreEqual(1, ex.BlockingDependencies.Count);
        Assert.AreEqual("Compute", ex.BlockingDependencies[0].Member);
        StringAssert.Contains(ex.BlockingDependencies[0].Reason, "IOverrideContract.Compute");
        StringAssert.Contains(ex.BlockingDependencies[0].Reason, "OverrideInterfaceBase.Compute");
    }

    [TestMethod]
    public async Task ExtractType_VirtualMemberWithoutOverrides_StillExtractsWithoutVirtual()
    {
        // Control: the virtual rule is not over-broad. A never-overridden `virtual` member keeps extracting;
        // the modifier is dropped because the new type is sealed and every reference is rebound or refused.
        var fixture = await CreateExtractionFixtureAsync("VirtualFreeFixture.cs", """
            namespace SampleLib;

            public class VirtualFreeFixture
            {
                public virtual int Slot() => 1;
                public int Keep() => 2;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "VirtualFreeFixture", ["Slot"], "SlotHelper", null,
                CancellationToken.None);

            var newFilePath = Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "SlotHelper.cs");
            var newTypeSource = await GetModifiedDocumentTextAsync(preview.PreviewToken, newFilePath);
            StringAssert.Contains(newTypeSource, "public int Slot(");
            Assert.IsFalse(
                System.Text.RegularExpressions.Regex.IsMatch(newTypeSource, @"\bvirtual\b"),
                $"the extracted member must not keep 'virtual'. Source:\n{newTypeSource}");
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task ExtractType_NewMember_StripsNewModifierFromNewType()
    {
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        }
    }

    [TestMethod]
    public async Task ExtractType_ImplicitInterfaceImplementation_RefusesWithStructuredBlockingDependency()
    {
        // extract-type-interface-implementation-guard: moving a member that implements an interface of
        // the source type leaves `: IGuardedContract` on the source without the member (CS0535).
        var ex = await PreviewRefusedFixtureAsync(
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
        var ex = await PreviewRefusedFixtureAsync(
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
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        }
    }

    private async Task<ExtractTypeBlockingDependencyException> PreviewRefusedFixtureAsync(
        string typeName, string[] bodyLines, string memberName)
    {
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        }
    }

    [TestMethod]
    public async Task ExtractType_AmbiguousOverload_RefusesWithStructuredCandidates()
    {
        // Regression for `type-extraction-member-shape-validation` (3/3): `PartitionMembers` removed
        // a name from its pending set on the FIRST source-order match, so for an overloaded name the
        // first-declared overload was extracted and every later same-named overload silently fell
        // through to the keep list — the caller was never told a choice had been made for them.
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        }
    }

    [TestMethod]
    public async Task ExtractType_ImplicitConstructor_SynthesizesAssigningConstructor()
    {
        // Regression for type-extraction-composition-constructor-coverage (1/4): a source type
        // with no declared constructor previously got the readonly composition field with no
        // parameter and no assignment — a permanently-null field that compiled cleanly, so the
        // breakage was silent. The extraction must now synthesize a constructor that assigns it.
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
            StringAssert.Contains(updatedSource, "MethodGroupUser() => _caféHelper.Compute");
            StringAssert.Contains(updatedSource, "CaféHelper.StaticCompute(21)");
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
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
        }
    }

    [TestMethod]
    public async Task ExtractType_MultipartPartialType_RefusesBeforeCompositionRewrite()
    {
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
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
        }
    }

    /// <summary>
    /// extract-type-preview-compile-delta-gate: a preview whose rewritten solution adds compiler errors
    /// must be refused before a token, workspace-version change or disk effect exists.
    /// </summary>
    [TestMethod]
    [DataRow("StaticHostFixture", "StaticHostFixture", "Compute", "CS0708")]
    [DataRow("PartialHostFixture", "PartialHostFixture", "Hook", "CS0751")]
    public async Task ExtractType_PreviewThatAddsCompilerErrors_IsRefusedWithoutToken(
        string fixtureName, string typeName, string member, string expectedId)
    {
        var source = fixtureName switch
        {
            "StaticHostFixture" => """
                namespace SampleLib;

                public static class StaticHostFixture
                {
                    public static int Keep() => 1;
                    public static int Compute(int value) => value * 2;
                }
                """,
            "PartialHostFixture" => """
                namespace SampleLib;

                public partial class PartialHostFixture
                {
                    partial void Hook();
                    partial void Hook() { }
                    public int Keep() => 1;
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(fixtureName), fixtureName, null),
        };
        var fixture = await CreateExtractionFixtureAsync(fixtureName + ".cs", source);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            var error = await Assert.ThrowsAsync<PublicInvalidOperationException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, typeName, [member], "ExtractedHelper", null, CancellationToken.None));

            StringAssert.Contains(error.Message, expectedId);
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ExtractedHelper.cs")));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    private async Task<(string WorkspaceId, string FilePath, string SolutionDirectory)> CreateExtractionFixtureAsync(
        string fileName,
        string source)
    {
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
        var solutionDirectory = Path.GetDirectoryName(copiedSolutionPath)!;
        var filePath = Path.Combine(solutionDirectory, "SampleLib", fileName);
        await File.WriteAllTextAsync(filePath, source);
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        return (loadResult.WorkspaceId, filePath, solutionDirectory);
    }

    [TestMethod]
    public async Task CreateExtractionFixture_WhenFixtureWriteFails_DeletesCopiedSolution()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateExtractionFixtureAsync("invalid\0name.cs", "namespace SampleLib;"));

        var copiedDirectory = _directoriesToDelete.Single();
        Assert.IsTrue(Directory.Exists(copiedDirectory), "Fixture setup must have created the copied solution before the write failed.");

        await TestCleanup();

        Assert.IsFalse(Directory.Exists(copiedDirectory), "Cleanup must delete a copied solution even when fixture setup fails.");
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
    /// extract-type-composition-assigned-before-use: the composition field used to be assigned at the END of
    /// every root constructor, so constructor code that reaches an extracted member through a retained
    /// helper dereferenced a null field. The previewed solution is emitted and run: <c>V</c> must be 7.
    /// </summary>
    [TestMethod]
    [DataRow("AssignFirstBlockFixture", "public AssignFirstBlockFixture() { _v = Init(); }")]
    [DataRow("AssignFirstExpressionFixture", "public AssignFirstExpressionFixture() => _v = Init();")]
    public async Task ExtractType_CompositionFieldAssignedBeforeConstructorCodeRuns(string typeName, string constructor)
    {
        var fixture = await CreateExtractionFixtureAsync(
            typeName + ".cs",
            $$"""
            namespace SampleLib;

            public class {{typeName}}
            {
                private int _v;
                public int V => _v;
                {{constructor}}
                private int Init() => Compute();
                private int Compute() => 7;
            }
            """);

        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, typeName, ["Compute"], "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);

            var project = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib");
            var compilation = (await project.GetCompilationAsync(CancellationToken.None))!;
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            var context = new System.Runtime.Loader.AssemblyLoadContext(typeName, isCollectible: true);
            try
            {
                image.Position = 0;
                var assembly = context.LoadFromStream(image);
                var helper = Activator.CreateInstance(assembly.GetType("SampleLib.ComputeHelper")!);
                var host = Activator.CreateInstance(assembly.GetType("SampleLib." + typeName)!, helper)!;
                Assert.AreEqual(7, (int)host.GetType().GetProperty("V")!.GetValue(host)!,
                    "constructor code must observe an assigned composition field");
            }
            finally
            {
                context.Unload();
            }
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow("Read()", "public override int Read() => Compute();")]
    [DataRow("((IBaseRead)this).Read()", "int IBaseRead.Read() => Compute();")]
    [DataRow("Read()", "public override int Read() => Relay(); private int Relay() => Compute();")]
    [DataRow("Dispatch()", "public override int Read() => Compute();")]
    public async Task ExtractType_BaseConstructorDispatchToExtractedHelper_RefusesBeforeStorage(
        string baseCall, string implementation)
    {
        var fixture = await CreateExtractionFixtureAsync("BaseDispatchFixture.cs", $$"""
            namespace SampleLib;

            public interface IBaseRead { int Read(); }
            public class BaseDispatchFixture : IBaseRead
            {
                public BaseDispatchFixture() { _ = {{baseCall}}; }
                public virtual int Read() => 0;
                private int Dispatch() => Read();
            }
            public class DerivedDispatchFixture : BaseDispatchFixture, IBaseRead
            {
                {{implementation}}
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            var target = Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ComputeHelper.cs");
            var exception = await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "DerivedDispatchFixture", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            Assert.IsTrue(exception.BlockingDependencies.Any(d => d.Member.Contains("Read", StringComparison.Ordinal)),
                string.Join("; ", exception.BlockingDependencies.Select(d => $"{d.Member}: {d.Reason}")));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            Assert.IsFalse(File.Exists(target));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorWithoutDispatch_AllowsRetainedOverride()
    {
        var fixture = await CreateExtractionFixtureAsync("SafeBaseFixture.cs", """
            namespace SampleLib;
            public class SafeBase { public SafeBase() { } public virtual int Read() => 0; }
            public class SafeDerived : SafeBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "SafeDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow("this[0]", "public override int this[int index] => Compute();")]
    [DataRow("Read()", "public override int Read() => this[0]; public override int this[int index] => Compute();")]
    [DataRow("Read()", "public override int Read() => -this; public static int operator -(IndexerDerived value) => value.Compute();")]
    public async Task ExtractType_BaseConstructorIndexerDispatch_RefusesBeforeStorage(
        string baseCall, string implementation)
    {
        var fixture = await CreateExtractionFixtureAsync("IndexerDispatchFixture.cs", $$"""
            namespace SampleLib;
            public class IndexerBase
            {
                public IndexerBase() { _ = {{baseCall}}; }
                public virtual int Read() => 0;
                public virtual int this[int index] => 0;
            }
            public class IndexerDerived : IndexerBase
            {
                {{implementation}}
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            var exception = await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "IndexerDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            Assert.IsTrue(exception.BlockingDependencies.Count > 0);
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ComputeHelper.cs")));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task ExtractType_UnselectedBaseConstructorDispatch_AllowsPreview()
    {
        var fixture = await CreateExtractionFixtureAsync("SelectedCtorFixture.cs", """
            namespace SampleLib;
            public class SelectedCtorBase
            {
                public SelectedCtorBase() { }
                public SelectedCtorBase(int value) { _ = Read(); }
                public virtual int Read() => 0;
            }
            public class SelectedCtorDerived : SelectedCtorBase
            {
                public SelectedCtorDerived() : base() { }
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "SelectedCtorDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow("using (this) { }")]
    [DataRow("using (var owned = this) { }")]
    [DataRow("using var owned = this;")]
    public async Task ExtractType_BaseConstructorImplicitDispose_RefusesBeforeStorage(string constructorBody)
    {
        var fixture = await CreateExtractionFixtureAsync("DisposeDispatchFixture.cs", $$"""
            namespace SampleLib;
            public class DisposeBase : System.IDisposable
            {
                public DisposeBase() { {{constructorBody}} }
                public virtual void Dispose() { }
            }
            public class DisposeDerived : DisposeBase
            {
                public override void Dispose() => Compute();
                private void Compute() { }
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "DisposeDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ComputeHelper.cs")));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow("foreach (var item in this) { break; }")]
    [DataRow("ForeachBase resource = new ForeachBase(true); resource = this; foreach (var item in resource) { break; }")]
    public async Task ExtractType_BaseConstructorImplicitGetEnumerator_RefusesBeforeStorage(string constructorBody)
    {
        var fixture = await CreateExtractionFixtureAsync("ForeachDispatchFixture.cs", $$"""
            namespace SampleLib;
            public class ForeachBase
            {
                public ForeachBase() { {{constructorBody}} }
                private ForeachBase(bool unused) { }
                public virtual System.Collections.Generic.IEnumerator<int> GetEnumerator()
                    => System.Linq.Enumerable.Empty<int>().GetEnumerator();
            }
            public class ForeachDerived : ForeachBase
            {
                public override System.Collections.Generic.IEnumerator<int> GetEnumerator()
                    => System.Linq.Enumerable.Repeat(Compute(), 1).GetEnumerator();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "ForeachDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ComputeHelper.cs")));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorDisposesNewBaseInstance_AllowsPreview()
    {
        var fixture = await CreateExtractionFixtureAsync("NewResourceFixture.cs", """
            namespace SampleLib;
            public class NewResourceBase : System.IDisposable
            {
                public NewResourceBase() { using (new NewResourceBase(true)) { } }
                private NewResourceBase(bool unused) { }
                public virtual void Dispose() { }
            }
            public class NewResourceDerived : NewResourceBase
            {
                public override void Dispose() => Compute();
                private void Compute() { }
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "NewResourceDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorDisposesReassignedCurrentInstance_Refuses()
    {
        var fixture = await CreateExtractionFixtureAsync("ReassignedResourceFixture.cs", """
            namespace SampleLib;
            public class ReassignedResourceBase : System.IDisposable
            {
                public ReassignedResourceBase()
                {
                    ReassignedResourceBase resource = new ReassignedResourceBase(true);
                    resource = this;
                    using (resource) { }
                }
                private ReassignedResourceBase(bool unused) { }
                public virtual void Dispose() { }
            }
            public class ReassignedResourceDerived : ReassignedResourceBase
            {
                public override void Dispose() => Compute();
                private void Compute() { }
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "ReassignedResourceDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow("this", "public override bool MoveNext() => Compute() > 0;", true)]
    [DataRow("this", "public override int Current => Compute();", true)]
    [DataRow("this", "public override void Dispose() => _ = Compute();", true)]
    [DataRow("new SelfEnumeratorBase(true)", "public override bool MoveNext() => Compute() > 0;", false)]
    public async Task ExtractType_BaseConstructorEnumeratorMembers_RespectReceiverIdentity(
        string enumerator, string memberOverride, bool shouldRefuse)
    {
        var fixture = await CreateExtractionFixtureAsync("SelfEnumeratorFixture.cs", $$"""
            namespace SampleLib;
            public class SelfEnumeratorBase : System.Collections.Generic.IEnumerator<int>
            {
                public SelfEnumeratorBase() { foreach (var item in this) { break; } }
                private SelfEnumeratorBase(bool unused) { }
                public SelfEnumeratorBase GetEnumerator() => {{enumerator}};
                public virtual bool MoveNext() => true;
                public virtual int Current => 0;
                object System.Collections.IEnumerator.Current => Current;
                public void Reset() { }
                public virtual void Dispose() { }
            }
            public class SelfEnumeratorDerived : SelfEnumeratorBase
            {
                {{memberOverride}}
                private int Compute() => 7;
            }
            """);
        try
        {
            if (shouldRefuse)
            {
                var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
                await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                    TypeExtractionService.PreviewExtractTypeAsync(
                        fixture.WorkspaceId, fixture.FilePath, "SelfEnumeratorDerived", ["Compute"],
                        "ComputeHelper", null, CancellationToken.None));
                FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
                Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            }
            else
            {
                var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "SelfEnumeratorDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None);
                await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
            }
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorCallsVirtualOnNewBase_AllowsPreview()
    {
        var fixture = await CreateExtractionFixtureAsync("NewReceiverFixture.cs", """
            namespace SampleLib;
            public class NewReceiverBase
            {
                public NewReceiverBase() { _ = new NewReceiverBase(true).Read(); }
                private NewReceiverBase(bool unused) { }
                public virtual int Read() => 0;
            }
            public class NewReceiverDerived : NewReceiverBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "NewReceiverDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("System.Func<int> unused = () => Read();")]
    [DataRow("int Local() => Read();")]
    [DataRow("_ = nameof(Read);")]
    public async Task ExtractType_UninvokedNestedBodiesAndNameof_AllowsPreview(string constructorBody)
    {
        var fixture = await CreateExtractionFixtureAsync("UninvokedFixture.cs", $$"""
            namespace SampleLib;
            public class UninvokedBase
            {
                public UninvokedBase()
                {
                    {{constructorBody}}
                }
                public virtual int Read() => 0;
            }
            public class UninvokedDerived : UninvokedBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "UninvokedDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorImplicitAsyncDispose_RefusesBeforeStorage()
    {
        var fixture = await CreateExtractionFixtureAsync("AsyncDisposeFixture.cs", """
            namespace SampleLib;
            public class AsyncDisposeBase : System.IAsyncDisposable
            {
                public AsyncDisposeBase() { _ = InitAsync(); }
                private async System.Threading.Tasks.Task InitAsync() { await using (this) { } }
                public virtual System.Threading.Tasks.ValueTask DisposeAsync()
                    => System.Threading.Tasks.ValueTask.CompletedTask;
            }
            public class AsyncDisposeDerived : AsyncDisposeBase
            {
                public override System.Threading.Tasks.ValueTask DisposeAsync()
                {
                    Compute();
                    return System.Threading.Tasks.ValueTask.CompletedTask;
                }
                private void Compute() { }
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "AsyncDisposeDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("foreach (var item in this) { break; }", "public System.Collections.Generic.IEnumerator<int> GetEnumerator() { _ = Read(); return System.Linq.Enumerable.Empty<int>().GetEnumerator(); } public void Dispose() { }")]
    [DataRow("using (this) { }", "public void Dispose() { _ = Read(); }")]
    public async Task ExtractType_ImplicitMemberBodyCallsOverride_RefusesBeforeStorage(
        string constructorBody, string implicitMember)
    {
        var fixture = await CreateExtractionFixtureAsync("ImplicitBodyFixture.cs", $$"""
            namespace SampleLib;
            public class ImplicitBodyBase : System.IDisposable
            {
                public ImplicitBodyBase() { {{constructorBody}} }
                public virtual int Read() => 0;
                {{implicitMember}}
            }
            public class ImplicitBodyDerived : ImplicitBodyBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "ImplicitBodyDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorInvokedLambdaCallsOverride_RefusesBeforeStorage()
    {
        var fixture = await CreateExtractionFixtureAsync("InvokedLambdaFixture.cs", """
            namespace SampleLib;
            public class InvokedLambdaBase
            {
                public InvokedLambdaBase()
                {
                    System.Action action = () => _ = Read();
                    action();
                }
                public virtual int Read() => 0;
            }
            public class InvokedLambdaDerived : InvokedLambdaBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "InvokedLambdaDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("System.Action first = () => _ = Read(); System.Action second = first; second();")]
    [DataRow("System.Action first = () => { }; first = () => _ = Read(); System.Action second = first; second();")]
    [DataRow("Run(() => _ = Read());")]
    public async Task ExtractType_BaseConstructorInvokedDelegateAliasCallsOverride_Refuses(string constructorBody)
    {
        var fixture = await CreateExtractionFixtureAsync("DelegateAliasFixture.cs", $$"""
            namespace SampleLib;
            public class DelegateAliasBase
            {
                public DelegateAliasBase()
                {
                    {{constructorBody}}
                }
                private void Run(System.Action callback) => callback();
                public virtual int Read() => 0;
            }
            public class DelegateAliasDerived : DelegateAliasBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "DelegateAliasDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("return callback();", true)]
    [DataRow("return 0;", false)]
    public async Task ExtractType_RetainedOverrideDelegateUse_RespectsInvocation(
        string returnStatement, bool shouldRefuse)
    {
        var fixture = await CreateExtractionFixtureAsync("RetainedDelegateFixture.cs", $$"""
            namespace SampleLib;
            public class RetainedDelegateBase
            {
                public RetainedDelegateBase() { _ = Read(); }
                public virtual int Read() => 0;
            }
            public class RetainedDelegateDerived : RetainedDelegateBase
            {
                public override int Read()
                {
                    System.Func<int> callback = () => Compute();
                    {{returnStatement}}
                }
                private int Compute() => 7;
            }
            """);
        try
        {
            if (shouldRefuse)
            {
                var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
                await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                    TypeExtractionService.PreviewExtractTypeAsync(
                        fixture.WorkspaceId, fixture.FilePath, "RetainedDelegateDerived", ["Compute"],
                        "ComputeHelper", null, CancellationToken.None));
                FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
                Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            }
            else
            {
                var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "RetainedDelegateDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None);
                await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
            }
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("action = () => action();", false)]
    [DataRow("action = () => { _ = Read(); action(); };", true)]
    public async Task ExtractType_RecursiveDelegateAnalysisTerminates(string assignment, bool shouldRefuse)
    {
        var fixture = await CreateExtractionFixtureAsync("RecursiveDelegateFixture.cs", $$"""
            namespace SampleLib;
            public class RecursiveDelegateBase
            {
                public RecursiveDelegateBase()
                {
                    System.Action action = () => { };
                    {{assignment}}
                    action();
                }
                public virtual int Read() => 0;
            }
            public class RecursiveDelegateDerived : RecursiveDelegateBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            if (shouldRefuse)
            {
                await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                    TypeExtractionService.PreviewExtractTypeAsync(
                        fixture.WorkspaceId, fixture.FilePath, "RecursiveDelegateDerived", ["Compute"],
                        "ComputeHelper", null, CancellationToken.None));
            }
            else
            {
                var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "RecursiveDelegateDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None);
                await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
            }
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorInvokesSafeDelegateParameter_AllowsPreview()
    {
        var fixture = await CreateExtractionFixtureAsync("SafeDelegateParameterFixture.cs", """
            namespace SampleLib;
            public class SafeDelegateParameterBase
            {
                public SafeDelegateParameterBase() { Run(() => { }); }
                private void Run(System.Action callback) => callback();
                public virtual int Read() => 0;
            }
            public class SafeDelegateParameterDerived : SafeDelegateParameterBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "SafeDelegateParameterDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorReassignsDelegateParameter_Refuses()
    {
        var fixture = await CreateExtractionFixtureAsync("ReassignedDelegateParameterFixture.cs", """
            namespace SampleLib;
            public class ReassignedDelegateParameterBase
            {
                public ReassignedDelegateParameterBase() { Run(() => { }); }
                private void Run(System.Action callback)
                {
                    callback = () => _ = Read();
                    callback();
                }
                public virtual int Read() => 0;
            }
            public class ReassignedDelegateParameterDerived : ReassignedDelegateParameterBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "ReassignedDelegateParameterDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
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

    private string CreateQueuedSampleSolutionCopy()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        QueueDirectoryForCleanup(Path.GetDirectoryName(copiedSolutionPath)!);
        return copiedSolutionPath;
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
