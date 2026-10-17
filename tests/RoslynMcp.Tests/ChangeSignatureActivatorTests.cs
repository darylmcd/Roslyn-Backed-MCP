using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ChangeSignatureActivatorTests : ChangeSignatureTestBase
{

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
    public async Task ChangeSignaturePreview_LastStructParameter_ActivatorNullableDiagnostics()
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static object? Zero(System.Type type) => System.Activator.CreateInstance(type); public static string Run() { var zero = (SelectedStruct)Zero(typeof(SelectedStruct))!; try { Zero(null!); } catch(System.ArgumentNullException) { } return new SelectedStruct(2).Value + \"|\" + zero.Value; } }";
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        var pair = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(pair);
        var compilation = await pair.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync();
        Assert.IsNotNull(compilation);
        Assert.AreEqual(0, compilation.GetDiagnostics().Count(d => d.Id.StartsWith("CS86", StringComparison.Ordinal)
            && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning), string.Join(Environment.NewLine, compilation.GetDiagnostics()));
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(pair.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        var apply = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }
}
