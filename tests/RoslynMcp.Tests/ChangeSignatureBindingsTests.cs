using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ChangeSignatureBindingsTests : ChangeSignatureTestBase
{

    [TestMethod]
    [DataRow("add-omitted")]
    [DataRow("reorder-omitted")]
    [DataRow("reorder-expanded")]
    [DataRow("remove-expanded")]
    public async Task ChangeSignaturePreview_PreservesOriginalRuntimeBindings(string scenario)
    {
        await using var workspace = CreateSingleProjectWorkspace();
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
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var request = scenario switch
        {
            "add-omitted" => new ChangeSignatureRequest("add", Name: "c", ParameterType: "int", DefaultValue: "7"),
            "remove-expanded" => new ChangeSignatureRequest("remove", Name: "tail"),
            _ => new ChangeSignatureRequest("reorder", NewOrder: expanded ? "b,a,tail" : "b,a"),
        };
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, SymbolLocator.BySource(path, 6, 23), request, CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        var originalValue = await RunSignatureFixtureAsync(original, "SampleLib.SignatureRuntimeFixture");
        var candidateValue = await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureRuntimeFixture");
        // Removing a parameter intentionally removes its argument expressions. Retained expressions
        // must keep their original evaluation order and original parameter values.
        Assert.AreEqual(scenario == "remove-expanded" ? "12|120" : originalValue, candidateValue);
        if (scenario.EndsWith("omitted", StringComparison.Ordinal))
            await AssertOptionalArgumentBindingsAsync(original, stored.Value.ModifiedSolution, path);
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        if (scenario.EndsWith("omitted", StringComparison.Ordinal))
            await AssertOptionalArgumentBindingsAsync(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), path);
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
    public async Task ChangeSignaturePreview_CrossDocument_DefaultStructAndUnrelatedOverloadStayBound()
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var declarationPath = workspace.GetPath("SampleLib", "CrossTarget.cs");
        var callerPath = workspace.GetPath("SampleLib", "CrossCaller.cs");
        var declaration = "public struct CrossTarget(int removed, int retained) { public int Value => retained; public CrossTarget(string value) : this(0, int.Parse(value)) { } }";
        await File.WriteAllTextAsync(declarationPath, "namespace SampleLib;\n" + declaration);
        await File.WriteAllTextAsync(callerPath, "namespace SampleLib;\npublic static class CrossCaller { public static int Run() => " +
            "new CrossTarget(1, 2).Value + new CrossTarget(\"3\").Value + new CrossTarget().Value + default(CrossTarget).Value; }");
        await workspace.LoadAsync(CancellationToken.None);
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(declarationPath, 2, declaration.IndexOf("CrossTarget", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None);
        var pair = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(pair);
        Assert.AreEqual("5", await RunSignatureFixtureAsync(pair.Value.ModifiedSolution, "SampleLib.CrossCaller"));
        Assert.AreEqual(2, preview.Changes.Count);
        Assert.AreEqual(2, preview.CallsiteUpdates?.Sum(c => c.CallsiteCount));
        var apply = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        var changed = await File.ReadAllTextAsync(callerPath);
        StringAssert.Contains(changed, "new CrossTarget(\"3\")");
        StringAssert.Contains(changed, "new CrossTarget()");
        StringAssert.Contains(changed, "default(CrossTarget)");
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
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "MethodGroupDrift.cs");
        var source = "namespace SampleLib;\npublic static class MethodGroupDrift { " +
            "public static int Compute(int first) => first; " +
            "public static int Compute(int first, object second) => first; " +
            "public static System.Func<int, string, int> Capture() => Compute; " +
            "public static int Direct() => Compute(1); }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
                SymbolLocator.BySource(path, 2, source.Split('\n')[1].IndexOf("Compute", StringComparison.Ordinal) + 1),
                new ChangeSignatureRequest("add", Name: "second", ParameterType: "string"),
                CancellationToken.None));
        StringAssert.Contains(error.Message, "retained target");
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
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
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_RemoveExtensionReceiver_PreservesGenericSyntax(bool anonymous)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "ExtensionGenericProof.cs");
        var declaration = "public static class SignatureExtensions { public static string Compute<T>(this Receiver receiver, T value) where T : notnull => typeof(T).Name + \":\" + value.ToString(); }";
        var call = anonymous
            ? "new Receiver() /*before-dot*/ . /*after-dot*/ Compute /*method*/ (new { Value = 2 })"
            : "new Receiver() /*before-dot*/ . /*after-dot*/ Compute /*method*/ < /*type*/ int /*type-close*/ >(2)";
        var source = "namespace SampleLib;\npublic sealed class Receiver { }\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => " + call + "; }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var expected = await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof");
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 3, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        var candidate = stored.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
        var text = (await candidate.GetTextAsync()).ToString();
        foreach (var marker in anonymous ? new[] { "before-dot", "after-dot", "method" } : new[] { "before-dot", "after-dot", "method", "type", "type-close" })
            Assert.AreEqual(1, text.Split("/*" + marker + "*/", StringSplitOptions.None).Length - 1, marker);
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
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
        await using var workspace = CreateSingleProjectWorkspace();
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
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
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
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        await VerifyAsync(stored.Value.ModifiedSolution);
        Assert.AreEqual(1, preview.CallsiteUpdates!.Sum(c => c.CallsiteCount));
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await VerifyAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }
}
