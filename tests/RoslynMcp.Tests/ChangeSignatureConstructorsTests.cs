using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ChangeSignatureConstructorsTests : ChangeSignatureTestBase
{

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
        await using var workspace = CreateSingleProjectWorkspace();

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
            await ChangeSignature.PreviewChangeSignatureAsync(
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
        await using var workspace = CreateSingleProjectWorkspace();

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

        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, locator, request, CancellationToken.None);

        Assert.IsFalse(string.IsNullOrEmpty(preview.PreviewToken), "ordinary constructor must produce a preview token");
        Assert.AreEqual(1, preview.CallsiteUpdates?.Sum(c => c.CallsiteCount));
        var candidate = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(candidate);
        Assert.AreEqual("1", await RunSignatureFixtureAsync(candidate.Value.ModifiedSolution, "SampleLib.OrdinaryCtorFixture"));
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual("1", await RunSignatureFixtureAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.OrdinaryCtorFixture"));
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
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "PrimaryBindingFixture.cs");
        var declaration = $"public {kind} PrimaryBindingFixture(int first, int second = 7)";
        var text = $"namespace SampleLib;\n{declaration} {{ }}\npublic static class PrimaryCaller {{ public static PrimaryBindingFixture Make() => new(1); }}\n";
        await File.WriteAllTextAsync(path, text);
        await workspace.LoadAsync(CancellationToken.None);
        var column = declaration.IndexOf(parameterCaret ? "first" : "PrimaryBindingFixture", StringComparison.Ordinal) + 1;
        var request = op == "add"
            ? new ChangeSignatureRequest("add", Name: "third", ParameterType: "int", DefaultValue: "9")
            : new ChangeSignatureRequest("remove", Position: 0);
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, SymbolLocator.BySource(path, 2, column), request, CancellationToken.None);
        var pair = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(pair);
        var compilation = await pair.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync();
        Assert.IsNotNull(compilation);
        Assert.AreEqual(0, compilation.GetDiagnostics().Count(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error),
            string.Join("\n", compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)));
        var apply = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        var changed = await File.ReadAllTextAsync(path);
        StringAssert.Contains(changed, op == "add" ? "int third = 9" : "PrimaryBindingFixture(int second = 7)");
        Assert.AreEqual(1, preview.CallsiteUpdates?.Sum(c => c.CallsiteCount));
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

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, false, true)]
    public async Task ChangeSignaturePreview_ParenthesisFreeConstruction_AddsBoundArguments(bool collection, bool generic, bool ordinaryOptional)
    {
        await using var workspace = CreateSingleProjectWorkspace();
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
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var expected = await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof");
        var originalDocument = original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
        var originalRoot = (await originalDocument.GetSyntaxRootAsync())!;
        var oldCreation = originalRoot.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax>().Single();
        Assert.IsNull(oldCreation.ArgumentList);
        var originalModel = (await originalDocument.GetSemanticModelAsync())!;
        var oldTarget = (Microsoft.CodeAnalysis.IMethodSymbol)originalModel.GetSymbolInfo(oldCreation).Symbol!;
        Assert.AreEqual(ordinaryOptional ? 1 : 0, oldTarget.Parameters.Length);
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf(ordinaryOptional ? "EmptyPrimary(int" : "EmptyPrimary", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 0), CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
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
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await VerifyAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }
}
