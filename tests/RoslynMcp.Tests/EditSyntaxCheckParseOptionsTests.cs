using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// edit-syntax-check-project-parse-options: the edit syntax check must parse under each loaded
/// project's parse options (DefineConstants, LangVersion), not Roslyn's defaults, on every route
/// that shares <c>EditService.GetCSharpSyntaxErrors</c>: direct apply, batch apply and preview.
/// </summary>
[TestClass]
public sealed class EditSyntaxCheckParseOptionsTests : IsolatedWorkspaceTestBase
{
    private const string Define = "<DefineConstants>$(DefineConstants);PROJECT_DEFINE</DefineConstants>";

    private const string ActiveBranchSource = "#if PROJECT_DEFINE\npublic class ParseTarget { }\n#endif\n";

    // Stray closing brace at top level of the PROJECT_DEFINE branch: CS1022 only when that branch is active.
    private static readonly TextEditDto[] _strayBraceEdit = [new(2, 1, 2, 1, "}\n")];

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("apply")]
    [DataRow("batch")]
    [DataRow("preview")]
    public async Task ActiveSymbolBranch_MalformedEdit_IsRefusedWithoutMutation(string route)
    {
        await using var workspace = await ArrangeAsync(ActiveBranchSource, Define);
        var target = workspace.GetPath("SampleLib", "ParseTarget.cs");
        var before = await File.ReadAllBytesAsync(target);
        var undoBefore = UndoService.GetLastOperation(workspace.WorkspaceId);

        switch (route)
        {
            case "apply":
                var apply = await NewEditService().ApplyTextEditsAsync(
                    workspace.WorkspaceId, target, _strayBraceEdit, "apply_text_edit", CancellationToken.None);
                Assert.IsFalse(apply.Success);
                Assert.AreEqual(0, apply.EditsApplied);
                Assert.IsNotNull(apply.SyntaxErrors);
                Assert.IsNotEmpty(apply.SyntaxErrors);
                Assert.AreEqual(2, apply.SyntaxErrors[0].Line);
                break;
            case "batch":
                var batch = await NewEditService().ApplyMultiFileTextEditsAsync(
                    workspace.WorkspaceId, [new(target, _strayBraceEdit)], "apply_multi_file_edit", CancellationToken.None);
                Assert.AreEqual(0, batch.Files.Single().EditsApplied);
                break;
            default:
                var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    NewEditService().PreviewMultiFileTextEditsAsync(
                        workspace.WorkspaceId, [new(target, _strayBraceEdit)], CancellationToken.None));
                StringAssert.Contains(ex.Message, "produce 1 C# syntax error(s)");
                break;
        }

        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(target));
        if (route == "apply")
        {
            Assert.AreEqual(undoBefore, UndoService.GetLastOperation(workspace.WorkspaceId),
                "A refused edit must not leave an undo entry.");
        }
    }

    [TestMethod]
    [DataRow("apply")]
    [DataRow("batch")]
    [DataRow("preview")]
    public async Task InactiveBranch_SameEdit_StillSucceeds(string route)
    {
        await using var workspace = await ArrangeAsync(ActiveBranchSource, projectProperties: null);
        var target = workspace.GetPath("SampleLib", "ParseTarget.cs");

        switch (route)
        {
            case "apply":
                var apply = await NewEditService().ApplyTextEditsAsync(
                    workspace.WorkspaceId, target, _strayBraceEdit, "apply_text_edit", CancellationToken.None);
                Assert.IsTrue(apply.Success);
                break;
            case "batch":
                var batch = await NewEditService().ApplyMultiFileTextEditsAsync(
                    workspace.WorkspaceId, [new(target, _strayBraceEdit)], "apply_multi_file_edit", CancellationToken.None);
                Assert.AreEqual(1, batch.Files.Single().EditsApplied);
                break;
            default:
                var preview = await NewEditService().PreviewMultiFileTextEditsAsync(
                    workspace.WorkspaceId, [new(target, _strayBraceEdit)], CancellationToken.None);
                Assert.IsFalse(string.IsNullOrEmpty(preview.PreviewToken));
                break;
        }
    }

    [TestMethod]
    public async Task ErrorDirectiveBranchDeactivatedByProjectSymbol_EditSucceeds()
    {
        // Default options leave PROJECT_DEFINE undefined, so the #error branch is active (CS1029).
        await using var workspace = await ArrangeAsync(
            "#if !PROJECT_DEFINE\n#error unsupported configuration\n#endif\npublic class ParseTarget { }\n", Define);
        var target = workspace.GetPath("SampleLib", "ParseTarget.cs");

        var result = await NewEditService().ApplyTextEditsAsync(
            workspace.WorkspaceId, target, [new(4, 1, 4, 1, "// benign\n")], "apply_text_edit", CancellationToken.None);

        Assert.IsTrue(result.Success, string.Join("; ", result.SyntaxErrors?.Select(e => e.Message) ?? []));
        StringAssert.StartsWith((await File.ReadAllTextAsync(target)).Split('\n')[3], "// benign");
    }

    [TestMethod]
    public async Task ProjectLangVersion_RefusesGrammarTheProjectLangVersionDoesNotParse()
    {
        await using var workspace = await ArrangeAsync(
            "public static class ParseTarget\n{\n}\n", "<LangVersion>13</LangVersion>");
        var target = workspace.GetPath("SampleLib", "ParseTarget.cs");
        var before = await File.ReadAllBytesAsync(target);

        // The C# 14 extension block changes the GRAMMAR, so it is a syntax-tree error under LangVersion 13.
        // Most newer features (interpolated strings, records, file-scoped namespaces, patterns) are gated
        // at declaration/binding time and never surface in a tree's diagnostics: compile_check owns those.
        var result = await NewEditService().ApplyTextEditsAsync(
            workspace.WorkspaceId, target, [new(3, 1, 3, 1, "    extension(string s) { public int L => 1; }\n")],
            "apply_text_edit", CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.IsNotEmpty(result.SyntaxErrors!);
        Assert.AreEqual(3, result.SyntaxErrors![0].Line);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(target));
    }

    [TestMethod]
    public async Task GenerateDocumentationFile_MalformedXmlDoc_DoesNotBlockEdit()
    {
        // Guard for the DocumentationMode normalization: a Diagnose-mode project would surface CS1570
        // (a non-Hidden tree diagnostic) and block an edit the default-options check accepts.
        await using var workspace = await ArrangeAsync(
            "public class ParseTarget { }\n", "<GenerateDocumentationFile>true</GenerateDocumentationFile>");
        var target = workspace.GetPath("SampleLib", "ParseTarget.cs");

        var result = await NewEditService().ApplyTextEditsAsync(
            workspace.WorkspaceId, target, [new(1, 1, 1, 1, "/// <summary>unclosed\n")], "apply_text_edit", CancellationToken.None);

        Assert.IsTrue(result.Success, string.Join("; ", result.SyntaxErrors?.Select(e => e.Message) ?? []));
    }

    [TestMethod]
    [DataRow("apply")]
    [DataRow("batch")]
    [DataRow("preview")]
    public async Task LinkedFile_IsRefusedWhenAnyLoadedContextIsMalformed(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var target = workspace.GetPath("SampleLib", "ParseTarget.cs");
        await File.WriteAllTextAsync(target, ActiveBranchSource);
        WriteLinkedProject(workspace, Define);
        await RestoreWorkspaceAsync(workspace);
        var workspaceId = await workspace.LoadAsync();
        AssertLinkedInTwoProjects(workspaceId, target);
        var before = await File.ReadAllBytesAsync(target);

        // SampleLib (no symbol) parses clean; only the linked project's context activates the branch.
        switch (route)
        {
            case "apply":
                var apply = await NewEditService().ApplyTextEditsAsync(
                    workspaceId, target, _strayBraceEdit, "apply_text_edit", CancellationToken.None);
                Assert.IsFalse(apply.Success);
                Assert.AreEqual(1, apply.SyntaxErrors!.Count, "Contexts with distinct options must not duplicate one diagnostic.");
                break;
            case "batch":
                var batch = await NewEditService().ApplyMultiFileTextEditsAsync(
                    workspaceId, [new(target, _strayBraceEdit)], "apply_multi_file_edit", CancellationToken.None);
                Assert.AreEqual(0, batch.Files.Single().EditsApplied);
                break;
            default:
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    NewEditService().PreviewMultiFileTextEditsAsync(
                        workspaceId, [new(target, _strayBraceEdit)], CancellationToken.None));
                break;
        }

        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(target));
    }

    [TestMethod]
    public async Task LinkedFile_CleanInEveryContext_Succeeds()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var target = workspace.GetPath("SampleLib", "ParseTarget.cs");
        await File.WriteAllTextAsync(target, ActiveBranchSource);
        WriteLinkedProject(workspace, Define);
        await RestoreWorkspaceAsync(workspace);
        var workspaceId = await workspace.LoadAsync();
        AssertLinkedInTwoProjects(workspaceId, target);

        var result = await NewEditService().ApplyTextEditsAsync(
            workspaceId, target, [new(1, 1, 1, 1, "// benign\n")], "apply_text_edit", CancellationToken.None);

        Assert.IsTrue(result.Success, string.Join("; ", result.SyntaxErrors?.Select(e => e.Message) ?? []));
    }

    [TestMethod]
    public async Task AddPragmaSuppression_UnderActiveBranchSyntaxError_ReturnsTheExistingFailureDto()
    {
        // The file is already malformed inside the active PROJECT_DEFINE branch; the otherwise-valid
        // pragma insertion applies through the same syntax check and is refused with Success=false.
        await using var workspace = await ArrangeAsync("#if PROJECT_DEFINE\n}\n#endif\npublic class ParseTarget { }\n", Define);
        var target = workspace.GetPath("SampleLib", "ParseTarget.cs");
        var before = await File.ReadAllBytesAsync(target);
        var suppression = new SuppressionService(EditorConfigService, NewEditService(), WorkspaceManager, CompileCheckService);

        var result = await suppression.AddPragmaWarningDisableAsync(
            workspace.WorkspaceId, target, 4, "CS0168", CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.IsNotEmpty(result.SyntaxErrors!);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(target));
    }

    private static EditService NewEditService() => new(
        WorkspaceManager,
        NullLogger<EditService>.Instance,
        UndoService,
        ChangeTracker,
        PreviewStore,
        CompileCheckService);

    private static async Task<IsolatedWorkspaceScope> ArrangeAsync(string source, string? projectProperties)
    {
        var workspace = CreateIsolatedWorkspaceCopy();
        try
        {
            await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "ParseTarget.cs"), source);
            if (projectProperties is not null)
            {
                var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
                var xml = await File.ReadAllTextAsync(projectPath);
                await File.WriteAllTextAsync(projectPath,
                    xml.Replace("</PropertyGroup>", projectProperties + "</PropertyGroup>", StringComparison.Ordinal));
            }

            await workspace.LoadAsync();
            return workspace;
        }
        catch
        {
            await workspace.DisposeAsync();
            throw;
        }
    }

    private static void WriteLinkedProject(IsolatedWorkspaceScope workspace, string projectProperties)
    {
        var directory = workspace.GetPath("LinkedLib");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "LinkedLib.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>" +
            "<EnableDefaultCompileItems>false</EnableDefaultCompileItems>" + projectProperties +
            "</PropertyGroup><ItemGroup><Compile Include=\"..\\SampleLib\\ParseTarget.cs\" Link=\"ParseTarget.cs\" /></ItemGroup></Project>");
        var slnx = File.ReadAllText(workspace.SolutionPath);
        File.WriteAllText(workspace.SolutionPath,
            slnx.Replace("</Solution>", "  <Project Path=\"LinkedLib/LinkedLib.csproj\" />\n</Solution>", StringComparison.Ordinal));
    }

    private static void AssertLinkedInTwoProjects(string workspaceId, string target)
    {
        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        Assert.HasCount(2, solution.GetDocumentIdsWithFilePath(target),
            "The fixture must load the file into two projects, or the test degrades to a single context.");
    }
}
