using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class SolutionFileMutationEscapingLifecycleTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    [DataRow("Lifecycle;Input.cs")]
    [DataRow("Lifecycle%3BInput.cs")]
    public Task LiteralLinkedDocument_PreviewForkSourceAndUndoPreserveExactMembership(string name)
        => WithNativeCompositionAsync(async context =>
    {
        var source = context.Source;
        var services = context.Services;
        var manager = services.WorkspaceManager;
        var workspaceId = context.WorkspaceId;
        WorkspaceForkApplyResultDto? fork = null;
        Exception? primary = null;
        try
        {
            var original = manager.GetCurrentSolution(workspaceId);
            var owner = original.Projects.Single();
            var before = await File.ReadAllBytesAsync(owner.FilePath!);
            var path = source.GetPath("linked", name);
            const string text = "namespace Owner; public class LinkedLiteral { public const int Value = 42; }";
            var modified = original.AddDocument(DocumentId.CreateNewId(owner.Id), name, SourceText.From(text),
                folders: ["Virtual;Folder%3B"], filePath: path);
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified,
                SolutionDiffReadContext.ForWorkspace(manager, workspaceId), CancellationToken.None);
            var token = services.PreviewStore.Store(workspaceId, original, modified, manager.GetCurrentVersion(workspaceId),
                "literal linked document lifecycle", changes, PreviewKind.Unspecified);
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(owner.FilePath!));
            Assert.IsFalse(File.Exists(path), "Preview preparation must leave source membership and bytes untouched.");

            var service = CreateForkService(context, services.DotnetCommandRunner);
            fork = await service.ApplyAsync(workspaceId, token, "keep", false, null, "literal-document", CancellationToken.None);
            Assert.IsTrue(fork.Success);
            var forkPath = Path.Combine(fork.ForkPath, Path.GetRelativePath(source.RootPath, path));
            var forkSolution = manager.GetCurrentSolution(fork.ForkWorkspaceId!);
            Assert.HasCount(1, forkSolution.Projects.Single().Documents.Where(document => FileSystemPath.Comparer.Equals(document.FilePath, forkPath)).ToArray());
            Assert.AreEqual(text, await File.ReadAllTextAsync(forkPath));
            Assert.IsNotNull(services.PreviewStore.Retrieve(token), "Fork execution must retain the source preview.");
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(owner.FilePath!));
            Assert.IsFalse(File.Exists(path));

            var applied = await services.RefactoringService.ApplyRefactoringAsync(token, "literal-lifecycle", CancellationToken.None);
            Assert.IsTrue(applied.Success, applied.Error);
            var current = manager.GetCurrentSolution(workspaceId);
            Assert.HasCount(1, current.Projects.Single().Documents.Where(document => FileSystemPath.Comparer.Equals(document.FilePath, path)).ToArray());
            Assert.AreEqual(text, await File.ReadAllTextAsync(path));
            Assert.HasCount(1, EvaluatedBuildGraph.Source(current, CancellationToken.None).Nodes.Single(node => !node.IsOuterBuild).FileItems
                .Where(item => FileSystemPath.Comparer.Equals(item.Path, path)).ToArray());

            Assert.IsTrue(await services.UndoService.RevertAsync(workspaceId));
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(owner.FilePath!));
            Assert.IsFalse(File.Exists(path));
            Assert.IsFalse(manager.GetCurrentSolution(workspaceId).Projects.Single().Documents
                .Any(document => FileSystemPath.Comparer.Equals(document.FilePath, path)));
            Assert.IsNull(services.UndoService.GetLastOperation(workspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            var cleanup = new List<Func<ValueTask>>();
            if (fork is not null)
            {
                cleanup.Add(CleanupFailureCollector.FromAction(() => manager.Close(fork.ForkWorkspaceId!)));
                cleanup.Add(CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fork.ForkPath)));
            }
            await CleanupFailureCollector.RunAfterFailureAsync("Literal document lifecycle cleanup failed.", primary, [.. cleanup]);
        }
    });

    [TestMethod]
    public Task SuccessfulDefaultDrop_PreservesValidationSuccessAndReportsTerminalCleanup()
        => WithNativeCompositionAsync(async context =>
        {
            var token = await StoreSourcePreviewAsync(context);
            var result = await CreateForkService(context, context.Services.DotnetCommandRunner).ApplyAsync(context.WorkspaceId,
                token, "drop-on-success", false, null, "terminal-cleanup", CancellationToken.None);
            Assert.AreEqual("clean", result.Validation.OverallStatus, "Actual native validation must succeed before cleanup defines the verdict.");
            Assert.IsTrue(result.Success, "Completed POSIX namespace retention must not turn a successful fork into a failure.");
            Assert.IsFalse(result.Retained);
            Assert.IsNull(result.ForkWorkspaceId);
            Assert.HasCount(1, context.Services.WorkspaceManager.ListWorkspaces());
            Assert.IsFalse(Directory.Exists(result.ForkPath) && Directory.EnumerateFiles(result.ForkPath, "*", SearchOption.AllDirectories).Any());
            if (!OperatingSystem.IsWindows())
            {
                Assert.IsTrue(Directory.Exists(result.ForkPath), "POSIX retains the terminal empty directory skeleton.");
                Assert.IsTrue(result.CleanupWarnings.Any(warning => warning.Contains("directory names were retained", StringComparison.Ordinal)));
            }
            Assert.IsNotNull(context.Services.PreviewStore.Retrieve(token));
        });

    [TestMethod]
    public Task SuccessfulValidation_ActualForkDeletionFailureRemainsFailure()
        => WithNativeCompositionAsync(async context =>
        {
            if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows sharing excludes deletion of an actually locked fork file."); return; }
            var token = await StoreSourcePreviewAsync(context);
            using var runner = new CleanupLockingRunner(context.Services.DotnetCommandRunner);
            WorkspaceForkApplyResultDto? result = null;
            Exception? primary = null;
            try
            {
                result = await CreateForkService(context, runner).ApplyAsync(context.WorkspaceId,
                    token, "drop-on-success", false, null, "failed-cleanup", CancellationToken.None);
                Assert.IsTrue(runner.Locked, "The actual restored fork source must be locked before real validation and cleanup.");
                Assert.AreEqual("clean", result.Validation.OverallStatus);
                Assert.IsFalse(result.Success);
                Assert.IsFalse(result.Retained);
                Assert.IsTrue(result.CleanupWarnings.Any(warning => warning.Contains("Failed to delete fork directory", StringComparison.Ordinal)));
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Locked fork deletion fixture cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(runner.Dispose),
                    CleanupFailureCollector.FromAction(() => { if (result is not null) TestFixtureFileSystem.DeleteDirectoryIfExists(result.ForkPath); }));
            }
        });

    private static WorkspaceForkApplyService CreateForkService(NativeComposition context, IDotnetCommandRunner runner)
    {
        var services = context.Services;
        return new WorkspaceForkApplyService(services.PathPolicy, services.WorkspaceManager, services.WorkspaceExecutionGate,
            services.PreviewStore, services.WorkspaceValidationService, services.TestRunnerService, runner, NullLogger<WorkspaceForkApplyService>.Instance,
            PhysicalGraphNamespace.ForkParent, TimeProvider.System);
    }

    private static async Task<string> StoreSourcePreviewAsync(NativeComposition context)
    {
        var manager = context.Services.WorkspaceManager;
        var original = manager.GetCurrentSolution(context.WorkspaceId);
        var document = original.Projects.Single().Documents.Single(document => document.Name == "Base.cs");
        var modified = original.WithDocumentText(document.Id, SourceText.From("namespace Owner; public class ChangedBase { }"));
        var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified,
            SolutionDiffReadContext.ForWorkspace(manager, context.WorkspaceId), CancellationToken.None);
        return context.Services.PreviewStore.Store(context.WorkspaceId, original, modified, manager.GetCurrentVersion(context.WorkspaceId),
            "native cleanup lifecycle", changes, PreviewKind.Unspecified);
    }

    private sealed record NativeComposition(IsolatedWorkspaceScope Source, TestServiceContainer Services, string WorkspaceId);

    private static async Task WithNativeCompositionAsync(Func<NativeComposition, Task> inspect)
    {
        var source = await CreateNativeProjectFixtureAsync("Owner", CancellationToken.None);
        TestServiceContainer? services = null;
        Exception? primary = null;
        try
        {
            await File.WriteAllTextAsync(source.GetPath("Owner", "Base.cs"), "namespace Owner; public class Base { }");
            var policy = new ForkInvocationPathPolicy(await NativeSdkRestoreFixture.CreatePolicyAsync(source.GetPath("Owner", "Owner.csproj"), CancellationToken.None));
            services = TestServiceContainer.Create(ValidationOptions, policy);
            var loaded = await services.WorkspaceManager.LoadAsync(source.SolutionPath,
                OwnedNativeLoadContext.Properties(source.SolutionPath, new Dictionary<string, string>()), CancellationToken.None);
            await inspect(new(source, services, loaded.WorkspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native lifecycle composition cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => services?.Dispose()), () => source.DisposeAsync());
        }
    }

    private sealed class CleanupLockingRunner(IDotnetCommandRunner inner) : IDotnetCommandRunner, IDisposable
    {
        private FileStream? _lock;
        internal bool Locked => _lock is not null;
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct)
            => RunAsync(workingDirectory, targetPath, arguments, null, "dotnet", ct);
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
            => RunAsync(workingDirectory, targetPath, arguments, earlyKillPatterns, "dotnet", ct);
        public async Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, string executablePath, CancellationToken ct)
        {
            var result = await inner.RunAsync(workingDirectory, targetPath, arguments, earlyKillPatterns, executablePath, ct);
            if (result.Succeeded && arguments[0] == "restore")
            {
                if (_lock is not null) throw new AssertFailedException("The single-project cleanup fixture must execute exactly one restore.");
                _lock = new FileStream(Path.Combine(Path.GetDirectoryName(targetPath)!, "Base.cs"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            return result;
        }
        public void Dispose() => Interlocked.Exchange(ref _lock, null)?.Dispose();
    }
}
