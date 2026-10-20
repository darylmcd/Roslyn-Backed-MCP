using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ChangeSignaturePreviewTests : ChangeSignatureTestBase
{

    [TestMethod]
    public async Task ChangeSignaturePreview_AddOp_PreservesBodyBraceLineBreak()
    {
        await using var workspace = CreateSingleProjectWorkspace();

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

        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
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
        var applyResult = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
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
        await using var workspace = CreateSingleProjectWorkspace();

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

        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
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
        await using var workspace = CreateSingleProjectWorkspace();

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

        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
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
        await using var workspace = CreateSingleProjectWorkspace();

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

        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);

        Assert.IsNotNull(preview.PreviewToken);
        Assert.IsNotNull(preview.CallsiteUpdates, "CallsiteUpdates summary must be populated");
        var fileUpdate = preview.CallsiteUpdates.SingleOrDefault(
            u => u.FilePath.EndsWith("SameDocumentChangeSignatureFixture.cs", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(fileUpdate, "fixture file must appear in CallsiteUpdates");
        Assert.AreEqual(3, fileUpdate.CallsiteCount,
            "all 3 same-document invocations (svc/a/b.Compute) must be counted, matching find_references");

        var applyResult = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
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
        await using var workspace = CreateSingleProjectWorkspace();

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

        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
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
        await using var workspace = CreateSingleProjectWorkspace();

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

        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
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
        await using var workspace = CreateSingleProjectWorkspace();

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
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);
        Assert.IsTrue(preview.Changes.Count >= 1, "preview must report at least the declaration file");

        var applyResult = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
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
        await using var workspace = CreateSingleProjectWorkspace();

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

        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);

        var applyResult = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
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
        await using var workspace = CreateSingleProjectWorkspace();

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

        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
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
        await using var workspace = CreateSingleProjectWorkspace();

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

        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(async () =>
            await ChangeSignature.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, request, CancellationToken.None));

        // Error must name the wire parameter, the valid range, and the "omit position" hint,
        // and must NOT echo the caller-supplied Name or ParameterType.
        Assert.AreEqual("position", ex.ParamName);
        StringAssert.Contains(ex.Message, "'position'",
            $"error must cite the wire parameter; got: {ex.Message}");
        StringAssert.Contains(ex.Message, "0..2",
            $"error must cite the valid Position range; got: {ex.Message}");
        StringAssert.Contains(ex.Message, "omit position",
            $"error must hint that omitting position appends at end; got: {ex.Message}");
        Assert.IsFalse(ex.Message.Contains("cancellationToken", StringComparison.Ordinal),
            $"error must not echo the caller-supplied name; got: {ex.Message}");
        Assert.IsFalse(ex.Message.Contains("CancellationToken", StringComparison.Ordinal),
            $"error must not echo the caller-supplied parameter type; got: {ex.Message}");
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
        await using var workspace = CreateSingleProjectWorkspace();

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

        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
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
        await using var workspace = CreateSingleProjectWorkspace();

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

        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
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
        await using var workspace = CreateSingleProjectWorkspace();

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
        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(async () =>
            await ChangeSignature.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, new ChangeSignatureRequest(Op: "reorder"), CancellationToken.None));
        Assert.AreEqual("newOrder", ex.ParamName);
        StringAssert.Contains(ex.Message, "newOrder",
            $"missing-NewOrder error must cite 'NewOrder'; got: {ex.Message}");

        // Wrong arity (2 tokens for 3-param method).
        ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(async () =>
            await ChangeSignature.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, new ChangeSignatureRequest(Op: "reorder", NewOrder: "a,b"), CancellationToken.None));
        Assert.AreEqual("newOrder", ex.ParamName);
        StringAssert.Contains(ex.Message, "exactly once",
            $"wrong-arity error must explain that every parameter must appear exactly once; got: {ex.Message}");

        // Unknown name token.
        ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(async () =>
            await ChangeSignature.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, new ChangeSignatureRequest(Op: "reorder", NewOrder: "a,b,zz"), CancellationToken.None));
        Assert.AreEqual("newOrder", ex.ParamName);
        StringAssert.Contains(ex.Message, "entry 3",
            $"unknown-token error must cite the offending entry position; got: {ex.Message}");
        Assert.IsFalse(ex.Message.Contains("zz", StringComparison.Ordinal),
            $"unknown-token error must not echo the caller's token; got: {ex.Message}");

        // Duplicate token.
        ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(async () =>
            await ChangeSignature.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, new ChangeSignatureRequest(Op: "reorder", NewOrder: "a,a,b"), CancellationToken.None));
        Assert.AreEqual("newOrder", ex.ParamName);
        StringAssert.Contains(ex.Message, "more than once",
            $"duplicate-token error must explain the parameter is listed twice; got: {ex.Message}");

        // Identity permutation (no-op).
        ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(async () =>
            await ChangeSignature.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, new ChangeSignatureRequest(Op: "reorder", NewOrder: "a,b,c"), CancellationToken.None));
        Assert.AreEqual("newOrder", ex.ParamName);
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
        await using var workspace = CreateSingleProjectWorkspace();

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
            await ChangeSignature.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, locator, request, CancellationToken.None));
        StringAssert.Contains(ex.Message, "mixes positional and named",
            $"mixed-callsite refusal must explain why; got: {ex.Message}");
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_RemoveOp_CaretOnParameter_ExplicitPositionOverridesAutoResolve()
    {
        await using var workspace = CreateSingleProjectWorkspace();

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

        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);
        var applyResult = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
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
        await using var workspace = CreateSingleProjectWorkspace();

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

        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);

        // Only the two genuine invocations count as call sites.
        Assert.IsNotNull(preview.CallsiteUpdates, "CallsiteUpdates summary must be populated when apply rewrites invocations");
        var fixtureUpdate = preview.CallsiteUpdates.Single(
            u => u.FilePath.EndsWith("ChangeSignatureRefKindsFixture.cs", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(2, fixtureUpdate.CallsiteCount,
            "method-group and nameof references are not call sites and must not be counted");

        var applyResult = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, $"apply must succeed: {applyResult.Error}");

        var postApplyText = await File.ReadAllTextAsync(fixturePath);
        StringAssert.Contains(postApplyText, "Compute(1, 2, 0)", $"direct call must be rewritten; got:\n{postApplyText}");
        StringAssert.Contains(postApplyText, "other.Compute(3, 4, 0)", $"receiver call must be rewritten; got:\n{postApplyText}");
        StringAssert.Contains(postApplyText, "Apply(Compute<int>, 5, 6)",
            $"enclosing Apply(...) of a method-group reference must be untouched; got:\n{postApplyText}");
        StringAssert.Contains(postApplyText, "Log(nameof(Compute), \"m\")",
            $"nameof(...) and its enclosing Log(...) must be untouched; got:\n{postApplyText}");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_PreservesInternalAndCommaTrivia(bool named)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "TriviaTarget.cs");
        var declaration = "public class TriviaTarget(int /*parameter-one*/ first, /*parameter-comma*/ int second = 7) { }";
        var call = named
            ? "new TriviaTarget(first /*name-comment*/: /*colon-comment*/ 1, /*argument-comma*/ second: 2)"
            : "new TriviaTarget(1, /*argument-comma*/ 2)";
        var source = "namespace SampleLib;\n" + declaration + "\npublic static class TriviaCaller { public static object Run() => " + call + "; }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf("TriviaTarget", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", DefaultValue: "9"), CancellationToken.None);
        var pair = Services.PreviewStore.Retrieve(preview.PreviewToken);
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
}
