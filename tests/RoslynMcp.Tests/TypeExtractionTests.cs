using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Stands up a real in-process MCP client/server pair over an in-memory duplex pipe so tests
/// can exercise <see cref="ClientRootPathValidator.ValidatePathAgainstRootsAsync"/>'s
/// root-rejection branch through the actual <see cref="McpServer.RequestRootsAsync"/> round
/// trip. <see cref="McpServer"/> cannot be constructed as a bare test double — its
/// <c>ClientCapabilities</c> getter and <c>RequestRootsAsync</c> are populated by the real
/// handshake/session machinery, not settable fields — and the SDK's concrete server
/// implementation type is internal, so the only public construction path is the same
/// <c>AddMcpServer().With...Transport()</c> DI composition <c>Program.cs</c> uses, pointed at
/// an in-memory duplex pipe (<see cref="Pipe"/>) instead of stdio.
/// </summary>
internal static class McpRootsTestServerFactory
{
    public sealed record Session(McpServer Server, McpClient Client, IHost Host) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync().ConfigureAwait(false);
            await Host.StopAsync().ConfigureAwait(false);
            Host.Dispose();
        }
    }

    /// <summary>
    /// Creates a connected client/server pair where the client advertises exactly one
    /// sanctioned root at <paramref name="sanctionedRootPath"/>.
    /// </summary>
    public static async Task<Session> CreateWithSanctionedRootAsync(string sanctionedRootPath, CancellationToken ct)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        hostBuilder.Logging.ClearProviders();
        hostBuilder.Services
            .AddMcpServer()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        var host = hostBuilder.Build();
        await host.StartAsync(ct).ConfigureAwait(false);
        var server = host.Services.GetRequiredService<McpServer>();

        var clientTransport = new StreamClientTransport(
            clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream(), NullLoggerFactory.Instance);
        var rootUri = new Uri(sanctionedRootPath).AbsoluteUri;
        var clientOptions = new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                Roots = new RootsCapability(),
            },
            Handlers = new McpClientHandlers
            {
                RootsHandler = (_, _) => ValueTask.FromResult(new ListRootsResult
                {
                    Roots = [new Root { Uri = rootUri }],
                }),
            },
        };

        var client = await McpClient.CreateAsync(clientTransport, clientOptions, NullLoggerFactory.Instance, ct)
            .ConfigureAwait(false);

        return new Session(server, client, host);
    }
}

[DoNotParallelize]
[TestClass]
public sealed class TypeExtractionTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    // host-refactor-tools-root-boundary-validation: extract_type_preview now calls
    // ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, ct) before
    // dispatching to the service, mirroring GetSyntaxTree/GetCodeActions. Passing server: null!
    // exercises the no-MCP-server-context fail-open branch pinned by
    // ClientRootPathValidatorTests.ValidatePath_NullServer_AllowsAccess — the validator's actual
    // root-matching logic (accept/reject/traversal/case-insensitivity) is exhaustively unit-tested
    // there via IsPathUnderAnyRoot. A live root-rejection round trip through the tool would require
    // standing up the SDK's full transport pipeline to populate McpServer.ClientCapabilities.Roots,
    // which the existing precedent (ExpandedSurfaceIntegrationTests' AnalyzeDataFlow/
    // AnalyzeControlFlow/GetOperations coverage) treats as impractical for a unit test.
    [TestMethod]
    public async Task PreviewExtractType_Tool_NullServer_AllowsAccess_And_ProducesPreview()
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
            var json = await TypeExtractionTools.PreviewExtractType(
                null!,
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
            TryDeleteDirectory(solutionDir);
        }
    }

    [TestMethod]
    public async Task PreviewExtractType_Tool_OutOfRootPath_RejectsWithArgumentException()
    {
        // Root-boundary regression: when a real MCP client session sanctions a root that
        // does NOT cover the requested file, extract_type_preview must reject before
        // dispatching to the service. Uses a real client/server pair (McpRootsTestServerFactory)
        // rather than server: null! so the actual ValidatePathAgainstRootsAsync root-matching
        // path (not just the fail-open no-server branch) is exercised end-to-end.
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
            var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
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

            StringAssert.Contains(ex.Message, "not under any client-sanctioned root");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            TryDeleteDirectory(solutionDir);
            TryDeleteDirectory(sanctionedRoot);
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
            var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    wsId, sourcePath, "ExternalConsumerFixture", ["Compute"], "ComputeHelper", null, CancellationToken.None));

            StringAssert.Contains(ex.Message, "external consumer");
            StringAssert.Contains(ex.Message, "ExternalConsumerCaller.cs",
                "error message must name the affected external file so callers know which code to update");
        }
        finally
        {
            WorkspaceManager.Close(wsId);
            TryDeleteDirectory(solutionDir);
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
