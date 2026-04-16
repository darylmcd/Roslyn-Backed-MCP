using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for `change-signature-preview-brace-concat-on-add-op` (P3) and
/// the related FORMAT-BUG-005 (`dr-9-10-format-bug-005-renders-declaration-without-spac`,
/// I-32 in the backlog sweep plan). Pre-fix, `change_signature_preview op=add` produced
/// a diff where the new signature line concatenated with the method body's opening brace
/// AND the comma-separator spacing was stripped; on Jellyfin's
/// `MusicManager.GetInstantMixFromSong` (audit 2026-04-16 §9.1) the rendered preview was
/// uncompilable C#. The fix routes the new parameter through `SyntaxFactory.ParseParameter`
/// and rebuilds the parameter list via `SyntaxFactory.ParseParameterList`, preserving the
/// existing list's enclosing trivia (which includes the close-paren's trailing whitespace
/// before the body brace).
/// </summary>
[TestClass]
public sealed class ChangeSignaturePreviewTests : TestBase
{
    private static ChangeSignatureService _changeSignatureService = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
        _changeSignatureService = new ChangeSignatureService(
            WorkspaceManager,
            PreviewStore,
            RefactoringService);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_PreservesBodyBraceLineBreak()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        // Method with two params + body brace on its own line. Pre-fix this rendered as
        // `public int Compute(int a, int b, int c) {` — body brace concatenated, original
        // `{` line removed, comma-separator space stripped between `b` and the new `c`.
        var fixturePath = Path.Combine(sampleLibDir, "ChangeSignatureFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureFixture",
            "{",
            "    public int Compute(int a, int b)",
            "    {",
            "        return a + b;",
            "    }",
            "",
            "    public int CallCompute()",
            "    {",
            "        return Compute(1, 2);",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            // Add a third parameter `int c = 0` at position 2.
            var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 16); // `Compute` identifier
            var request = new ChangeSignatureRequest(
                Op: "add",
                Name: "c",
                ParameterType: "int",
                Position: 2,
                NewName: null,
                DefaultValue: "0");

            var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
                workspaceId, locator, request, CancellationToken.None);

            Assert.IsNotNull(preview.PreviewToken);
            Assert.IsTrue(preview.Changes.Count >= 1, "preview must report at least one changed file");

            var declarationDiff = preview.Changes
                .FirstOrDefault(c => c.FilePath.EndsWith("ChangeSignatureFixture.cs", StringComparison.OrdinalIgnoreCase))
                ?.UnifiedDiff;
            Assert.IsNotNull(declarationDiff, "expected diff for ChangeSignatureFixture.cs");

            // Apply and inspect the resulting file. The previous bug rendered as
            // `public int Compute(int a, int b, int c = 0) {` (one line) — body brace
            // concatenated. Post-fix the body brace stays on its own line.
            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

            var postApplyText = await File.ReadAllTextAsync(fixturePath);

            // Body brace must remain on its own line — i.e. the close-paren is followed
            // by a newline + indentation + `{`, NOT by ` {` on the same line.
            StringAssert.Contains(postApplyText, "public int Compute(int a, int b, int c = 0)\r\n    {",
                $"body brace must stay on its own line; got:\n{postApplyText}");

            // Comma-separator spacing must be preserved between every parameter.
            StringAssert.Contains(postApplyText, "int a, int b, int c = 0",
                $"comma-separator spacing must be preserved; got:\n{postApplyText}");

            // Default value emitted.
            StringAssert.Contains(postApplyText, "int c = 0",
                "default value '= 0' must appear on the new parameter");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_NewParameterEmitsCorrectInternalSpacing()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        // Fixture exercises: the previous trailing-space hack on `ParseTypeName(parameterType + " ")`
        // built parameters like `Stringx` (no space) when the type itself didn't tokenize cleanly.
        // Post-fix `SyntaxFactory.ParseParameter(paramText)` produces `string x` reliably.
        var fixturePath = Path.Combine(sampleLibDir, "ChangeSignatureSpacingFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureSpacingFixture",
            "{",
            "    public string Format(int n)",
            "    {",
            "        return n.ToString();",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 19); // `Format` identifier
            var request = new ChangeSignatureRequest(
                Op: "add",
                Name: "prefix",
                ParameterType: "string",
                Position: 0,
                NewName: null,
                DefaultValue: "\"\"");

            var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
                workspaceId, locator, request, CancellationToken.None);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

            var postApplyText = await File.ReadAllTextAsync(fixturePath);

            // Type and identifier must be space-separated, NOT concatenated.
            StringAssert.Contains(postApplyText, "string prefix",
                $"new parameter must have proper type/identifier spacing; got:\n{postApplyText}");

            // Default value must appear with `= ` separator.
            StringAssert.Contains(postApplyText, "string prefix = \"\"",
                $"default value must render with `= ` separator; got:\n{postApplyText}");

            // Comma between new param and existing `int n` must have a space after it.
            StringAssert.Contains(postApplyText, "string prefix = \"\", int n",
                $"comma-separator spacing must be preserved; got:\n{postApplyText}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_RemoveOp_PreservesBodyBraceLineBreak()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        var fixturePath = Path.Combine(sampleLibDir, "ChangeSignatureRemoveFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureRemoveFixture",
            "{",
            "    public int Compute(int a, int b, int c)",
            "    {",
            "        return a + b + c;",
            "    }",
            "",
            "    public int CallCompute()",
            "    {",
            "        return Compute(1, 2, 3);",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 16);
            var request = new ChangeSignatureRequest(
                Op: "remove",
                Name: null,
                ParameterType: null,
                Position: 1,
                NewName: null,
                DefaultValue: null);

            var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
                workspaceId, locator, request, CancellationToken.None);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, CancellationToken.None);
            Assert.IsTrue(applyResult.Success);

            var postApplyText = await File.ReadAllTextAsync(fixturePath);
            StringAssert.Contains(postApplyText, "public int Compute(int a, int c)\r\n    {",
                $"body brace must stay on its own line after remove; got:\n{postApplyText}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }
}
