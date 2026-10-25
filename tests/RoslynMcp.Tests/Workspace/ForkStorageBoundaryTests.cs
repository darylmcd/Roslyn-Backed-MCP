using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ForkStorageBoundaryTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task HostStorageValidator_MissingRequestContext_FailsClosed()
    {
        using var provider = CreateHostBoundaryProvider(TestTempRoot.Current);
        var validator = provider.GetRequiredService<ForkStoragePathValidator>();
        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            validator("missing-request", Path.Combine(TestTempRoot.Current, "never-created"), CancellationToken.None));
        StringAssert.Contains(refusal.Message, "active MCP request");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HostStorageValidator_ActualLegacyClientNarrowing_IsPreserved(bool narrow)
    {
        var root = Path.Combine(TestTempRoot.Current, "fork-client-boundary-" + Guid.NewGuid().ToString("N"));
        var retained = Path.Combine(root, ".roslynmcp", "forks", "20000101T000000000Z-first");
        Directory.CreateDirectory(retained);
        try
        {
            await using var session = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(root, CancellationToken.None,
                clientRootPaths: [narrow ? retained : root]);
            using var provider = CreateHostBoundaryProvider(root);
            using var context = RequestMcpServerContext.Begin(session.Server);
            var validator = provider.GetRequiredService<ForkStoragePathValidator>();
            var pool = Path.GetDirectoryName(retained)!;
            if (narrow)
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => validator("narrow-client", pool, CancellationToken.None));
            else Assert.AreEqual(pool, await validator("allowed-client", pool, CancellationToken.None));
            Assert.AreEqual(1, Directory.GetDirectories(pool).Length, "Authorization has no storage effects.");
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(root); }
    }

    private static ServiceProvider CreateHostBoundaryProvider(string root)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRoslynMcpHostServices(new WorkspaceManagerOptions(), new ValidationServiceOptions(), new PreviewStoreOptions(),
            new ExecutionGateOptions(), new SecurityOptions { SanctionedRoots = [root] }, new ScriptingServiceOptions());
        return services.BuildServiceProvider();
    }

    [TestMethod]
    [DataRow("canonical")]
    [DataRow("collision")]
    [DataRow("deny-pool")]
    [DataRow("deny-destination")]
    public async Task ForkStorageAuthorization_PinsCanonicalPathsAndReauthorizesCollision(string mode)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var source = await CreateIsolatedWorkspaceAsync(timeout.Token);
        var store = new PreviewStore();
        var solution = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
        var token = store.Store(source.WorkspaceId, solution, solution, WorkspaceManager.GetCurrentVersion(source.WorkspaceId), "storage identity", false, RoslynMcp.Core.Models.PreviewKind.Unspecified);
        var pool = Path.Combine(source.RootPath, ".roslynmcp", "forks");
        var canonical = Path.Combine(TestTempRoot.Current, "canonical-fork-pool-" + Guid.NewGuid().ToString("N"));
        if (mode == "canonical")
        {
            Directory.CreateDirectory(canonical);
            Directory.CreateDirectory(Path.Combine(canonical, "20000101T000000000Z-authored"));
            File.WriteAllText(Path.Combine(canonical, "20000101T000000000Z-authored", "authored.txt"), "Unrelated authored sibling");
            Directory.CreateDirectory(Path.GetDirectoryName(pool)!);
            WorkspaceForkGraphReplayTests.CreateLink(pool, canonical);
        }
        var authorized = new List<string>();
        string? occupied = null;
        var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService, TestRunnerService, WorkspaceManager, ChangeTracker);
        await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store, validation, TestRunnerService, DotnetCommandRunner,
            async (workspaceId, path, ct) =>
            {
                if (mode == "deny-pool" || (mode == "deny-destination" && authorized.Count == 1)) throw new ArgumentException("Fixture boundary denied storage.");
                var physical = await ForkStorageFixtureBoundary.ValidateAsync(workspaceId, path, ct);
                authorized.Add(physical);
                if (mode == "collision" && authorized.Count == 2)
                {
                    occupied = physical;
                    Directory.CreateDirectory(physical);
                    File.WriteAllText(Path.Combine(physical, "occupied.txt"), "Existing candidate bytes");
                }
                return physical;
            }, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceForkApplyService>.Instance, WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
        RoslynMcp.Core.Models.WorkspaceForkApplyResultDto? result = null;
        var subsequentIds = new List<string>();
        try
        {
            if (mode.StartsWith("deny-", StringComparison.Ordinal))
            {
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ApplyAsync(source.WorkspaceId, token, "keep", false, null, "identity", timeout.Token));
                Assert.IsFalse(Directory.Exists(Path.Combine(source.RootPath, ".roslynmcp")), "Boundary refusal must precede pool creation, sweep and copy.");
                Assert.AreSame(solution, WorkspaceManager.GetCurrentSolution(source.WorkspaceId));
                Assert.IsNotNull(store.Retrieve(token));
            }
            else
            {
                result = await service.ApplyAsync(source.WorkspaceId, token, "keep", false, null, "identity", timeout.Token);
                Assert.IsTrue(result.Success);
                Assert.AreEqual(authorized[^1], result.ForkPath, "Use the exact canonical destination returned by authorization.");
                Assert.AreEqual(mode == "canonical" ? canonical : pool, Path.GetDirectoryName(result.ForkPath));
                if (mode == "collision")
                {
                    Assert.AreEqual(3, authorized.Count, "Authorize pool, occupied candidate and replacement candidate separately.");
                    Assert.AreNotEqual(occupied, result.ForkPath);
                    Assert.AreEqual("Existing candidate bytes", File.ReadAllText(Path.Combine(occupied!, "occupied.txt")));
                }
                if (mode == "canonical")
                {
                    Assert.AreEqual("Unrelated authored sibling", File.ReadAllText(Path.Combine(canonical, "20000101T000000000Z-authored", "authored.txt")),
                        "Canonical pool authority must not turn an unrelated timestamp-shaped sibling into owned expiry material.");
                    var previous = result;
                    foreach (var generation in new[] { "second", "third" })
                    {
                        Assert.IsNotNull(previous.ForkWorkspaceId);
                        var nextSolution = WorkspaceManager.GetCurrentSolution(previous.ForkWorkspaceId);
                        var nextToken = store.Store(previous.ForkWorkspaceId, nextSolution, nextSolution, WorkspaceManager.GetCurrentVersion(previous.ForkWorkspaceId),
                            generation + " canonical generation", false, RoslynMcp.Core.Models.PreviewKind.Unspecified);
                        var next = await service.ApplyAsync(previous.ForkWorkspaceId, nextToken, "keep", false, null, generation, timeout.Token);
                        Assert.IsTrue(next.Success);
                        Assert.IsNotNull(next.ForkWorkspaceId);
                        subsequentIds.Add(next.ForkWorkspaceId);
                        Assert.AreEqual(canonical, Path.GetDirectoryName(next.ForkPath), "Verified canonical ownership must remain usable across generations regardless of pool spelling.");
                        Assert.AreEqual(previous.ForkPath, ForkAdmissionMaterial.Find(WorkspaceManager.GetStatus(next.ForkWorkspaceId).LoadedPath!)!.SourceRoot);
                        previous = next;
                    }
                }
            }
        }
        finally
        {
            foreach (var subsequentId in subsequentIds) WorkspaceManager.Close(subsequentId);
            if (result?.ForkWorkspaceId is { } id) WorkspaceManager.Close(id);
            if (mode == "canonical") Directory.Delete(pool);
            TestFixtureFileSystem.DeleteDirectoryIfExists(canonical);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CanonicalPool_InsideOrdinarySource_PreservesAuthoredAssetsAcrossGenerations(bool descendant)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var root = Path.Combine(TestTempRoot.Current, "inside-pool-" + Guid.NewGuid().ToString("N"));
        var pool = descendant ? Path.Combine(root, "assets") : root;
        Directory.CreateDirectory(pool);
        var import = Path.Combine(pool, "required.props");
        var content = Path.Combine(pool, "authored.txt");
        File.WriteAllText(import, "<Project><PropertyGroup><RequiredForkAsset>true</RequiredForkAsset></PropertyGroup></Project>");
        File.WriteAllText(content, "Required authored pool bytes");
        var project = Path.Combine(root, "Entry.csproj");
        File.WriteAllText(Path.Combine(root, "Entry.cs"), "public sealed class AuthoredEntry { }");
        var relativeImport = Path.GetRelativePath(root, import).Replace('\\', '/');
        File.WriteAllText(project, $"<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"{relativeImport}\"/><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup><Target Name=\"RequireAuthoredAsset\" BeforeTargets=\"CollectPackageReferences\"><Error Condition=\"'$(RequiredForkAsset)' != 'true'\" Text=\"Required authored import missing\"/></Target></Project>");
        var old = Path.Combine(pool, "29990101T000000000Z-old");
        var oldProject = Path.Combine(old, "Old.csproj");
        Directory.CreateDirectory(old);
        File.WriteAllText(oldProject, "<Project/>");
        ForkAdmissionMaterial.Install(root, old, [oldProject], timeout.Token);
        var logicalPool = Path.Combine(root, ".roslynmcp", "forks");
        Directory.CreateDirectory(Path.GetDirectoryName(logicalPool)!);
        WorkspaceForkGraphReplayTests.CreateLink(logicalPool, pool);
        var ids = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRoslynMcpHostServices(new WorkspaceManagerOptions(), new ValidationServiceOptions(), new PreviewStoreOptions(),
            new ExecutionGateOptions(), new SecurityOptions { SanctionedRoots = [TestTempRoot.Current] }, new ScriptingServiceOptions());
        services.AddSingleton<ForkStoragePathValidator>(ForkStorageFixtureBoundary.ValidateAsync);
        await using var provider = services.BuildServiceProvider();
        var manager = (WorkspaceManager)provider.GetRequiredService<IWorkspaceManager>();
        var gate = provider.GetRequiredService<IWorkspaceExecutionGate>();
        var store = (RoslynMcp.Roslyn.Services.PreviewStore)provider.GetRequiredService<IPreviewStore>();
        var refactoring = provider.GetRequiredService<IRefactoringService>();
        var undo = provider.GetRequiredService<IUndoService>();
        try
        {
            var restored = await DotnetCommandRunner.RunAsync(root, project, ["restore", project, "--nologo", "-nr:false"], timeout.Token);
            Assert.IsTrue(restored.Succeeded, restored.StdOut + restored.StdErr);
            var originalBuild = await DotnetCommandRunner.RunAsync(root, project, ["build", project, "--no-restore", "--nologo", "-nr:false"], timeout.Token);
            Assert.IsTrue(originalBuild.Succeeded, originalBuild.StdOut + originalBuild.StdErr);
            var loaded = await manager.LoadAsync(project, timeout.Token);
            ids.Add(loaded.WorkspaceId);
            var originalPreview = await refactoring.PreviewRenameAsync(loaded.WorkspaceId,
                RoslynMcp.Core.Models.SymbolLocator.ByMetadataName("AuthoredEntry"), "RenamedEntry", timeout.Token);
            Assert.IsTrue(originalPreview.Changes.Count > 0);
            var originalSolution = manager.GetCurrentSolution(loaded.WorkspaceId);
            var originalStatus = manager.GetStatus(loaded.WorkspaceId);
            var originalEntry = WorkspaceForkGraphReplayTests.CapturePreviewEntry(store, originalPreview.PreviewToken);
            var originalBytes = File.ReadAllBytes(Path.Combine(root, "Entry.cs"));
            var service = provider.GetRequiredService<IWorkspaceForkApplyService>();
            var current = loaded.WorkspaceId;
            foreach (var generation in new[] { "first", "second", "third" })
            {
                var solution = manager.GetCurrentSolution(current);
                var token = current == loaded.WorkspaceId ? originalPreview.PreviewToken
                    : store.Store(current, solution, solution, manager.GetCurrentVersion(current), generation, false,
                        RoslynMcp.Core.Models.PreviewKind.Unspecified);
                var next = await service.ApplyAsync(current, token, "keep", false, null, generation, timeout.Token);
                Assert.IsTrue(next.Success);
                Assert.IsNotNull(next.ForkWorkspaceId);
                ids.Add(next.ForkWorkspaceId);
                Assert.AreEqual(pool, Path.GetDirectoryName(next.ForkPath));
                Assert.IsTrue(Path.GetFileName(next.ForkPath).StartsWith(".", StringComparison.Ordinal), "Generated storage must remain outside SDK default source globs.");
                var copiedAsset = Path.Combine(next.ForkPath, Path.GetRelativePath(root, content));
                Assert.AreEqual("Required authored pool bytes", File.ReadAllText(copiedAsset));
                Assert.IsFalse(Directory.Exists(Path.Combine(next.ForkPath, Path.GetRelativePath(root, old))), "Previous verified fork children must never be copied as authored inputs.");
                Assert.IsFalse(Directory.GetDirectories(next.ForkPath, "*", SearchOption.AllDirectories)
                    .Any(path => Path.GetFileName(path).EndsWith("-" + generation, StringComparison.Ordinal)), "The actual destination must not recursively copy itself.");
                var sourceBuild = await DotnetCommandRunner.RunAsync(root, project, ["build", project, "--no-restore", "--nologo", "-nr:false"], timeout.Token);
                Assert.IsTrue(sourceBuild.Succeeded, "Retaining a fork cannot alter original-source default Compile membership: " + sourceBuild.StdOut + sourceBuild.StdErr);
                current = next.ForkWorkspaceId;
            }
            Assert.AreEqual("Required authored pool bytes", File.ReadAllText(content));
            Assert.IsTrue(File.Exists(import));
            Assert.IsTrue(File.Exists(oldProject), "A fresh verified sibling is excluded from copying, not deleted.");
            Assert.AreSame(originalSolution, manager.GetCurrentSolution(loaded.WorkspaceId));
            Assert.AreEqual(originalStatus.WorkspaceVersion, manager.GetCurrentVersion(loaded.WorkspaceId));
            Assert.AreEqual(originalStatus.SnapshotToken, manager.GetStatus(loaded.WorkspaceId).SnapshotToken);
            Assert.AreSame(originalEntry, WorkspaceForkGraphReplayTests.CapturePreviewEntry(store, originalPreview.PreviewToken));
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(Path.Combine(root, "Entry.cs")));
            Assert.IsFalse(manager.IsStale(loaded.WorkspaceId), "Generated fork files cannot make their unchanged original source externally stale.");
            await RoslynMcp.Host.Stdio.Tools.RefactoringTools.ApplyRename(gate, refactoring, store,
                originalPreview.PreviewToken, timeout.Token);
            StringAssert.Contains(File.ReadAllText(Path.Combine(root, "Entry.cs")), "RenamedEntry");
            var reverted = await gate.RunWriteAsync(loaded.WorkspaceId,
                ct => undo.RevertAsync(loaded.WorkspaceId, ct), timeout.Token, applyStalenessPolicy: false);
            Assert.IsTrue(reverted);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(Path.Combine(root, "Entry.cs")));
        }
        finally
        {
            foreach (var id in ids.AsEnumerable().Reverse()) manager.Close(id);
            if (Directory.Exists(logicalPool)) Directory.Delete(logicalPool);
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CanonicalPool_WithinVerifiedOwner_RefusesBeforeEffects(bool descendant)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var source = await CreateIsolatedWorkspaceAsync(timeout.Token);
        var owner = Path.Combine(TestTempRoot.Current, "pool-owner-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(owner, "Entry.csproj");
        Directory.CreateDirectory(owner);
        File.WriteAllText(project, "<Project/>");
        ForkAdmissionMaterial.Install(source.RootPath, owner, [project], timeout.Token);
        var canonical = descendant ? Path.Combine(owner, "nested-pool") : owner;
        Directory.CreateDirectory(canonical);
        var logical = Path.Combine(source.RootPath, ".roslynmcp", "forks");
        Directory.CreateDirectory(Path.GetDirectoryName(logical)!);
        WorkspaceForkGraphReplayTests.CreateLink(logical, canonical);
        try
        {
            var before = Directory.GetFiles(owner, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            var solution = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
            var store = new PreviewStore();
            var token = store.Store(source.WorkspaceId, solution, solution, version, "owner reentry", false, RoslynMcp.Core.Models.PreviewKind.Unspecified);
            await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store, null!, null!, DotnetCommandRunner,
                ForkStorageFixtureBoundary.ValidateAsync, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceForkApplyService>.Instance,
                WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
            var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.ApplyAsync(source.WorkspaceId, token, "keep", false, null, "refused", timeout.Token));
            StringAssert.Contains(refusal.Message, "outside retained fork ownership");
            CollectionAssert.AreEquivalent(before.Keys.ToArray(), Directory.GetFiles(owner, "*", SearchOption.AllDirectories));
            foreach (var (path, bytes) in before) CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
            Assert.AreSame(solution, WorkspaceManager.GetCurrentSolution(source.WorkspaceId));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            Assert.IsNotNull(store.Retrieve(token));
        }
        finally
        {
            Directory.Delete(logical);
            TestFixtureFileSystem.DeleteDirectoryIfExists(owner);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualHostRequest_RetainedForkStorage_UsesConfiguredBoundary(bool retainedOnly)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var root = Path.Combine(TestTempRoot.Current, "fork-boundary-" + Guid.NewGuid().ToString("N"));
        var retained = Path.Combine(root, ".roslynmcp", "forks", "20000101T000000000Z-first");
        Directory.CreateDirectory(retained);
        var project = Path.Combine(retained, "Entry.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultItems>false</EnableDefaultItems><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
        var restored = await DotnetCommandRunner.RunAsync(retained, project, ["restore", "Entry.csproj", "--nologo", "-nr:false"], timeout.Token);
        Assert.IsTrue(restored.Succeeded, restored.StdOut + restored.StdErr);
        ForkAdmissionMaterial.Install(root, retained, [project], timeout.Token);
        var loaded = await WorkspaceManager.LoadAsync(project, timeout.Token);
        var store = new PreviewStore();
        var solution = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
        var token = store.Store(loaded.WorkspaceId, solution, solution, WorkspaceManager.GetCurrentVersion(loaded.WorkspaceId), "host boundary", false, RoslynMcp.Core.Models.PreviewKind.Unspecified);
        var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService, TestRunnerService, WorkspaceManager, ChangeTracker);
        string? createdId = null;
        try
        {
            await using var harness = await ProductionParityMcpHarness.CreateAsync("fork-storage-boundary", null, services =>
            {
                services.AddSingleton(new SecurityOptions { SanctionedRoots = [retainedOnly ? retained : root] });
                services.AddSingleton<IWorkspaceManager>(WorkspaceManager);
                services.AddSingleton<IWorkspaceExecutionGate>(WorkspaceExecutionGate);
                services.AddSingleton<IPreviewStore>(store);
                services.AddSingleton<IWorkspaceValidationService>(validation);
            });
            var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            var response = await harness.Client.CallToolAsync("workspace_fork_apply", new Dictionary<string, object?>
            {
                ["workspaceId"] = loaded.WorkspaceId,
                ["previewToken"] = token,
                ["retention"] = "keep",
                ["runTests"] = false,
                ["forkName"] = "next",
            }, cancellationToken: timeout.Token);
            var text = string.Join("\n", response.Content.OfType<TextContentBlock>().Select(block => block.Text));
            using var json = JsonDocument.Parse(text);
            if (retainedOnly)
            {
                Assert.IsTrue(response.IsError == true, text);
                StringAssert.Contains(text, "sanctioned-root boundary");
                Assert.IsFalse(Directory.Exists(Path.Combine(retained, ".roslynmcp")), "The retained configuration cannot authorize sibling writes.");
                CollectionAssert.AreEquivalent(before.Keys.ToArray(), Directory.GetFiles(root, "*", SearchOption.AllDirectories));
                foreach (var (path, expected) in before) CollectionAssert.AreEqual(expected, File.ReadAllBytes(path), path);
            }
            else
            {
                Assert.IsFalse(response.IsError == true, text);
                createdId = json.RootElement.GetProperty("forkWorkspaceId").GetString();
                var path = json.RootElement.GetProperty("forkPath").GetString()!;
                Assert.AreEqual(Path.GetDirectoryName(retained), Path.GetDirectoryName(path));
                Assert.AreEqual(retained, ForkAdmissionMaterial.Find(Path.Combine(path, "Entry.csproj"))!.SourceRoot);
            }
        }
        finally
        {
            if (createdId is not null) WorkspaceManager.Close(createdId);
            WorkspaceManager.Close(loaded.WorkspaceId);
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }
}
