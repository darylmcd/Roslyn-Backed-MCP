using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PreparedUndoCallerTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    public async Task RejectedEdit_RetainsPreviousUndoAndVersion()
    {
        IsolatedWorkspaceScope? scope = null;
        Exception? primary = null;
        try
        {
            scope = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
            var (path, original) = await EstablishPreviousUndoAsync(scope);
            var prior = UndoService.GetLastOperation(scope.WorkspaceId);
            var before = await File.ReadAllBytesAsync(path, CancellationToken.None);
            var version = WorkspaceManager.GetCurrentVersion(scope.WorkspaceId);
            var rejected = false;
            var refusing = CreateRejectingManager(() => rejected = true);
            var service = new EditService(refusing, NullLogger<EditService>.Instance, UndoService, ChangeTracker);
            var result = await service.ApplyTextEditsAsync(scope.WorkspaceId, path,
                [new TextEditDto(1, 1, 1, 1, "// rejected operation\n")], "apply_text_edit", CancellationToken.None);
            Assert.IsTrue(rejected, "The native restoration publication boundary must execute the controlled refusal.");
            Assert.IsFalse(result.Success);
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path, CancellationToken.None));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(scope.WorkspaceId));
            Assert.AreEqual(prior, UndoService.GetLastOperation(scope.WorkspaceId));
            Assert.IsTrue(await UndoService.RevertAsync(scope.WorkspaceId, CancellationToken.None));
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path, CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Prepared undo fixture cleanup failed.", primary,
                async () => { if (scope is not null) await scope.DisposeAsync(); });
        }
    }

    [TestMethod]
    public async Task ProjectMutation_RejectedCandidate_RestoresBytesAndPreviousUndo()
    {
        IsolatedWorkspaceScope? scope = null;
        Exception? primary = null;
        try
        {
            scope = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
            _ = await EstablishPreviousUndoAsync(scope);
            var prior = UndoService.GetLastOperation(scope.WorkspaceId);
            var version = WorkspaceManager.GetCurrentVersion(scope.WorkspaceId);
            var path = scope.GetPath("SampleLib", "SampleLib.csproj");
            var original = await File.ReadAllBytesAsync(path, CancellationToken.None);
            var updated = (await File.ReadAllTextAsync(path, CancellationToken.None))
                .Replace("</Project>", "<!-- proposed project edit --></Project>", StringComparison.Ordinal);
            var previews = new ProjectMutationPreviewStore();
            var token = previews.Store(scope.WorkspaceId, path, updated, version, "candidate refusal");
            var rejected = false;
            var refusing = CreateRejectingManager(() => rejected = true);
            var service = new ProjectMutationService(refusing, previews, MsBuildEvaluationService,
                NullLogger<ProjectMutationService>.Instance, ChangeTracker, UndoService);
            var result = await service.ApplyProjectMutationAsync(token, CancellationToken.None);
            Assert.IsTrue(rejected, "The native restoration publication boundary must execute the controlled refusal.");
            Assert.IsFalse(result.Success);
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path, CancellationToken.None));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(scope.WorkspaceId));
            Assert.AreEqual(prior, UndoService.GetLastOperation(scope.WorkspaceId));
            Assert.IsNotNull(previews.Retrieve(token));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Prepared undo fixture cleanup failed.", primary,
                async () => { if (scope is not null) await scope.DisposeAsync(); });
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompositeFailure_PublishesRecoveryOnlyWhenWritesRemain(bool partial)
    {
        IsolatedWorkspaceScope? scope = null;
        Exception? primary = null;
        try
        {
            scope = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
            var (path, _) = await EstablishPreviousUndoAsync(scope);
            var before = await File.ReadAllBytesAsync(path, CancellationToken.None);
            var prior = UndoService.GetLastOperation(scope.WorkspaceId);
            var created = scope.GetPath("SampleLib", "PartialOperation.cs");
            var mutations = new List<CompositeFileMutation>();
            if (partial) mutations.Add(new CompositeFileMutation(created, "internal class PartialOperation { }"));
            mutations.Add(new CompositeFileMutation(scope.GetPath("SampleLib"), "cannot replace a directory"));
            var previews = new CompositePreviewStore();
            var token = previews.Store(scope.WorkspaceId, WorkspaceManager.GetCurrentVersion(scope.WorkspaceId), "failed composite", mutations);
            var service = new CompositeApplyOrchestrator(WorkspaceManager, previews, ChangeTracker,
                NullLogger<CompositeApplyOrchestrator>.Instance, undoService: UndoService);
            var result = await service.ApplyCompositeAsync(token, CancellationToken.None);
            Assert.IsFalse(result.Success);
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path, CancellationToken.None));
            if (!partial)
            {
                Assert.AreEqual(0, result.AppliedFiles.Count);
                Assert.AreEqual(prior, UndoService.GetLastOperation(scope.WorkspaceId));
            }
            else
            {
                CollectionAssert.Contains(result.AppliedFiles.ToArray(), created);
                Assert.IsTrue(File.Exists(created));
                Assert.AreNotEqual(prior, UndoService.GetLastOperation(scope.WorkspaceId));
                Assert.IsTrue(await UndoService.RevertAsync(scope.WorkspaceId, CancellationToken.None));
                Assert.IsFalse(File.Exists(created));
                CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path, CancellationToken.None));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Prepared undo fixture cleanup failed.", primary,
                async () => { if (scope is not null) await scope.DisposeAsync(); });
        }
    }

    [TestMethod]
    public async Task RejectedBatch_RestoresAllFilesAndPreviousUndo()
    {
        IsolatedWorkspaceScope? scope = null;
        Exception? primary = null;
        try
        {
            scope = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
            var (first, _) = await EstablishPreviousUndoAsync(scope);
            var second = scope.GetPath("SampleLib", "Cat.cs");
            var paths = new[] { first, second };
            var before = await Task.WhenAll(paths.Select(path => File.ReadAllBytesAsync(path, CancellationToken.None)));
            var prior = UndoService.GetLastOperation(scope.WorkspaceId);
            var version = WorkspaceManager.GetCurrentVersion(scope.WorkspaceId);
            var rejected = false;
            var refusing = CreateRejectingManager(() => rejected = true);
            var service = new EditService(refusing, NullLogger<EditService>.Instance, UndoService, ChangeTracker);
            var result = await service.ApplyMultiFileTextEditsAsync(scope.WorkspaceId,
                paths.Select(path => new FileEditsDto(path, [new TextEditDto(1, 1, 1, 1, "// refused batch\n")])).ToArray(),
                "apply_multi_file_edit", CancellationToken.None);
            Assert.IsTrue(rejected, "The native restoration publication boundary must execute the controlled refusal.");
            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, result.FilesModified);
            for (var index = 0; index < paths.Length; index++)
                CollectionAssert.AreEqual(before[index], await File.ReadAllBytesAsync(paths[index], CancellationToken.None));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(scope.WorkspaceId));
            Assert.AreEqual(prior, UndoService.GetLastOperation(scope.WorkspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Prepared undo fixture cleanup failed.", primary,
                async () => { if (scope is not null) await scope.DisposeAsync(); });
        }
    }

    [TestMethod]
    public async Task RepeatedFileBatch_ComposesEditsAndPublishesOneUndo()
    {
        IsolatedWorkspaceScope? scope = null;
        Exception? primary = null;
        try
        {
            scope = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
            var path = scope.GetPath("SampleLib", "Dog.cs");
            var before = await File.ReadAllBytesAsync(path, CancellationToken.None);
            var result = await EditService.ApplyMultiFileTextEditsAsync(scope.WorkspaceId,
                [
                    new FileEditsDto(path, [new TextEditDto(1, 1, 1, 1, "// first\n")]),
                    new FileEditsDto(path, [new TextEditDto(1, 4, 1, 9, "second")])
                ], "apply_multi_file_edit", CancellationToken.None);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.FilesModified);
            StringAssert.StartsWith(await File.ReadAllTextAsync(path, CancellationToken.None), "// second\n");
            Assert.IsTrue(await UndoService.RevertAsync(scope.WorkspaceId, CancellationToken.None));
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path, CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Prepared undo fixture cleanup failed.", primary,
                async () => { if (scope is not null) await scope.DisposeAsync(); });
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CompositeWriteThenThrow_ReportsActualEffectsAndRetainsRecovery(bool configureUndo)
    {
        IsolatedWorkspaceScope? scope = null;
        Exception? primary = null;
        try
        {
            scope = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
            var (path, _) = await EstablishPreviousUndoAsync(scope);
            var before = await File.ReadAllBytesAsync(path, CancellationToken.None);
            var prior = UndoService.GetLastOperation(scope.WorkspaceId);
            var previews = new CompositePreviewStore();
            var token = previews.Store(scope.WorkspaceId, WorkspaceManager.GetCurrentVersion(scope.WorkspaceId),
                "post-replacement failure", [new CompositeFileMutation(path, "// changed before failure")]);
            var service = new CompositeApplyOrchestrator(WorkspaceManager, previews, ChangeTracker,
                NullLogger<CompositeApplyOrchestrator>.Instance, null, configureUndo ? UndoService : null,
                async (target, expected, bytes, operation, authority, ct) =>
                {
                    await operation.TransitionSourceAsync(target, expected, bytes, PhysicalSourceTransitionMode.FreshReplacement, authority, null, ct);
                    throw new IOException("Controlled failure after physical replacement.");
                });
            var result = await service.ApplyCompositeAsync(token, CancellationToken.None);
            Assert.IsFalse(result.Success);
            CollectionAssert.AreEqual(new[] { path }, result.AppliedFiles.ToArray());
            Assert.AreEqual("// changed before failure", await File.ReadAllTextAsync(path, CancellationToken.None));
            Assert.AreNotEqual(prior, UndoService.GetLastOperation(scope.WorkspaceId));
            var unrelated = scope.GetPath("SampleLib", "Cat.cs");
            var beforeRefusal = await File.ReadAllBytesAsync(unrelated, CancellationToken.None);
            var untrackedEdit = new EditService(WorkspaceManager, NullLogger<EditService>.Instance);
            var refused = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                untrackedEdit.ApplyTextEditsAsync(scope.WorkspaceId, unrelated,
                    [new TextEditDto(1, 1, 1, 1, "// must not bypass recovery\n")], "apply_text_edit", CancellationToken.None));
            StringAssert.Contains(refused.PublicMessage, "Recover");
            CollectionAssert.AreEqual(beforeRefusal, await File.ReadAllBytesAsync(unrelated, CancellationToken.None));
            Assert.IsTrue(await UndoService.RevertAsync(scope.WorkspaceId, CancellationToken.None));
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path, CancellationToken.None));
            Assert.AreEqual(prior, UndoService.GetLastOperation(scope.WorkspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Prepared undo fixture cleanup failed.", primary,
                async () => { if (scope is not null) await scope.DisposeAsync(); });
        }
    }

    [TestMethod]
    public async Task InvalidBatch_RefusesBeforeAnyEffectWithPublicGuidance()
    {
        IsolatedWorkspaceScope? scope = null;
        Exception? primary = null;
        try
        {
            scope = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
            var (path, _) = await EstablishPreviousUndoAsync(scope);
            var second = scope.GetPath("SampleLib", "Cat.cs");
            var before = await File.ReadAllBytesAsync(path, CancellationToken.None);
            var secondBefore = await File.ReadAllBytesAsync(second, CancellationToken.None);
            var prior = UndoService.GetLastOperation(scope.WorkspaceId);
            var version = WorkspaceManager.GetCurrentVersion(scope.WorkspaceId);
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                EditService.ApplyMultiFileTextEditsAsync(scope.WorkspaceId,
                    [
                        new FileEditsDto(path, [new TextEditDto(1, 1, 1, 1, "// must not land\n")]),
                        new FileEditsDto(second, [new TextEditDto(1, 1, 1, 1, "class {")])
                    ], "apply_multi_file_edit", CancellationToken.None));
            StringAssert.Contains(error.PublicMessage, "syntax error");
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path, CancellationToken.None));
            CollectionAssert.AreEqual(secondBefore, await File.ReadAllBytesAsync(second, CancellationToken.None));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(scope.WorkspaceId));
            Assert.AreEqual(prior, UndoService.GetLastOperation(scope.WorkspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Prepared undo fixture cleanup failed.", primary,
                async () => { if (scope is not null) await scope.DisposeAsync(); });
        }
    }

    private static FailClosedWorkspaceManagerStub CreateRejectingManager(Action onRestorationRejected)
    {
        var recovery = (IWorkspaceRecoveryAuthority)WorkspaceManager;
        return FailClosedWorkspaceManagerStub.ForRecovery(WorkspaceManager,
            recovery.AuthorizePathsAsync, recovery.StageRestorationAsync,
            (_, _, _, _) =>
            {
                onRestorationRejected();
                return Task.FromException(new IOException("Controlled candidate refusal."));
            });
    }

    private static async Task<(string Path, byte[] Original)> EstablishPreviousUndoAsync(IsolatedWorkspaceScope scope)
    {
        var path = scope.GetPath("SampleLib", "Dog.cs");
        var original = await File.ReadAllBytesAsync(path, CancellationToken.None);
        var result = await EditService.ApplyTextEditsAsync(scope.WorkspaceId, path,
            [new TextEditDto(1, 1, 1, 1, "// prior successful operation\n")], "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(UndoService.GetLastOperation(scope.WorkspaceId));
        return (path, original);
    }
}
