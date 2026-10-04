using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class FileOperationIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Create_File_Preview_And_Apply_Adds_File_To_Isolated_Workspace_Copy()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var newFilePath = workspace.GetPath("SampleLib", "Generated", "Bird.cs");

        var preview = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(
                "SampleLib",
                newFilePath,
                "namespace SampleLib.Generated;\n\npublic sealed class Bird\n{\n}\n"),
            CancellationToken.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));
        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, newFilePath, StringComparison.OrdinalIgnoreCase)));

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(newFilePath));
        StringAssert.Contains(await File.ReadAllTextAsync(newFilePath, CancellationToken.None), "class Bird");

        var document = SymbolResolver.FindDocument(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), newFilePath);
        Assert.IsNotNull(document, "Created document should be present after reload.");
    }

    [TestMethod]
    public async Task Delete_File_Preview_And_Apply_Removes_File_From_Isolated_Workspace_Copy()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib", "Cat.cs");

        var preview = await FileOperationService.PreviewDeleteFileAsync(
            workspace.WorkspaceId,
            new DeleteFileDto(targetFilePath),
            CancellationToken.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));
        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, targetFilePath, StringComparison.OrdinalIgnoreCase)));

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsFalse(File.Exists(targetFilePath));
        Assert.IsNull(SymbolResolver.FindDocument(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), targetFilePath));
    }

    /// <summary>
    /// Create-file previews survive one workspace-version bump, but two reloads exceed
    /// <see cref="PreviewStore.DefaultMaxVersionSpan"/> and reject the token without writing
    /// the requested file. This retains producer-specific stale-token and no-side-effect coverage.
    /// </summary>
    [TestMethod]
    public async Task File_Operation_Preview_Token_Is_Rejected_After_Two_Reloads()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var newFilePath = workspace.GetPath("SampleLib", "Generated", "StaleBird.cs");

        var preview = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(
                "SampleLib",
                newFilePath,
                "namespace SampleLib.Generated;\n\npublic sealed class StaleBird { }\n"),
            CancellationToken.None);

        await workspace.ReloadAsync();
        await workspace.ReloadAsync();

        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken,
            "test_apply",
            CancellationToken.None);

        Assert.IsFalse(applyResult.Success, "Stale create-file previews must be rejected.");
        StringAssert.Contains(applyResult.Error ?? string.Empty, "stale");
        Assert.IsFalse(File.Exists(newFilePath), "A rejected stale create preview must not touch disk.");
    }

    [TestMethod]
    public async Task Move_File_Preview_And_Apply_Updates_Isolated_Workspace_Copy()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var sourceFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var destinationFilePath = workspace.GetPath("SampleLib", "Animals", "Dog.cs");

        var preview = await FileOperationService.PreviewMoveFileAsync(
            workspace.WorkspaceId,
            new MoveFileDto(sourceFilePath, destinationFilePath, null, UpdateNamespace: true),
            CancellationToken.None);

        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, destinationFilePath, StringComparison.OrdinalIgnoreCase)));

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsFalse(File.Exists(sourceFilePath));
        Assert.IsTrue(File.Exists(destinationFilePath));
        var movedContents = await File.ReadAllTextAsync(destinationFilePath, CancellationToken.None);
        StringAssert.Contains(movedContents, "namespace SampleLib.Animals");
    }


    [TestMethod]
    [DataRow("create-existing")]
    [DataRow("delete-missing")]
    [DataRow("move-missing")]
    [DataRow("move-existing")]
    [DataRow("unknown-project")]
    [DataRow("identical")]
    [DataRow("create-prefix")]
    [DataRow("move-prefix")]
    [DataRow("create-traversal")]
    [DataRow("move-traversal")]
    [DataRow("create-root")]
    [DataRow("move-root")]
    public async Task File_Refusals_Return_Safe_Actionable_Envelopes_Without_Mutations(string scenario)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var source = workspace.GetPath("SampleLib", "Dog.cs");
        var missing = workspace.GetPath("SampleLib", "hostile-private-name.cs");
        var existing = workspace.GetPath("SampleLib", "Cat.cs");
        var destination = workspace.GetPath("SampleLib", "SafeNew.cs");
        var outside = scenario.EndsWith("prefix", StringComparison.Ordinal) ? workspace.GetPath("SampleLib2", "Escape.cs")
            : scenario.EndsWith("traversal", StringComparison.Ordinal) ? workspace.GetPath("SampleLib", "..", "Escape.cs")
            : workspace.GetPath("SampleLib");
        var originalSolution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var originalContent = await File.ReadAllTextAsync(source);
        Func<Task> act = scenario switch
        {
            "create-existing" => () => FileOperationService.PreviewCreateFileAsync(workspace.WorkspaceId, new CreateFileDto("SampleLib", existing, ""), CancellationToken.None),
            "delete-missing" => () => FileOperationService.PreviewDeleteFileAsync(workspace.WorkspaceId, new DeleteFileDto(missing), CancellationToken.None),
            "move-missing" => () => FileOperationService.PreviewMoveFileAsync(workspace.WorkspaceId, new MoveFileDto(missing, destination, null, false), CancellationToken.None),
            "move-existing" => () => FileOperationService.PreviewMoveFileAsync(workspace.WorkspaceId, new MoveFileDto(source, existing, null, false), CancellationToken.None),
            "unknown-project" => () => FileOperationService.PreviewCreateFileAsync(workspace.WorkspaceId, new CreateFileDto("hostile-project-input", destination, ""), CancellationToken.None),
            "identical" => () => FileOperationService.PreviewMoveFileAsync(workspace.WorkspaceId, new MoveFileDto(source, source, null, false), CancellationToken.None),
            _ when scenario.StartsWith("create-", StringComparison.Ordinal) => () => FileOperationService.PreviewCreateFileAsync(workspace.WorkspaceId, new CreateFileDto("SampleLib", outside, ""), CancellationToken.None),
            _ => () => FileOperationService.PreviewMoveFileAsync(workspace.WorkspaceId, new MoveFileDto(source, outside, null, false), CancellationToken.None)
        };
        var guidance = scenario switch
        {
            "create-existing" or "move-existing" => "already exists",
            "delete-missing" or "move-missing" => "loaded workspace",
            "unknown-project" => "workspace_status",
            "identical" => "different",
            _ => "project directory"
        };
        var parameter = scenario switch
        {
            "identical" or "move-prefix" or "move-traversal" or "move-root" => "targetFilePath",
            "create-prefix" or "create-traversal" or "create-root" => "filePath",
            _ => null
        };
        var error = await Assert.ThrowsAsync<Exception>(act);
        AssertSafeRefusal(error, guidance, parameter, workspace.RootPath, "hostile-project-input", "hostile-private-name");
        Assert.AreSame(originalSolution, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        Assert.IsFalse(File.Exists(destination));
        Assert.AreEqual(originalContent, await File.ReadAllTextAsync(source));
        AssertNoStoredPreviews(PreviewStore, workspace.WorkspaceId);
    }

    [TestMethod]
    public async Task Malformed_File_Arguments_Name_Their_Wire_Parameter_Without_Leaking_Input()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var source = workspace.GetPath("SampleLib", "Dog.cs");
        var malformed = new List<string> { "", " ", ".", "..", "private\0input.cs",
            workspace.GetPath("SampleLib") + Path.DirectorySeparatorChar };
        if (OperatingSystem.IsWindows())
        {
            malformed.Add(workspace.GetPath("SampleLib", "private*.cs"));
            malformed.Add(workspace.GetPath("SampleLib", "private*.cs", "Child.cs"));
            malformed.Add(workspace.GetPath("SampleLib", "bad?folder", "Private.cs"));
        }
        foreach (var input in malformed)
        {
            var create = await Assert.ThrowsAsync<ArgumentException>(() => FileOperationService.PreviewCreateFileAsync(
                workspace.WorkspaceId, new CreateFileDto("SampleLib", input, ""), CancellationToken.None));
            AssertSafeRefusal(create, "file", "filePath", "private", workspace.RootPath);
            var delete = await Assert.ThrowsAsync<ArgumentException>(() => FileOperationService.PreviewDeleteFileAsync(
                workspace.WorkspaceId, new DeleteFileDto(input), CancellationToken.None));
            AssertSafeRefusal(delete, "file", "filePath", "private", workspace.RootPath);
            var moveSource = await Assert.ThrowsAsync<ArgumentException>(() => FileOperationService.PreviewMoveFileAsync(
                workspace.WorkspaceId, new MoveFileDto(input, source, null, false), CancellationToken.None));
            AssertSafeRefusal(moveSource, "file", "sourceFilePath", "private", workspace.RootPath);
            var moveTarget = await Assert.ThrowsAsync<ArgumentException>(() => FileOperationService.PreviewMoveFileAsync(
                workspace.WorkspaceId, new MoveFileDto(source, input, null, false), CancellationToken.None));
            AssertSafeRefusal(moveTarget, "file", "targetFilePath", "private", workspace.RootPath);
            AssertNoStoredPreviews(PreviewStore, workspace.WorkspaceId);
        }
    }

    internal static void AssertSafeRefusal(Exception error, string guidance, string? parameter, params string[] hidden)
    {
        var json = ToolErrorHandler.ClassifyAndFormat(error, "refusal_test");
        using var envelope = JsonDocument.Parse(json);
        var root = envelope.RootElement;
        Assert.IsTrue(StructuredCallToolFilter.BuildErrorResult("refusal_test", error).IsError);
        Assert.AreEqual(parameter is null ? "InvalidOperation" : "InvalidArgument", root.GetProperty("category").GetString());
        Assert.AreEqual(parameter is null ? "InvalidOperationException" : "ArgumentException", root.GetProperty("exceptionType").GetString());
        StringAssert.Contains(root.GetProperty("message").GetString(), guidance);
        if (parameter is not null) Assert.AreEqual(parameter, ((ArgumentException)error).ParamName);
        foreach (var text in hidden)
        {
            Assert.IsFalse(root.GetProperty("message").GetString()!.Contains(text, StringComparison.Ordinal),
                $"Decoded envelope disclosed input: {text}");
            Assert.IsFalse(json.Contains(text, StringComparison.Ordinal), $"Envelope disclosed input: {text}");
        }
    }


    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("/")]
    public void File_Refusals_Explain_Missing_Project_Disk_Location(string? projectPath)
    {
        var method = typeof(RoslynMcp.Roslyn.Services.FileOperationService).GetMethod(
            "ValidateFilePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var error = Assert.ThrowsExactly<System.Reflection.TargetInvocationException>(() =>
            method.Invoke(null, [projectPath, Path.GetFullPath("Candidate.cs"), "filePath"]));
        AssertSafeRefusal(error.InnerException!, "project", null);
    }

    [TestMethod]
    public async Task File_Case_Identity_Uses_The_Platform_Policy()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var source = workspace.GetPath("SampleLib", "Dog.cs");
        var destination = workspace.GetPath("SampleLib", "dog.cs");
        if (OperatingSystem.IsWindows())
        {
            var error = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() => FileOperationService.PreviewMoveFileAsync(
                workspace.WorkspaceId, new MoveFileDto(source, destination, null, false), CancellationToken.None));
            AssertSafeRefusal(error, "different", "targetFilePath", source);
        }
        else
        {
            var preview = await FileOperationService.PreviewMoveFileAsync(workspace.WorkspaceId,
                new MoveFileDto(source, destination, null, false), CancellationToken.None);
            Assert.IsFalse(string.IsNullOrEmpty(preview.PreviewToken));
            Assert.IsFalse(File.Exists(destination));
        }
    }


    [TestMethod]
    public async Task Path_Normalization_Failure_Retains_Inner_Detail_Only_On_The_Server()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows GetFullPath rejects paths longer than its extended-path limit.");
        }

        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var input = workspace.GetPath("SampleLib", new string('x', 40_000) + ".cs");
        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() => FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId, new CreateFileDto("SampleLib", input, ""), CancellationToken.None));
        AssertSafeRefusal(error, "Parameter 'filePath' is invalid", "filePath", workspace.RootPath);
        Assert.IsInstanceOfType<PathTooLongException>(error.InnerException);
        var json = ToolErrorHandler.ClassifyAndFormat(error, "refusal_test");
        Assert.IsFalse(json.Contains(error.InnerException.Message, StringComparison.Ordinal));
    }


    [TestMethod]
    [DataRow("CON.cs")]
    [DataRow("COM1.txt")]
    [DataRow("NUL.cs")]
    [DataRow("PRN")]
    [DataRow("CONIN$.txt")]
    public async Task Supported_Windows_Device_Name_Forms_Remain_Valid_File_Paths(string fileName)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("This regression covers Windows path normalization.");
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        foreach (var path in new[] { workspace.GetPath("SampleLib", fileName),
            workspace.GetPath("SampleLib", fileName, "Child.cs") })
        {
            var preview = await FileOperationService.PreviewCreateFileAsync(workspace.WorkspaceId,
                new CreateFileDto("SampleLib", path, "public class DeviceName {}"), CancellationToken.None);
            Assert.IsFalse(string.IsNullOrEmpty(preview.PreviewToken));
            Assert.AreEqual(path, preview.Changes.Single().FilePath, StringComparer.OrdinalIgnoreCase);
            Assert.IsFalse(File.Exists(path), "A preview must not write its file.");
        }
    }


    [TestMethod]
    [DataRow("private.cs.", false)]
    [DataRow("private.cs ", false)]
    [DataRow("private.cs.", true)]
    [DataRow("private.cs ", true)]
    public async Task Windows_Path_Aliases_Are_Refused_Before_Normalization(string segment, bool folder)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows trims trailing dot and space aliases.");
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var path = folder ? workspace.GetPath("SampleLib", segment, "Child.cs") : workspace.GetPath("SampleLib", segment);
        var error = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() => FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId, new CreateFileDto("SampleLib", path, ""), CancellationToken.None));
        AssertSafeRefusal(error, "trailing dots or spaces", "filePath", segment, workspace.RootPath);
        AssertNoStoredPreviews(PreviewStore, workspace.WorkspaceId);
        Assert.IsFalse(File.Exists(path));
    }

    internal static void AssertNoStoredPreviews(RoslynMcp.Roslyn.Contracts.IPreviewStore previewStore, string workspaceId)
    {
        var field = typeof(BoundedStore<RoslynMcp.Roslyn.Services.PreviewStore.PreviewEntry>)
            .GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var entries = (IReadOnlyDictionary<string, RoslynMcp.Roslyn.Services.PreviewStore.PreviewEntry>)field.GetValue(previewStore)!;
        Assert.IsFalse(entries.Values.Any(entry => entry.WorkspaceId == workspaceId),
            "A refused operation must not leave a redeemable preview.");
    }

}
