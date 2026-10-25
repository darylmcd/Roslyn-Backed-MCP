using System.Text;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RefactoringPhysicalUndoCaptureTests : OwnedRefactoringTestBase
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("attribute", false)]
    [DataRow("element", false)]
    [DataRow("import", false)]
    [DataRow("attribute", true)]
    [DataRow("element", true)]
    [DataRow("import", true)]
    public async Task ExplicitCompileMembership_PublicApplyAndUndoRestorePhysicalBytes(string sdkShape, bool remove)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync(sdkShape, timeout.Token);
        var path = workspace.GetPath(remove ? "Original.cs" : "Added.cs");
        var originalProjectBytes = File.ReadAllBytes(workspace.SolutionPath);
        var originalSourceBytes = remove ? File.ReadAllBytes(path) : null;
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var originalVersion = Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        Assert.AreEqual(remove ? 1 : 0, CountCompile(original, workspace.SolutionPath, path));
        Assert.IsFalse(ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems(workspace.SolutionPath),
            "Direct EnableDefaultCompileItems=false isolates capture from effective-policy classification.");
        var preview = remove
            ? await Services.FileOperationService.PreviewDeleteFileAsync(workspace.WorkspaceId,
                new DeleteFileDto(path), timeout.Token)
            : await Services.FileOperationService.PreviewCreateFileAsync(workspace.WorkspaceId,
                new CreateFileDto("Entry", path, "public sealed class Added { public string Value => \"caf\u00e9\"; }\n"), timeout.Token);
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(originalVersion, Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        CollectionAssert.AreEqual(originalProjectBytes, File.ReadAllBytes(workspace.SolutionPath));
        Assert.IsNotNull(Services.PreviewStore.Retrieve(preview.PreviewToken));

        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken,
            remove ? "delete_file" : "create_file", timeout.Token);

        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual(!remove, File.Exists(path));
        Assert.AreEqual(remove ? 0 : 1, CountCompile(original, workspace.SolutionPath, path));
        Assert.IsNull(Services.PreviewStore.Retrieve(preview.PreviewToken));
        Assert.AreNotEqual(originalVersion, Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        Assert.IsTrue(await Services.UndoService.RevertAsync(workspace.WorkspaceId, timeout.Token));
        var restoredCount = CountCompile(original, workspace.SolutionPath, path);
        TestContext.WriteLine($"PHYSICAL_UNDO sdk={sdkShape} remove={remove} applied={applied.Success} restoredCompile={restoredCount} exactProject={originalProjectBytes.AsSpan().SequenceEqual(File.ReadAllBytes(workspace.SolutionPath))}");
        Assert.AreEqual(remove ? 1 : 0, restoredCount, "Undo must restore native Compile membership with the source file.");
        CollectionAssert.AreEqual(originalProjectBytes, File.ReadAllBytes(workspace.SolutionPath),
            "Restore original XML bytes, including UTF16 BOM, Unicode comments and whitespace.");
        if (remove) CollectionAssert.AreEqual(originalSourceBytes!, File.ReadAllBytes(path));
        else Assert.IsFalse(File.Exists(path));
        Assert.IsFalse(await Services.UndoService.RevertAsync(workspace.WorkspaceId, timeout.Token));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ChangedOnly_UncachedPhysicalSource_RestoresExactBytes(bool encodingOnly, bool rebase)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync("attribute", timeout.Token);
        var path = workspace.GetPath("Original.cs");
        var originalBytes = File.ReadAllBytes(path);
        var projectBytes = File.ReadAllBytes(workspace.SolutionPath);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var document = original.Projects.Single().Documents.Single(item => item.Name == "Original.cs");
        Assert.IsFalse(document.TryGetText(out _), "Regression requires an uncached original TextLoader.");
        var intendedEncoding = encodingOnly ? new UnicodeEncoding(true, true) : Encoding.Unicode;
        var body = encodingOnly ? File.ReadAllText(path) : "// chang\u00e9\npublic class Original { public int Value => 43; }\n";
        var intendedText = SourceText.From(body, intendedEncoding, SourceHashAlgorithm.Sha256);
        var modified = original.WithDocumentText(document.Id, intendedText, PreservationMode.PreserveIdentity);
        var token = Services.PreviewStore.Store(workspace.WorkspaceId, modified,
            Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "changed-only physical before-image", false);
        var stored = Services.PreviewStore.Retrieve(token)!.Value;
        Assert.AreSame(original, stored.OriginalSolution);
        Assert.AreSame(modified, stored.ModifiedSolution);
        var siblingPath = workspace.GetPath("Sibling.cs");
        if (rebase)
        {
            var sibling = original.Projects.Single().Documents.Single(item => item.Name == "Sibling.cs");
            var siblingText = SourceText.From("public class Sibling { public int Value => 17; }\n", Encoding.UTF8);
            var siblingToken = Services.PreviewStore.Store(workspace.WorkspaceId,
                original.WithDocumentText(sibling.Id, siblingText),
                Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "unrelated sibling edit", false);
            var siblingResult = await Services.RefactoringService.ApplyRefactoringAsync(siblingToken, "test_apply", timeout.Token);
            Assert.IsTrue(siblingResult.Success, siblingResult.Error);
            Assert.IsNotNull(Services.PreviewStore.Retrieve(token));
            Assert.AreNotSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        }
        var siblingBytes = File.ReadAllBytes(siblingPath);
        var current = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var currentDocument = current.GetDocument(document.Id)!;
        Assert.IsFalse(currentDocument.TryGetText(out _), "Physical capture must not depend on prior source materialization.");

        var applied = await Services.RefactoringService.ApplyRefactoringAsync(token, "test_apply", timeout.Token);

        Assert.IsTrue(applied.Success, applied.Error);
        CollectionAssert.AreEqual(Bytes(intendedEncoding, body), File.ReadAllBytes(path));
        Assert.IsNull(Services.PreviewStore.Retrieve(token));
        Assert.AreSame(intendedText, await modified.GetDocument(document.Id)!.GetTextAsync(timeout.Token));
        Assert.AreSame(original, stored.OriginalSolution);
        Assert.IsTrue(await Services.UndoService.RevertAsync(workspace.WorkspaceId, timeout.Token));
        var exactSource = originalBytes.AsSpan().SequenceEqual(File.ReadAllBytes(path));
        TestContext.WriteLine($"PHYSICAL_UNDO encodingOnly={encodingOnly} rebase={rebase} exactSource={exactSource}");
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(path), "Undo must restore pre-write physical bytes, not lazy post-write text.");
        CollectionAssert.AreEqual(projectBytes, File.ReadAllBytes(workspace.SolutionPath));
        CollectionAssert.AreEqual(siblingBytes, File.ReadAllBytes(siblingPath));
        Assert.AreEqual(File.ReadAllText(path), (await Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId)
            .Projects.Single().Documents.Single(item => item.Name == "Original.cs").GetTextAsync(timeout.Token)).ToString());
    }

    [TestMethod]
    public async Task SamePhysicalPathReplacement_PreservesFinalMembershipAndUndoBytes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync("attribute", timeout.Token);
        var path = workspace.GetPath("Original.cs");
        var sourceBytes = File.ReadAllBytes(path);
        var projectBytes = File.ReadAllBytes(workspace.SolutionPath);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var owner = original.Projects.Single();
        var document = owner.Documents.Single(item => item.Name == "Original.cs");
        var replacement = "public class Original { public int Value => 55; }\n";
        var modified = original.RemoveDocument(document.Id).AddDocument(DocumentId.CreateNewId(owner.Id),
            document.Name, SourceText.From(replacement, Encoding.Unicode), filePath: path);
        var token = Services.PreviewStore.Store(workspace.WorkspaceId, modified,
            Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "same path replacement", false);
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(token, "test_apply", timeout.Token);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual(1, CountCompile(original, workspace.SolutionPath, path));
        CollectionAssert.AreEqual(Bytes(Encoding.Unicode, replacement), File.ReadAllBytes(path));
        Assert.IsTrue(await Services.UndoService.RevertAsync(workspace.WorkspaceId, timeout.Token));
        Assert.AreEqual(1, CountCompile(original, workspace.SolutionPath, path));
        CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(path));
        CollectionAssert.AreEqual(projectBytes, File.ReadAllBytes(workspace.SolutionPath));
    }

    private async Task<IsolatedWorkspaceTestBase.IsolatedWorkspaceScope> CreateFixtureAsync(string sdkShape, CancellationToken ct)
    {
        var root = Path.Combine(TestTempRoot.Current, "physical-undo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "Entry.csproj");
        var scope = new IsolatedWorkspaceTestBase.IsolatedWorkspaceScope(root, projectPath, Services.WorkspaceManager);
        return await IsolatedWorkspaceTestBase.InitializeWithCleanupAsync(scope, async (workspace, token) =>
        {
            const string body = "\n  <!-- pr\u00e9server \u03a9 whitespace -->\n  <PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>\n  <ItemGroup><Compile Include=\"Original.cs\" /><Compile Include=\"Sibling.cs\" /></ItemGroup>\n";
            var xml = sdkShape switch
            {
                "attribute" => "<Project Sdk=\"Microsoft.NET.Sdk\">" + body + "</Project>\n",
                "element" => "<Project><Sdk Name=\"Microsoft.NET.Sdk\" />" + body + "</Project>\n",
                "import" => "<Project><Import Project=\"Sdk.props\" Sdk=\"Microsoft.NET.Sdk\" />" + body + "<Import Project=\"Sdk.targets\" Sdk=\"Microsoft.NET.Sdk\" /></Project>\n",
                _ => throw new ArgumentOutOfRangeException(nameof(sdkShape)),
            };
            await File.WriteAllBytesAsync(projectPath, Bytes(Encoding.Unicode, xml), token);
            await File.WriteAllBytesAsync(workspace.GetPath("Original.cs"),
                Bytes(Encoding.Unicode, "// pr\u00e9server \u03a9\r\npublic class Original { public int Value => 1; }\r\n"), token);
            await File.WriteAllTextAsync(workspace.GetPath("Sibling.cs"), "public class Sibling { }\n", token);
            await IsolatedWorkspaceTestBase.RestoreWorkspaceAsync(workspace, Services.DotnetCommandRunner, token);
            await workspace.LoadAsync(token);
        }, ct);
    }

    private static byte[] Bytes(Encoding encoding, string text) => [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    private static int CountCompile(Solution original, string projectPath, string sourcePath)
    {
        using var collection = new ProjectCollection();
        var globals = MsBuildMetadataHelper.GetWorkspaceGlobalProperties(original.Workspace);
        var project = new ProjectInstance(projectPath, new Dictionary<string, string>(globals, StringComparer.OrdinalIgnoreCase), null, collection);
        return project.GetItems("Compile").Count(item => FileSystemPath.Comparer.Equals(sourcePath,
            Path.GetFullPath(item.EvaluatedInclude, Path.GetDirectoryName(projectPath)!)));
    }
}
