using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class FailedApplyUndoTargetTests : OwnedRefactoringTestBase
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("single")]
    [DataRow("batch")]
    [DataRow("batch-syntax")]
    public async Task EditWithoutOwnedEffects_MustLeavePriorUndo(string route)
    {
        await using var fixture = CreateSingleProjectWorkspace();
        await fixture.LoadAsync();
        var id = fixture.WorkspaceId;
        var prior = await ApplyPriorAsync(id, fixture.GetPath("SampleLib", "Dog.cs"));
        var cat = fixture.GetPath("SampleLib", "Cat.cs");
        var before = await File.ReadAllBytesAsync(cat);
        var manager = new FaultingManager(Services.WorkspaceManager);
        manager.Apply = (_, _) => false;
        var edit = CreateEdit(manager, Services.CompileCheckService);
        if (route == "single")
        {
            var result = await edit.ApplyTextEditsAsync(id, cat, [Insert("// B\n")], "apply_text_edit", CancellationToken.None);
            Assert.IsFalse(result.Success);
        }
        else
        {
            var change = route == "batch-syntax" ? "}\n" : "// B\n";
            var result = await edit.ApplyMultiFileTextEditsAsync(id, [new(cat, [Insert(change)])],
                "apply_multi_file_edit", CancellationToken.None);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, result.FilesModified);
        }
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(cat));
        Assert.AreEqual(prior.AfterVersion, Services.WorkspaceManager.GetCurrentVersion(id));
        await RequirePriorUndoAsync(id, prior);
    }

    [TestMethod]
    [DataRow("single")]
    [DataRow("batch")]
    [DataRow("mixed")]
    public async Task IdenticalFailedEdit_DoesNotPublishOwnedEffects(string route)
    {
        await using var fixture = CreateSingleProjectWorkspace();
        await fixture.LoadAsync();
        var id = fixture.WorkspaceId;
        var prior = await ApplyPriorAsync(id, fixture.GetPath("SampleLib", "Dog.cs"));
        var cat = fixture.GetPath("SampleLib", "Cat.cs");
        var changed = fixture.GetPath("SampleLib", "IAnimal.cs");
        var original = await File.ReadAllBytesAsync(cat);
        var changedOriginal = await File.ReadAllBytesAsync(changed);
        var text = await GetWorkspaceTextAsync(id, cat);
        var target = Services.WorkspaceManager.GetCurrentSolution(id).Projects.Single().Documents
            .Single(document => document.FilePath == cat).Id;
        var manager = new FaultingManager(Services.WorkspaceManager);
        var applied = 0;
        manager.Apply = (workspace, modified) =>
        {
            Assert.IsTrue(Services.WorkspaceManager.TryApplyChanges(workspace, modified));
            if (++applied == 1)
                manager.NextSolutionRead = solution => solution.RemoveDocument(target);
            return true;
        };
        var edit = CreateEdit(manager, Services.CompileCheckService);
        var identical = new TextEditDto(1, 1, 1, 2, text[..1]);
        if (route == "single")
        {
            var result = await edit.ApplyTextEditsAsync(id, cat, [identical], "apply_text_edit", CancellationToken.None);
            Assert.IsFalse(result.Success);
        }
        else
        {
            var targets = new List<FileEditsDto> { new(cat, [identical]) };
            if (route == "mixed")
                targets.Add(new(changed, [Insert("// actual change\n")]));
            var result = await edit.ApplyMultiFileTextEditsAsync(id, targets, "apply_multi_file_edit", CancellationToken.None);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(route == "mixed" ? 1 : 0, result.FilesModified);
        }

        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(cat));
        Assert.AreEqual(text, await GetWorkspaceTextAsync(id, cat));
        var changes = Services.ChangeTracker.GetChanges(id);
        if (route == "mixed")
        {
            Assert.HasCount(2, changes);
            var entry = changes.Single(change => change.ToolName == "apply_multi_file_edit");
            CollectionAssert.AreEqual(new[] { changed }, entry.AffectedFiles.ToArray());
            var result = await Services.UndoService.RevertBySequenceAsync(id, entry.SequenceNumber, CancellationToken.None);
            Assert.IsTrue(result.Reverted);
            CollectionAssert.AreEqual(changedOriginal, await File.ReadAllBytesAsync(changed));
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(cat));
        }
        else
        {
            Assert.HasCount(1, changes);
            await RequirePriorUndoAsync(id, prior);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SingleEditSurvivingWorkspaceApply_RemainsUndoable(bool persistThrows)
    {
        await using var fixture = CreateSingleProjectWorkspace();
        await fixture.LoadAsync();
        var id = fixture.WorkspaceId;
        var prior = await ApplyPriorAsync(id, fixture.GetPath("SampleLib", "Dog.cs"));
        var cat = fixture.GetPath("SampleLib", "Cat.cs");
        var original = await File.ReadAllBytesAsync(cat);
        var target = Services.WorkspaceManager.GetCurrentSolution(id).Projects.SelectMany(project => project.Documents)
            .Single(document => document.FilePath == cat).Id;
        var manager = new FaultingManager(Services.WorkspaceManager);
        manager.Apply = (workspace, modified) =>
        {
            Assert.IsTrue(Services.WorkspaceManager.TryApplyChanges(workspace, modified));
            manager.NextSolutionRead = solution => persistThrows
                ? throw new OperationCanceledException("Diagnostic persistence cancellation")
                : solution.RemoveDocument(target);
            return true;
        };
        var edit = CreateEdit(manager, Services.CompileCheckService);
        var apply = () => edit.ApplyTextEditsAsync(id, cat, [Insert("// surviving B\n")], "apply_text_edit", CancellationToken.None);
        if (persistThrows)
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(apply);
        }
        else
        {
            Assert.IsFalse((await apply()).Success);
        }
        StringAssert.Contains(await Services.WorkspaceManager.GetSourceTextAsync(id, cat, CancellationToken.None),
            "// surviving B");
        await RequireRecoveryAsync(id, prior, cat, original, "Apply text edit");
    }

    [TestMethod]
    [DataRow("mixed")]
    [DataRow("throw")]
    [DataRow("cancel")]
    public async Task BatchWithSurvivingLeg_RemainsUndoable(string outcome)
    {
        await using var fixture = CreateSingleProjectWorkspace();
        var later = fixture.GetPath("SampleLib", "UndoWitnessLater.cs");
        await File.WriteAllTextAsync(later, "namespace SampleLib; public class UndoWitnessLater { }\n");
        await fixture.LoadAsync();
        var id = fixture.WorkspaceId;
        var prior = await ApplyPriorAsync(id, fixture.GetPath("SampleLib", "Dog.cs"));
        var cat = fixture.GetPath("SampleLib", "Cat.cs");
        var original = await File.ReadAllBytesAsync(cat);
        var laterBefore = await File.ReadAllBytesAsync(later);
        var manager = new FaultingManager(Services.WorkspaceManager);
        var issued = 0;
        manager.Apply = (workspace, modified) =>
        {
            if (++issued == 1)
            {
                return Services.WorkspaceManager.TryApplyChanges(workspace, modified);
            }
            if (outcome == "cancel")
            {
                throw new OperationCanceledException("Diagnostic cancellation after first batch leg");
            }
            throw new InvalidOperationException("Diagnostic failure after first batch leg");
        };
        var edit = CreateEdit(manager, Services.CompileCheckService);
        var apply = () => edit.ApplyMultiFileTextEditsAsync(id,
            [new(cat, [Insert("// surviving batch B\n")]), new(later, [Insert(outcome == "mixed" ? "}\n" : "// later B\n")])],
            "apply_multi_file_edit", CancellationToken.None);
        if (outcome == "mixed")
        {
            var result = await apply();
            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, result.FilesModified);
        }
        else if (outcome == "cancel")
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(apply);
        }
        else
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(apply);
        }
        CollectionAssert.AreNotEqual(original, await File.ReadAllBytesAsync(cat));
        CollectionAssert.AreEqual(laterBefore, await File.ReadAllBytesAsync(later));
        await RequireRecoveryAsync(id, prior, cat, original, "Apply edits to 2 file(s)");
    }

    [TestMethod]
    public async Task BatchAutoRevertWithNoEffects_LeavesPriorBytes()
    {
        await using var fixture = CreateSingleProjectWorkspace();
        await fixture.LoadAsync();
        var id = fixture.WorkspaceId;
        var prior = await ApplyPriorAsync(id, fixture.GetPath("SampleLib", "Dog.cs"));
        var cat = fixture.GetPath("SampleLib", "Cat.cs");
        var before = await File.ReadAllBytesAsync(cat);
        var probe = new AppearingCompileError(cat);
        var edit = CreateEdit(Services.WorkspaceManager, probe);
        var result = await edit.ApplyMultiFileTextEditsAsync(id, [new(cat, [Insert("}\n")])],
            "apply_multi_file_edit", CancellationToken.None, verify: true, autoRevertOnError: true);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.FilesModified);
        Assert.AreEqual(2, probe.Calls);
        Assert.IsNotNull(result.Verification);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(cat));
        CollectionAssert.AreEqual(prior.Applied, await File.ReadAllBytesAsync(prior.Path));
        Assert.AreEqual("revert_failed", result.Verification.Status);
        await RequirePriorUndoAsync(id, prior);
    }

    [TestMethod]
    public async Task ProjectReplacementBeforePublication_MustLeavePriorUndo()
    {
        await using var fixture = CreateSingleProjectWorkspace();
        await fixture.LoadAsync();
        var id = fixture.WorkspaceId;
        var prior = await ApplyPriorAsync(id, fixture.GetPath("SampleLib", "Dog.cs"));
        var target = fixture.GetPath("SampleLib", "BlockedMutation.csproj");
        Directory.CreateDirectory(target);
        var service = CreateProjectMutation(Services.WorkspaceManager, out var previews);
        var token = previews.Store(id, target, "<Project />", Services.WorkspaceManager.GetCurrentVersion(id), "B");
        var result = await service.ApplyProjectMutationAsync(token, CancellationToken.None);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("Failed to apply project mutation.", result.Error);
        Assert.IsTrue(Directory.Exists(target));
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(target).Any());
        Assert.AreEqual(prior.AfterVersion, Services.WorkspaceManager.GetCurrentVersion(id));
        await RequirePriorUndoAsync(id, prior);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProjectPublicationThenReloadFailure_RemainsUndoable(bool cancel)
    {
        await using var fixture = CreateSingleProjectWorkspace();
        await fixture.LoadAsync();
        var id = fixture.WorkspaceId;
        var prior = await ApplyPriorAsync(id, fixture.GetPath("SampleLib", "Dog.cs"));
        var project = Services.WorkspaceManager.GetCurrentSolution(id).Projects.Single().FilePath!;
        var original = await File.ReadAllBytesAsync(project);
        var content = (await File.ReadAllTextAsync(project)).Replace("</Project>", "  <!-- B -->\n</Project>", StringComparison.Ordinal);
        var manager = new FaultingManager(Services.WorkspaceManager);
        manager.Reload = _ => cancel
            ? throw new OperationCanceledException("Diagnostic cancellation after verified replacement")
            : throw new IOException("Diagnostic reload failure after verified replacement");
        var service = CreateProjectMutation(manager, out var previews);
        var token = previews.Store(id, project, content, Services.WorkspaceManager.GetCurrentVersion(id), "B");
        if (cancel)
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.ApplyProjectMutationAsync(token, CancellationToken.None));
        }
        else
        {
            var result = await service.ApplyProjectMutationAsync(token, CancellationToken.None);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("Failed to apply project mutation.", result.Error);
        }
        StringAssert.Contains(await File.ReadAllTextAsync(project), "<!-- B -->");
        await RequireRecoveryAsync(id, prior, project, original, "Project mutation");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RefactoringWorkspaceRejectionRestoresBytes_MustLeavePriorUndo(bool reloadFails)
    {
        await using var fixture = CreateSingleProjectWorkspace();
        await fixture.LoadAsync();
        var id = fixture.WorkspaceId;
        var prior = await ApplyPriorAsync(id, fixture.GetPath("SampleLib", "Dog.cs"));
        var cat = fixture.GetPath("SampleLib", "Cat.cs");
        var original = await File.ReadAllBytesAsync(cat);
        var manager = new FaultingManager(Services.WorkspaceManager)
        {
            Apply = (_, _) => false,
            Reload = reloadFails ? _ => throw new IOException("Diagnostic compensating reload failure") : null,
        };
        var beforeText = await GetWorkspaceTextAsync(id, cat);
        var beforeProject = Services.WorkspaceManager.GetCurrentSolution(id).Projects.Single();
        var beforeVersion = Services.WorkspaceManager.GetCurrentVersion(id);
        var (service, token) = await CreateRefactoringAsync(manager, id, [cat], fileSystem: null);
        if (reloadFails)
        {
            var failure = await Assert.ThrowsExactlyAsync<AggregateException>(() =>
                service.ApplyRefactoringAsync(token, "rename_apply", CancellationToken.None));
            Assert.IsTrue(failure.InnerExceptions.Any(exception => exception.Message.Contains("compensating reload", StringComparison.Ordinal)));
        }
        else
        {
            var result = await service.ApplyRefactoringAsync(token, "rename_apply", CancellationToken.None);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("Failed to apply changes to the workspace.", result.Error);
        }
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(cat));
        Assert.AreEqual(beforeText, await GetWorkspaceTextAsync(id, cat));
        if (reloadFails)
        {
            Assert.AreEqual(beforeVersion, Services.WorkspaceManager.GetCurrentVersion(id));
            var afterProject = Services.WorkspaceManager.GetCurrentSolution(id).Projects.Single();
            CollectionAssert.AreEqual(beforeProject.DocumentIds.ToArray(), afterProject.DocumentIds.ToArray());
            CollectionAssert.AreEqual(beforeProject.ProjectReferences.ToArray(), afterProject.ProjectReferences.ToArray());
            CollectionAssert.AreEqual(beforeProject.MetadataReferences.Select(reference => reference.Display).ToArray(),
                afterProject.MetadataReferences.Select(reference => reference.Display).ToArray());
            TestContext.WriteLine($"Rejected apply and failed reload left disk, live text and references unchanged at version {beforeVersion}.");
        }
        await RequirePriorUndoAsync(id, prior);
    }

    [TestMethod]
    public async Task RefactoringAcceptedApplyAndFailedReload_MustRecoverWorkspaceOnly()
    {
        await using var fixture = CreateSingleProjectWorkspace();
        await fixture.LoadAsync();
        var id = fixture.WorkspaceId;
        var prior = await ApplyPriorAsync(id, fixture.GetPath("SampleLib", "Dog.cs"));
        var project = Services.WorkspaceManager.GetCurrentSolution(id).Projects.Single();
        var projectPath = project.FilePath!;
        var projectBytes = await File.ReadAllBytesAsync(projectPath);
        var cat = fixture.GetPath("SampleLib", "Cat.cs");
        var catBytes = await File.ReadAllBytesAsync(cat);
        var catText = await GetWorkspaceTextAsync(id, cat);
        var addedPath = fixture.GetPath("SampleLib", "WitnessAdded.cs");
        var added = DocumentId.CreateNewId(project.Id);
        var accepted = false;
        var blockReload = true;
        var manager = new FaultingManager(Services.WorkspaceManager)
        {
            Apply = (workspaceId, solution) => accepted = Services.WorkspaceManager.TryApplyChanges(workspaceId, solution),
            Reload = workspaceId => blockReload
                ? throw new IOException("Diagnostic compensating reload failure")
                : Services.WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None),
        };
        using var recovery = new UndoService(NullLogger<UndoService>.Instance, manager);
        var files = new DiagnosticFileSystem("project") { ProjectPath = projectPath };
        var (service, token) = await CreateRefactoringAsync(manager, id, [cat], files,
            solution => solution.AddDocument(added, "WitnessAdded.cs", SourceText.From("class WitnessAdded {}"), filePath: addedPath), recovery);
        var error = await Assert.ThrowsExactlyAsync<AggregateException>(() =>
            service.ApplyRefactoringAsync(token, "rename_apply", CancellationToken.None));

        Assert.IsTrue(accepted, "This witness must reach real workspace acceptance.");
        Assert.IsTrue(files.ProjectFault, "The production SDK project restore must reach the injected file-write fault.");
        Assert.IsTrue(error.InnerExceptions.Any(exception => exception.Message.Contains("compensating reload", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(catBytes, await File.ReadAllBytesAsync(cat));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        Assert.IsFalse(File.Exists(addedPath));
        Assert.IsNotNull(Services.WorkspaceManager.GetCurrentSolution(id).GetDocument(added));
        StringAssert.Contains(await GetWorkspaceTextAsync(id, cat), "// refactoring B");
        TestContext.WriteLine("Real workspace accepted B, disk was restored, and failed reload left B visible in the workspace.");

        // A disk-only change after compensation distinguishes reload from replaying B's pre-images.
        var external = "// external after rollback\n" + catText;
        await File.WriteAllTextAsync(cat, external);
        Assert.IsFalse(await recovery.RevertAsync(id), "The unchanged reload failure must leave recovery retryable.");
        Assert.AreEqual("Refactoring B", recovery.GetLastOperation(id)?.Description);
        Assert.AreEqual(external, await File.ReadAllTextAsync(cat));
        Assert.IsNotNull(Services.WorkspaceManager.GetCurrentSolution(id).GetDocument(added));

        blockReload = false;
        var undo = await ObserveUndoAsync(id, prior, recovery);
        Assert.IsTrue(undo.Reverted);
        Assert.AreEqual("Refactoring B", undo.Description);
        Assert.IsGreaterThan(prior.AfterVersion, undo.Version);
        Assert.IsNull(Services.WorkspaceManager.GetCurrentSolution(id).GetDocument(added));
        Assert.AreEqual(external, await File.ReadAllTextAsync(cat), "Reload-only undo must preserve the disk source.");
        Assert.AreEqual(external, await GetWorkspaceTextAsync(id, cat));
    }

    [TestMethod]
    [DataRow("owned")]
    [DataRow("prefix")]
    [DataRow("reload-foreign")]
    [DataRow("foreign")]
    [DataRow("mixed-ownership")]
    [DataRow("unreadable")]
    [DataRow("cancel")]
    public async Task RefactoringFailure_LeavesOnlyItsRecordedOwnedEffectsRecoverable(string outcome)
    {
        await using var fixture = CreateSingleProjectWorkspace();
        var extra = fixture.GetPath("SampleLib", "UndoWitnessExtra.cs");
        await File.WriteAllTextAsync(extra, "namespace SampleLib; public class UndoWitnessExtra { }\n");
        var last = fixture.GetPath("SampleLib", "UndoWitnessLast.cs");
        if (outcome is "mixed-ownership" or "unreadable")
        {
            await File.WriteAllTextAsync(last, "namespace SampleLib; public class UndoWitnessLast { }\n");
        }
        await fixture.LoadAsync();
        var id = fixture.WorkspaceId;
        var prior = await ApplyPriorAsync(id, fixture.GetPath("SampleLib", "Dog.cs"));
        var cat = fixture.GetPath("SampleLib", "Cat.cs");
        var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [cat] = await File.ReadAllBytesAsync(cat),
            [extra] = await File.ReadAllBytesAsync(extra),
        };
        if (outcome is "mixed-ownership" or "unreadable")
        {
            originals.Add(last, await File.ReadAllBytesAsync(last));
        }
        var files = new DiagnosticFileSystem(outcome);
        IWorkspaceManager manager = Services.WorkspaceManager;
        var cache = new CompilationCache(manager);
        var beforeCompilation = outcome == "mixed-ownership"
            ? await cache.GetCompilationAsync(id, manager.GetCurrentSolution(id).Projects.Single(), CancellationToken.None)
            : null;
        if (outcome == "reload-foreign")
        {
            manager = new FaultingManager(manager)
            {
                Reload = async workspaceId =>
                {
                    Assert.IsNotNull(files.FirstPath);
                    await File.AppendAllTextAsync(files.FirstPath, "// independent write during reload\n");
                    return await Services.WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None);
                },
            };
        }
        var (service, token) = await CreateRefactoringAsync(manager, id, originals.Keys.ToArray(), files);
        var apply = () => service.ApplyRefactoringAsync(token, "rename_apply", CancellationToken.None);
        if (outcome == "cancel")
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(apply);
        }
        else
        {
            var error = await Assert.ThrowsExactlyAsync<AggregateException>(apply);
            if (outcome == "mixed-ownership")
            {
                Assert.IsTrue(error.InnerExceptions.Any(exception => exception.Message.Contains("Rollback preserved", StringComparison.Ordinal)));
                Assert.IsTrue(error.InnerExceptions.Any(exception => exception.Message.Contains("restoring the first owned", StringComparison.Ordinal)));
            }
            TestContext.WriteLine($"Refactoring {outcome} outcome: {string.Join(" | ", error.InnerExceptions.Select(exception => exception.Message))}");
        }
        Assert.IsNotNull(files.FirstPath);
        Assert.IsNotNull(files.RejectedPath);
        CollectionAssert.AreEqual(originals[files.RejectedPath], await File.ReadAllBytesAsync(files.RejectedPath));
        if (outcome is "owned" or "prefix")
        {
            var survivingText = await File.ReadAllTextAsync(files.FirstPath);
            var undo = await ObserveUndoAsync(id, prior);
            Assert.AreEqual("Refactoring B", undo.Description,
                $"A failed compensating write must leave an owned recovery target. Observed disk text: {survivingText}");
            Assert.IsTrue(undo.Reverted);
            CollectionAssert.AreEqual(originals[files.FirstPath], await File.ReadAllBytesAsync(files.FirstPath));
            CollectionAssert.AreEqual(prior.Applied, await File.ReadAllBytesAsync(prior.Path));
            Assert.AreEqual(prior.AfterVersion, undo.Version);
            StringAssert.Contains(survivingText, "// refactoring B");
        }
        else if (outcome == "reload-foreign")
        {
            var foreign = await File.ReadAllBytesAsync(files.FirstPath);
            StringAssert.Contains(await File.ReadAllTextAsync(files.FirstPath), "// independent write during reload");
            await RequirePriorUndoAsync(id, prior);
            CollectionAssert.AreEqual(foreign, await File.ReadAllBytesAsync(files.FirstPath));
        }
        else if (outcome == "unreadable")
        {
            Assert.IsNotNull(files.SecondPath);
            var unreadableDuringRollback = await File.ReadAllBytesAsync(files.FirstPath);
            StringAssert.Contains(await File.ReadAllTextAsync(files.FirstPath), "// refactoring B");
            StringAssert.Contains(await File.ReadAllTextAsync(files.SecondPath), "// refactoring B");
            var versionBeforeUndo = Services.WorkspaceManager.GetCurrentVersion(id);
            var undo = await ObserveUndoAsync(id, prior);
            Assert.IsTrue(undo.Reverted);
            Assert.AreEqual("Refactoring B", undo.Description);
            CollectionAssert.AreEqual(unreadableDuringRollback, await File.ReadAllBytesAsync(files.FirstPath));
            CollectionAssert.AreEqual(originals[files.SecondPath], await File.ReadAllBytesAsync(files.SecondPath));
            Assert.IsGreaterThan(versionBeforeUndo, undo.Version);
        }
        else if (outcome == "mixed-ownership")
        {
            Assert.IsNotNull(files.SecondPath);
            var foreign = await File.ReadAllBytesAsync(files.SecondPath);
            StringAssert.Contains(await File.ReadAllTextAsync(files.SecondPath), "// external edit");
            StringAssert.Contains(await File.ReadAllTextAsync(files.FirstPath), "// refactoring B");
            var versionBeforeUndo = Services.WorkspaceManager.GetCurrentVersion(id);
            var undo = await ObserveUndoAsync(id, prior);
            Assert.IsTrue(undo.Reverted);
            Assert.AreEqual("Refactoring B", undo.Description);
            CollectionAssert.AreEqual(originals[files.FirstPath], await File.ReadAllBytesAsync(files.FirstPath));
            CollectionAssert.AreEqual(prior.Applied, await File.ReadAllBytesAsync(prior.Path));
            CollectionAssert.AreEqual(foreign, await File.ReadAllBytesAsync(files.SecondPath));
            Assert.IsGreaterThanOrEqualTo(versionBeforeUndo, undo.Version, "Undo over unrestored foreign state should not resurrect older versions.");
            var fresh = await cache.GetCompilationAsync(id, manager.GetCurrentSolution(id).Projects.Single(), CancellationToken.None);
            Assert.IsNotNull(fresh);
            Assert.AreNotSame(beforeCompilation, fresh);
            var tree = fresh.SyntaxTrees.Single(candidate => candidate.FilePath == files.SecondPath);
            StringAssert.Contains((await tree.GetTextAsync()).ToString(), "// external edit");
        }
        else if (outcome == "foreign")
        {
            var foreign = await File.ReadAllBytesAsync(files.FirstPath);
            StringAssert.Contains(await File.ReadAllTextAsync(files.FirstPath), "// external edit");
            await ObserveUndoAsync(id, prior);
            CollectionAssert.AreEqual(foreign, await File.ReadAllBytesAsync(files.FirstPath),
                "Undo must not restore a pre-image over a path that rollback identified as foreign.");
        }
        else
        {
            CollectionAssert.AreEqual(originals[files.FirstPath], await File.ReadAllBytesAsync(files.FirstPath));
            await RequirePriorUndoAsync(id, prior);
        }
    }

    private async Task<string> GetWorkspaceTextAsync(string id, string path)
    {
        var document = Services.WorkspaceManager.GetCurrentSolution(id).Projects.SelectMany(project => project.Documents)
            .Single(candidate => candidate.FilePath == path);
        return (await document.GetTextAsync()).ToString();
    }

    private async Task<PriorApply> ApplyPriorAsync(string id, string path)
    {
        var original = await File.ReadAllBytesAsync(path);
        var version = Services.WorkspaceManager.GetCurrentVersion(id);
        var result = await Services.EditService.ApplyTextEditsAsync(id, path, [Insert("// prior successful A\n")],
            "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(result.Success);
        var applied = await File.ReadAllBytesAsync(path);
        CollectionAssert.AreNotEqual(original, applied);
        var afterVersion = Services.WorkspaceManager.GetCurrentVersion(id);
        Assert.IsGreaterThan(version, afterVersion);
        return new(path, original, applied, version, afterVersion);
    }

    private async Task<(bool Reverted, string? Description, int Version)> ObserveUndoAsync(
        string id, PriorApply prior, IUndoService? undo = null)
    {
        undo ??= Services.UndoService;
        var target = undo.GetLastOperation(id)?.Description;
        var before = Services.WorkspaceManager.GetCurrentVersion(id);
        var result = await undo.RevertAsync(id, CancellationToken.None);
        var restored = Services.WorkspaceManager.GetCurrentVersion(id);
        TestContext.WriteLine($"A version {prior.BeforeVersion}->{prior.AfterVersion}; LIFO target {target ?? "<empty>"}; reverted={result}; workspace version {before}->{restored}.");
        return (result, target, restored);
    }

    private async Task RequirePriorUndoAsync(string id, PriorApply prior)
    {
        var outcome = await ObserveUndoAsync(id, prior);
        CollectionAssert.AreEqual(prior.Original, await File.ReadAllBytesAsync(prior.Path),
            $"LIFO selected '{outcome.Description}' and did not restore A. Reverted={outcome.Reverted}; restored version={outcome.Version}; A pre-image version={prior.BeforeVersion}.");
        Assert.IsTrue(outcome.Reverted);
        Assert.AreEqual(prior.BeforeVersion, outcome.Version);
    }

    private async Task RequireRecoveryAsync(string id, PriorApply prior, string path, byte[] original, string description)
    {
        var outcome = await ObserveUndoAsync(id, prior);
        Assert.IsTrue(outcome.Reverted);
        StringAssert.Contains(outcome.Description, description);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
        CollectionAssert.AreEqual(prior.Applied, await File.ReadAllBytesAsync(prior.Path));
        Assert.AreEqual(prior.AfterVersion, outcome.Version);
    }

    private EditService CreateEdit(IWorkspaceManager manager, ICompileCheckService compile) =>
        new(manager, NullLogger<EditService>.Instance, Services.UndoService, Services.ChangeTracker, Services.PreviewStore, compile);

    private ProjectMutationService CreateProjectMutation(IWorkspaceManager manager, out ProjectMutationPreviewStore previews)
    {
        previews = new ProjectMutationPreviewStore();
        return new(manager, previews, Services.MsBuildEvaluationService, NullLogger<ProjectMutationService>.Instance,
            Services.ChangeTracker, Services.UndoService);
    }

    private async Task<(RefactoringService Service, string Token)> CreateRefactoringAsync(
        IWorkspaceManager manager, string id, IReadOnlyList<string> paths, IDocumentSetFileSystem? fileSystem,
        Func<Solution, Solution>? modify = null, IUndoService? undo = null)
    {
        var before = Services.WorkspaceManager.GetCurrentSolution(id);
        var modified = before;
        foreach (var path in paths)
        {
            var document = before.Projects.SelectMany(project => project.Documents).Single(candidate => candidate.FilePath == path);
            var text = await document.GetTextAsync();
            modified = modified.WithDocumentText(document.Id, text.WithChanges(new TextChange(new TextSpan(0, 0), "// refactoring B\n")));
        }
        var previews = new PreviewStore();
        modified = modify?.Invoke(modified) ?? modified;
        var token = previews.Store(id, before, modified, Services.WorkspaceManager.GetCurrentVersion(id), "Refactoring B", diffTruncated: false);
        var service = new RefactoringService(manager, previews, NullLogger<RefactoringService>.Instance,
            undo ?? Services.UndoService, Services.ChangeTracker);
        if (fileSystem is not null)
        {
            var seam = typeof(RefactoringService).GetField("_documentSetPersistence", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(seam);
            seam.SetValue(service, new DocumentSetPersistenceService(manager, NullLogger.Instance, fileSystem));
        }
        return (service, token);
    }

    private static TextEditDto Insert(string text) => new(1, 1, 1, 1, text);

    private sealed record PriorApply(string Path, byte[] Original, byte[] Applied, int BeforeVersion, int AfterVersion);

    private sealed class FaultingManager(IWorkspaceManager inner) : DelegatingWorkspaceManager(inner), IWorkspaceReloader
    {
        public Func<string, Solution, bool>? Apply { get; set; }
        public Func<Solution, Solution>? NextSolutionRead { get; set; }
        public Func<string, Task<WorkspaceStatusDto>>? Reload { get; set; }

        public override bool TryApplyChanges(string workspaceId, Solution newSolution) =>
            Apply?.Invoke(workspaceId, newSolution) ?? base.TryApplyChanges(workspaceId, newSolution);

        public override Solution GetCurrentSolution(string workspaceId)
        {
            var solution = base.GetCurrentSolution(workspaceId);
            var transform = NextSolutionRead;
            NextSolutionRead = null;
            return transform?.Invoke(solution) ?? solution;
        }

        Task<WorkspaceStatusDto> IWorkspaceReloader.ReloadAsync(string workspaceId, CancellationToken ct) =>
            Reload?.Invoke(workspaceId) ?? base.ReloadAsync(workspaceId, ct);
    }

    private sealed class AppearingCompileError(string path) : ICompileCheckService
    {
        public int Calls { get; private set; }

        public Task<CompileCheckDto> CheckAsync(string workspaceId, CompileCheckOptions options, CancellationToken ct)
        {
            DiagnosticDto[] diagnostics = ++Calls == 1 ? [] : [new("CS0000", "Diagnostic control", "error", "Compiler", path)];
            return Task.FromResult(new CompileCheckDto(diagnostics.Length == 0, diagnostics.Length, 0,
                diagnostics.Length, diagnostics.Length, 0, 100, false, diagnostics, 0, CompletedProjects: 1, TotalProjects: 1));
        }
    }

    private sealed class DiagnosticFileSystem(string outcome) : IDocumentSetFileSystem
    {
        public string? FirstPath { get; private set; }
        public string? RejectedPath { get; private set; }
        public string? SecondPath { get; private set; }
        public string? ProjectPath { get; init; }
        public bool ProjectFault { get; private set; }
        public bool FileExists(string path) => File.Exists(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void DeleteFile(string path) => File.Delete(path);
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct)
        {
            if (outcome == "unreadable" && RejectedPath is not null && path == FirstPath)
                throw new IOException("Diagnostic sharing failure on the first rollback read");
            return File.ReadAllBytesAsync(path, ct);
        }
        public Task<string> ReadAllTextAsync(string path, CancellationToken ct) => File.ReadAllTextAsync(path, ct);

        public Task RestoreBytesAtomicallyAsync(string path, byte[] bytes, bool expectedExists, byte[]? expectedBytes,
            Action ownershipLost, CancellationToken ct)
        {
            if (outcome == "prefix" && path == FirstPath)
            {
                return AtomicFileWriter.WriteGuardedReplacementAsync(path, async (_, stream) =>
                {
                    await stream.WriteAsync(bytes.AsMemory(0, 5), ct);
                    throw new IOException("Diagnostic rollback fault after writing a temp prefix");
                }, () => true, access: null, Task.Delay, ct);
            }
            return WriteAllBytesAsync(path, bytes, ct);
        }

        public async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct)
        {
            if (outcome == "prefix" && path == FirstPath)
            {
                await File.WriteAllBytesAsync(path, bytes[..5], ct);
                throw new IOException("Diagnostic rollback fault after truncating the destination");
            }
            if (outcome == "project" && path == ProjectPath && !ProjectFault)
            {
                ProjectFault = true;
                throw new IOException("Diagnostic SDK project restore failed");
            }
            if (outcome == "unreadable" && path == SecondPath)
                throw new IOException("Diagnostic failure restoring the second owned document");
            if ((outcome is "owned" or "mixed-ownership" or "reload-foreign") && path == FirstPath)
            {
                throw new IOException("Diagnostic failure restoring the first owned document");
            }
            await File.WriteAllBytesAsync(path, bytes, ct);
        }

        public async Task WriteAllTextAsync(string path, string content, CancellationToken ct)
        {
            if (outcome == "project")
            {
                await File.WriteAllTextAsync(path, content, ct);
                return;
            }
            if (FirstPath is null)
            {
                FirstPath = path;
                await File.WriteAllTextAsync(path, content, ct);
                return;
            }
            if ((outcome is "mixed-ownership" or "unreadable") && SecondPath is null)
            {
                SecondPath = path;
                await File.WriteAllTextAsync(path, content, ct);
                return;
            }
            RejectedPath = path;
            if (outcome == "mixed-ownership")
            {
                await File.WriteAllTextAsync(SecondPath!, "// external edit\n" + await File.ReadAllTextAsync(SecondPath!, ct), ct);
            }
            if (outcome == "foreign")
            {
                await File.WriteAllTextAsync(FirstPath, "// external edit\n" + await File.ReadAllTextAsync(FirstPath, ct), ct);
            }
            if (outcome == "cancel")
            {
                throw new OperationCanceledException("Diagnostic cancellation after the first document write");
            }
            throw new IOException("Diagnostic failure before the second document write");
        }
    }
}
