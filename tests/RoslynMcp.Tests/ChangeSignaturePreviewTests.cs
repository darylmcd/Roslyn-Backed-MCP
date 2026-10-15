using Microsoft.CodeAnalysis.CSharp;
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
/// uncompilable C#. Retaining declaration nodes and their delimiter trivia keeps
/// the body brace and original separators intact.
/// </summary>
[TestClass]
public sealed partial class ChangeSignaturePreviewTests : IsolatedWorkspaceTestBase
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
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Method with two params + body brace on its own line. Pre-fix this rendered as
        // `public int Compute(int a, int b, int c) {` — body brace concatenated, original
        // `{` line removed, comma-separator space stripped between `b` and the new `c`.
        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureFixture.cs");
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

        await workspace.LoadAsync(CancellationToken.None);

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
            workspace.WorkspaceId, locator, request, CancellationToken.None);

        Assert.IsNotNull(preview.PreviewToken);
        Assert.IsTrue(preview.Changes.Count >= 1, "preview must report at least one changed file");

        var declarationDiff = preview.Changes
            .FirstOrDefault(c => c.FilePath.EndsWith("ChangeSignatureFixture.cs", StringComparison.OrdinalIgnoreCase))
            ?.UnifiedDiff;
        Assert.IsNotNull(declarationDiff, "expected diff for ChangeSignatureFixture.cs");

        // Apply and inspect the resulting file. The previous bug rendered as
        // `public int Compute(int a, int b, int c = 0) {` (one line) — body brace
        // concatenated. Post-fix the body brace stays on its own line.
        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
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

    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_NewParameterEmitsCorrectInternalSpacing()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Fixture exercises: the previous trailing-space hack on `ParseTypeName(parameterType + " ")`
        // built parameters like `Stringx` (no space) when the type itself didn't tokenize cleanly.
        // Parsing the new parameter produces `string x` reliably.
        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureSpacingFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureSpacingFixture",
            "{",
            "    public string Format(int n = 0)",
            "    {",
            "        return n.ToString();",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        await workspace.LoadAsync(CancellationToken.None);

        var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 19); // `Format` identifier
        var request = new ChangeSignatureRequest(
            Op: "add",
            Name: "prefix",
            ParameterType: "string",
            Position: 0,
            NewName: null,
            DefaultValue: "\"\"");

        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
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

    /// <summary>
    /// Regression for the change_signature_preview callsite-summary gap (shipped fix): firewall-analyzer
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
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Interface + 2 implementers + a caller invoking through the interface. Pre-fix,
        // change_signature_preview on `IService.Compute` would only diff the interface
        // file (and maybe Caller.cs). The two implementations would be invisible until
        // apply rewrote them.
        var interfacePath = workspace.GetPath("SampleLib", "ICallsiteSummaryService.cs");
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

        var impl1Path = workspace.GetPath("SampleLib", "CallsiteSummaryServiceA.cs");
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

        var impl2Path = workspace.GetPath("SampleLib", "CallsiteSummaryServiceB.cs");
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

        var callerPath = workspace.GetPath("SampleLib", "CallsiteSummaryCaller.cs");
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

        await workspace.LoadAsync(CancellationToken.None);

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
            workspace.WorkspaceId, locator, request, CancellationToken.None);

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

    /// <summary>
    /// Regression for `change-signature-add-skips-same-document-impls`: when the interface,
    /// its implementations and its callers all live in ONE document, the builder used to
    /// re-resolve each declaration/caller against the already-edited document using the
    /// ORIGINAL syntax tree and spans. After the first edit every later declaration was
    /// silently skipped (leaving CS0535) and callers were undercounted.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_OnInterfaceMethod_UpdatesSameDocumentImplementationsAndCallers()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "SameDocumentChangeSignatureFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            string.Join("\r\n", new[]
            {
                "namespace SampleLib;",
                "",
                "public interface ISameDocService",
                "{",
                "    int Compute(int a, int b);",
                "}",
                "",
                "public class SameDocServiceA : ISameDocService",
                "{",
                "    public int Compute(int a, int b) => a + b;",
                "}",
                "",
                "public class SameDocServiceB : ISameDocService",
                "{",
                "    public int Compute(int a, int b) => a * b;",
                "}",
                "",
                "public class SameDocCaller",
                "{",
                "    public int UseInterface(ISameDocService svc) => svc.Compute(1, 2);",
                "    public int UseA(SameDocServiceA a) => a.Compute(3, 4);",
                "    public int UseB(SameDocServiceB b) => b.Compute(5, 6);",
                "}",
                "",
            }));

        await workspace.LoadAsync(CancellationToken.None);

        var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 9); // `Compute` on interface
        var request = new ChangeSignatureRequest(
            Op: "add",
            Name: "c",
            ParameterType: "int",
            Position: 2,
            NewName: null,
            DefaultValue: "0");

        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);

        Assert.IsNotNull(preview.PreviewToken);
        Assert.IsNotNull(preview.CallsiteUpdates, "CallsiteUpdates summary must be populated");
        var fileUpdate = preview.CallsiteUpdates.SingleOrDefault(
            u => u.FilePath.EndsWith("SameDocumentChangeSignatureFixture.cs", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(fileUpdate, "fixture file must appear in CallsiteUpdates");
        Assert.AreEqual(3, fileUpdate.CallsiteCount,
            "all 3 same-document invocations (svc/a/b.Compute) must be counted, matching find_references");

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

        var postApplyText = await File.ReadAllTextAsync(fixturePath);
        StringAssert.Contains(postApplyText, "    int Compute(int a, int b, int c = 0);",
            $"interface declaration must gain the parameter; got:\n{postApplyText}");
        StringAssert.Contains(postApplyText, "public int Compute(int a, int b, int c = 0) => a + b;",
            $"implementation A must gain the parameter (else CS0535); got:\n{postApplyText}");
        StringAssert.Contains(postApplyText, "public int Compute(int a, int b, int c = 0) => a * b;",
            $"implementation B must gain the parameter (else CS0535); got:\n{postApplyText}");
        StringAssert.Contains(postApplyText, "svc.Compute(1, 2, 0)",
            $"interface caller must be rewritten; got:\n{postApplyText}");
        StringAssert.Contains(postApplyText, "a.Compute(3, 4, 0)",
            $"implementation A caller must be rewritten; got:\n{postApplyText}");
        StringAssert.Contains(postApplyText, "b.Compute(5, 6, 0)",
            $"implementation B caller must be rewritten; got:\n{postApplyText}");
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_RemoveOp_PreservesBodyBraceLineBreak()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureRemoveFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureRemoveFixture",
            "{",
            "    public int Compute(int a, int b, int c)",
            "    {",
            "        return a + c;",
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

        await workspace.LoadAsync(CancellationToken.None);

        var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 16);
        var request = new ChangeSignatureRequest(
            Op: "remove",
            Name: null,
            ParameterType: null,
            Position: 1,
            NewName: null,
            DefaultValue: null);

        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success);

        var postApplyText = await File.ReadAllTextAsync(fixturePath);
        StringAssert.Contains(postApplyText, "public int Compute(int a, int c)\r\n    {",
            $"body brace must stay on its own line after remove; got:\n{postApplyText}");
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
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureCaretFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureCaretFixture",
            "{",
            "    public int Compute(int a, int b, int c)",
            "    {",
            "        return a + c;",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        await workspace.LoadAsync(CancellationToken.None);

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
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

        var postApplyText = await File.ReadAllTextAsync(fixturePath);
        StringAssert.Contains(postApplyText, "public int Compute(int a, int c)",
            $"parameter `b` at position 1 must be removed via caret-on-parameter; got:\n{postApplyText}");
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
    /// a hard stop and could not see the preview at all. Original parameter bindings and
    /// omitted defaults are now materialized, and the new parameter appends to the declaration.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_CancellationToken_SucceedsWithEndAppend_WhenCallsiteOmitsDefaultArg()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Method already has a default-valued parameter that the caller omits — this is the
        // shape that previously broke. method has 2 params (the second has a default value);
        // callsite passes only 1 arg. Adding `CancellationToken ct = default` at end makes
        // insertPosition=2 but args.Count=1 → pre-fix throws.
        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureCtAppendFixture.cs");
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

        await workspace.LoadAsync(CancellationToken.None);

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
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);
        Assert.IsTrue(preview.Changes.Count >= 1, "preview must report at least the declaration file");

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

        var postApplyText = await File.ReadAllTextAsync(fixturePath);
        StringAssert.Contains(postApplyText, "int a, int b = 0, System.Threading.CancellationToken cancellationToken = default",
            $"new parameter must append at end of declaration; got:\n{postApplyText}");
        // Original arguments and omitted defaults retain their parameter identities.
        StringAssert.Contains(postApplyText, "Compute(a: 1, b: (int)(0), cancellationToken: default)");
    }

    /// <summary>
    /// change-signature-preview-add-unhelpful-error: with an explicit Position equal to
    /// method.Parameters.Length the behavior must match the omitted-Position default —
    /// the preview must succeed without bubbling the internal `ArgumentOutOfRangeException`
    /// from the callsite-arg splice. Guards against a regression where the explicit-position
    /// branch loses original bindings and reverts to the unhelpful "index out of range" error.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_ExplicitEndPosition_Succeeds_WhenCallsiteOmitsDefaultArg()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureExplicitEndFixture.cs");
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

        await workspace.LoadAsync(CancellationToken.None);

        var locator = SymbolLocator.BySource(fixturePath, line: 7, column: 16);
        var request = new ChangeSignatureRequest(
            Op: "add",
            Name: "cancellationToken",
            ParameterType: "System.Threading.CancellationToken",
            Position: 2, // explicit end-position
            NewName: null,
            DefaultValue: "default");

        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

        var postApplyText = await File.ReadAllTextAsync(fixturePath);
        StringAssert.Contains(postApplyText, "int a, int b = 0, System.Threading.CancellationToken cancellationToken = default",
            $"explicit end-position must succeed same as omitted-Position; got:\n{postApplyText}");
    }

    /// <summary>
    /// A head insertion with a complete positional call retains positional bindings
    /// while inserting the new parameter at the front of the declaration.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_HeadPosition_StillSplicesAtFrontOfDeclaration()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureHeadPosFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureHeadPosFixture",
            "{",
            "    public int Compute(int a = 0)",
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

        await workspace.LoadAsync(CancellationToken.None);

        var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 16);
        var request = new ChangeSignatureRequest(
            Op: "add",
            Name: "prefix",
            ParameterType: "string",
            Position: 0, // head insertion
            NewName: null,
            DefaultValue: "\"\"");

        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

        var postApplyText = await File.ReadAllTextAsync(fixturePath);
        // Declaration: `string prefix = ""` then `int a`.
        StringAssert.Contains(postApplyText, "string prefix = \"\", int a",
            $"head insert must put new parameter first in declaration; got:\n{postApplyText}");
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
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureOutOfRangeFixture.cs");
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

        await workspace.LoadAsync(CancellationToken.None);

        var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 16);
        var request = new ChangeSignatureRequest(
            Op: "add",
            Name: "cancellationToken",
            ParameterType: "System.Threading.CancellationToken",
            Position: 99, // genuinely out of range — method has 2 params, valid 0..2
            NewName: null,
            DefaultValue: "default");

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await _changeSignatureService.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, request, CancellationToken.None));

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

    /// <summary>
    /// Reorder the declaration while naming positional arguments in their original
    /// evaluation order and retaining their original parameter bindings.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_ReorderOp_PositionalCallsite_ReordersDeclarationAndNamesArguments()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureReorderFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureReorderFixture",
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

        await workspace.LoadAsync(CancellationToken.None);

        var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 16);
        // Reorder (a, b, c) -> (c, a, b). Validates name-token parsing too.
        var request = new ChangeSignatureRequest(
            Op: "reorder",
            NewOrder: "c, a, b");

        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

        var postApplyText = await File.ReadAllTextAsync(fixturePath);

        // Declaration reordered.
        StringAssert.Contains(postApplyText, "public int Compute(int c, int a, int b)",
            $"declaration must be reordered to (c, a, b); got:\n{postApplyText}");
        // Retained declaration trivia keeps the body brace on its own line.
        StringAssert.Contains(postApplyText, "public int Compute(int c, int a, int b)\r\n    {",
            $"body brace must stay on its own line; got:\n{postApplyText}");
        // Name the original arguments to retain evaluation order and their parameter bindings.
        StringAssert.Contains(postApplyText, "Compute(a: 1, b: 2, c: 3)",
            $"positional callsite arguments must keep evaluation order and named bindings; got:\n{postApplyText}");
    }

    /// <summary>
    /// change-signature-reorder-preview: op='reorder' accepts a 0-based index permutation
    /// and leaves all-named callsites untouched (named arguments bind by name regardless of
    /// the declaration order).
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_ReorderOp_NamedCallsite_LeftUntouched_IndexPermutation()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureReorderNamedFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureReorderNamedFixture",
            "{",
            "    public int Compute(int a, int b)",
            "    {",
            "        return a + b;",
            "    }",
            "",
            "    public int CallCompute()",
            "    {",
            "        return Compute(a: 1, b: 2);",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        await workspace.LoadAsync(CancellationToken.None);

        var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 16);
        // Index-form permutation: (a, b) -> (b, a).
        var request = new ChangeSignatureRequest(
            Op: "reorder",
            NewOrder: "1,0");

        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

        var postApplyText = await File.ReadAllTextAsync(fixturePath);

        StringAssert.Contains(postApplyText, "public int Compute(int b, int a)",
            $"declaration must be reordered via index permutation; got:\n{postApplyText}");
        // Named callsite must remain untouched — args bind by name.
        StringAssert.Contains(postApplyText, "Compute(a: 1, b: 2)",
            $"all-named callsite must remain unchanged after reorder; got:\n{postApplyText}");
    }

    /// <summary>
    /// change-signature-reorder-preview: invalid NewOrder inputs surface actionable errors
    /// rather than internal exceptions. Covers wrong arity, unknown name token, duplicate
    /// token, and identity-permutation no-op.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_ReorderOp_InvalidNewOrder_EmitsActionableError()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureReorderInvalidFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureReorderInvalidFixture",
            "{",
            "    public int Compute(int a, int b, int c)",
            "    {",
            "        return a + b + c;",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        await workspace.LoadAsync(CancellationToken.None);

        var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 16);

        // Missing NewOrder.
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await _changeSignatureService.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, new ChangeSignatureRequest(Op: "reorder"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "NewOrder",
            $"missing-NewOrder error must cite 'NewOrder'; got: {ex.Message}");

        // Wrong arity (2 tokens for 3-param method).
        ex = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await _changeSignatureService.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, new ChangeSignatureRequest(Op: "reorder", NewOrder: "a,b"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "exactly once",
            $"wrong-arity error must explain that every parameter must appear exactly once; got: {ex.Message}");

        // Unknown name token.
        ex = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await _changeSignatureService.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, new ChangeSignatureRequest(Op: "reorder", NewOrder: "a,b,zz"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "zz",
            $"unknown-token error must cite the offending token; got: {ex.Message}");

        // Duplicate token.
        ex = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await _changeSignatureService.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, new ChangeSignatureRequest(Op: "reorder", NewOrder: "a,a,b"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "more than once",
            $"duplicate-token error must explain the parameter is listed twice; got: {ex.Message}");

        // Identity permutation (no-op).
        ex = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await _changeSignatureService.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, new ChangeSignatureRequest(Op: "reorder", NewOrder: "a,b,c"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "identity",
            $"identity-permutation error must call out the no-op; got: {ex.Message}");
    }

    /// <summary>
    /// change-signature-reorder-preview: callsite that mixes positional and named arguments
    /// is refused with an actionable message because reordering the positional prefix can
    /// silently change which parameter each positional argument binds to.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_ReorderOp_MixedPositionalAndNamedCallsite_Refused()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureReorderMixedFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureReorderMixedFixture",
            "{",
            "    public int Compute(int a, int b, int c)",
            "    {",
            "        return a + b + c;",
            "    }",
            "",
            "    public int CallCompute()",
            "    {",
            "        return Compute(1, b: 2, c: 3);",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        await workspace.LoadAsync(CancellationToken.None);

        var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 16);
        var request = new ChangeSignatureRequest(Op: "reorder", NewOrder: "c,a,b");

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(async () =>
            await _changeSignatureService.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, request, CancellationToken.None));
        StringAssert.Contains(ex.Message, "mixes positional and named",
            $"mixed-callsite refusal must explain why; got: {ex.Message}");
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_RemoveOp_CaretOnParameter_ExplicitPositionOverridesAutoResolve()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureCaretOverrideFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureCaretOverrideFixture",
            "{",
            "    public int Compute(int a, int b, int c)",
            "    {",
            "        return a + b;",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        await workspace.LoadAsync(CancellationToken.None);

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
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success);

        var postApplyText = await File.ReadAllTextAsync(fixturePath);
        StringAssert.Contains(postApplyText, "public int Compute(int a, int b)",
            $"explicit Position=2 must win over caret-on-b auto-resolve; got:\n{postApplyText}");
    }

    /// <summary>
    /// change-signature-callsite-rewrites-enclosing-invocation: a non-invocation reference
    /// (method group, <c>nameof</c>) must not climb to its nearest enclosing invocation and get
    /// that unrelated call's argument list rewritten, nor be counted as a call-site update.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_MethodGroupAndNameofReferences_LeaveEnclosingInvocationsUntouched()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureRefKindsFixture.cs");
        await File.WriteAllTextAsync(fixturePath, string.Join("\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class ChangeSignatureRefKindsFixture",
            "{",
            "    public int Compute(int a, int b) => a + b;",
            "",
            "    public int Compute<T>(int a, int b) => a + b;",
            "    private static int Apply(System.Func<int, int, int> f, int x, int y) => f(x, y);",
            "    private static string Log(string name, string message) => name + message;",
            "",
            "    public int Direct() => Compute(1, 2);",
            "    public int Receiver(ChangeSignatureRefKindsFixture other) => other.Compute(3, 4);",
            "    public int MethodGroup() => Apply(Compute<int>, 5, 6);",
            "    public string NameOf() => Log(nameof(Compute), \"m\");",
            "}",
            "",
        }));

        await workspace.LoadAsync(CancellationToken.None);

        var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 16); // `Compute` identifier
        var request = new ChangeSignatureRequest(
            Op: "add",
            Name: "c",
            ParameterType: "int",
            Position: 2,
            NewName: null,
            DefaultValue: "0");

        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);

        // Only the two genuine invocations count as call sites.
        Assert.IsNotNull(preview.CallsiteUpdates, "CallsiteUpdates summary must be populated when apply rewrites invocations");
        var fixtureUpdate = preview.CallsiteUpdates.Single(
            u => u.FilePath.EndsWith("ChangeSignatureRefKindsFixture.cs", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(2, fixtureUpdate.CallsiteCount,
            "method-group and nameof references are not call sites and must not be counted");

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

        var postApplyText = await File.ReadAllTextAsync(fixturePath);
        StringAssert.Contains(postApplyText, "Compute(1, 2, 0)", $"direct call must be rewritten; got:\n{postApplyText}");
        StringAssert.Contains(postApplyText, "other.Compute(3, 4, 0)", $"receiver call must be rewritten; got:\n{postApplyText}");
        StringAssert.Contains(postApplyText, "Apply(Compute<int>, 5, 6)",
            $"enclosing Apply(...) of a method-group reference must be untouched; got:\n{postApplyText}");
        StringAssert.Contains(postApplyText, "Log(nameof(Compute), \"m\")",
            $"nameof(...) and its enclosing Log(...) must be untouched; got:\n{postApplyText}");
    }

    /// <summary>
    /// change-signature-primary-constructor-parameters: primary-constructor parameters live on the
    /// type declaration's parameter list, which the rewriters never visit. Pre-fix every op on a
    /// primary-constructor target threw the misleading "produced no changes" error; the service now
    /// refuses up-front with a specific, host-visible message. Covers a positional record and a class
    /// primary constructor, with the caret on the type name and on a parameter, for every op.
    /// </summary>
    [TestMethod]
    [DataRow("record", "add", false)]
    [DataRow("record", "remove", false)]
    [DataRow("record", "rename", false)]
    [DataRow("record", "reorder", false)]
    [DataRow("class", "rename", false)]
    [DataRow("class", "reorder", false)]
    [DataRow("record", "remove", true)]
    public async Task ChangeSignaturePreview_PrimaryConstructor_RefusesWithSpecificMessage(
        string typeKind, string op, bool caretOnParameter)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignaturePrimaryCtorFixture.cs");
        var content = string.Join("\n", new[]
        {
            "namespace SampleLib;",
            "",
            $"public {typeKind} PrimaryCtorFixture(int first, string second)",
            typeKind == "record" ? ";" : "{",
            typeKind == "record" ? "" : "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        await workspace.LoadAsync(CancellationToken.None);

        // Line 3: `public <kind> PrimaryCtorFixture(int first, string second)`.
        var declaration = content.Split('\n')[2];
        var column = caretOnParameter
            ? declaration.IndexOf("first", StringComparison.Ordinal) + 1
            : declaration.IndexOf("PrimaryCtorFixture", StringComparison.Ordinal) + 1;
        var locator = SymbolLocator.BySource(fixturePath, line: 3, column: column);
        var request = new ChangeSignatureRequest(
            Op: op,
            Name: op == "add" ? "third" : (op == "rename" ? "first" : null),
            ParameterType: op == "add" ? "int" : null,
            Position: op == "remove" ? 0 : null,
            NewName: op == "rename" ? "renamed" : null,
            DefaultValue: null,
            NewOrder: op == "reorder" ? "second, first" : null);

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(async () =>
            await _changeSignatureService.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, request, CancellationToken.None));

        StringAssert.Contains(ex.Message, "primary-constructor parameters",
            $"refusal must name the unsupported shape; got: {ex.Message}");
        StringAssert.Contains(ex.Message, "PrimaryCtorFixture",
            $"refusal must name the type; got: {ex.Message}");
        Assert.AreEqual(typeKind == "record", ex.Message.Contains("synthesized public properties", StringComparison.Ordinal),
            $"only records carry the synthesized-property note; got: {ex.Message}");
        Assert.IsFalse(ex.Message.Contains("produced no changes", StringComparison.OrdinalIgnoreCase),
            $"refusal must replace the misleading 'produced no changes' error; got: {ex.Message}");
    }

    /// <summary>
    /// A class whose constructor is an ordinary declaration (no primary constructor) must still
    /// reach the normal pipeline: the primary-constructor refusal is scoped to parameter-list ctors.
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_OrdinaryConstructor_IsNotRefusedAsPrimary()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "ChangeSignatureOrdinaryCtorFixture.cs");
        var content = string.Join("\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class OrdinaryCtorFixture",
            "{",
            "    public OrdinaryCtorFixture(int first)",
            "    {",
            "    }",
            "",
            "    public static int Run() { _ = new OrdinaryCtorFixture(1); return 1; }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        await workspace.LoadAsync(CancellationToken.None);

        var locator = SymbolLocator.BySource(fixturePath, line: 5, column: 12);
        var request = new ChangeSignatureRequest(
            Op: "add", Name: "second", ParameterType: "int", Position: null, NewName: null, DefaultValue: "0");

        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);

        Assert.IsFalse(string.IsNullOrEmpty(preview.PreviewToken), "ordinary constructor must produce a preview token");
        Assert.AreEqual(1, preview.CallsiteUpdates?.Sum(c => c.CallsiteCount));
        var candidate = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(candidate);
        Assert.AreEqual("1", await RunSignatureFixtureAsync(candidate.Value.ModifiedSolution, "SampleLib.OrdinaryCtorFixture"));
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual("1", await RunSignatureFixtureAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.OrdinaryCtorFixture"));
        StringAssert.Contains(await File.ReadAllTextAsync(fixturePath), "new OrdinaryCtorFixture(1, 0)");
    }

    [TestMethod]
    [DataRow("class", "add", false)]
    [DataRow("struct", "add", true)]
    [DataRow("class", "remove", true)]
    [DataRow("struct", "remove", false)]
    public async Task ChangeSignaturePreview_PrimaryConstructor_RewritesBoundConstruction(
        string kind, string op, bool parameterCaret)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "PrimaryBindingFixture.cs");
        var declaration = $"public {kind} PrimaryBindingFixture(int first, int second = 7)";
        var text = $"namespace SampleLib;\n{declaration} {{ }}\npublic static class PrimaryCaller {{ public static PrimaryBindingFixture Make() => new(1); }}\n";
        await File.WriteAllTextAsync(path, text);
        await workspace.LoadAsync(CancellationToken.None);
        var column = declaration.IndexOf(parameterCaret ? "first" : "PrimaryBindingFixture", StringComparison.Ordinal) + 1;
        var request = op == "add"
            ? new ChangeSignatureRequest("add", Name: "third", ParameterType: "int", DefaultValue: "9")
            : new ChangeSignatureRequest("remove", Position: 0);
        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, SymbolLocator.BySource(path, 2, column), request, CancellationToken.None);
        var pair = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(pair);
        var compilation = await pair.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync();
        Assert.IsNotNull(compilation);
        Assert.AreEqual(0, compilation.GetDiagnostics().Count(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error),
            string.Join("\n", compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)));
        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        var changed = await File.ReadAllTextAsync(path);
        StringAssert.Contains(changed, op == "add" ? "int third = 9" : "PrimaryBindingFixture(int second = 7)");
        Assert.AreEqual(1, preview.CallsiteUpdates?.Sum(c => c.CallsiteCount));
    }

    [TestMethod]
    [DataRow("add-omitted")]
    [DataRow("reorder-omitted")]
    [DataRow("reorder-expanded")]
    [DataRow("remove-expanded")]
    public async Task ChangeSignaturePreview_PreservesOriginalRuntimeBindings(string scenario)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "SignatureRuntimeFixture.cs");
        var expanded = scenario.EndsWith("expanded", StringComparison.Ordinal);
        var declaration = expanded
            ? "public static int Compute(int a, int b, params int[] tail) => a * 100 + b * 10" +
                (scenario == "remove-expanded" ? ";" : " + System.Linq.Enumerable.Sum(tail);")
            : "public static int Compute(int a = 1, int b = 2) => a * 10 + b;";
        var call = expanded ? "Compute(Trace(1), Trace(2), Trace(3), Trace(4))" : "Compute(9)";
        var source = string.Join("\n",
            "namespace SampleLib;",
            "public static class SignatureRuntimeFixture",
            "{",
            "    private static string trace = \"\";",
            "    private static int Trace(int value) { trace += value.ToString(); return value; }",
            "    " + declaration,
            "    public static string Run() { var value = " + call + "; return trace + \"|\" + value.ToString(); }",
            "}");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var request = scenario switch
        {
            "add-omitted" => new ChangeSignatureRequest("add", Name: "c", ParameterType: "int", DefaultValue: "7"),
            "remove-expanded" => new ChangeSignatureRequest("remove", Name: "tail"),
            _ => new ChangeSignatureRequest("reorder", NewOrder: expanded ? "b,a,tail" : "b,a"),
        };
        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, SymbolLocator.BySource(path, 6, 23), request, CancellationToken.None);
        var stored = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        var originalValue = await RunSignatureFixtureAsync(original, "SampleLib.SignatureRuntimeFixture");
        var candidateValue = await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureRuntimeFixture");
        // Removing a parameter intentionally removes its argument expressions. Retained expressions
        // must keep their original evaluation order and original parameter values.
        Assert.AreEqual(scenario == "remove-expanded" ? "12|120" : originalValue, candidateValue);
        if (scenario.EndsWith("omitted", StringComparison.Ordinal))
            await AssertOptionalArgumentBindingsAsync(original, stored.Value.ModifiedSolution, path);
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        if (scenario.EndsWith("omitted", StringComparison.Ordinal))
            await AssertOptionalArgumentBindingsAsync(original, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), path);
    }

    private static async Task<string> RunSignatureFixtureAsync(
        Microsoft.CodeAnalysis.Solution solution, string typeName)
    {
        var project = solution.Projects.Single(p => p.Name == "SampleLib");
        var compilation = await project.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.IsTrue(emission.Success, string.Join("\n", emission.Diagnostics.OrderByDescending(d => d.Severity)));
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("signature-proof", isCollectible: true);
        context.Resolving += (loader, name) =>
        {
            var reference = compilation.References.OfType<Microsoft.CodeAnalysis.PortableExecutableReference>()
                .FirstOrDefault(r => r.FilePath is { } file && File.Exists(file)
                    && System.Reflection.AssemblyName.GetAssemblyName(file).Name == name.Name);
            if (reference?.FilePath is not { } dependency) return null;
            using var dependencyImage = new MemoryStream(File.ReadAllBytes(dependency));
            return loader.LoadFromStream(dependencyImage);
        };
        try
        {
            var assembly = context.LoadFromStream(image);
            var method = assembly.GetType(typeName, throwOnError: true)!.GetMethod("Run");
            Assert.IsNotNull(method);
            return Convert.ToString(method.Invoke(null, null), System.Globalization.CultureInfo.InvariantCulture)!;
        }
        finally
        {
            context.Unload();
        }
    }

    [TestMethod]
    [DataRow("class", "add", 0, "type")]
    [DataRow("class", "add", 1, "constructor")]
    [DataRow("class", "add", 3, "type")]
    [DataRow("struct", "add", 0, "type")]
    [DataRow("struct", "add", 1, "constructor")]
    [DataRow("struct", "add", 3, "type")]
    [DataRow("class", "remove", 0, "parameter")]
    [DataRow("class", "remove", 1, "type")]
    [DataRow("class", "remove", 2, "constructor")]
    [DataRow("struct", "remove", 0, "parameter")]
    [DataRow("struct", "remove", 1, "type")]
    [DataRow("struct", "remove", 2, "constructor")]
    public async Task ChangeSignaturePreview_PrimaryMatrix_PreservesEvaluation(
        string kind, string op, int position, string locatorKind)
    {
        var declaration = $"public {kind} PrimaryMatrix(int first, int second, int third) {{ }}";
        var source = "namespace SampleLib;\n" + declaration + "\n" +
            "public static class SignatureProof { private static string trace = \"\"; " +
            "public static int Trace(int x) { trace += x; return x; } " +
            "public static string Run() { _ = new PrimaryMatrix(Trace(1), Trace(2), Trace(3)); return trace; } }";
        var request = op == "add"
            ? new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: position)
            : new ChangeSignatureRequest("remove", Position: locatorKind == "parameter" ? null : position);
        var column = locatorKind == "parameter"
            ? declaration.IndexOf(new[] { "first", "second", "third" }[position], StringComparison.Ordinal) + 1
            : declaration.IndexOf("PrimaryMatrix", StringComparison.Ordinal) + 1;
        if (locatorKind == "constructor")
            column = source.Split('\n')[2].IndexOf("new PrimaryMatrix", StringComparison.Ordinal) + 5;
        await AssertSignatureProofAsync(source, request,
            locatorKind == "constructor" ? 3 : 2, column,
            op == "add" ? "123" : new[] { "23", "13", "12" }[position], 1);
    }

    [TestMethod]
    [DataRow("named")]
    [DataRow("mixed")]
    [DataRow("generic")]
    [DataRow("target-typed")]
    [DataRow("empty")]
    [DataRow("last-class")]
    [DataRow("last-struct")]
    [DataRow("this")]
    [DataRow("base")]
    [DataRow("primary-base")]
    [DataRow("nested")]
    public async Task ChangeSignaturePreview_PrimaryBoundShapes(string shape)
    {
        var declaration = "public class BoundTarget(int removed, int retained) { }";
        var call = "new BoundTarget(Trace(1), Trace(2))";
        var expected = "2";
        var count = 1;
        var request = new ChangeSignatureRequest("remove", Position: 0);
        var extra = "";
        switch (shape)
        {
            case "named":
                call = "new BoundTarget(retained: Trace(2), removed: Trace(1))";
                break;
            case "mixed":
                declaration = "public class BoundTarget(int removed, int retained, int tail) { }";
                call = "new BoundTarget(Trace(1), tail: Trace(3), retained: Trace(2))";
                expected = "32";
                break;
            case "generic":
                declaration = "public class BoundTarget<T>(int removed, T retained) { }";
                call = "new BoundTarget<long>(Trace(1), Trace(2))";
                break;
            case "target-typed":
                call = "new(Trace(1), Trace(2))";
                break;
            case "empty":
                declaration = "public class BoundTarget() { }";
                call = "new BoundTarget()";
                expected = "";
                request = new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int");
                break;
            case "last-class":
                declaration = "public class BoundTarget(int removed) { }";
                call = "new BoundTarget(Trace(1))";
                expected = "";
                break;
            case "last-struct":
                declaration = "public struct BoundTarget(int removed) { }";
                call = "new BoundTarget(Trace(1))";
                expected = "";
                break;
            case "this":
                declaration = "public class BoundTarget(int removed, int retained) { public BoundTarget() : this(SignatureProof.Trace(1), SignatureProof.Trace(2)) { } }";
                call = "new BoundTarget()";
                break;
            case "base":
                extra = "public class Derived : BoundTarget { public Derived() : base(SignatureProof.Trace(1), SignatureProof.Trace(2)) { } }\n";
                call = "new Derived()";
                break;
            case "primary-base":
                extra = "public class Derived() : BoundTarget(SignatureProof.Trace(1), SignatureProof.Trace(2)) { }\n";
                call = "new Derived()";
                break;
            case "nested":
                declaration = "public class BoundTarget(int removed, object retained) { }";
                call = "new BoundTarget(Trace(1), new BoundTarget(Trace(2), Trace(3)))";
                expected = "3";
                count = 2;
                break;
        }
        var resultType = shape == "generic" ? "BoundTarget<long>" : shape is "base" or "primary-base" ? "Derived" : "BoundTarget";
        var source = "namespace SampleLib;\n" + declaration + "\n" + extra +
            "public static class SignatureProof { private static string trace = \"\"; " +
            "public static int Trace(int x) { trace += x; return x; } " +
            "public static string Run() { " + resultType + " value = " + call + "; return trace; } }";
        await AssertSignatureProofAsync(source, request, 2,
            declaration.IndexOf("BoundTarget", StringComparison.Ordinal) + 1, expected, count);
    }

    [TestMethod]
    [DataRow("int[]")]
    [DataRow("System.Collections.Generic.List<int>")]
    public async Task ChangeSignaturePreview_PrimaryParams_PreservesElementConversions(string parameterType)
    {
        var declaration = $"public class BoundTarget(int first, params {parameterType} tail) {{ public int Sum => first + System.Linq.Enumerable.Sum(tail); }}";
        var source = "namespace SampleLib;\n" + declaration + "\n" +
            "public static class SignatureProof { private static string trace = \"\"; " +
            "public static short Trace(short x) { trace += x; return x; } " +
            "public static string Run() { var value = new BoundTarget(Trace(1), Trace(2), Trace(3)); return trace + \"|\" + value.Sum; } }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 0),
            2, declaration.IndexOf("BoundTarget", StringComparison.Ordinal) + 1, "123|6", 1);
    }

    private static async Task AssertSignatureProofAsync(
        string source, ChangeSignatureRequest request, int line, int column, string? expected, int expectedCalls)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var originalValue = await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof");
        var expectedValue = expected ?? originalValue;
        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, SymbolLocator.BySource(path, line, column), request, CancellationToken.None);
        var pair = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(pair);
        Assert.AreEqual(expectedValue, await RunSignatureFixtureAsync(pair.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        Assert.AreEqual(expectedCalls, preview.CallsiteUpdates?.Sum(c => c.CallsiteCount));
        var proofText = (await pair.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib")
            .Documents.Single(d => d.FilePath == path).GetTextAsync()).ToString();
        foreach (var marker in new[] { "init-comma", "iterator-comma", "receiver", "question", "dot", "name", "arrow", "semicolon" })
        {
            var comment = "/*signature-" + marker + "*/";
            Assert.AreEqual(source.Split(comment, StringSplitOptions.None).Length - 1,
                proofText.Split(comment, StringSplitOptions.None).Length - 1, comment);
        }
        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        Assert.AreEqual(expectedValue, await RunSignatureFixtureAsync(
            WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }

    [TestMethod]
    [DataRow("capture")]
    [DataRow("base-reference")]
    [DataRow("optional-placement")]
    [DataRow("duplicate-name")]
    [DataRow("unknown-type")]
    [DataRow("invalid-syntax")]
    [DataRow("params-placement")]
    [DataRow("struct-initializer")]
    [DataRow("overload-drift")]
    [DataRow("valid-warning")]
    public async Task ChangeSignaturePreview_ConcreteInvalidCandidate_IsAtomic(string shape)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var declaration = "public class AtomicTarget(int removed) { }";
        var request = new ChangeSignatureRequest("remove", Position: 0);
        var extra = "";
        switch (shape)
        {
            case "capture":
                declaration = "public class AtomicTarget(int removed) { public int Value => removed; }";
                break;
            case "base-reference":
                declaration = "public class AtomicTarget(int removed) : System.Exception(removed.ToString()) { }";
                break;
            case "optional-placement":
                request = new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", DefaultValue: "0", Position: 0);
                break;
            case "duplicate-name":
                request = new ChangeSignatureRequest("add", Name: "removed", ParameterType: "int");
                break;
            case "unknown-type":
                request = new ChangeSignatureRequest("add", Name: "marker", ParameterType: "UndefinedParameterType");
                break;
            case "invalid-syntax":
                request = new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int) { } class Injected(");
                break;
            case "params-placement":
                declaration = "public class AtomicTarget(params int[] removed) { }";
                request = new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int");
                break;
            case "struct-initializer":
                declaration = "public struct AtomicTarget(int removed) { public int Value = removed; }";
                break;
            case "overload-drift":
                declaration = "public class AtomicTarget(int removed, object retained) { public AtomicTarget(string retained) : this(0, (object)retained) { } }";
                extra = "public static class AtomicCaller { public static AtomicTarget Make() => new AtomicTarget(1, \"value\"); }";
                break;
            case "valid-warning":
                request = new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int");
                break;
        }
        var path = workspace.GetPath("SampleLib", "AtomicTarget.cs");
        var source = "namespace SampleLib;\n" + declaration + "\n" + extra;
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var probe = new SignatureStoreProbe(PreviewStore);
        var service = new ChangeSignatureService(WorkspaceManager, probe, RefactoringService);
        var locator = SymbolLocator.BySource(path, 2, declaration.IndexOf("AtomicTarget", StringComparison.Ordinal) + 1);
        if (shape == "valid-warning")
        {
            var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator, request, CancellationToken.None);
            Assert.AreEqual(1, probe.StoreCalls);
            Assert.IsNotNull(preview.Warnings);
            Assert.IsTrue(preview.Warnings.Any(w => w.Contains("CS9113", StringComparison.Ordinal)));
        }
        else
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator, request, CancellationToken.None));
            Assert.IsFalse(error.Message.Contains(path, StringComparison.Ordinal), "authored evidence must be path-free");
            Assert.AreEqual(0, probe.StoreCalls, "invalid candidates must never reach token storage");
        }
        Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_CallerInfo_IsMaterializedFromOriginalOperation()
    {
        var declaration = "public class CallerTarget(int value, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0, " +
            "[System.Runtime.CompilerServices.CallerMemberName] string member = \"\", " +
            "[System.Runtime.CompilerServices.CallerArgumentExpression(\"value\")] string expression = \"\") " +
            "{ public string Value => value + \"|\" + line + \"|\" + member + \"|\" + expression; }";
        var source = "namespace SampleLib;\n" + declaration + "\n" +
            "public static class SignatureProof { public static string Run() => new CallerTarget(1 + 2).Value; }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 0),
            2, declaration.IndexOf("CallerTarget", StringComparison.Ordinal) + 1, "3|3|Run|1 + 2", 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_PreservesInternalAndCommaTrivia(bool named)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "TriviaTarget.cs");
        var declaration = "public class TriviaTarget(int /*parameter-one*/ first, /*parameter-comma*/ int second = 7) { }";
        var call = named
            ? "new TriviaTarget(first /*name-comment*/: /*colon-comment*/ 1, /*argument-comma*/ second: 2)"
            : "new TriviaTarget(1, /*argument-comma*/ 2)";
        var source = "namespace SampleLib;\n" + declaration + "\npublic static class TriviaCaller { public static object Run() => " + call + "; }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf("TriviaTarget", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", DefaultValue: "9"), CancellationToken.None);
        var pair = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(pair);
        var candidate = await pair.Value.ModifiedSolution.GetDocument(
            pair.Value.ModifiedSolution.GetDocumentIdsWithFilePath(path).Single())!.GetTextAsync();
        foreach (var marker in named
                     ? new[] { "/*parameter-one*/", "/*parameter-comma*/", "/*argument-comma*/", "/*name-comment*/", "/*colon-comment*/" }
                     : new[] { "/*parameter-one*/", "/*parameter-comma*/", "/*argument-comma*/" })
            Assert.AreEqual(1, candidate.ToString().Split(marker, StringSplitOptions.None).Length - 1, marker);
        var compilation = await pair.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync();
        Assert.IsNotNull(compilation);
        Assert.AreEqual(0, compilation.GetDiagnostics().Count(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_CrossDocument_DefaultStructAndUnrelatedOverloadStayBound()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var declarationPath = workspace.GetPath("SampleLib", "CrossTarget.cs");
        var callerPath = workspace.GetPath("SampleLib", "CrossCaller.cs");
        var declaration = "public struct CrossTarget(int removed, int retained) { public int Value => retained; public CrossTarget(string value) : this(0, int.Parse(value)) { } }";
        await File.WriteAllTextAsync(declarationPath, "namespace SampleLib;\n" + declaration);
        await File.WriteAllTextAsync(callerPath, "namespace SampleLib;\npublic static class CrossCaller { public static int Run() => " +
            "new CrossTarget(1, 2).Value + new CrossTarget(\"3\").Value + new CrossTarget().Value + default(CrossTarget).Value; }");
        await workspace.LoadAsync(CancellationToken.None);
        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(declarationPath, 2, declaration.IndexOf("CrossTarget", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None);
        var pair = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(pair);
        Assert.AreEqual("5", await RunSignatureFixtureAsync(pair.Value.ModifiedSolution, "SampleLib.CrossCaller"));
        Assert.AreEqual(2, preview.Changes.Count);
        Assert.AreEqual(2, preview.CallsiteUpdates?.Sum(c => c.CallsiteCount));
        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        var changed = await File.ReadAllTextAsync(callerPath);
        StringAssert.Contains(changed, "new CrossTarget(\"3\")");
        StringAssert.Contains(changed, "new CrossTarget()");
        StringAssert.Contains(changed, "default(CrossTarget)");
    }

    private sealed class SignatureStoreProbe(IPreviewStore inner) : IPreviewStore
    {
        public int StoreCalls { get; private set; }

        public string Store(string workspaceId, Microsoft.CodeAnalysis.Solution modifiedSolution, int workspaceVersion, string description)
        {
            StoreCalls++;
            return inner.Store(workspaceId, modifiedSolution, workspaceVersion, description);
        }

        public string Store(string workspaceId, Microsoft.CodeAnalysis.Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated)
        {
            StoreCalls++;
            return inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, diffTruncated);
        }

        public string Store(string workspaceId, Microsoft.CodeAnalysis.Solution modifiedSolution, int workspaceVersion, string description, IReadOnlyList<FileChangeDto> changes)
        {
            StoreCalls++;
            return inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, changes);
        }

        public (string WorkspaceId, Microsoft.CodeAnalysis.Solution OriginalSolution, Microsoft.CodeAnalysis.Solution ModifiedSolution,
            int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token) => inner.Retrieve(token);

        public void Invalidate(string token) => inner.Invalidate(token);
        public void InvalidateAll(string? workspaceId = null) => inner.InvalidateAll(workspaceId);
        public void InvalidateOnVersionBump(string workspaceId, int newWorkspaceVersion) => inner.InvalidateOnVersionBump(workspaceId, newWorkspaceVersion);
        public string? PeekWorkspaceId(string token) => inner.PeekWorkspaceId(token);
    }

    [TestMethod]
    [DataRow("float", "float.NaN")]
    [DataRow("float", "float.PositiveInfinity")]
    [DataRow("float", "float.NegativeInfinity")]
    [DataRow("double", "double.NaN")]
    [DataRow("double", "double.PositiveInfinity")]
    [DataRow("double", "double.NegativeInfinity")]
    [DataRow("float", "-0F")]
    [DataRow("double", "-0D")]
    public async Task ChangeSignaturePreview_OptionalConstants_PreserveExactRuntimeValues(string kind, string defaultValue)
    {
        var bits = kind == "float" ? "System.BitConverter.SingleToInt32Bits(value)" : "System.BitConverter.DoubleToInt64Bits(value)";
        var declaration = $"public static string Compute({kind} value = {defaultValue}, decimal amount = 12.50M, " +
            "Choice choice = Choice.Third, char character = 'z', bool flag = true, string? label = null) => " +
            bits + " + \"|\" + string.Join(\",\", decimal.GetBits(amount)) + \"|\" + (int)choice + \"|\" + character + \"|\" + flag + \"|\" + (label ?? \"null\");";
        var source = "namespace SampleLib;\npublic enum Choice { Third = 3 }\n" +
            "public static class SignatureProof { " + declaration + " public static string Run() => Compute(); }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", DefaultValue: "9"),
            3, source.Split('\n')[2].IndexOf("Compute", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_ParamsCollection_PreservesBuilderAllocationAndEvaluationOrder()
    {
        var declaration = "public class BuilderTarget(int first, params NumberCollection tail) { public int Sum => first + System.Linq.Enumerable.Sum(tail); }";
        var source = "namespace SampleLib;\n" + declaration + "\n" +
            "[System.Runtime.CompilerServices.CollectionBuilder(typeof(NumberBuilder), \"Create\")] " +
            "public class NumberCollection : System.Collections.Generic.IEnumerable<int> { private readonly int[] items; " +
            "public NumberCollection(int[] source) { SignatureProof.TraceText += \"A\"; items = source; } " +
            "public System.Collections.Generic.IEnumerator<int> GetEnumerator() => ((System.Collections.Generic.IEnumerable<int>)items).GetEnumerator(); " +
            "System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); } " +
            "public static class NumberBuilder { public static NumberCollection Create(System.ReadOnlySpan<int> items) " +
            "{ SignatureProof.TraceText += \"B\"; return new NumberCollection(items.ToArray()); } } " +
            "public static class SignatureProof { public static string TraceText = \"\"; " +
            "public static int Trace(int value) { TraceText += value; return value; } " +
            "public static string Run() { var value = new BuilderTarget(Trace(1), Trace(2), Trace(3)); return TraceText + \"|\" + value.Sum; } }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 0),
            2, declaration.IndexOf("BuilderTarget", StringComparison.Ordinal) + 1, "123BA|6", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_ParamsElements_PreserveUserDefinedConversionOrder()
    {
        var declaration = "public class ConversionTarget(int first, params int[] tail) { public int Sum => first + System.Linq.Enumerable.Sum(tail); }";
        var source = "namespace SampleLib;\n" + declaration + "\n" +
            "public readonly struct Number(int value) { public int Value => value; public static implicit operator int(Number number) " +
            "{ SignatureProof.TraceText += \"C\" + number.Value; return number.Value; } } " +
            "public static class SignatureProof { public static string TraceText = \"\"; " +
            "public static Number Trace(int value) { TraceText += \"T\" + value; return new Number(value); } " +
            "public static string Run() { var value = new ConversionTarget(Trace(1), Trace(2), Trace(3)); return TraceText + \"|\" + value.Sum; } }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 0),
            2, declaration.IndexOf("ConversionTarget", StringComparison.Ordinal) + 1, "T1C1T2C2T3C3|6", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_MethodGroupOverloadDrift_IsRefusedWithoutToken()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "MethodGroupDrift.cs");
        var source = "namespace SampleLib;\npublic static class MethodGroupDrift { " +
            "public static int Compute(int first) => first; " +
            "public static int Compute(int first, object second) => first; " +
            "public static System.Func<int, string, int> Capture() => Compute; " +
            "public static int Direct() => Compute(1); }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var probe = new SignatureStoreProbe(PreviewStore);
        var service = new ChangeSignatureService(WorkspaceManager, probe, RefactoringService);
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
                SymbolLocator.BySource(path, 2, source.Split('\n')[1].IndexOf("Compute", StringComparison.Ordinal) + 1),
                new ChangeSignatureRequest("add", Name: "second", ParameterType: "string"),
                CancellationToken.None));
        StringAssert.Contains(error.Message, "retained target");
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    private static async Task AssertOptionalArgumentBindingsAsync(
        Microsoft.CodeAnalysis.Solution original, Microsoft.CodeAnalysis.Solution candidate, string path)
    {
        static async Task<Microsoft.CodeAnalysis.Operations.IInvocationOperation> ComputeOperationAsync(
            Microsoft.CodeAnalysis.Solution solution, string file)
        {
            var document = solution.GetDocument(solution.GetDocumentIdsWithFilePath(file).Single())!;
            var root = await document.GetSyntaxRootAsync();
            var model = await document.GetSemanticModelAsync();
            Assert.IsNotNull(root);
            Assert.IsNotNull(model);
            var call = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Single(n => n.Expression.ToString() == "Compute");
            var operation = model.GetOperation(call) as Microsoft.CodeAnalysis.Operations.IInvocationOperation;
            Assert.IsNotNull(operation);
            return operation;
        }
        var before = await ComputeOperationAsync(original, path);
        var after = await ComputeOperationAsync(candidate, path);
        Assert.AreEqual(before.TargetMethod.ContainingType.Name, after.TargetMethod.ContainingType.Name);
        Assert.AreEqual(before.TargetMethod.Name, after.TargetMethod.Name);
        foreach (var argument in before.Arguments)
        {
            Assert.IsNotNull(argument.Parameter);
            var retained = after.Arguments.Single(a => a.Parameter?.Name == argument.Parameter.Name);
            Assert.IsNotNull(retained.Parameter);
            Assert.AreEqual(argument.Parameter.Type.ToDisplayString(), retained.Parameter.Type.ToDisplayString());
            Assert.IsTrue(argument.Value.ConstantValue.HasValue);
            Assert.IsTrue(retained.Value.ConstantValue.HasValue);
            Assert.AreEqual(argument.Value.ConstantValue.Value, retained.Value.ConstantValue.Value,
                $"original binding for {argument.Parameter.Name} must retain its value");
            Assert.AreEqual(argument.InConversion.IsIdentity, retained.InConversion.IsIdentity);
        }
    }

    [TestMethod]
    [DataRow("ref")]
    [DataRow("out")]
    [DataRow("in")]
    public async Task ChangeSignaturePreview_OrdinaryMethod_PreservesRefKindBindings(string modifier)
    {
        var assignment = modifier == "out" ? "value = 1; " : "";
        var source = "namespace SampleLib;\npublic static class SignatureProof { " +
            "public static int Compute(" + modifier + " int value, int optional = 7) { " + assignment + "return value + optional; } " +
            "public static int Run() { int value = 1; return Compute(" + modifier + " value); } }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", DefaultValue: "9"),
            2, source.Split('\n')[1].IndexOf("Compute", StringComparison.Ordinal) + 1, "8", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_PartialMethod_UpdatesDefinitionAndImplementation()
    {
        var source = "namespace SampleLib;\n" +
            "public partial class PartialTarget { public partial int Compute(int value); }\n" +
            "public partial class PartialTarget { public partial int Compute(int value) => value; }\n" +
            "public static class SignatureProof { public static int Run() => new PartialTarget().Compute(1); }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", DefaultValue: "0"),
            2, source.Split('\n')[1].IndexOf("Compute", StringComparison.Ordinal) + 1, "1", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_OverrideTarget_UpdatesBaseAndSiblingOverrides()
    {
        var source = "namespace SampleLib;\n" +
            "public class SignatureBase { public virtual int Compute(int value) => value; }\n" +
            "public class SignatureLeft : SignatureBase { public override int Compute(int value) => value + 1; }\n" +
            "public class SignatureRight : SignatureBase { public override int Compute(int value) => value + 2; }\n" +
            "public static class SignatureProof { public static int Run() { SignatureBase left = new SignatureLeft(), right = new SignatureRight(); return left.Compute(1) + right.Compute(1); } }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", DefaultValue: "0"),
            3, source.Split('\n')[2].IndexOf("Compute", StringComparison.Ordinal) + 1, "5", 2);
    }

    [TestMethod]
    [DataRow("add")]
    [DataRow("remove")]
    [DataRow("reorder")]
    public async Task ChangeSignaturePreview_ReducedExtension_PreservesOriginalParameterBindings(string op)
    {
        var source = "namespace SampleLib;\npublic static class SignatureExtensions { " +
            "public static int Compute(this int receiver, int unused = 7, int retained = 2) => receiver * 10 + retained; }\n" +
            "public static class SignatureProof { public static int Run() => 1.Compute(); }";
        var request = op switch
        {
            "add" => new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 1),
            "remove" => new ChangeSignatureRequest("remove", Name: "unused"),
            _ => new ChangeSignatureRequest("reorder", NewOrder: "receiver,retained,unused"),
        };
        await AssertSignatureProofAsync(source, request, 2,
            source.Split('\n')[1].IndexOf("Compute", StringComparison.Ordinal) + 1, "12", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_RemoveUnusedExtensionReceiver_RemovesOnlyItsEvaluation()
    {
        var source = "namespace SampleLib;\npublic static class SignatureExtensions { " +
            "public static int Compute(this int unused, int retained) => retained; }\n" +
            "public static class SignatureProof { private static string trace = \"\"; " +
            "public static int Trace(int value) { trace += value; return value; } " +
            "public static string Run() { var value = Trace(1).Compute(Trace(2)); return trace + \"|\" + value; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"),
            2, source.Split('\n')[1].IndexOf("Compute", StringComparison.Ordinal) + 1, "2|2", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_EmptyCandidate_StoresNoToken()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var probe = new SignatureStoreProbe(PreviewStore);
        var service = new ChangeSignatureService(WorkspaceManager, probe, RefactoringService);
        // Reach the defensive empty-candidate boundary directly: valid non-identity signature
        // operations always edit a source declaration before this boundary.
        var builder = typeof(ChangeSignatureService).GetMethod("BuildPreviewDto",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(builder);
        var error = Assert.ThrowsExactly<System.Reflection.TargetInvocationException>(() =>
            builder.Invoke(service, [
                workspace.WorkspaceId, original, new List<FileChangeDto>(),
                new List<CallsiteUpdateDto>(), new List<string>(), "empty candidate",
            ]));
        Assert.AreEqual(typeof(PublicInvalidOperationException), error.InnerException?.GetType());
        StringAssert.Contains(error.InnerException!.Message, "no editable declaration changes");
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }


    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_RemoveExtensionReceiver_PreservesGenericSyntax(bool anonymous)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "ExtensionGenericProof.cs");
        var declaration = "public static class SignatureExtensions { public static string Compute<T>(this Receiver receiver, T value) where T : notnull => typeof(T).Name + \":\" + value.ToString(); }";
        var call = anonymous
            ? "new Receiver() /*before-dot*/ . /*after-dot*/ Compute /*method*/ (new { Value = 2 })"
            : "new Receiver() /*before-dot*/ . /*after-dot*/ Compute /*method*/ < /*type*/ int /*type-close*/ >(2)";
        var source = "namespace SampleLib;\npublic sealed class Receiver { }\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => " + call + "; }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var expected = await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof");
        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 3, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None);
        var stored = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        var candidate = stored.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
        var text = (await candidate.GetTextAsync()).ToString();
        foreach (var marker in anonymous ? new[] { "before-dot", "after-dot", "method" } : new[] { "before-dot", "after-dot", "method", "type", "type-close" })
            Assert.AreEqual(1, text.Split("/*" + marker + "*/", StringSplitOptions.None).Length - 1, marker);
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }

    [TestMethod]
    [DataRow("value", false)]
    [DataRow("value", true)]
    [DataRow("void", false)]
    [DataRow("void", true)]
    [DataRow("arrow", false)]
    [DataRow("arrow", true)]
    [DataRow("for", false)]
    [DataRow("for", true)]
    [DataRow("direct-loop", false)]
    [DataRow("flow", false)]
    [DataRow("return", false)]
    [DataRow("init", false)]
    [DataRow("chain", false)]
    [DataRow("chain", true)]
    [DataRow("lambda", false)]
    [DataRow("lambda", true)]
    [DataRow("lambda-value", false)]
    [DataRow("lambda-value", true)]
    [DataRow("ctor", false)]
    [DataRow("ctor", true)]
    public async Task ChangeSignaturePreview_RemoveExtensionReceiver_PreservesConditionalControl(string shape, bool isNull)
    {
        var declaration = shape is "value" or "lambda-value"
            ? "public static class SignatureExtensions { public static int Compute(this Receiver receiver, int value) => SignatureProof.Mark(\"M\", value); }"
            : "public static class SignatureExtensions { public static void Compute(this Receiver receiver, int value) { SignatureProof.Mark(\"M\", value); } }";
        var run = shape switch
        {
            "value" => "var value = Get()?.Compute(Argument()); return trace + \"|\" + Select(value);",
            "void" => "Get()?.Compute(Argument()); return trace;",
            "arrow" => "Call(); return trace;",
            "direct-loop" => "int inner = 0; for (int i = 0; i < 2; i++, Get()?.Compute(Argument())) while (inner++ < 3) { trace += \"W\"; continue; } return trace;",
            "flow" => "for (int i = 0; i < 3; i++, Get()?.Compute(Argument())) { try { for (int j = 0; j < 2; j++) { trace += \"N\"; continue; } if (i == 1) break; trace += \"B\"; continue; } finally { trace += \"F\"; } } return trace;",
            "return" => "try { for (int i = 0; i < 2; i++, Get()?.Compute(Argument())) { trace += \"B\"; return trace; } } finally { trace += \"F\"; } return trace;",
            "init" => "int i = 0; for (Mark(\"I\", 0) /*signature-init-comma*/, Get() /*signature-receiver*/ ? /*signature-question*/ . /*signature-dot*/ Compute /*signature-name*/ (Argument()), Mark(\"J\", 0); i < 1; Mark(\"K\", 0) /*signature-iterator-comma*/, Get()?.Compute(Argument()), i++) { trace += \"B\"; } return trace;",
            "chain" => "Get()?.Self()?.Compute(Argument()); return trace;",
            "lambda" or "lambda-value" => "System.Action action = () => Get()?.Compute(Argument()); action(); return trace;",
            "ctor" => "new Holder(); return trace;",
            _ => "for (int i = 0; i < 2; i++, Get()?.Compute(Argument())) { try { trace += \"B\"; continue; } finally { trace += \"F\"; } } return trace;",
        };
        var arrow = shape == "arrow" ? "public static void Call() => /*signature-arrow*/ Get()?.Compute(Argument()); /*signature-semicolon*/"
            : shape == "ctor" ? "private sealed class Holder { public Holder() => Get()?.Compute(Argument()); ~Holder() => ((Receiver?)null)?.Compute(Argument()); }" : "";
        var source = "namespace SampleLib;\npublic sealed class Receiver { public Receiver Self() { SignatureProof.Mark(\"S\", 0); return this; } }\n" + declaration + "\n"
            + "public static class SignatureProof { private static string trace = \"\"; "
            + "public static int Mark(string id, int value) { trace += id; return value; } "
            + "private static Receiver? Get() { trace += \"R\"; return " + (isNull ? "null" : "new Receiver()") + "; } "
            + "private static int Argument() => Mark(\"A\", 7); "
            + "private static string Select(int? value) => \"nullable:\" + value; "
            + "private static string Select(int value) => \"value:\" + value; " + arrow
            + "public static string Run() { " + run + " } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Position: 0),
            3, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1, null, shape is "ctor" or "init" ? 2 : 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_DiagnosticExchange_IsAtomic(bool boundConditional)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "DiagnosticExchange.cs");
        var declaration = "public class DiagnosticTarget { public static int M(int missing) => missing; public static int Run() => M(" + (boundConditional ? "missing ? 1 : 2" : "missing") + "); }";
        var source = "namespace SampleLib;\n" + declaration;
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = original.Projects.Single(p => p.Name == "SampleLib");
        var diagnostic = (await project.GetCompilationAsync())!.GetDiagnostics().Single(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.AreEqual("CS0103", diagnostic.Id);
        Assert.IsTrue(diagnostic.Location.SourceSpan.Start > source.IndexOf("Run()", StringComparison.Ordinal));
        var document = project.Documents.Single(d => d.FilePath == path);
        var root = (await document.GetSyntaxRootAsync())!;
        var model = (await document.GetSemanticModelAsync())!;
        var call = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().Single();
        if (boundConditional) Assert.IsNotNull(model.GetSymbolInfo(call).Symbol, "typed conditional argument must retain an actual original method binding");
        else
        {
            Assert.IsNull(model.GetSymbolInfo(call).Symbol);
            Assert.IsTrue(model.GetSymbolInfo(call).CandidateSymbols.OfType<Microsoft.CodeAnalysis.IMethodSymbol>()
                .Any(m => m.Name == "M"));
        }
        var probe = new SignatureStoreProbe(PreviewStore);
        var service = new ChangeSignatureService(WorkspaceManager, probe, RefactoringService);
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
                SymbolLocator.BySource(path, 2, declaration.IndexOf("M(", StringComparison.Ordinal) + 1),
                new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None));
        if (boundConditional) StringAssert.Contains(error.Message, "CS0103");
        else Assert.AreEqual("An unresolved invocation may bind to the edited member. Resolve its binding errors before changing this signature.", error.Message);
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("syntax")]
    [DataRow("entrypoint")]
    public async Task ChangeSignaturePreview_UnrelatedExistingDiagnostic_RemainsAccepted(string shape)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "ExistingDiagnostic.cs");
        var broken = shape == "missing" ? "public static int Broken() => missing;" : shape == "syntax" ? "public static int Broken() =>;" : "";
        var declaration = "public class DiagnosticTarget { public static int M(int unused, int retained) => retained; public static int Run() => M(1, 2); " + broken + " }";
        if (shape == "entrypoint")
        {
            var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
            var projectText = await File.ReadAllTextAsync(projectPath);
            await File.WriteAllTextAsync(projectPath, projectText.Replace("<PropertyGroup>", "<PropertyGroup><OutputType>Exe</OutputType>", StringComparison.Ordinal));
        }
        await File.WriteAllTextAsync(path, "namespace SampleLib;\n" + declaration);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var before = (await original.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
        Assert.IsTrue(before.Length > 0);
        var probe = new SignatureStoreProbe(PreviewStore);
        var service = new ChangeSignatureService(WorkspaceManager, probe, RefactoringService);
        var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf("M(", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None);
        Assert.AreEqual(1, probe.StoreCalls);
        var stored = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        var after = (await stored.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
        CollectionAssert.AreEqual(before.Select(d => d.Id + ":" + d.GetMessage()).Order(StringComparer.Ordinal).ToArray(), after.Select(d => d.Id + ":" + d.GetMessage()).Order(StringComparer.Ordinal).ToArray());
        if (shape != "entrypoint") Assert.AreNotEqual(before[0].Location.SourceSpan.Start, after[0].Location.SourceSpan.Start, "the surviving original diagnostic moves with retained syntax");
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_IndependentInitializersRetainConstruction()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => new SelectedStruct(2).Value + \"|\" + new SelectedStruct().Value; }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var document = original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
        var represented = source.Replace(declaration,
            "public struct SelectedStruct { public int Value = 7; public SelectedStruct() { } }", StringComparison.Ordinal)
            .Replace("new SelectedStruct().Value", "default(SelectedStruct).Value", StringComparison.Ordinal)
            .Replace("new SelectedStruct(2).Value", "new SelectedStruct().Value", StringComparison.Ordinal);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(original.WithDocumentText(document.Id,
            Microsoft.CodeAnalysis.Text.SourceText.From(represented)), "SampleLib.SignatureProof"));
        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        var stored = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }

    [TestMethod]
    [DataRow("explicit")]
    [DataRow("implicit")]
    [DataRow("generic")]
    [DataRow("property")]
    public async Task ChangeSignaturePreview_LastStructParameter_DefaultInitializerRetainsEffects(string shape)
    {
        var declaration = shape == "property"
            ? "public struct SelectedStruct(int unused) { public int Value { get; set; } = SignatureProof.Trace(7); public int Other { get; set; } }"
            : "public struct SelectedStruct" + (shape == "generic" ? "<T>" : "")
                + "(int unused) { public int Value = SignatureProof.Trace(7); public int Other { get; set; } }";
        var type = shape == "generic" ? "SelectedStruct<string>" : "SelectedStruct";
        var zero = shape == "implicit" ? "new()" : "new " + type + "()";
        var source = "namespace SampleLib;\n" + declaration + "\npublic static class SignatureProof { static string trace = \"\"; public static int Trace(int value) { trace += value; return value; } public static string Run() { "
            + type + " zero = " + zero + " { /*signature-dot*/ Other = Trace(3) }; var selected = new " + type
            + "(Trace(2)); return trace + \"|\" + zero.Value + \"|\" + zero.Other + \"|\" + selected.Value; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "37|0|3|7", 1);
    }

    [TestMethod]
    [DataRow("plain")]
    [DataRow("multiline")]
    [DataRow("no-initializer")]
    public async Task ChangeSignaturePreview_LastStructParameter_PreservesConstructorAttributes(string shape)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        var declaration = "[method: /*signature-method*/ Marker]" + (shape == "multiline" ? "\n" : " ")
            + "public struct SelectedStruct(int unused) { public int Value"
            + (shape == "no-initializer" ? " => 7;" : " = SignatureProof.Line();") + " }";
        var source = "namespace SampleLib;\n" + declaration
            + "\n[System.AttributeUsage(System.AttributeTargets.Constructor)] public sealed class MarkerAttribute : System.Attribute { }"
            + "\npublic static class SignatureProof { public static int Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line; public static string Run() => System.Linq.Enumerable.Count(typeof(SelectedStruct).GetConstructors(), c => c.IsDefined(typeof(MarkerAttribute), false)) + \"|\" + new SelectedStruct(2).Value; }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var expected = shape == "no-initializer" ? "1|7" : shape == "multiline" ? "1|3" : "1|2";
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, shape == "multiline" ? 3 : 2, declaration[(declaration.LastIndexOf("\n", StringComparison.Ordinal) + 1)..].IndexOf("SelectedStruct", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        var stored = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        var text = (await stored.Value.ModifiedSolution.GetDocument(original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path).Id)!.GetTextAsync()).ToString();
        Assert.AreEqual(1, text.Split("/*signature-method*/", StringSplitOptions.None).Length - 1);
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_SemicolonDeclaration()
    {
        var declaration = "public struct SelectedStruct(int unused);";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static bool Run() => new SelectedStruct(2).Equals(new SelectedStruct()); }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "True", 1);
    }

    [TestMethod]
    [DataRow("ordinary")]
    [DataRow("generic")]
    [DataRow("private")]
    [DataRow("escaped")]
    public async Task ChangeSignaturePreview_LastStructParameter_GenericDefaultConstruction(string shape)
    {
        var type = shape == "generic" ? "SelectedStruct<int>" : shape == "escaped" ? "@event.SelectedStruct" : "SelectedStruct";
        var declaration = "public struct SelectedStruct" + (shape == "generic" ? "<T>" : "") + "(int unused) { public int Value = 7; }";
        if (shape == "escaped") declaration = "namespace @event { " + declaration + " }";
        var allocationMethod = "public static long Allocated() { for (int i = 0; i < 10000; i++) _ = SignatureProof.Zero<" + type + ">(); var before = System.GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 1000; i++) _ = SignatureProof.Zero<" + type + ">(); return System.GC.GetAllocatedBytesForCurrentThread() - before; }";
        var selected = "new " + type + "(2).Value + \"|\" + SignatureProof.Zero<" + type + ">().Value";
        if (shape == "private")
        {
            declaration = "public class Owner { " + declaration.Replace("public struct", "private struct", StringComparison.Ordinal)
                + " public static string Proof() => " + selected + "; " + allocationMethod + " }";
            selected = "Owner.Proof()";
        }
        var source = (shape == "escaped" ? "namespace SampleLib {\n" : "namespace SampleLib;\n") + declaration
            + "\npublic struct OtherStruct { public int Value; public OtherStruct() { Value = 9; } } public class OtherClass { public int Value = 11; }"
            + "\npublic static class SignatureProof { " + (shape == "private" ? "" : allocationMethod) + " public static T Zero<T>() where T : new() => new T(); public static string Run() => "
            + selected + " + \"|\" + Zero<OtherStruct>().Value + \"|\" + Zero<OtherClass>().Value + \"|\" + " + (shape == "private" ? "Owner." : "") + "Allocated(); }" + (shape == "escaped" ? " }" : "");
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|9|11|0", 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_GenericInitializerRetainsEffects(bool structConstraint)
    {
        var declaration = "public struct SelectedStruct(int unused) : IValue { public int Value = 7; public int Other { get; set; } }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IValue { int Other { get; set; } } public struct OtherStruct : IValue { public int Other { get; set; } public OtherStruct() { Other = 9; } }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int Trace() { trace += \"3\"; return 3; } public static T Zero<T>() where T : "
            + (structConstraint ? "struct, IValue" : "IValue, new()")
            + " => new T { /*signature-dot*/ Other = Trace() }; public static string Run() { var selected = new SelectedStruct(2); var zero = Zero<SelectedStruct>(); var other = Zero<OtherStruct>(); return selected.Value + \"|\" + zero.Value + \"|\" + zero.Other + \"|\" + other.Other + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|3|3|33", 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_CollectionInitializerRetainsEffects(bool generic)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int Trace(int value) { trace += value; return value; } public static "
            + (generic ? "T Zero<T>() where T : IBag, new()" : "SelectedStruct Zero()")
            + " => new " + (generic ? "T" : "SelectedStruct") + " { /*signature-dot*/ Trace(3), Trace(4) }; public static string Run() { var selected = new SelectedStruct(2); var zero = "
            + (generic ? "Zero<SelectedStruct>()" : "Zero()") + "; return selected.Value + \"|\" + zero.Value + \"|\" + zero.Sum + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|7|34", 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_NestedCollectionRetainsEffects(bool generic)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBagOwner { public int Value = 7; public Bag Bag => SignatureProof.GetBag(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBagOwner { Bag Bag { get; } } public class Bag : System.Collections.Generic.List<int> { public new void Add(int value) { SignatureProof.Mark(); base.Add(value); } }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static Bag bag = new(); public static Bag GetBag() { trace += \"H\"; return bag; } public static void Mark() { trace += \"A\"; } static int Trace(int value) { trace += value; return value; } public static "
            + (generic ? "T Zero<T>() where T : IBagOwner, new()" : "SelectedStruct Zero()") + " => new " + (generic ? "T" : "SelectedStruct")
            + " { Bag = { /*signature-dot*/ Trace(3), Trace(4) } }; public static string Run() { var selected = new SelectedStruct(2); var zero = "
            + (generic ? "Zero<SelectedStruct>()" : "Zero()") + "; return selected.Value + \"|\" + zero.Value + \"|\" + string.Join(\"\", bag) + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_NestedIndexerRetainsEffects()
    {
        var declaration = "public struct SelectedStruct(int unused) : IChild { public int Value = 7; public Child this[int index] => SignatureProof.GetChild(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IChild { Child this[int index] { get; } } public class Child { public int A; public int B; }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static Child child = new(); public static Child GetChild() { trace += \"H\"; return child; } static int Trace(int value) { trace += value; return value; } public static T Zero<T>() where T : IChild, new() => new T { [Trace(1)] = { A = Trace(3), B = Trace(4) } }; public static string Run() { var selected = new SelectedStruct(2); var zero = Zero<SelectedStruct>(); return selected.Value + \"|\" + zero.Value + \"|\" + child.A + \"|\" + child.B + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow("static")]
    [DataRow("receiver")]
    [DataRow("ref")]
    [DataRow("readonly-receiver")]
    [DataRow("generic-receiver")]
    public async Task ChangeSignaturePreview_LastStructParameter_CollectionArgumentRetainsEffects(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int Trace(int value) { trace += value; return value; } " + (shape == "receiver"
                ? "public struct Receiver { public int Calls; public string Take<T>(int before, T value, int after) where T : IBag { Calls++; return value.Sum + \"|\" + trace; } } public static string Zero<T>() where T : IBag, new() { var receiver = new Receiver(); var result = receiver.Take(Trace(1), new T { Trace(3), Trace(4) }, Trace(5)); return result + \"|\" + receiver.Calls; }"
                : shape == "readonly-receiver"
                    ? "public struct Receiver { public int Calls; public string Take<T>(int before, T value, int after) where T : IBag { Calls++; return value.Sum + \"|\" + trace; } } public static string Call<T>(in Receiver receiver) where T : IBag, new() => receiver.Take(Trace(1), new T { Trace(3), Trace(4) }, Trace(5)); public static string Zero<T>() where T : IBag, new() { var receiver = new Receiver(); var result = Call<T>(in receiver); return result + \"|\" + receiver.Calls; }"
                : shape == "generic-receiver"
                    ? "public interface IReceiver { int Calls { get; } string Take<T>(int before, T value, int after) where T : IBag; } public struct Receiver : IReceiver { public int Calls { get; private set; } public string Take<T>(int before, T value, int after) where T : IBag { Calls++; return value.Sum + \"|\" + trace; } } public static string Call<T, R>() where T : IBag, new() where R : IReceiver, new() { var receiver = new R(); var result = receiver.Take(Trace(1), new T { Trace(3), Trace(4) }, Trace(5)); return result + \"|\" + receiver.Calls; } public static string Zero<T>() where T : IBag, new() => Call<T, Receiver>();"
                : shape == "ref"
                    ? "public static string Take<T>(ref int before, T value, int after) where T : IBag { before = 9; return value.Sum + \"|\" + trace; } public static string Zero<T>() where T : IBag, new() { int[] before = { 1 }; var result = Take(ref before[Trace(0)], new T { Trace(3), Trace(4) }, Trace(5)); return result + \"|\" + before[0]; }"
                    : "public static string Take<T>(int before, T value, int after) where T : IBag => value.Sum + \"|\" + trace; public static string Zero<T>() where T : IBag, new() => Take(Trace(1), new T { Trace(3), Trace(4) }, Trace(5));")
            + " public static string Run() => new SelectedStruct(2).Value + \"|\" + Zero<SelectedStruct>(); }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, shape is "receiver" or "generic-receiver" ? "7|7|1345|1" : shape == "readonly-receiver" ? "7|7|1345|0" : shape == "ref" ? "7|7|0345|9" : "7|7|1345", 1);
    }

    [TestMethod]
    [DataRow("void")]
    [DataRow("binary")]
    [DataRow("conditional-true")]
    [DataRow("conditional-false")]
    [DataRow("and-true")]
    [DataRow("and-false")]
    [DataRow("coalesce-null")]
    [DataRow("coalesce-value")]
    [DataRow("if")]
    [DataRow("while")]
    [DataRow("for")]
    [DataRow("do")]
    [DataRow("foreach")]
    [DataRow("using")]
    [DataRow("lambda")]
    public async Task ChangeSignaturePreview_LastStructParameter_CollectionControlFlow(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public void Dispose() { SignatureProof.Mark(); } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var collection = "new T { Trace(3), Trace(4) }";
        var member = shape switch
        {
            "void" => "public static void Zero<T>() where T : IBag, new() { Consume(Trace(1), " + collection + ", Trace(5)); }",
            "binary" => "public static string Zero<T>() where T : IBag, new() => Left() + Take(" + collection + ");",
            "conditional-true" or "conditional-false" => "public static string Zero<T>() where T : IBag, new() => Gate(" + (shape.EndsWith("true", StringComparison.Ordinal) ? "true" : "false") + ") ? Take(" + collection + ") : Take(new T { Trace(8) });",
            "and-true" or "and-false" => "public static bool Zero<T>() where T : IBag, new() => Gate(" + (shape.EndsWith("true", StringComparison.Ordinal) ? "true" : "false") + ") && Test(" + collection + ");",
            "coalesce-null" or "coalesce-value" => "public static string Zero<T>() where T : IBag, new() => Maybe(" + (shape.EndsWith("null", StringComparison.Ordinal) ? "true" : "false") + ") ?? Take(" + collection + ");",
            "if" => "public static string Zero<T>() where T : IBag, new() { if (Test(" + collection + ")) trace += \"I\"; return trace; }",
            "for" => "public static string Zero<T>() where T : IBag, new() { for (; Test(" + collection + "); Trace(6)) { if (count == 1) continue; break; } return trace; }",
            "do" => "public static string Zero<T>() where T : IBag, new() { do { trace += \"W\"; if (count == 0) { count++; continue; } break; } while (Test(" + collection + ")); return trace; }",
            "foreach" => "public static string Zero<T>() where T : IBag, new() { foreach (var item in " + collection + ") trace += item; return trace; }",
            "using" => "public static string Zero<T>() where T : IBag, new() { using (" + collection + ") trace += \"U\"; return trace; }",
            "lambda" => "public static string Zero<T>() where T : IBag, new() { System.Action action = () => Consume(Trace(1), " + collection + ", Trace(5)); action(); return trace; }",
            _ => "public static string Zero<T>() where T : IBag, new() { while (Test(" + collection + ")) { trace += \"W\"; if (count == 1) continue; break; } return trace; }",
        };
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int>, System.IDisposable { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int count; public static void Mark() { trace += \"D\"; } static int Trace(int value) { trace += value; return value; } static bool Gate(bool value) { trace += \"G\"; return value; } static string Left() { trace += \"L\"; return \"left\"; } static string? Maybe(bool isNull) { trace += \"M\"; return isNull ? null : \"value\"; } static bool Test<T>(T value) where T : IBag { count++; trace += \"T\"; return value.Sum == 7; } static string Take<T>(T value) where T : IBag => value.Sum.ToString(); static void Consume<T>(int before, T value, int after) where T : IBag { trace += \"B\"; } "
            + member + " public static string Run() { " + (shape == "void" ? "Zero<SelectedStruct>(); var value = trace;" : "var value = Zero<SelectedStruct>();")
            + " return new SelectedStruct(2).Value + \"|\" + value + \"|\" + trace; } }";
        source = source.Replace(" ? Take(", " ? /*signature-dot*/ Take(", StringComparison.Ordinal)
            .Replace(" && Test(", " && /*signature-dot*/ Test(", StringComparison.Ordinal)
            .Replace(" ?? Take(", " ?? /*signature-dot*/ Take(", StringComparison.Ordinal);
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_GenericReceiverReassignment(bool reference)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { SignatureProof.Mutate?.Invoke(); Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); } public interface IReceiver { int Calls { get; } string Take<T>(T bag) where T : IBag; }"
            + "\npublic static class SignatureProof { public static System.Action? Mutate; public " + (reference ? "class" : "struct") + " Receiver : IReceiver { public int Calls { get; private set; } public string Take<T>(T bag) where T : IBag { Calls++; return bag.Sum + \"|\" + Calls; } } public static string Call<T, R>() where T : IBag, new() where R : IReceiver, new() { var receiver = new R(); Mutate = () => receiver = new R(); var result = receiver.Take(new T { 3, 4 }); Mutate = null; return result + \"|\" + receiver.Calls; } public static string Run() => new SelectedStruct(2).Value + \"|\" + Call<SelectedStruct, Receiver>(); }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, reference ? "7|7|1|0" : "7|7|1|1", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_ReadonlyReceiverRetainsAddress()
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { SignatureProof.ChangeReceiver(); Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { public struct Receiver { public int Calls; public readonly int Take<T>(T value) where T : IBag => Calls; } static Receiver receiver; public static void ChangeReceiver() { receiver.Calls = 9; } public static int Call<T>(in Receiver target) where T : IBag, new() => target.Take(new T { 3, 4 }); public static string Run() => new SelectedStruct(2).Value + \"|\" + Call<SelectedStruct>(in receiver); }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|9", 1);
    }

    [TestMethod]
    [DataRow("while")]
    [DataRow("for")]
    [DataRow("do")]
    public async Task ChangeSignaturePreview_LastStructParameter_CustomTruthLoopRetainsEffects(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var condition = "Test(new T { Trace(3), Trace(4) })";
        var loop = shape == "for" ? "for (; " + condition + "; Trace(6)) { trace += \"W\"; continue; }"
            : shape == "do" ? "do { trace += \"W\"; continue; } while (" + condition + ");"
            : "while (" + condition + ") { trace += \"W\"; continue; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int count; public struct Truth(bool value) { public bool Value => value; public static bool operator true(Truth value) { trace += \"T\"; return value.Value; } public static bool operator false(Truth value) { trace += \"F\"; return false; } } static int Trace(int value) { trace += value; return value; } static Truth Test<T>(T value) where T : IBag { count++; trace += \"C\"; return new Truth(count < 2); } public static string Zero<T>() where T : IBag, new() { "
            + loop + " return trace; } public static string Run() => new SelectedStruct(2).Value + \"|\" + Zero<SelectedStruct>(); }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_CustomShortCircuitInlineAssignment(bool value)
    {
        var declaration = "public struct SelectedStruct(int unused) : IValue { public int Value = 7; public int Other { get; set; } }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IValue { int Other { get; set; } } public static class SignatureProof { static string trace = \"\"; public struct Truth(bool value) { public bool Value => value; public static bool operator true(Truth value) { trace += \"T\"; return true; } public static bool operator false(Truth value) { trace += \"F\"; return !value.Value; } public static Truth operator &(Truth left, Truth right) { trace += \"A\"; return new Truth(left.Value && right.Value); } } static int Trace() { trace += \"3\"; return 3; } static Truth Test<T>(T value) where T : IValue => new Truth(value.Other == 3); public static Truth Zero<T>(Truth left) where T : IValue, new() => left && Test(new T { Other = Trace() }); public static string Run() => new SelectedStruct(2).Value + \"|\" + Zero<SelectedStruct>(new Truth("
            + (value ? "true" : "false") + ")).Value + \"|\" + trace; }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, value ? "7|True|F3A" : "7|False|F", 1);
    }

    [TestMethod]
    [DataRow("and", false)]
    [DataRow("and", true)]
    [DataRow("or", false)]
    [DataRow("or", true)]
    public async Task ChangeSignaturePreview_LastStructParameter_CustomShortCircuitCollection(string shape, bool value)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; public struct Truth(bool value) { public bool Value => value; public static bool operator true(Truth value) { trace += \"T\"; return value.Value; } public static bool operator false(Truth value) { trace += \"F\"; return !value.Value; } public static Truth operator &(Truth left, Truth right) { trace += \"A\"; return new Truth(left.Value && right.Value); } public static Truth operator |(Truth left, Truth right) { trace += \"O\"; return new Truth(left.Value || right.Value); } } static int Trace(int value) { trace += value; return value; } static Truth Test<T>(T bag, [System.Runtime.CompilerServices.CallerMemberName] string caller = \"\") where T : IBag { trace += caller; return new Truth(bag.Sum == 7); } public static Truth Zero<T>(Truth left) where T : IBag, new() => left "
            + (shape == "and" ? "&&" : "||") + " /*signature-dot*/ Test(new T { Trace(3), Trace(4) }); public static string Run() => new SelectedStruct(2).Value + \"|\" + Zero<SelectedStruct>(new Truth("
            + (value ? "true" : "false") + ")).Value + \"|\" + trace; }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_ConstructorAttributeBindings(bool multiline)
    {
        var declaration = "[method: global::SampleLib.Marker(Code = 8)]" + (multiline ? "\n" : " ") + "public struct SelectedStruct(int unused) { public int Value = 7; public sealed class MarkerAttribute : System.Attribute { public int Code; } }";
        var source = "namespace SampleLib;\n" + declaration
            + "\n[System.AttributeUsage(System.AttributeTargets.Constructor)] public sealed class MarkerAttribute : System.Attribute { public int Code; public int Line { get; } public MarkerAttribute([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { Line = line; } }"
            + "\npublic static class SignatureProof { public static string Run() { var attribute = (MarkerAttribute)System.Attribute.GetCustomAttribute(typeof(SelectedStruct).GetConstructors()[0], typeof(MarkerAttribute))!; return new SelectedStruct(2).Value + \"|\" + attribute.Code + \"|\" + attribute.Line; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), multiline ? 3 : 2,
            declaration.Split('\n')[^1].IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|8|2", 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_CustomCollectionRefCapture(bool deferred)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { public struct Truth(bool value) { public bool Value => value; public static bool operator true(Truth value) => value.Value; public static bool operator false(Truth value) => !value.Value; public static Truth operator &(Truth left, Truth right) => new Truth(left.Value && right.Value); } static int Trace(ref int value, [System.Runtime.CompilerServices.CallerMemberName] string caller = \"\") { if (caller != \"Zero\") throw new System.InvalidOperationException(caller); return ++value; } static Truth Test<T>(T bag) where T : IBag => new Truth(bag.Sum > 0); public static Truth Zero<T>(ref int count) where T : IBag, new() => new Truth(" + (deferred ? "true" : "false") + ") && Test(new T { Trace(ref count), Trace(ref count) }); public static string Run() { int count = 0; Zero<SelectedStruct>(ref count); var before = System.GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 100; i++) Zero<SelectedStruct>(ref count); var allocated = System.GC.GetAllocatedBytesForCurrentThread() - before; return new SelectedStruct(2).Value + \"|\" + count + \"|\" + allocated; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, deferred ? "7|202|0" : "7|0|0", 1);
    }

    [TestMethod]
    [DataRow("await")]
    [DataRow("span")]
    [DataRow("instance")]
    [DataRow("readonly")]
    public async Task ChangeSignaturePreview_LastStructParameter_CustomCollectionOwnership(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var member = shape == "await"
            ? "static System.Threading.Tasks.Task<Truth> TestAsync<T>(T bag) where T : IBag => System.Threading.Tasks.Task.FromResult(Test(bag)); public static async System.Threading.Tasks.Task<Truth> Zero<T>() where T : IBag, new() => new Truth(true) && await TestAsync(new T { 3, 4 });"
            : shape == "span" ? "public static Truth Zero<T>() where T : IBag, new() { System.Span<int> values = stackalloc int[] { 3, 4 }; return new Truth(true) && Test(new T { values[0], values[1] }); }"
            : "public struct Factory { public int Count; int Trace() => ++Count; public " + (shape == "readonly" ? "readonly " : "") + "Truth Zero<T>() where T : IBag, new() => new Truth(true) && Test(new T { Trace(), Trace() }); }";
        var call = shape == "await" ? "Zero<SelectedStruct>().GetAwaiter().GetResult().Value"
            : shape == "span" ? "Zero<SelectedStruct>().Value" : "factory.Zero<SelectedStruct>().Value";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { public struct Truth(bool value) { public bool Value => value; public static bool operator true(Truth value) => value.Value; public static bool operator false(Truth value) => !value.Value; public static Truth operator &(Truth left, Truth right) => new Truth(left.Value && right.Value); } static Truth Test<T>(T bag) where T : IBag => new Truth(bag.Sum > 0); "
            + member + " public static string Run() { " + (shape is "instance" or "readonly" ? "var factory = new Factory();" : "")
            + " return new SelectedStruct(2).Value + \"|\" + " + call + (shape is "instance" or "readonly" ? " + \"|\" + factory.Count" : "") + "; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, shape == "instance" ? "7|True|2" : shape == "readonly" ? "7|True|0" : "7|True", 1);
    }

    [TestMethod]
    [DataRow("checked")]
    [DataRow("unchecked")]
    [DataRow("constructor")]
    [DataRow("delegate")]
    public async Task ChangeSignaturePreview_LastStructParameter_CollectionExpressionHosts(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var expression = shape == "constructor" ? "new Box(Trace(1), new T { Trace(3), Trace(4) }, Trace(5)).Value"
            : shape == "delegate" ? "callee(Trace(1), new T { Trace(3), Trace(4) }, Trace(5))"
            : shape + "(Take(new T { Trace(int.MaxValue) + Trace(1) }))";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int Trace(int value) { trace += value + \",\"; return value; } static int Take<T>(T bag) where T : IBag => bag.Sum; public sealed class Box { public int Value; public Box(int first, IBag bag, int last) { Value = bag.Sum; } } public static int Zero<T>() where T : IBag, new() { System.Func<int,T,int,int> callee = (first, bag, last) => bag.Sum; try { return "
            + expression + "; } catch (System.OverflowException) { return 9; } } public static string Run() => new SelectedStruct(2).Value + \"|\" + Zero<SelectedStruct>() + \"|\" + trace; }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow("out")]
    [DataRow("assigned-out")]
    [DataRow("in")]
    public async Task ChangeSignaturePreview_LastStructParameter_CustomCollectionParameterKinds(string kind)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var member = kind == "in"
            ? "static int Fill(in int count) => count; public static Truth Zero<T>(in int count) where T : IBag, new() => new Truth(true) && Test(new T { Fill(in count), Fill(in count) });"
            : "static int Fill(out int count) { count = 3; return count; } public static Truth Zero<T>(out int count) where T : IBag, new() { " + (kind == "assigned-out" ? "count = 0;" : "") + " var value = new Truth(true) && Test(new T { Fill(out count), Fill(out count) }); count = 9; return value; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { public struct Truth(bool value) { public bool Value => value; public static bool operator true(Truth value) => value.Value; public static bool operator false(Truth value) => !value.Value; public static Truth operator &(Truth left, Truth right) => new Truth(left.Value && right.Value); } static Truth Test<T>(T bag) where T : IBag => new Truth(bag.Sum > 0); "
            + member + " public static string Run() { int count = 3; var value = Zero<SelectedStruct>(" + (kind == "in" ? "in" : "out") + " count); return new SelectedStruct(2).Value + \"|\" + value.Value + \"|\" + count; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, kind == "in" ? "7|True|3" : "7|True|9", 1);
    }

    [TestMethod]
    [DataRow("generic")]
    [DataRow("factory")]
    [DataRow("type")]
    [DataRow("nonpublic")]
    [DataRow("empty-arguments")]
    [DataRow("activation-attributes")]
    [DataRow("binding-flags")]
    [DataRow("binding-flags-attributes")]
    public async Task ChangeSignaturePreview_LastStructParameter_ActivatorDefaultConstruction(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var expression = shape switch
        {
            "generic" => "System.Activator.CreateInstance<SelectedStruct>()",
            "factory" => "Factory<SelectedStruct>()",
            "nonpublic" => "(SelectedStruct)System.Activator.CreateInstance(typeof(SelectedStruct), true)!",
            "empty-arguments" => "(SelectedStruct)System.Activator.CreateInstance(typeof(SelectedStruct), System.Array.Empty<object>())!",
            "activation-attributes" => "(SelectedStruct)System.Activator.CreateInstance(typeof(SelectedStruct), System.Array.Empty<object>(), System.Array.Empty<object>())!",
            "binding-flags" => "(SelectedStruct)System.Activator.CreateInstance(typeof(SelectedStruct), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance, null, System.Array.Empty<object>(), null)!",
            "binding-flags-attributes" => "(SelectedStruct)System.Activator.CreateInstance(typeof(SelectedStruct), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance, null, System.Array.Empty<object>(), null, System.Array.Empty<object>())!",
            _ => "(SelectedStruct)System.Activator.CreateInstance(typeof(SelectedStruct))!",
        };
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static T Factory<T>() => System.Activator.CreateInstance<T>(); public static string Run() { var zero = "
            + expression + "; return new SelectedStruct(2).Value + \"|\" + zero.Value; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0", 1);
    }

    [TestMethod]
    [DataRow("type")]
    [DataRow("nonpublic")]
    [DataRow("arguments")]
    [DataRow("named")]
    [DataRow("attributes")]
    [DataRow("flags")]
    [DataRow("flags-attributes")]
    public async Task ChangeSignaturePreview_LastStructParameter_ActivatorArgumentsAndOtherTypes(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var expression = shape switch
        {
            "nonpublic" => "System.Activator.CreateInstance(TypeOf(type), Flag())",
            "arguments" => "System.Activator.CreateInstance(TypeOf(type), Args())",
            "named" => "System.Activator.CreateInstance(args: Args(), type: TypeOf(type))",
            "attributes" => "System.Activator.CreateInstance(TypeOf(type), Args(), Attributes())",
            "flags" => "System.Activator.CreateInstance(TypeOf(type), Flags(), Binder(), Args(), Culture())",
            "flags-attributes" => "System.Activator.CreateInstance(TypeOf(type), Flags(), Binder(), Args(), Culture(), Attributes())",
            _ => "System.Activator.CreateInstance(TypeOf(type))",
        };
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic struct OtherStruct { public int Value; public OtherStruct() { Value = 9; } } public class OtherClass { public int Value = 11; }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static System.Type TypeOf(System.Type type) { trace += \"T\"; return type; } static bool Flag() { trace += \"B\"; return true; } static object[] Args() { trace += \"A\"; return System.Array.Empty<object>(); } static object[] Attributes() { trace += \"X\"; return System.Array.Empty<object>(); } static System.Reflection.BindingFlags Flags() { trace += \"F\"; return System.Reflection.BindingFlags.Default; } static System.Reflection.Binder? Binder() { trace += \"B\"; return null; } static System.Globalization.CultureInfo? Culture() { trace += \"C\"; return null; } public static object? Zero(System.Type type) => "
            + expression + "; public static string Run() { var selected = (SelectedStruct)Zero(typeof(SelectedStruct))!; var other = (OtherStruct)Zero(typeof(OtherStruct))!; var reference = (OtherClass)Zero(typeof(OtherClass))!; string error; try { Zero(null!); error = \"none\"; } catch(System.ArgumentNullException) { error = \"null\"; } return new SelectedStruct(2).Value + \"|\" + selected.Value + \"|\" + other.Value + \"|\" + reference.Value + \"|\" + error + \"|\" + trace; } }";
        var trace = shape == "nonpublic" ? "TB" : shape == "arguments" ? "TA" : shape == "named" ? "AT" : shape == "attributes" ? "TAX" : shape == "flags" ? "TFBAC" : shape == "flags-attributes" ? "TFBACX" : "T";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|9|11|null|" + string.Concat(Enumerable.Repeat(trace, 4)), 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_ActivatorGenericAllocationAndExceptions()
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic struct OtherStruct { public int Value; public OtherStruct() { Value = 9; } } public class OtherClass { public int Value = 11; } public class NoDefault { public NoDefault(int value) { } }"
            + "\npublic static class SignatureProof { public static T Zero<T>() => System.Activator.CreateInstance<T>(); public static string Run() { for(int i=0;i<5000;i++) Zero<SelectedStruct>(); var before = System.GC.GetAllocatedBytesForCurrentThread(); int sum=0; for(int i=0;i<5000;i++) sum += Zero<SelectedStruct>().Value; var allocated = System.GC.GetAllocatedBytesForCurrentThread()-before; string error; try { Zero<NoDefault>(); error=\"none\"; } catch(System.MissingMethodException) { error=\"missing\"; } return new SelectedStruct(2).Value + \"|\" + sum + \"|\" + Zero<OtherStruct>().Value + \"|\" + Zero<OtherClass>().Value + \"|\" + error + \"|\" + allocated; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|9|11|missing|0", 1);
    }

    [TestMethod]
    [DataRow("flags")]
    [DataRow("attributes")]
    [DataRow("custom-type")]
    [DataRow("ignore-case")]
    public async Task ChangeSignaturePreview_LastStructParameter_ActivatorInvalidInputs(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var expression = shape == "ignore-case" ? "System.Activator.CreateInstance(typeof(SelectedStruct), System.Reflection.BindingFlags.IgnoreCase, null, System.Array.Empty<object>(), null)" : shape == "flags"
            ? "System.Activator.CreateInstance(typeof(SelectedStruct), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, System.Array.Empty<object>(), null)"
            : shape == "attributes" ? "System.Activator.CreateInstance(typeof(SelectedStruct), System.Array.Empty<object>(), new object[] { 1 })"
            : "System.Activator.CreateInstance(new WrappedType(typeof(SelectedStruct)))";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic class WrappedType(System.Type type) : System.Reflection.TypeDelegator(type) { public override System.Reflection.Assembly Assembly { get { SignatureProof.Trace += \"A\"; return base.Assembly; } } }"
            + "\npublic static class SignatureProof { public static string Trace = \"\"; public static string Run() { string error; try { var result = " + expression
            + "; error = result is SelectedStruct selected ? selected.Value.ToString() : \"none\"; } catch(System.Exception exception) { error = exception.GetType().Name; } return new SelectedStruct(2).Value + \"|\" + error + \"|\" + Trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_ActivatorMultipleCallsAndTrivia()
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() { var first = (SelectedStruct)System.Activator.CreateInstance(/*signature-receiver*/typeof(SelectedStruct))!; var second = (SelectedStruct)System.Activator.CreateInstance(typeof(SelectedStruct),/*signature-init-comma*/true)!; return new SelectedStruct(2).Value + \"|\" + first.Value + \"|\" + second.Value; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|0", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_ActivatorGenericExpressionTree()
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic struct OtherStruct { public int Value; public OtherStruct() { Value = 9; } }"
            + "\npublic static class SignatureProof { public static T Zero<T>() { System.Linq.Expressions.Expression<System.Func<T>> expression = () => System.Activator.CreateInstance<T>(); return expression.Compile()(); } public static string Run() => new SelectedStruct(2).Value + \"|\" + Zero<SelectedStruct>().Value + \"|\" + Zero<OtherStruct>().Value; }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|9", 1);
    }

    [TestMethod]
    [DataRow("selected")]
    [DataRow("other")]
    [DataRow("self")]
    [DataRow("null")]
    [DataRow("throw")]
    public async Task ChangeSignaturePreview_LastStructParameter_ActivatorUnderlyingTypeEffects(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var getter = shape == "self" ? "this" : shape == "null" ? "null!" : shape == "throw"
            ? "throw new System.InvalidOperationException(\"getter\")" : "base.UnderlyingSystemType";
        var type = shape == "other" ? "OtherStruct" : "SelectedStruct";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic struct OtherStruct { public int Value; public OtherStruct() { Value = 9; } }"
            + "\npublic class WrappedType(System.Type type) : System.Reflection.TypeDelegator(type) { public override System.Type UnderlyingSystemType { get { SignatureProof.Trace += \"U\"; " + (shape == "throw" ? getter : "return " + getter) + "; } } }"
            + "\npublic static class SignatureProof { public static string Trace = \"\"; public static string Run() { var oldCulture = System.Globalization.CultureInfo.CurrentUICulture; try { System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(\"fr-FR\"); string value; try { var instance = System.Activator.CreateInstance(new WrappedType(typeof(" + type + "))); value = instance is SelectedStruct selected ? selected.Value.ToString() : ((OtherStruct)instance!).Value.ToString(); } catch(System.Exception error) { value = error.GetType().Name + \":\" + error.Message + \":\" + (error as System.ArgumentException)?.ParamName; } return new SelectedStruct(2).Value + \"|\" + value + \"|\" + Trace; } finally { System.Globalization.CultureInfo.CurrentUICulture = oldCulture; } } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_ActivatorRefLikeGeneric(bool notNull)
    {
        var declaration = "public ref struct SelectedStruct(int unused) { public int Value = 7; }";
        var constraint = notNull ? "notnull, allows ref struct" : "allows ref struct";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static T Zero<T>() where T : " + constraint + " => System.Activator.CreateInstance<T>(); public static string Run() => new SelectedStruct(2).Value + \"|\" + Zero<SelectedStruct>().Value; }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_ActivatorUnrelatedSourceName()
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib {\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => new SelectedStruct(2).Value + \"|\" + System.Activator.CreateInstance<SelectedStruct>().Value; } }"
            + "\nnamespace System { public static class Activator { public static T CreateInstance<T>() => (T)(object)new SampleLib.SelectedStruct(3); } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|7", 2);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_ActivatorTypeBuilder(bool nonPublicOverload)
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var call = nonPublicOverload ? "System.Activator.CreateInstance(type, true)" : "System.Activator.CreateInstance(type)";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() { var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName(\"SignatureDynamic\"), System.Reflection.Emit.AssemblyBuilderAccess.Run); var type = assembly.DefineDynamicModule(\"Module\").DefineType(\"Pending\"); string result; try { " + call + "; result = \"accepted\"; } catch(System.Exception error) { result = error.GetType().Name + \":\" + error.Message + \":\" + (error as System.ArgumentException)?.ParamName; } return new SelectedStruct(2).Value + \"|\" + result; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow("none")]
    [DataRow("field-zero")]
    [DataRow("property-zero")]
    [DataRow("default-zero")]
    [DataRow("pure-zero")]
    [DataRow("static-effect")]
    [DataRow("static-empty")]
    [DataRow("effectful-zero")]
    [DataRow("field-nonzero")]
    [DataRow("property-nonzero")]
    [DataRow("attribute")]
    public async Task ChangeSignaturePreview_LastStructParameter_NeutralInitializationBoundary(string shape)
    {
        var members = shape switch
        {
            "field-zero" => "public int Value = 0;",
            "field-nonzero" => "public int Value = 7;",
            "property-nonzero" => "public int Value { get; set; } = 7;",
            "static-empty" => "public int Value = 0; static SelectedStruct() { }",
            "effectful-zero" => "public int Value = Zero(); private static int Zero() { SignatureProof.Trace += \"I\"; return 0; }",
            "property-zero" => "public int Value { get; set; } = 0;",
            "default-zero" => "public int Value = default(int);",
            "pure-zero" => "public int Value = Zero(); private static int Zero() => 0;",
            "static-effect" => "public int Value = 0; static SelectedStruct() { SignatureProof.Trace += \"S\"; }",
            _ => "public int Value;"
        };
        var declaration = (shape == "attribute" ? "[method: Marker] " : "")
            + "public struct SelectedStruct(int unused) { " + members + " }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic sealed class MarkerAttribute : System.Attribute { }"
            + "\npublic static class SignatureProof { public static string Trace = \"\"; public static string Run() { var first = System.Text.Json.JsonSerializer.Deserialize<SelectedStruct>(\"{}\"); var before = Trace; var selected = new SelectedStruct(2); var middle = Trace; var last = System.Text.Json.JsonSerializer.Deserialize<SelectedStruct>(\"{}\"); var attributes = System.Linq.Enumerable.Sum(typeof(SelectedStruct).GetConstructors(), c => c.GetCustomAttributes(typeof(MarkerAttribute), false).Length); return first.Value + \"|\" + selected.Value + \"|\" + last.Value + \"|\" + before + \"|\" + middle + \"|\" + Trace + \"|\" + attributes; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow("positive-double")]
    [DataRow("negative-double")]
    [DataRow("decimal-scale")]
    [DataRow("nullable-value")]
    [DataRow("nullable-default")]
    [DataRow("nullable-source-value")]
    [DataRow("nullable-source-default")]
    [DataRow("nullable-lifted-default")]
    [DataRow("enum-zero")]
    [DataRow("default-struct")]
    public async Task ChangeSignaturePreview_LastStructParameter_TypedInitializationBoundary(string shape)
    {
        var member = shape switch
        {
            "positive-double" => "public double Value = 0.0D;",
            "negative-double" => "public double Value = -0.0D;",
            "decimal-scale" => "public decimal Value = 0.0M;",
            "nullable-value" => "public int? Value = 0;",
            "nullable-default" => "public int? Value = default(int?);",
            "nullable-source-value" => "public int? Value = Zero(); private static int? Zero() => 0;",
            "nullable-source-default" => "public int? Value = Zero(); private static int? Zero() => default(int?);",
            "nullable-lifted-default" => "public long? Value = Zero(); private static int? Zero() => default(int?);",
            "enum-zero" => "public Kind Value = Kind.Zero;",
            _ => "public Payload Value = default(Payload);"
        };
        var value = shape switch
        {
            "positive-double" or "negative-double" => "System.BitConverter.DoubleToInt64Bits(value.Value).ToString()",
            "decimal-scale" => "string.Join(\",\", decimal.GetBits(value.Value))",
            "nullable-value" or "nullable-default" or "nullable-source-value" or "nullable-source-default" or "nullable-lifted-default" => "value.Value.HasValue.ToString()",
            "enum-zero" => "((int)value.Value).ToString()",
            _ => "value.Value.Inner.ToString()"
        };
        var declaration = "public struct SelectedStruct(int unused) { " + member + " }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic enum Kind { Zero } public struct Payload { public int Inner; }"
            + "\npublic static class SignatureProof { private static string Show(SelectedStruct value) => " + value
            + "; public static string Run() { var first = System.Text.Json.JsonSerializer.Deserialize<SelectedStruct>(\"{}\"); var selected = new SelectedStruct(2); var last = System.Text.Json.JsonSerializer.Deserialize<SelectedStruct>(\"{}\"); return Show(first) + \"|\" + Show(selected) + \"|\" + Show(last); } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_ExistingZeroConstructor_PreservesBothPaths()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        var declaration = "public struct SelectedStruct(int unused) { public int Value; public SelectedStruct() : this(0) { Value = 3; } }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => new SelectedStruct(2).Value + \"|\" + new SelectedStruct().Value; }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("0|3", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var probe = new SignatureStoreProbe(PreviewStore);
        var service = new ChangeSignatureService(WorkspaceManager, probe, RefactoringService);
        var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
                SymbolLocator.BySource(path, 2, declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1),
                new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        Assert.AreEqual(1, probe.StoreCalls);
        Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
        var stored = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        Assert.AreEqual("0|3", await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        Assert.AreEqual(2, preview.CallsiteUpdates!.Sum(update => update.CallsiteCount));
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual("0|3", await RunSignatureFixtureAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_JsonSerializerDefaultCreation()
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => new SelectedStruct(2).Value + \"|\" + System.Text.Json.JsonSerializer.Deserialize<SelectedStruct>(\"{}\").Value; }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_ActivatorNullableDiagnostics()
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static object? Zero(System.Type type) => System.Activator.CreateInstance(type); public static string Run() { var zero = (SelectedStruct)Zero(typeof(SelectedStruct))!; try { Zero(null!); } catch(System.ArgumentNullException) { } return new SelectedStruct(2).Value + \"|\" + zero.Value; } }";
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        var pair = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(pair);
        var compilation = await pair.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync();
        Assert.IsNotNull(compilation);
        Assert.AreEqual(0, compilation.GetDiagnostics().Count(d => d.Id.StartsWith("CS86", StringComparison.Ordinal)
            && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning), string.Join(Environment.NewLine, compilation.GetDiagnostics()));
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(pair.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_NestedInitializerRetainsEffects()
    {
        var declaration = "public struct SelectedStruct(int unused) : IChild { public int Value = 7; public Child Child => SignatureProof.GetChild(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IChild { Child Child { get; } } public class Child { public int A; public int B; }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static Child child = new(); public static Child GetChild() { trace += \"H\"; return child; } static int Trace(int value) { trace += value; return value; } public static T Zero<T>() where T : IChild, new() => new T { Child = { A = Trace(3), B = Trace(4) } }; public static string Run() { var selected = new SelectedStruct(2); var zero = Zero<SelectedStruct>(); return selected.Value + \"|\" + zero.Value + \"|\" + child.A + \"|\" + child.B + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_DiscardedZeroConstruction(bool generic)
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static void Zero<T>() where T : new() { new T(); } public static int Run() { "
            + (generic ? "Zero<SelectedStruct>();" : "new SelectedStruct();") + " return new SelectedStruct(2).Value; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7", 1);
    }

    [TestMethod]
    [DataRow("property")]
    [DataRow("getter")]
    [DataRow("lambda")]
    [DataRow("local")]
    public async Task ChangeSignaturePreview_LastStructParameter_CollectionInitializerHosts(string host)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var factory = host switch
        {
            "property" => "public static SelectedStruct Zero => new SelectedStruct { Trace(3), Trace(4) };",
            "getter" => "public static SelectedStruct Zero { get => new SelectedStruct { Trace(3), Trace(4) }; }",
            "lambda" => "public static System.Func<SelectedStruct> Zero = () => new SelectedStruct { Trace(3), Trace(4) };",
            _ => "public static SelectedStruct Zero() { SelectedStruct Local() => new SelectedStruct { Trace(3), Trace(4) }; return Local(); }",
        };
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int Trace(int value) { trace += value; return value; } "
            + factory + " public static string Run() { var selected = new SelectedStruct(2); var zero = Zero"
            + (host is "property" or "getter" ? "" : "()") + "; return selected.Value + \"|\" + zero.Value + \"|\" + zero.Sum + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|7|34", 1);
    }

    [TestMethod]
    [DataRow("arrow")]
    [DataRow("constructor")]
    [DataRow("for")]
    public async Task ChangeSignaturePreview_LastStructParameter_DiscardedConstructionHosts(string host)
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var member = host switch
        {
            "arrow" => "public static void Zero<T>() where T : new() => new T();",
            "constructor" => "public class Factory<T> where T : new() { public Factory() => new T(); }",
            _ => "public static void Zero<T>() where T : new() { for (new T(), Trace(1); count < 2; new T(), Trace(2)) { count++; try { if (count == 1) continue; trace += \"B\"; } finally { trace += \"F\"; } } }",
        };
        var source = "namespace SampleLib;\n" + declaration + "\npublic static class SignatureProof { static int count; static string trace = \"\"; static int Trace(int value) { trace += value; return value; } "
            + member + " public static string Run() { " + (host == "constructor" ? "new Factory<SelectedStruct>();" : "Zero<SelectedStruct>();")
            + " return new SelectedStruct(2).Value + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, host == "for" ? "7|1F2BF2" : "7|", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_ExpressionTreeDefaultConstruction()
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic class OtherClass { public int Value = 11; } public static class SignatureProof { public static System.Linq.Expressions.Expression<System.Func<T>> Zero<T>() where T : new() => () => new T(); public static long Allocated<T>(System.Func<T> factory) { for (int i = 0; i < 10000; i++) _ = factory(); var before = System.GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 1000; i++) _ = factory(); return System.GC.GetAllocatedBytesForCurrentThread() - before; } public static string Run() { var factory = Zero<SelectedStruct>().Compile(); return new SelectedStruct(2).Value + \"|\" + factory().Value + \"|\" + Zero<OtherClass>().Compile()().Value + \"|\" + Allocated(factory); } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|11|0", 1);
    }

    [TestMethod]
    [DataRow("add")]
    [DataRow("remove")]
    [DataRow("last")]
    public async Task ChangeSignaturePreview_SelectedStructDefaultConstructor_RetainsIdentity(string operation)
    {
        var declaration = operation == "last"
            ? "public struct SelectedStruct(int unused) { public int Value => 0; }"
            : "public struct SelectedStruct(int first, int unused) { public int Value => first; }";
        var source = "namespace SampleLib;\n" + declaration + "\npublic static class SignatureProof { public static int Run() => new SelectedStruct().Value * 100 + "
            + (operation == "last" ? "new SelectedStruct(2)" : "new SelectedStruct(1, 2)") + ".Value; }";
        var request = operation == "add" ? new ChangeSignatureRequest("add", Position: 1, Name: "added", ParameterType: "int")
            : new ChangeSignatureRequest("remove", Name: "unused");
        await AssertSignatureProofAsync(source, request, 2, declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1,
            operation == "last" ? "0" : "1", 1);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task ChangeSignaturePreview_DynamicInvocation_RefusesOnlyPossibleEditedMember(bool unrelated, bool knownReceiver)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "DynamicSignature.cs");
        var declaration = "public class DynamicTarget { public int M(int x, int unused) => x; public int Other(int x, int unused) => x; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic class DynamicOther { public int M(int x, int unused) => x; }"
            + "\npublic static class SignatureProof { public static int Run() { "
            + (knownReceiver ? "dynamic arg = 1; var d = new " + (unrelated ? "DynamicOther" : "DynamicTarget") + "(); return d.M(arg, 2);"
                : "dynamic d = new DynamicTarget(); return d." + (unrelated ? "Other" : "M") + "(1, 2);") + " } }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("1", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var probe = new SignatureStoreProbe(PreviewStore);
        var service = new ChangeSignatureService(WorkspaceManager, probe, RefactoringService);
        var locator = SymbolLocator.BySource(path, 2, declaration.IndexOf("M(", StringComparison.Ordinal) + 1);
        if (!unrelated)
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                    new ChangeSignatureRequest("remove", Position: 1), CancellationToken.None));
            StringAssert.Contains(error.Message, "dynamic");
            Assert.AreEqual(0, probe.StoreCalls);
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
            Assert.AreEqual(source, await File.ReadAllTextAsync(path));
        }
        else
        {
            var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                new ChangeSignatureRequest("remove", Position: 1), CancellationToken.None);
            Assert.AreEqual(1, probe.StoreCalls);
            var stored = PreviewStore.Retrieve(preview.PreviewToken);
            Assert.IsNotNull(stored);
            Assert.AreEqual("1", await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
            var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applied.Success, applied.Error);
            Assert.AreEqual("1", await RunSignatureFixtureAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_DynamicPrimaryConstruction_IsAtomic(bool unrelated)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "DynamicConstructor.cs");
        var declaration = "public class DynamicConstructed(int x, int unused) { public int Value => x; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic class DynamicOther(int x, int unused) { public int Value => x; }"
            + "\npublic static class SignatureProof { public static int Run() { dynamic arg = 1; return new "
            + (unrelated ? "DynamicOther" : "DynamicConstructed") + "(arg, 2).Value; } }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("1", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var probe = new SignatureStoreProbe(PreviewStore);
        var service = new ChangeSignatureService(WorkspaceManager, probe, RefactoringService);
        var locator = SymbolLocator.BySource(path, 2, declaration.IndexOf("DynamicConstructed", StringComparison.Ordinal) + 1);
        if (!unrelated)
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                    new ChangeSignatureRequest("remove", Position: 1), CancellationToken.None));
            StringAssert.Contains(error.Message, "dynamic");
            Assert.AreEqual(0, probe.StoreCalls);
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
            Assert.AreEqual(source, await File.ReadAllTextAsync(path));
        }
        else
        {
            var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                new ChangeSignatureRequest("remove", Position: 1), CancellationToken.None);
            Assert.AreEqual(1, probe.StoreCalls);
            var stored = PreviewStore.Retrieve(preview.PreviewToken);
            Assert.IsNotNull(stored);
            Assert.AreEqual("1", await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_DynamicSimpleName_RespectsLocalDelegateIdentity(bool localDelegate)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "DynamicSimpleName.cs");
        var declaration = "public static class SignatureProof { public static int M(int x, int unused) => x;";
        var source = "namespace SampleLib;\n" + declaration + " public static int Run() { dynamic arg = 1; "
            + (localDelegate ? "System.Func<int, int, int> M = (x, y) => x;" : "")
            + " return M(arg, 2); } }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("1", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var probe = new SignatureStoreProbe(PreviewStore);
        var service = new ChangeSignatureService(WorkspaceManager, probe, RefactoringService);
        var locator = SymbolLocator.BySource(path, 2, declaration.IndexOf("M(", StringComparison.Ordinal) + 1);
        if (!localDelegate)
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                    new ChangeSignatureRequest("remove", Position: 1), CancellationToken.None));
            StringAssert.Contains(error.Message, "dynamic");
            Assert.AreEqual(0, probe.StoreCalls);
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        }
        else
        {
            var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                new ChangeSignatureRequest("remove", Position: 1), CancellationToken.None);
            Assert.AreEqual(1, probe.StoreCalls);
            var stored = PreviewStore.Retrieve(preview.PreviewToken);
            Assert.IsNotNull(stored);
            Assert.AreEqual("1", await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        }
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_UnnameableUninferableExtensionType_IsAtomic()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "UnnameableExtension.cs");
        var declaration = "public static class SignatureExtensions { public static int Compute<T>(this T unused, int value) => value; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static int Run() => new { Value = 1 }.Compute(2); }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("2", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var probe = new SignatureStoreProbe(PreviewStore);
        var service = new ChangeSignatureService(WorkspaceManager, probe, RefactoringService);
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
                SymbolLocator.BySource(path, 2, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1),
                new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None));
        StringAssert.Contains(error.Message, "CS0411");
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ChangeSignaturePreview_UnboundErrorTypeCall_ProtectsOnlyEditedCandidates(bool related)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "ErrorTypeSubstitution.cs");
        var declaration = "public static class SignatureProof { public static int Compute<T>(T unused, int value) => value;";
        var source = "namespace SampleLib;\n" + declaration
            + " public static int Other<T>(T value, int retained) => retained; public static int Run() { MissingType value = default; return "
            + (related ? "Compute(value, 2)" : "Other(value, Compute(\"x\", 2))") + "; } }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var document = original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
        var model = (await document.GetSemanticModelAsync())!;
        var call = (await document.GetSyntaxRootAsync())!.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().First();
        Assert.IsNull(model.GetSymbolInfo(call).Symbol);
        Assert.IsTrue(model.GetSymbolInfo(call).CandidateSymbols.OfType<Microsoft.CodeAnalysis.IMethodSymbol>()
            .Any(m => m.Name == (related ? "Compute" : "Other")));
        var probe = new SignatureStoreProbe(PreviewStore);
        var service = new ChangeSignatureService(WorkspaceManager, probe, RefactoringService);
        var locator = SymbolLocator.BySource(path, 2, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1);
        if (related)
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                    new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None));
            StringAssert.Contains(error.Message, "unresolved invocation");
            Assert.AreEqual(0, probe.StoreCalls);
        }
        else
        {
            var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
            var stored = PreviewStore.Retrieve(preview.PreviewToken);
            Assert.IsNotNull(stored);
            var updated = stored.Value.ModifiedSolution.GetDocument(document.Id)!;
            var updatedModel = (await updated.GetSemanticModelAsync())!;
            var updatedCall = (await updated.GetSyntaxRootAsync())!.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Single(n => n.Expression.ToString().Contains("Compute", StringComparison.Ordinal));
            var target = (Microsoft.CodeAnalysis.IMethodSymbol)updatedModel.GetSymbolInfo(updatedCall).Symbol!;
            Assert.AreEqual(Microsoft.CodeAnalysis.SpecialType.System_String, target.TypeArguments.Single().SpecialType);
            Assert.AreEqual("value", target.Parameters.Single().Name);
            Assert.AreEqual(1, probe.StoreCalls);
        }
        Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    [DataRow("ordinary")]
    [DataRow("extension")]
    [DataRow("array")]
    [DataRow("nested")]
    [DataRow("owner")]
    [DataRow("missing-directive")]
    [DataRow("missing-directive-preamble")]
    [DataRow("missing-directive-existing")]
    [DataRow("missing-directive-shadow")]
    [DataRow("missing-directive-shadow-use")]
    [DataRow("missing-directive-no-root-scope")]
    [DataRow("missing-directive-block")]
    [DataRow("missing-directive-global")]
    [DataRow("missing-directive-global-existing")]
    [DataRow("missing-directive-namespace-existing")]
    [DataRow("missing-directive-namespace-using")]
    [DataRow("existing-directive-shadow")]
    [DataRow("existing-directive-shadow-nested")]
    [DataRow("existing-directive-shadow-trivia")]
    [DataRow("global-visible-b")]
    [DataRow("optional")]
    [DataRow("expanded-array")]
    [DataRow("expanded-collection")]
    [DataRow("pointer")]
    [DataRow("function-pointer")]
    [DataRow("unmanaged-function-pointer")]
    [DataRow("ref-function-pointer")]
    [DataRow("conditional")]
    public async Task ChangeSignaturePreview_ExternAliasSubstitution_PreservesAssembly(string shape)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "AliasSubstitution.cs");
        var dotnetRoot = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "../../.."));
        var targetFramework = "net" + AppContext.TargetFrameworkName!.Split("Version=v", StringSplitOptions.None)[1];
        var referenceDirectory = Directory.GetDirectories(Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref"))
            .OrderByDescending(p => Version.Parse(Path.GetFileName(p).Split('-')[0]))
            .Select(p => Path.Combine(p, "ref", targetFramework)).First(Directory.Exists);
        var references = Directory.GetFiles(referenceDirectory, "*.dll")
            .Select(p => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p));
        foreach (var assemblyName in new[] { "AliasFirst", "AliasSecond" })
        {
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(assemblyName,
                [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("namespace Shared; public struct Value { }")],
                references, new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emission = compilation.Emit(image);
            Assert.IsTrue(emission.Success, string.Join("\n", emission.Diagnostics));
            await File.WriteAllBytesAsync(workspace.GetPath("SampleLib", assemblyName + ".dll"), image.ToArray());
        }
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var projectXml = System.Xml.Linq.XDocument.Load(projectPath);
        projectXml.Root!.Add(new System.Xml.Linq.XElement("ItemGroup",
            new System.Xml.Linq.XElement("Reference", new System.Xml.Linq.XAttribute("Include", "AliasFirst"),
                new System.Xml.Linq.XElement("HintPath", "AliasFirst.dll"), new System.Xml.Linq.XElement("Aliases", "first")),
            new System.Xml.Linq.XElement("Reference", new System.Xml.Linq.XAttribute("Include", "AliasSecond"),
                new System.Xml.Linq.XElement("HintPath", "AliasSecond.dll"), new System.Xml.Linq.XElement("Aliases", "global,second"))));
        projectXml.Root.Add(new System.Xml.Linq.XElement("PropertyGroup", new System.Xml.Linq.XElement("AllowUnsafeBlocks", "true")));
        projectXml.Save(projectPath);
        var extension = shape is "extension" or "conditional";
        var declaration = shape switch
        {
            "optional" => "public static unsafe class SignatureProof { public static string Compute(int unused, first::Shared.Value retained = default) => typeof(first::Shared.Value).Assembly.GetName().Name!;",
            "expanded-array" => "public static unsafe class SignatureProof { public static string Compute(int unused, params first::Shared.Value[] retained) => retained.GetType().GetElementType()!.Assembly.GetName().Name!;",
            "expanded-collection" => "public static unsafe class SignatureProof { public static string Compute(int unused, params System.Collections.Generic.List<first::Shared.Value> retained) => retained.GetType().GetGenericArguments()[0].Assembly.GetName().Name!;",
            "pointer" => "public static unsafe class SignatureProof { public static string Compute(int unused, first::Shared.Value* retained) => typeof(first::Shared.Value).Assembly.GetName().Name!;",
            "function-pointer" => "public static unsafe class SignatureProof { public static string Compute(int unused, delegate*<first::Shared.Value, first::Shared.Value> retained) => typeof(first::Shared.Value).Assembly.GetName().Name!;",
            "unmanaged-function-pointer" => "public static unsafe class SignatureProof { public static string Compute(int unused, delegate* unmanaged[Cdecl]<first::Shared.Value, first::Shared.Value> retained) => typeof(first::Shared.Value).Assembly.GetName().Name!;",
            "ref-function-pointer" => "public static unsafe class SignatureProof { public static string Compute(int unused, delegate*<first::Shared.Value, ref first::Shared.Value> retained) => typeof(first::Shared.Value).Assembly.GetName().Name!;",
            "conditional" => "public static unsafe class SignatureProof { public static first::Shared.Value Compute(this AliasReceiver unused, int retained) => new first::Shared.Value();",
            _ => "public static unsafe class SignatureProof { public static string Compute<T>("
                + (extension ? "this AliasReceiver receiver, " : "") + "T unused, int retained) => "
                + "(typeof(T).IsArray ? typeof(T).GetElementType()! : typeof(T).IsGenericType ? typeof(T).GetGenericArguments()[0] : typeof(T)).Assembly.GetName().Name!;",
        };
        var call = shape switch
        {
            "optional" => "Compute(1)",
            "expanded-array" or "expanded-collection" => "Compute(1, new first::Shared.Value(), new first::Shared.Value())",
            "pointer" => "Compute(1, default(first::Shared.Value*))",
            "function-pointer" => "Compute(1, default(delegate*<first::Shared.Value, first::Shared.Value>))",
            "unmanaged-function-pointer" => "Compute(1, default(delegate* unmanaged[Cdecl]<first::Shared.Value, first::Shared.Value>))",
            "ref-function-pointer" => "Compute(1, default(delegate*<first::Shared.Value, ref first::Shared.Value>))",
            "conditional" => "((object)(new AliasReceiver()?.Compute(1))!).GetType().Assembly.GetName().Name!",
            "owner" => "Forward(new first::Shared.Value())",
            "missing-directive" or "missing-directive-preamble" or "missing-directive-existing" or "missing-directive-shadow" or "missing-directive-block" or "missing-directive-namespace-existing" or "missing-directive-namespace-using" or "existing-directive-shadow" or "existing-directive-shadow-nested" => "Compute(AliasFactory.Create(), 1) + \"|\" + Line()",
            "missing-directive-no-root-scope" or "missing-directive-shadow-use" or "existing-directive-shadow-trivia" => "Compute(AliasFactory.Create(), 1) + \"|\" + first.Empty + Line() + \"|\" + nameof(first)",
            "global-visible-b" => "Compute(new global::Shared.Value(), 1) + \"|\" + second.Empty + Line()",
            "missing-directive-global" or "missing-directive-global-existing" => "Compute(SampleLib.AliasFactory.Create(), 1) + \"|\" + Line()",
            _ => (extension ? "new AliasReceiver().Compute" : "Compute") + "(" + (shape switch
            {
                "array" => "new first::Shared.Value[0]",
                "nested" => "new System.Collections.Generic.List<first::Shared.Value>()",
                _ => "new first::Shared.Value()",
            }) + ", 1)",
        };
        var source = (shape.StartsWith("missing-directive", StringComparison.Ordinal) || shape.StartsWith("existing-directive-shadow", StringComparison.Ordinal) || shape == "global-visible-b"
            ? shape switch
            {
                "existing-directive-shadow" => "extern alias first;\nnamespace SampleLib;\nusing first = System.String;\n",
                "existing-directive-shadow-nested" => "extern alias first;\nnamespace SampleLib { using first = System.String;\nnamespace Nested {\n",
                "existing-directive-shadow-trivia" => "extern alias first;\nnamespace SampleLib;\nusing /*alias*/ first /*name*/ = /*target*/ System.String;\n",
                "global-visible-b" => "extern alias first;\nusing second = System.String;\nnamespace SampleLib;\n",
                "missing-directive-preamble" => "#nullable enable\n/*header*/\nnamespace SampleLib;\n",
                "missing-directive-existing" => "extern alias second;\nnamespace SampleLib;\n",
                "missing-directive-shadow" => "using first = System.Object;\nnamespace SampleLib;\n",
                "missing-directive-shadow-use" or "missing-directive-no-root-scope" => "using first = System.String;\nnamespace SampleLib;\n",
                "missing-directive-block" => "namespace SampleLib {\n",
                "missing-directive-global" => "",
                "missing-directive-global-existing" => "extern alias second;\n",
                "missing-directive-namespace-existing" => "namespace SampleLib;\nextern alias second;\n",
                "missing-directive-namespace-using" => "namespace SampleLib;\nusing Collections = System.Collections.Generic;\n",
                _ => "namespace SampleLib;\n",
            }
            : "extern alias first;\nnamespace SampleLib;\n") + declaration
            + " public static string Run() => " + call + "; "
            + (shape == "owner" ? "private static string Forward<T>(T value) => Compute(value, 1); " : "")
            + "private static int Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line; } public class AliasReceiver { }"
            + (shape == "missing-directive-block" ? " }" : shape == "existing-directive-shadow-nested" ? " } }" : "");
        if (shape.StartsWith("missing-directive", StringComparison.Ordinal) || shape.StartsWith("existing-directive-shadow", StringComparison.Ordinal))
            await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "AliasFactory.cs"),
                shape == "missing-directive-no-root-scope"
                    ? "using first = System.String; namespace SampleLib { extern alias first; public static class AliasFactory { public static first::Shared.Value Create() => new first::Shared.Value(); } }"
                    : "extern alias first; namespace SampleLib; public static class AliasFactory { public static first::Shared.Value Create() => new first::Shared.Value(); }");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        if (shape == "missing-directive-no-root-scope")
        {
            var updated = original;
            foreach (var sourceDocument in original.Projects.Single(p => p.Name == "SampleLib").Documents)
            {
                var root = (Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)(await sourceDocument.GetSyntaxRootAsync())!;
                if (root.Usings.Any(u => u.Alias?.Name.Identifier.ValueText == "first")) continue;
                var alias = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseCompilationUnit("using first = System.String; ").Usings.Single();
                updated = updated.WithDocumentSyntaxRoot(sourceDocument.Id, root.WithUsings(root.Usings.Add(alias)));
            }
            Assert.IsTrue(WorkspaceManager.TryApplyChanges(workspace.WorkspaceId, updated));
            original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
            foreach (var sourceDocument in original.Projects.Single(p => p.Name == "SampleLib").Documents)
                Assert.IsTrue(((Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)(await sourceDocument.GetSyntaxRootAsync())!)
                    .Usings.Any(u => u.Alias?.Name.Identifier.ValueText == "first"));
        }
        var expected = (shape == "global-visible-b" ? "AliasSecond" : "AliasFirst") + (shape.StartsWith("missing-directive", StringComparison.Ordinal) || shape.StartsWith("existing-directive-shadow", StringComparison.Ordinal) || shape == "global-visible-b"
            ? "|" + (shape switch { "missing-directive-preamble" or "existing-directive-shadow" or "existing-directive-shadow-nested" or "existing-directive-shadow-trivia" or "global-visible-b" => "4", "missing-directive-existing" or "missing-directive-shadow" or "missing-directive-shadow-use" or "missing-directive-no-root-scope" or "missing-directive-namespace-existing" or "missing-directive-namespace-using" => "3", "missing-directive-global" => "1", _ => "2" }) : "") + (shape is "existing-directive-shadow-trivia" or "missing-directive-shadow-use" or "missing-directive-no-root-scope" ? "|first" : "");
        var proofType = shape.StartsWith("missing-directive-global", StringComparison.Ordinal) ? "SignatureProof"
            : shape == "existing-directive-shadow-nested" ? "SampleLib.Nested.SignatureProof" : "SampleLib.SignatureProof";
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(original, proofType));
        if (shape == "existing-directive-shadow")
        {
            var document = original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
            var representable = source.Replace("extern alias first;", "extern alias first; using __signatureAssembly = first;", StringComparison.Ordinal)
                .Replace("T unused, int retained", "int retained", StringComparison.Ordinal)
                .Replace("Compute(AliasFactory.Create(), 1)", "Compute<__signatureAssembly::Shared.Value>(1)", StringComparison.Ordinal);
            Assert.AreEqual(expected, await RunSignatureFixtureAsync(original.WithDocumentText(document.Id,
                Microsoft.CodeAnalysis.Text.SourceText.From(representable)), proofType));
        }
        if (shape is "missing-directive-shadow-use" or "missing-directive-no-root-scope")
        {
            var caller = original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
            var factory = caller.Project.Documents.Single(d => d.Name == "AliasFactory.cs");
            var represented = original.WithDocumentText(caller.Id, Microsoft.CodeAnalysis.Text.SourceText.From(source
                .Replace("T unused, int retained", "int retained", StringComparison.Ordinal)
                .Replace("Compute(AliasFactory.Create(), 1)", "Compute<__signatureAssembly::Shared.Value>(1)", StringComparison.Ordinal)));
            represented = shape == "missing-directive-no-root-scope"
                ? represented.AddDocument(Microsoft.CodeAnalysis.DocumentId.CreateNewId(caller.Project.Id), "AssemblyAlias.cs",
                    Microsoft.CodeAnalysis.Text.SourceText.From("extern alias first; global using __signatureAssembly = first;"))
                : represented.WithDocumentText(factory.Id, Microsoft.CodeAnalysis.Text.SourceText.From(
                    (await factory.GetTextAsync()).ToString().Replace("extern alias first;", "extern alias first; global using __signatureAssembly = first;", StringComparison.Ordinal)));
            Assert.AreEqual(expected, await RunSignatureFixtureAsync(represented, proofType));
        }
        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, source[..source.IndexOf(declaration, StringComparison.Ordinal)].Count(c => c == '\n') + 1, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        var stored = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        var additions = stored.Value.ModifiedSolution.GetChanges(original).GetProjectChanges()
            .SelectMany(p => p.GetAddedDocuments()).Select(id => stored.Value.ModifiedSolution.GetDocument(id)!).ToArray();
        if (shape == "missing-directive-no-root-scope")
        {
            Assert.AreEqual(1, additions.Length);
            StringAssert.StartsWith(additions.Single().Name, "__SignatureAssemblyAliases_");
            Assert.AreEqual(Path.GetDirectoryName(path), Path.GetDirectoryName(additions.Single().FilePath));
            Assert.IsFalse(File.Exists(additions.Single().FilePath));
        }
        else Assert.AreEqual(0, additions.Length);
        async Task VerifyAsync(Microsoft.CodeAnalysis.Solution solution)
        {
            Assert.AreEqual(expected, await RunSignatureFixtureAsync(solution, proofType));
            var document = solution.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
            var root = (await document.GetSyntaxRootAsync())!;
            var model = (await document.GetSemanticModelAsync())!;
            var call = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Single(n => model.GetSymbolInfo(n).Symbol is Microsoft.CodeAnalysis.IMethodSymbol { Name: "Compute" });
            var target = (Microsoft.CodeAnalysis.IMethodSymbol)model.GetSymbolInfo(call).Symbol!;
            var type = shape == "conditional" ? target.ReturnType : target.IsGenericMethod ? target.TypeArguments.Single() : target.Parameters.Single().Type;
            if (type is Microsoft.CodeAnalysis.IArrayTypeSymbol array) type = array.ElementType;
            else if (type is Microsoft.CodeAnalysis.IPointerTypeSymbol pointer) type = pointer.PointedAtType;
            else if (type is Microsoft.CodeAnalysis.IFunctionPointerTypeSymbol function) type = function.Signature.ReturnType;
            else if (type is Microsoft.CodeAnalysis.INamedTypeSymbol { IsGenericType: true } named) type = named.TypeArguments.Single();
            if (shape == "owner")
            {
                Assert.AreEqual(Microsoft.CodeAnalysis.TypeKind.TypeParameter, type.TypeKind);
                Assert.AreEqual("Forward", type.ContainingSymbol.Name);
            }
            else Assert.AreEqual(shape == "global-visible-b" ? "AliasSecond" : "AliasFirst", type.ContainingAssembly.Name);
            var updatedText = (await document.GetTextAsync()).ToString();
            if (shape.StartsWith("existing-directive-shadow", StringComparison.Ordinal))
            {
                StringAssert.Contains(updatedText, "__signatureAssembly::Shared.Value");
                StringAssert.Contains(updatedText, "using __signatureAssembly = first;");
                Assert.AreEqual(1, root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
                    .Count(u => u.Alias?.Name.Identifier.ValueText == "__signatureAssembly"));
            }
            else if (shape is "missing-directive-shadow" or "missing-directive-shadow-use" or "missing-directive-no-root-scope")
            {
                StringAssert.Contains(updatedText, "__signatureAssembly::Shared.Value");
                var globalAliases = new List<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>();
                foreach (var sourceDocument in document.Project.Documents)
                    globalAliases.AddRange((await sourceDocument.GetSyntaxRootAsync())!.DescendantNodes()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
                        .Where(u => u.Alias?.Name.Identifier.ValueText == "__signatureAssembly"));
                Assert.AreEqual(1, globalAliases.Count);
                Assert.AreEqual((int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.GlobalKeyword, globalAliases.Single().GlobalKeyword.RawKind);
                Assert.AreEqual("first", globalAliases.Single().NamespaceOrType!.ToString());
            }
            else StringAssert.Contains(updatedText, shape == "global-visible-b" ? "global::Shared.Value" : "first::");
            foreach (var marker in new[] { "/*header*/", "#nullable enable", "extern alias second;", "using first = System.Object;", "using Collections = System.Collections.Generic;", "/*alias*/", "/*name*/", "/*target*/", "using second = System.String;" })
                Assert.AreEqual(source.Split(marker, StringSplitOptions.None).Length - 1, updatedText.Split(marker, StringSplitOptions.None).Length - 1, marker);
            Assert.AreEqual(shape is "missing-directive-shadow" or "missing-directive-shadow-use" or "missing-directive-no-root-scope" ? 0 : 1, root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ExternAliasDirectiveSyntax>()
                .Count(a => a.Identifier.ValueText == "first"));
        }
        await VerifyAsync(stored.Value.ModifiedSolution);
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await VerifyAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        if (shape == "missing-directive-no-root-scope")
        {
            Assert.IsTrue(File.Exists(additions.Single().FilePath));
            Assert.AreEqual((await additions.Single().GetTextAsync()).ToString(), await File.ReadAllTextAsync(additions.Single().FilePath!));
            await WorkspaceManager.ReloadAsync(workspace.WorkspaceId, CancellationToken.None);
            await VerifyAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        }
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, false, true)]
    public async Task ChangeSignaturePreview_ParenthesisFreeConstruction_AddsBoundArguments(bool collection, bool generic, bool ordinaryOptional)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "ParenthesisFree.cs");
        var declaration = ordinaryOptional
            ? collection ? "public class EmptyPrimary : System.Collections.Generic.List<int> { public EmptyPrimary(int retained = 2) { Default = retained; } public int Default { get; } }"
                : "public class EmptyPrimary { public EmptyPrimary(int retained = 2) { Default = retained; } public int Default { get; } public int Value { get; set; } }"
            : collection ? "public class EmptyPrimary" + (generic ? "<T>" : "") + "() : System.Collections.Generic.List<int> { }"
                : "public class EmptyPrimary" + (generic ? "<T>" : "") + "() { public int Value { get; set; } }";
        var construction = collection ? "new EmptyPrimary /*type*/ { /*open*/ Trace(1), /*comma*/ Trace(2) /*close*/ }"
            : "new EmptyPrimary /*type*/ { /*open*/ Value = Trace(1) /*close*/ }";
        if (generic) construction = construction.Replace("new EmptyPrimary ", "new EmptyPrimary<string> ", StringComparison.Ordinal);
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { private static string trace = \"\"; public static int Trace(int value) { trace += value; return value; } public static string Run() { var item = "
            + construction + "; return trace + \"|\" + " + (collection ? "item[0] + item[1]" : "item.Value")
            + (ordinaryOptional ? " + \":\" + item.Default" : "") + "; } }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var expected = await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof");
        var originalDocument = original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
        var originalRoot = (await originalDocument.GetSyntaxRootAsync())!;
        var oldCreation = originalRoot.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax>().Single();
        Assert.IsNull(oldCreation.ArgumentList);
        var originalModel = (await originalDocument.GetSemanticModelAsync())!;
        var oldTarget = (Microsoft.CodeAnalysis.IMethodSymbol)originalModel.GetSymbolInfo(oldCreation).Symbol!;
        Assert.AreEqual(ordinaryOptional ? 1 : 0, oldTarget.Parameters.Length);
        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf(ordinaryOptional ? "EmptyPrimary(int" : "EmptyPrimary", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 0), CancellationToken.None);
        var stored = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        async Task VerifyAsync(Microsoft.CodeAnalysis.Solution solution)
        {
            var document = solution.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
            var root = (await document.GetSyntaxRootAsync())!;
            var creation = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax>().Single();
            var model = (await document.GetSemanticModelAsync())!;
            var target = (Microsoft.CodeAnalysis.IMethodSymbol)model.GetSymbolInfo(creation).Symbol!;
            Assert.AreEqual(oldTarget.ContainingType.ToDisplayString(), target.ContainingType.ToDisplayString());
            Assert.AreEqual(oldTarget.Parameters.Length + 1, target.Parameters.Length);
            var arguments = ((Microsoft.CodeAnalysis.Operations.IObjectCreationOperation)model.GetOperation(creation)!).Arguments;
            Assert.AreEqual("marker", arguments.Single(a => a.Parameter!.Name == "marker").Parameter!.Name);
            if (ordinaryOptional)
            {
                var retained = arguments.Single(a => a.Parameter!.Name == "retained");
                Assert.AreEqual(2, retained.Value.ConstantValue.Value);
                Assert.AreEqual(oldTarget.Parameters.Single().Type.ToDisplayString(),
                    target.Parameters.Single(p => p.Name == "retained").Type.ToDisplayString());
            }
            Assert.AreEqual(oldCreation.Initializer!.ToFullString(), creation.Initializer!.ToFullString());
            var text = (await document.GetTextAsync()).ToString();
            foreach (var marker in collection ? new[] { "type", "open", "comma", "close" } : new[] { "type", "open", "close" })
                Assert.AreEqual(1, text.Split("/*" + marker + "*/", StringSplitOptions.None).Length - 1, marker);
            Assert.AreEqual(expected, await RunSignatureFixtureAsync(solution, "SampleLib.SignatureProof"));
        }
        await VerifyAsync(stored.Value.ModifiedSolution);
        Assert.AreEqual(1, preview.CallsiteUpdates!.Sum(c => c.CallsiteCount));
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await VerifyAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }

    [TestMethod]
    [DataRow("static", false)]
    [DataRow("instance", false)]
    [DataRow("conditional-instance", false)]
    [DataRow("conditional-instance", true)]
    [DataRow("extension", false)]
    [DataRow("conditional-extension", false)]
    [DataRow("conditional-extension", true)]
    [DataRow("explicit", false)]
    public async Task ChangeSignaturePreview_RemoveInferenceBearingParameter_RetainsSubstitution(string shape, bool isNull)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "GenericRemoval.cs");
        var extension = shape is "extension" or "conditional-extension";
        var declaration = extension
            ? "public static class GenericExtension { public static int Compute<T>(this GenericTarget receiver, T unused, int value) => SignatureProof.Trace(value); }"
            : "public class GenericTarget { public " + (shape is "static" or "explicit" ? "static " : "")
                + "int Compute<T>(T unused, int value) => SignatureProof.Trace(value); }";
        var call = shape switch
        {
            "static" => "GenericTarget.Compute(\"x\", Argument())",
            "explicit" => "GenericTarget.Compute /*method*/ < /*type*/ string /*close*/ >(\"x\", Argument())",
            "instance" or "extension" => "Get() /*receiver*/ . /*dot*/ Compute /*method*/ (\"x\", Argument())",
            _ => "Get() /*receiver*/ ? /*question*/ . /*dot*/ Compute /*method*/ (\"x\", Argument())",
        };
        var source = "namespace SampleLib;\n" + declaration + "\n"
            + (extension ? "public class GenericTarget { }" : "")
            + "public static class SignatureProof { private static string trace = \"\"; public static int Trace(int value) { trace += value; return value; }"
            + "private static int Argument() => Trace(1); private static GenericTarget? Get() { trace += \"R\"; return "
            + (isNull ? "null" : "new GenericTarget()") + "; } public static string Run() { var value = "
            + call + "; return trace + \"|\" + value; } }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var expected = await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof");
        async Task VerifyAsync(Microsoft.CodeAnalysis.Solution solution)
        {
            var document = solution.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
            var root = (await document.GetSyntaxRootAsync())!;
            var callNode = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Single(n => n.Expression.ToString().Contains("Compute", StringComparison.Ordinal));
            var model = (await document.GetSemanticModelAsync())!;
            var target = (Microsoft.CodeAnalysis.IMethodSymbol)model.GetSymbolInfo(callNode).Symbol!;
            Assert.AreEqual(Microsoft.CodeAnalysis.SpecialType.System_String, target.TypeArguments.Single().SpecialType);
            Assert.AreEqual("value", ((Microsoft.CodeAnalysis.Operations.IInvocationOperation)model.GetOperation(callNode)!)
                .Arguments.Single(a => a.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentSyntax).Parameter!.Name);
            Assert.AreEqual(expected, await RunSignatureFixtureAsync(solution, "SampleLib.SignatureProof"));
            var text = (await document.GetTextAsync()).ToString();
            foreach (var marker in new[] { "receiver", "question", "dot", "method", "type", "close" })
                Assert.AreEqual(source.Split("/*" + marker + "*/", StringSplitOptions.None).Length - 1,
                    text.Split("/*" + marker + "*/", StringSplitOptions.None).Length - 1, marker);
        }
        var originalDoc = original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
        var originalModel = (await originalDoc.GetSemanticModelAsync())!;
        var originalCall = (await originalDoc.GetSyntaxRootAsync())!.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Single(n => n.Expression.ToString().Contains("Compute", StringComparison.Ordinal));
        Assert.AreEqual(Microsoft.CodeAnalysis.SpecialType.System_String,
            ((Microsoft.CodeAnalysis.IMethodSymbol)originalModel.GetSymbolInfo(originalCall).Symbol!).TypeArguments.Single().SpecialType);
        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        var stored = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        await VerifyAsync(stored.Value.ModifiedSolution);
        Assert.AreEqual(1, preview.CallsiteUpdates!.Sum(c => c.CallsiteCount));
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await VerifyAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }

}
