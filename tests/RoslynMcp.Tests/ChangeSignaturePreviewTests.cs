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

    /// <summary>
    /// Regression for `change-signature-preview-callsite-summary` (P3): firewall-analyzer
    /// 2026-04-15 reproduced `change_signature_preview op=remove` on `IPanosClient.GetRegisteredIpsAsync`
    /// returning a preview with only the interface-owner file's diff, while the apply
    /// rewrote 4 callsite files (concrete impl + tests) invisibly. Agent abandoned the
    /// preview and switched to manual `Edit` calls. Post-fix, `ApplyAddRemoveAsync` walks
    /// every related symbol (interface members, implementations, overrides, base
    /// virtuals) and unions the callers — preview now enumerates every file the apply
    /// will touch. Also surfaces a `CallsiteUpdates` per-file count summary on the DTO.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_OnInterfaceMethod_EnumeratesAllImplementerCallsiteFiles()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        // Interface + 2 implementers + a caller invoking through the interface. Pre-fix,
        // change_signature_preview on `IService.Compute` would only diff the interface
        // file (and maybe Caller.cs). The two implementations would be invisible until
        // apply rewrote them.
        var interfacePath = Path.Combine(sampleLibDir, "ICallsiteSummaryService.cs");
        await File.WriteAllTextAsync(interfacePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public interface ICallsiteSummaryService",
                "{",
                "    int Compute(int a, int b);",
                "}",
                "",
            }));

        var impl1Path = Path.Combine(sampleLibDir, "CallsiteSummaryServiceA.cs");
        await File.WriteAllTextAsync(impl1Path,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class CallsiteSummaryServiceA : ICallsiteSummaryService",
                "{",
                "    public int Compute(int a, int b) => a + b;",
                "}",
                "",
            }));

        var impl2Path = Path.Combine(sampleLibDir, "CallsiteSummaryServiceB.cs");
        await File.WriteAllTextAsync(impl2Path,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class CallsiteSummaryServiceB : ICallsiteSummaryService",
                "{",
                "    public int Compute(int a, int b) => a * b;",
                "}",
                "",
            }));

        var callerPath = Path.Combine(sampleLibDir, "CallsiteSummaryCaller.cs");
        await File.WriteAllTextAsync(callerPath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public class CallsiteSummaryCaller",
                "{",
                "    public int Use(ICallsiteSummaryService svc) => svc.Compute(1, 2);",
                "    public int UseConcreteA(CallsiteSummaryServiceA a) => a.Compute(3, 4);",
                "}",
                "",
            }));

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            // Locate the interface method declaration.
            var locator = SymbolLocator.BySource(interfacePath, line: 5, column: 9); // `Compute` on interface
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

            // The preview MUST enumerate the interface declaration + both implementers +
            // the caller file (because it invokes through the interface). Pre-fix only
            // the interface file would appear.
            var touchedFiles = preview.Changes.Select(c => Path.GetFileName(c.FilePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.IsTrue(touchedFiles.Contains("ICallsiteSummaryService.cs"),
                $"interface file must appear in changes; got: [{string.Join(", ", touchedFiles)}]");
            Assert.IsTrue(touchedFiles.Contains("CallsiteSummaryServiceA.cs"),
                $"impl A must appear in changes; got: [{string.Join(", ", touchedFiles)}]");
            Assert.IsTrue(touchedFiles.Contains("CallsiteSummaryServiceB.cs"),
                $"impl B must appear in changes; got: [{string.Join(", ", touchedFiles)}]");
            Assert.IsTrue(touchedFiles.Contains("CallsiteSummaryCaller.cs"),
                $"caller file must appear in changes; got: [{string.Join(", ", touchedFiles)}]");

            // CallsiteUpdates summary populated for the caller file (2 invocations).
            Assert.IsNotNull(preview.CallsiteUpdates, "CallsiteUpdates summary must be populated when apply rewrites invocations");
            var callerUpdate = preview.CallsiteUpdates.FirstOrDefault(u => u.FilePath.EndsWith("CallsiteSummaryCaller.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(callerUpdate, "caller file must appear in CallsiteUpdates");
            Assert.AreEqual(2, callerUpdate.CallsiteCount,
                $"caller file has 2 invocations (svc.Compute + a.Compute); CallsiteUpdates must reflect that");
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

    /// <summary>
    /// change-signature-parameter-span-hint-for-remove: caret on the PARAMETER (not the
    /// method) with <c>op=remove</c> and no explicit <c>Position</c>/<c>Name</c> now
    /// auto-dispatches. Pre-fix this threw "requires a method symbol" because the resolved
    /// <c>IParameterSymbol</c> wasn't promoted to its owner.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_RemoveOp_CaretOnParameter_AutoResolvesIndex()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        var fixturePath = Path.Combine(sampleLibDir, "ChangeSignatureCaretFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureCaretFixture",
            "{",
            "    public int Compute(int a, int b, int c)",
            "    {",
            "        return a + b + c;",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            // Caret on `b` — the parameter name at position 1. Pre-fix this would throw
            // "requires a method symbol". Post-fix it auto-promotes + dispatches.
            // Line 5 content: `    public int Compute(int a, int b, int c)`
            // Column math: 4 spaces + "public int Compute(" = 23 chars; "int " = 4 more → `a` at col 27.
            // `, int ` between a and b = 6 chars; `b` at col 27 + 1 + 6 = 34.
            var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 35);
            var request = new ChangeSignatureRequest(
                Op: "remove",
                Name: null,
                ParameterType: null,
                Position: null,     // explicitly unset — auto-resolve should fill
                NewName: null,
                DefaultValue: null);

            var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
                workspaceId, locator, request, CancellationToken.None);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

            var postApplyText = await File.ReadAllTextAsync(fixturePath);
            StringAssert.Contains(postApplyText, "public int Compute(int a, int c)",
                $"parameter `b` at position 1 must be removed via caret-on-parameter; got:\n{postApplyText}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    /// <summary>
    /// change-signature-preview-add-unhelpful-error: pre-fix
    /// `change_signature_preview(op=add, name=cancellationToken, parameterType=CancellationToken)`
    /// against a method whose existing callsites pass fewer args than declared (the typical
    /// "default-valued trailing param" shape that motivates adding a CancellationToken in
    /// the first place) failed with `"Parameter 'index' has an out-of-range value"` — an
    /// internal `ArgumentOutOfRangeException` from
    /// `SeparatedSyntaxList&lt;ArgumentSyntax&gt;.Insert(insertPosition, ...)` where
    /// `insertPosition` (= method.Parameters.Length) exceeded `args.Count`. The user reached
    /// a hard stop and could not see the preview at all. Post-fix the callsite splice
    /// clamps to `args.Count`, so the preview succeeds and the declaration is rewritten
    /// with the new parameter appended at the end (end-append default).
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_CancellationToken_SucceedsWithEndAppend_WhenCallsiteOmitsDefaultArg()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        // Method already has a default-valued parameter that the caller omits — this is the
        // shape that previously broke. method has 2 params (the second has a default value);
        // callsite passes only 1 arg. Adding `CancellationToken ct = default` at end makes
        // insertPosition=2 but args.Count=1 → pre-fix throws.
        var fixturePath = Path.Combine(sampleLibDir, "ChangeSignatureCtAppendFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "using System.Threading;",
            "",
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureCtAppendFixture",
            "{",
            "    public int Compute(int a, int b = 0)",
            "    {",
            "        return a + b;",
            "    }",
            "",
            "    public int CallCompute()",
            "    {",
            "        return Compute(1);",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            // Caret on `Compute` (line 7, column 16). Add CancellationToken at end (no Position
            // specified — defaults to method.Parameters.Length=2).
            var locator = SymbolLocator.BySource(fixturePath, line: 7, column: 16);
            var request = new ChangeSignatureRequest(
                Op: "add",
                Name: "cancellationToken",
                ParameterType: "System.Threading.CancellationToken",
                Position: null, // explicit end-append default
                NewName: null,
                DefaultValue: "default");

            // Pre-fix this throws ArgumentOutOfRangeException with paramName='index'.
            // Post-fix the preview succeeds and the declaration is rewritten.
            var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
                workspaceId, locator, request, CancellationToken.None);
            Assert.IsNotNull(preview.PreviewToken);
            Assert.IsTrue(preview.Changes.Count >= 1, "preview must report at least the declaration file");

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

            var postApplyText = await File.ReadAllTextAsync(fixturePath);
            StringAssert.Contains(postApplyText, "int a, int b = 0, System.Threading.CancellationToken cancellationToken = default",
                $"new parameter must append at end of declaration; got:\n{postApplyText}");
            // Callsite Compute(1) remains valid post-rewrite because both `b` and the new
            // CancellationToken parameter have defaults — the file still compiles. The
            // backlog row's contract is "preview succeeds + declaration shows the change",
            // not "every callsite is rewritten" (that's a separate concern tracked by
            // change-signature-preview-callsite-summary).
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    /// <summary>
    /// change-signature-preview-add-unhelpful-error: with an explicit Position equal to
    /// method.Parameters.Length the behavior must match the omitted-Position default —
    /// the preview must succeed without bubbling the internal `ArgumentOutOfRangeException`
    /// from the callsite-arg splice. Guards against a regression where the explicit-position
    /// branch loses the clamping and reverts to the unhelpful "index out of range" error.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_ExplicitEndPosition_Succeeds_WhenCallsiteOmitsDefaultArg()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        var fixturePath = Path.Combine(sampleLibDir, "ChangeSignatureExplicitEndFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "using System.Threading;",
            "",
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureExplicitEndFixture",
            "{",
            "    public int Compute(int a, int b = 0)",
            "    {",
            "        return a + b;",
            "    }",
            "",
            "    public int CallCompute()",
            "    {",
            "        return Compute(1);",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var locator = SymbolLocator.BySource(fixturePath, line: 7, column: 16);
            var request = new ChangeSignatureRequest(
                Op: "add",
                Name: "cancellationToken",
                ParameterType: "System.Threading.CancellationToken",
                Position: 2, // explicit end-position
                NewName: null,
                DefaultValue: "default");

            var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
                workspaceId, locator, request, CancellationToken.None);
            Assert.IsNotNull(preview.PreviewToken);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

            var postApplyText = await File.ReadAllTextAsync(fixturePath);
            StringAssert.Contains(postApplyText, "int a, int b = 0, System.Threading.CancellationToken cancellationToken = default",
                $"explicit end-position must succeed same as omitted-Position; got:\n{postApplyText}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    /// <summary>
    /// change-signature-preview-add-unhelpful-error risk: ensure the end-append clamping
    /// fix doesn't collide with positional inserts. With Position=0 (head insert) and a
    /// callsite that already has the right arg count, the new parameter must splice in at
    /// the front of the declaration. Math.Min(0, args.Count) is still 0, so positional
    /// behavior is unchanged.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_HeadPosition_StillSplicesAtFrontOfDeclaration()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        var fixturePath = Path.Combine(sampleLibDir, "ChangeSignatureHeadPosFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureHeadPosFixture",
            "{",
            "    public int Compute(int a)",
            "    {",
            "        return a;",
            "    }",
            "",
            "    public int CallCompute()",
            "    {",
            "        return Compute(42);",
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
                Op: "add",
                Name: "prefix",
                ParameterType: "string",
                Position: 0, // head insert — Math.Min(0, args.Count) must remain 0
                NewName: null,
                DefaultValue: "\"\"");

            var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
                workspaceId, locator, request, CancellationToken.None);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

            var postApplyText = await File.ReadAllTextAsync(fixturePath);
            // Declaration: `string prefix = ""` then `int a`.
            StringAssert.Contains(postApplyText, "string prefix = \"\", int a",
                $"head insert must put new parameter first in declaration; got:\n{postApplyText}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    /// <summary>
    /// change-signature-preview-add-unhelpful-error: when the caller supplies an explicit
    /// Position that genuinely exceeds method.Parameters.Length (i.e. user error, not a
    /// callsite-omits-default case), the error must cite user-facing fields (Name +
    /// ParameterType + the valid range) instead of bubbling an internal
    /// ArgumentOutOfRangeException with paramName='index'.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_OutOfRangePosition_EmitsActionableError()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        var fixturePath = Path.Combine(sampleLibDir, "ChangeSignatureOutOfRangeFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureOutOfRangeFixture",
            "{",
            "    public int Compute(int a, int b)",
            "    {",
            "        return a + b;",
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
                Op: "add",
                Name: "cancellationToken",
                ParameterType: "System.Threading.CancellationToken",
                Position: 99, // genuinely out of range — method has 2 params, valid 0..2
                NewName: null,
                DefaultValue: "default");

            var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
                await _changeSignatureService.PreviewChangeSignatureAsync(
                    workspaceId, locator, request, CancellationToken.None));

            // Error must cite Name, ParameterType, valid range, and the "omit Position" hint.
            // Must NOT mention internal "index" identifier.
            StringAssert.Contains(ex.Message, "cancellationToken",
                $"error must cite caller-supplied Name; got: {ex.Message}");
            StringAssert.Contains(ex.Message, "CancellationToken",
                $"error must cite caller-supplied ParameterType; got: {ex.Message}");
            StringAssert.Contains(ex.Message, "0..2",
                $"error must cite the valid Position range; got: {ex.Message}");
            StringAssert.Contains(ex.Message, "omit Position",
                $"error must hint that omitting Position appends at end; got: {ex.Message}");
            Assert.IsFalse(ex.Message.Contains("Parameter 'index'", StringComparison.Ordinal),
                $"error must NOT leak the internal 'index' paramName; got: {ex.Message}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_RemoveOp_CaretOnParameter_ExplicitPositionOverridesAutoResolve()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        var fixturePath = Path.Combine(sampleLibDir, "ChangeSignatureCaretOverrideFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureCaretOverrideFixture",
            "{",
            "    public int Compute(int a, int b, int c)",
            "    {",
            "        return a + b + c;",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            // Caret on `b` (parameter 1) but explicit Position=2 — auto-resolve must defer.
            var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 35);
            var request = new ChangeSignatureRequest(
                Op: "remove",
                Name: null,
                ParameterType: null,
                Position: 2,
                NewName: null,
                DefaultValue: null);

            var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
                workspaceId, locator, request, CancellationToken.None);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, CancellationToken.None);
            Assert.IsTrue(applyResult.Success);

            var postApplyText = await File.ReadAllTextAsync(fixturePath);
            StringAssert.Contains(postApplyText, "public int Compute(int a, int b)",
                $"explicit Position=2 must win over caret-on-b auto-resolve; got:\n{postApplyText}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }
}
